using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.UIA3;

namespace UiEventRecorder;

internal static class Program
{
    private static readonly string[] DefaultProcessNames = { "DemoApp" };
    private const string DefaultOutputFile = "events.jsonl";

    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options is null)
        {
            PrintUsage();
            return 1;
        }

        Console.WriteLine("UI Event Recorder");
        Console.WriteLine($"  processes  : {string.Join(", ", options.ProcessNames)}");
        Console.WriteLine($"  output     : {Path.GetFullPath(options.OutputPath)}");
        Console.WriteLine($"  echo       : {options.EchoToConsole}");
        Console.WriteLine();

        UIA3Automation? automation = null;
        JsonlEventSink? sink = null;
        WindowTracker? tracker = null;

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            automation = new UIA3Automation();
            sink       = new JsonlEventSink(options.OutputPath, options.EchoToConsole);
            tracker    = new WindowTracker(automation, sink, options.ProcessNames);
            tracker.Start();

            ReportInitialState(tracker, options);
            Console.WriteLine("Tracking - press Ctrl+C to stop.");
            Console.WriteLine();

            shutdown.Token.WaitHandle.WaitOne();

            Console.WriteLine();
            Console.WriteLine("Stop requested.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Recorder failed: {ex.Message}");
            return 2;
        }
        finally
        {
            tracker?.Dispose();
            sink?.Dispose();
            automation?.Dispose();
        }

        Console.WriteLine($"Wrote {sink?.WrittenCount ?? 0} events to '{sink?.OutputPath}'.");
        return 0;
    }

    // ------------------------------------------------------------------ helpers

    private static void ReportInitialState(WindowTracker tracker, Options options)
    {
        var tracked = tracker.TrackedWindows;
        if (tracked.Count == 0)
        {
            var filter = options.ProcessNames.Count == 0
                ? "any process"
                : string.Join(", ", options.ProcessNames);
            Console.WriteLine(
                $"No matching windows are open yet ({filter}). " +
                "Start the target application now - the recorder will attach automatically.");
        }
        else
        {
            Console.WriteLine($"Currently tracking {tracked.Count} window(s):");
            foreach (var ctx in tracked)
            {
                Console.WriteLine($"  - {ctx.ProcessName} (pid {ctx.ProcessId}) \"{ctx.WindowTitle}\"");
            }
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: UiEventRecorder [--process <name>] [--output <path>] [--quiet]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  --process <name>   Process name(s) to attach to (no .exe). Repeatable, and");
        Console.Error.WriteLine("                     comma-separated within one value is also accepted.");
        Console.Error.WriteLine("                     Defaults to 'DemoApp' if omitted.");
        Console.Error.WriteLine("                     Examples:");
        Console.Error.WriteLine("                       --process ms-teams");
        Console.Error.WriteLine("                       --process ms-teams --process outlook");
        Console.Error.WriteLine("                       --process ms-teams,outlook,winword");
        Console.Error.WriteLine("  --output  <path>   Path of the JSONL log file (default: events.jsonl).");
        Console.Error.WriteLine("  --quiet            Don't echo a one-line summary of each event to the console.");
    }

    private sealed record Options(IReadOnlyList<string> ProcessNames, string OutputPath, bool EchoToConsole)
    {
        public static Options? Parse(string[] args)
        {
            var processes = new List<string>();
            var outputPath = DefaultOutputFile;
            var echo = true;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--process":
                    case "-p":
                        if (++i >= args.Length) return null;
                        processes.AddRange(SplitProcessList(args[i]));
                        break;

                    case "--output":
                    case "-o":
                        if (++i >= args.Length) return null;
                        outputPath = args[i];
                        break;

                    case "--quiet":
                    case "-q":
                        echo = false;
                        break;

                    case "--help":
                    case "-h":
                    case "/?":
                        return null;

                    default:
                        Console.Error.WriteLine($"Unrecognised argument: {args[i]}");
                        return null;
                }
            }

            if (processes.Count == 0)
            {
                processes.AddRange(DefaultProcessNames);
            }

            // Dedupe while preserving order.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = processes.Where(seen.Add).ToList();

            return new Options(deduped, outputPath, echo);
        }

        private static IEnumerable<string> SplitProcessList(string value) =>
            value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
