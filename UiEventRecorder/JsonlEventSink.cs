using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace UiEventRecorder;

/// <summary>
/// Thread-safe sink that serialises <see cref="RecordedEvent"/> instances as
/// JSON Lines into a file (and optionally echoes a one-line summary to the
/// console). A background drain task keeps UIA event callbacks fast and
/// non-blocking.
/// </summary>
internal sealed class JsonlEventSink : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly StreamWriter _writer;
    private readonly bool _echoToConsole;
    private readonly BlockingCollection<RecordedEvent> _queue = new(new ConcurrentQueue<RecordedEvent>());
    private readonly Task _drainTask;
    private readonly CancellationTokenSource _cts = new();
    private long _written;

    public JsonlEventSink(string outputPath, bool echoToConsole)
    {
        _echoToConsole = echoToConsole;

        var fullPath = Path.GetFullPath(outputPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        OutputPath = fullPath;
        _writer = new StreamWriter(new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = false,
        };

        _drainTask = Task.Factory.StartNew(Drain, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public string OutputPath { get; }

    public long WrittenCount => Interlocked.Read(ref _written);

    public void Enqueue(RecordedEvent evt)
    {
        if (_queue.IsAddingCompleted)
        {
            return;
        }

        try
        {
            _queue.Add(evt);
        }
        catch (InvalidOperationException)
        {
            // Queue completed between the check and the call; drop the event.
        }
    }

    private void Drain()
    {
        try
        {
            foreach (var evt in _queue.GetConsumingEnumerable(_cts.Token))
            {
                WriteOne(evt);
            }
        }
        catch (OperationCanceledException)
        {
            while (_queue.TryTake(out var evt))
            {
                WriteOne(evt);
            }
        }

        try
        {
            _writer.Flush();
        }
        catch
        {
            // Ignore flush failures during shutdown.
        }
    }

    private void WriteOne(RecordedEvent evt)
    {
        try
        {
            var json = JsonSerializer.Serialize(evt, JsonOptions);
            _writer.WriteLine(json);
            _writer.Flush();
            Interlocked.Increment(ref _written);

            if (_echoToConsole)
            {
                Console.WriteLine(SummariseForConsole(evt));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[recorder] failed to write event: {ex.Message}");
        }
    }

    private static string SummariseForConsole(RecordedEvent evt)
    {
        var id    = evt.AutomationId is { Length: > 0 } a ? a : evt.Name ?? "?";
        var ctrl  = evt.ControlType ?? "?";
        var proc  = evt.ProcessName ?? "?";
        var win   = evt.WindowTitle is { Length: > 0 } w ? Truncate(w, 28) : "-";
        var detail = evt.Property switch
        {
            null => evt.NewValue?.ToString() ?? string.Empty,
            _    => $"{evt.Property}: {Format(evt.OldValue)} -> {Format(evt.NewValue)}",
        };

        return $"{evt.Timestamp:HH:mm:ss.fff}  {proc,-14} [{win,-28}]  {evt.EventType,-18} {ctrl,-12} [{id}] {detail}".TrimEnd();

        static string Format(object? v) =>
            v switch
            {
                null     => "null",
                string s => $"\"{s}\"",
                _        => v.ToString() ?? string.Empty,
            };

        static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    public void Dispose()
    {
        try
        {
            _queue.CompleteAdding();
        }
        catch
        {
            // Already completed.
        }

        try
        {
            _cts.Cancel();
            _drainTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort.
        }

        try
        {
            _writer.Flush();
            _writer.Dispose();
        }
        catch
        {
            // Best-effort.
        }

        _cts.Dispose();
        _queue.Dispose();
    }
}
