namespace UiEventRecorder;

/// <summary>
/// Lightweight identification of the top-level window an
/// <see cref="EventRecorder"/> is observing. Captured once at attach time and
/// stamped onto every event the recorder emits.
/// </summary>
internal sealed record WindowContext(int ProcessId, string ProcessName, string WindowTitle);
