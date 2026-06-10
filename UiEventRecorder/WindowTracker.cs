using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.UIA3;

namespace UiEventRecorder;

/// <summary>
/// Owns the per-process discovery loop:
///
///   1. At <see cref="Start"/>, walks every top-level window currently on the
///      desktop and creates an <see cref="EventRecorder"/> for the ones whose
///      owning process matches the configured filter.
///   2. Subscribes a single desktop-scoped <c>WindowOpenedEvent</c> hook so
///      that new top-level windows (popups, additional Teams chat windows,
///      etc.) are picked up automatically while the user works.
///   3. Subscribes a single desktop-scoped <c>WindowClosedEvent</c> hook to
///      tear the per-window recorder down again.
///   4. Subscribes a single global <c>FocusChangedEvent</c> hook and forwards
///      every focus change whose focused element lives in a tracked process.
/// </summary>
internal sealed class WindowTracker : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly JsonlEventSink _sink;
    private readonly HashSet<string> _processFilter; // lower-cased, empty == match all
    private readonly Dictionary<IntPtr, EventRecorder> _recorders = new();
    private readonly object _gate = new();

    private AutomationEventHandlerBase? _windowOpenedHandler;
    private AutomationEventHandlerBase? _windowClosedHandler;
    private FocusChangedEventHandlerBase? _focusHandler;
    private bool _disposed;

    public WindowTracker(UIA3Automation automation, JsonlEventSink sink, IEnumerable<string> processNames)
    {
        _automation    = automation;
        _sink          = sink;
        _processFilter = new HashSet<string>(
            processNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim().ToLowerInvariant()),
            StringComparer.Ordinal);
    }

    public int TrackedWindowCount
    {
        get { lock (_gate) { return _recorders.Count; } }
    }

    public IReadOnlyList<WindowContext> TrackedWindows
    {
        get { lock (_gate) { return _recorders.Values.Select(r => r.Context).ToList(); } }
    }

    public IReadOnlyCollection<string> ProcessFilter => _processFilter;

    public void Start()
    {
        var desktop = _automation.GetDesktop();

        // 1) Snapshot every top-level window already on the desktop.
        AutomationElement[] existing;
        try
        {
            existing = desktop.FindAllChildren();
        }
        catch (Exception ex)
        {
            Warn("desktop.FindAllChildren", ex);
            existing = Array.Empty<AutomationElement>();
        }

        foreach (var w in existing)
        {
            TryAttach(w);
        }

        // 2) Listen for new top-level windows opening.
        try
        {
            _windowOpenedHandler = desktop.RegisterAutomationEvent(
                _automation.EventLibrary.Window.WindowOpenedEvent,
                TreeScope.Subtree,
                (window, _) => TryAttach(window));
        }
        catch (Exception ex)
        {
            Warn("RegisterAutomationEvent(WindowOpened)", ex);
        }

        // 3) Listen for top-level windows closing.
        try
        {
            _windowClosedHandler = desktop.RegisterAutomationEvent(
                _automation.EventLibrary.Window.WindowClosedEvent,
                TreeScope.Subtree,
                (window, _) => OnWindowClosed(window));
        }
        catch (Exception ex)
        {
            Warn("RegisterAutomationEvent(WindowClosed)", ex);
        }

        // 4) One global focus subscription routed back to the matching window.
        try
        {
            _focusHandler = _automation.RegisterFocusChangedEvent(OnFocusChanged);
        }
        catch (Exception ex)
        {
            Warn("RegisterFocusChangedEvent", ex);
        }
    }

    // ------------------------------------------------------------------ attach / detach

    private void TryAttach(AutomationElement? window)
    {
        if (_disposed || window is null)
        {
            return;
        }

        var pid = TryGetPid(window);
        if (pid == 0)
        {
            return;
        }

        string processName;
        try
        {
            using var proc = Process.GetProcessById(pid);
            processName = proc.ProcessName ?? string.Empty;
        }
        catch
        {
            // Process has exited between event and lookup, or access denied
            // (e.g. some elevated processes). Ignore.
            return;
        }

        if (!MatchesFilter(processName))
        {
            return;
        }

        var hwnd = TryGetHwnd(window);
        if (hwnd == IntPtr.Zero)
        {
            // No HWND means this isn't really a top-level OS window (e.g. a
            // transient tooltip surfaced by UIA). Ignore.
            return;
        }

        var title = TryGet(() => window.Name) ?? string.Empty;
        var ctx = new WindowContext(pid, processName, title);

        EventRecorder recorder;
        lock (_gate)
        {
            if (_recorders.ContainsKey(hwnd))
            {
                return;
            }
            recorder = new EventRecorder(_automation, window, ctx, _sink);
            _recorders[hwnd] = recorder;
        }

        try
        {
            recorder.Start();
            EmitTrackerEvent("WindowAttached", ctx);
            Console.WriteLine($"[tracker] attached  pid={pid,-6} {processName,-20} hwnd=0x{hwnd.ToInt64():X}  \"{title}\"");
        }
        catch (Exception ex)
        {
            Warn($"attach to {processName}({pid})", ex);
            lock (_gate)
            {
                _recorders.Remove(hwnd);
            }
            recorder.Dispose();
        }
    }

    private void OnWindowClosed(AutomationElement window)
    {
        if (_disposed)
        {
            return;
        }

        // The element handed to us by UIA is often partially defunct, but the
        // native window handle is still readable in most cases.
        var hwnd = TryGetHwnd(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        EventRecorder? recorder = null;
        lock (_gate)
        {
            if (_recorders.Remove(hwnd, out var existing))
            {
                recorder = existing;
            }
        }

        if (recorder is null)
        {
            return;
        }

        EmitTrackerEvent("WindowDetached", recorder.Context);
        Console.WriteLine($"[tracker] detached  pid={recorder.Context.ProcessId,-6} {recorder.Context.ProcessName,-20} hwnd=0x{hwnd.ToInt64():X}  \"{recorder.Context.WindowTitle}\"");
        recorder.Dispose();
    }

    // ------------------------------------------------------------------ focus

    private void OnFocusChanged(AutomationElement? element)
    {
        if (_disposed || element is null)
        {
            return;
        }

        var pid = TryGetPid(element);
        if (pid == 0)
        {
            return;
        }

        WindowContext? ctx = null;
        lock (_gate)
        {
            foreach (var r in _recorders.Values)
            {
                if (r.Context.ProcessId == pid)
                {
                    ctx = r.Context;
                    break;
                }
            }
        }
        if (ctx is null)
        {
            return; // focus moved to an element we're not tracking
        }

        _sink.Enqueue(new RecordedEvent
        {
            EventType    = "FocusChanged",
            ProcessId    = ctx.ProcessId,
            ProcessName  = ctx.ProcessName,
            WindowTitle  = ctx.WindowTitle,
            AutomationId = TryGet(() => element.AutomationId),
            Name         = TryGet(() => element.Name),
            ControlType  = TryGet(() => element.ControlType.ToString()),
            ClassName    = TryGet(() => element.ClassName),
        });
    }

    // ------------------------------------------------------------------ helpers

    private bool MatchesFilter(string processName)
    {
        if (_processFilter.Count == 0)
        {
            return true; // no filter -> track everything
        }
        return _processFilter.Contains(processName.ToLowerInvariant());
    }

    private void EmitTrackerEvent(string label, WindowContext ctx)
    {
        _sink.Enqueue(new RecordedEvent
        {
            EventType   = label,
            ProcessId   = ctx.ProcessId,
            ProcessName = ctx.ProcessName,
            WindowTitle = ctx.WindowTitle,
        });
    }

    private static int TryGetPid(AutomationElement w)
    {
        try { return w.Properties.ProcessId.ValueOrDefault; }
        catch { return 0; }
    }

    private static IntPtr TryGetHwnd(AutomationElement w)
    {
        try { return w.Properties.NativeWindowHandle.ValueOrDefault; }
        catch { return IntPtr.Zero; }
    }

    private static T? TryGet<T>(Func<T?> getter)
    {
        try { return getter(); }
        catch { return default; }
    }

    private static void Warn(string what, Exception ex) =>
        Console.Error.WriteLine($"[tracker] {what} failed: {ex.Message}");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        TryDispose(_windowOpenedHandler);
        TryDispose(_windowClosedHandler);
        if (_focusHandler is not null)
        {
            try { _automation.UnregisterFocusChangedEvent(_focusHandler); }
            catch { /* best-effort */ }
            TryDispose(_focusHandler);
        }
        _windowOpenedHandler = null;
        _windowClosedHandler = null;
        _focusHandler = null;

        EventRecorder[] toDispose;
        lock (_gate)
        {
            toDispose = _recorders.Values.ToArray();
            _recorders.Clear();
        }
        foreach (var r in toDispose)
        {
            r.Dispose();
        }
    }

    private static void TryDispose(IDisposable? d)
    {
        if (d is null) return;
        try { d.Dispose(); } catch { /* best-effort */ }
    }
}
