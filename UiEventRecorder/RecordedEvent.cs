using System;
using System.Collections.Generic;

namespace UiEventRecorder;

/// <summary>
/// A single observed UI Automation occurrence. Serialised as one JSON object
/// per line into the recorder's output file.
/// </summary>
internal sealed class RecordedEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// High-level category: "Invoke", "TextChanged", "SelectionChanged",
    /// "PropertyChanged", "StructureChanged", "FocusChanged", "WindowOpened", etc.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Name of the OS process the event came from (e.g. "ms-teams").</summary>
    public string? ProcessName { get; init; }

    /// <summary>OS process id the event came from.</summary>
    public int? ProcessId { get; init; }

    /// <summary>Title of the top-level window the event came from.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>UIA AutomationId of the element the event was raised on (if any).</summary>
    public string? AutomationId { get; init; }

    /// <summary>UIA Name of the element (button text, label text, etc.).</summary>
    public string? Name { get; init; }

    /// <summary>UIA ControlType of the element (Button, Edit, CheckBox, ...).</summary>
    public string? ControlType { get; init; }

    /// <summary>UIA ClassName of the element (e.g. "TextBox", "Button").</summary>
    public string? ClassName { get; init; }

    /// <summary>For property-changed events: the property that changed.</summary>
    public string? Property { get; init; }

    /// <summary>For property-changed events: the previous value (best effort).</summary>
    public object? OldValue { get; init; }

    /// <summary>
    /// For property-changed events: the new value. For other events, may carry
    /// the most relevant payload (e.g. structure-change type).
    /// </summary>
    public object? NewValue { get; init; }

    /// <summary>Free-form extra context per event type.</summary>
    public Dictionary<string, object?>? Extra { get; init; }
}
