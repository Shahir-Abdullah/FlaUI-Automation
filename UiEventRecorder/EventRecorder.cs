using System;
using System.Collections.Generic;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.Core.Identifiers;
using FlaUI.UIA3;

namespace UiEventRecorder;

/// <summary>
/// Subscribes to a verbose set of UI Automation events on a single top-level
/// window (its whole subtree) and pushes each occurrence into the supplied
/// <see cref="JsonlEventSink"/>, stamped with the supplied
/// <see cref="WindowContext"/>.
///
/// Owned and orchestrated by <see cref="WindowTracker"/>; the tracker creates
/// one of these per matching window and disposes it when the window closes.
/// </summary>
internal sealed class EventRecorder : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly AutomationElement _root;
    private readonly JsonlEventSink _sink;
    private readonly WindowContext _context;

    // Keep handler references alive for the entire session - UIA hands them
    // out via COM and will silently stop delivering events if we let them be
    // collected. They are also IDisposable: disposing unregisters the hook.
    private readonly List<AutomationEventHandlerBase> _automationHandlers = new();
    private readonly List<PropertyChangedEventHandlerBase> _propertyHandlers = new();
    private readonly List<StructureChangedEventHandlerBase> _structureHandlers = new();

    private bool _disposed;

    public EventRecorder(UIA3Automation automation, AutomationElement root, WindowContext context, JsonlEventSink sink)
    {
        _automation = automation;
        _root = root;
        _context = context;
        _sink = sink;
    }

    public WindowContext Context => _context;

    public void Start()
    {
        var lib = _automation.EventLibrary;
        var props = _automation.PropertyLibrary;

        // --- Pattern-driven user actions ----------------------------------
        Subscribe(lib.Invoke.InvokedEvent,                            "Invoke");
        Subscribe(lib.SelectionItem.ElementSelectedEvent,             "SelectionItemSelected");
        Subscribe(lib.SelectionItem.ElementAddedToSelectionEvent,     "SelectionItemAdded");
        Subscribe(lib.SelectionItem.ElementRemovedFromSelectionEvent, "SelectionItemRemoved");
        Subscribe(lib.Selection.InvalidatedEvent,                     "SelectionInvalidated");
        Subscribe(lib.Text.TextChangedEvent,                          "TextChanged");
        Subscribe(lib.Text.TextSelectionChangedEvent,                 "TextSelectionChanged");

        // --- Generic element events ---------------------------------------
        Subscribe(lib.Element.MenuOpenedEvent,                        "MenuOpened");
        Subscribe(lib.Element.MenuClosedEvent,                        "MenuClosed");
        Subscribe(lib.Element.ToolTipOpenedEvent,                     "ToolTipOpened");
        Subscribe(lib.Element.ToolTipClosedEvent,                     "ToolTipClosed");
        Subscribe(lib.Element.LayoutInvalidatedEvent,                 "LayoutInvalidated");
        Subscribe(lib.Element.LiveRegionChangedEvent,                 "LiveRegionChanged");
        Subscribe(lib.Element.SystemAlertEvent,                       "SystemAlert");

        // --- Window lifecycle of nested/child windows --------------------
        Subscribe(lib.Window.WindowOpenedEvent, "WindowOpened");
        Subscribe(lib.Window.WindowClosedEvent, "WindowClosed");

        // --- Property changes (verbose mode) -----------------------------
        // UIA requires the caller to enumerate every property of interest.
        // The list below covers properties a user typically *causes* to
        // change while interacting with a window.
        SubscribeProperties(
            props.Element.Name,
            props.Element.HasKeyboardFocus,
            props.Element.IsEnabled,
            props.Element.IsOffscreen,
            props.Element.BoundingRectangle,
            props.Element.HelpText,
            props.Element.ItemStatus,
            props.Value.Value,
            props.Value.IsReadOnly,
            props.RangeValue.Value,
            props.Toggle.ToggleState,
            props.SelectionItem.IsSelected,
            props.ExpandCollapse.ExpandCollapseState,
            props.Window.WindowVisualState,
            props.Window.WindowInteractionState,
            props.Window.IsModal);

        // --- Structure changes (children added/removed/reordered) --------
        try
        {
            var handler = _root.RegisterStructureChangedEvent(TreeScope.Subtree, OnStructureChanged);
            if (handler is not null)
            {
                _structureHandlers.Add(handler);
            }
        }
        catch (Exception ex)
        {
            Warn("RegisterStructureChangedEvent", ex);
        }
    }

    // ------------------------------------------------------------------ subscriptions

    private void Subscribe(EventId eventId, string label)
    {
        if (eventId is null)
        {
            return;
        }

        try
        {
            var handler = _root.RegisterAutomationEvent(eventId, TreeScope.Subtree,
                (element, _) => OnAutomationEvent(label, element));
            if (handler is not null)
            {
                _automationHandlers.Add(handler);
            }
        }
        catch (Exception ex)
        {
            Warn($"RegisterAutomationEvent({label})", ex);
        }
    }

    private void SubscribeProperties(params PropertyId[] properties)
    {
        if (properties.Length == 0)
        {
            return;
        }

        try
        {
            var handler = _root.RegisterPropertyChangedEvent(TreeScope.Subtree, OnPropertyChanged, properties);
            if (handler is not null)
            {
                _propertyHandlers.Add(handler);
            }
        }
        catch (Exception ex)
        {
            Warn("RegisterPropertyChangedEvent", ex);
        }
    }

    // ------------------------------------------------------------------ callbacks

    private void OnAutomationEvent(string label, AutomationElement element)
    {
        var snap = Snapshot(element);
        _sink.Enqueue(new RecordedEvent
        {
            EventType    = label,
            ProcessId    = _context.ProcessId,
            ProcessName  = _context.ProcessName,
            WindowTitle  = _context.WindowTitle,
            AutomationId = snap.AutomationId,
            Name         = snap.Name,
            ControlType  = snap.ControlType,
            ClassName    = snap.ClassName,
        });
    }

    private void OnPropertyChanged(AutomationElement element, PropertyId property, object newValue)
    {
        var snap = Snapshot(element);
        _sink.Enqueue(new RecordedEvent
        {
            EventType    = "PropertyChanged",
            ProcessId    = _context.ProcessId,
            ProcessName  = _context.ProcessName,
            WindowTitle  = _context.WindowTitle,
            AutomationId = snap.AutomationId,
            Name         = snap.Name,
            ControlType  = snap.ControlType,
            ClassName    = snap.ClassName,
            Property     = property?.Name,
            NewValue     = StringifyValue(newValue),
        });
    }

    private void OnStructureChanged(AutomationElement element, StructureChangeType changeType, int[] runtimeId)
    {
        var snap = Snapshot(element);
        _sink.Enqueue(new RecordedEvent
        {
            EventType    = "StructureChanged",
            ProcessId    = _context.ProcessId,
            ProcessName  = _context.ProcessName,
            WindowTitle  = _context.WindowTitle,
            AutomationId = snap.AutomationId,
            Name         = snap.Name,
            ControlType  = snap.ControlType,
            ClassName    = snap.ClassName,
            NewValue     = changeType.ToString(),
            Extra        = new Dictionary<string, object?>
            {
                ["runtimeId"] = runtimeId,
            },
        });
    }

    // ------------------------------------------------------------------ snapshots

    private readonly record struct ElementSnapshot(
        string? AutomationId,
        string? Name,
        string? ControlType,
        string? ClassName);

    private static ElementSnapshot Snapshot(AutomationElement? element)
    {
        if (element is null)
        {
            return new ElementSnapshot(null, null, null, null);
        }

        // Every accessor talks to the target process via COM and may throw if
        // the element has just disappeared. Wrap each one so a single failure
        // can't poison the whole record.
        string? automationId = TryGet(() => element.AutomationId);
        string? name         = TryGet(() => element.Name);
        string? className    = TryGet(() => element.ClassName);
        string? controlType  = TryGet(() => element.ControlType.ToString());

        return new ElementSnapshot(
            string.IsNullOrEmpty(automationId) ? null : automationId,
            string.IsNullOrEmpty(name) ? null : name,
            controlType,
            string.IsNullOrEmpty(className) ? null : className);
    }

    private static string? TryGet(Func<string?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static object? StringifyValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            Enum e => e.ToString(),
            _      => value,
        };
    }

    private static void Warn(string what, Exception ex) =>
        Console.Error.WriteLine($"[recorder] {what} failed: {ex.Message}");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Disposing a FlaUI event handler unregisters it from UIA.
        foreach (var h in _automationHandlers)
        {
            TryDispose(h);
        }
        foreach (var h in _propertyHandlers)
        {
            TryDispose(h);
        }
        foreach (var h in _structureHandlers)
        {
            TryDispose(h);
        }

        _automationHandlers.Clear();
        _propertyHandlers.Clear();
        _structureHandlers.Clear();
    }

    private static void TryDispose(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch
        {
            // Best-effort teardown; the target process may already be gone.
        }
    }
}
