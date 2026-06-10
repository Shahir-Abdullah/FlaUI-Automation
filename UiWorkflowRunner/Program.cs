using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using UiWorkflowRunner.Execution;
using UiWorkflowRunner.Workflow;

namespace UiWorkflowRunner;

internal static class Program
{
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options is null)
        {
            PrintUsage();
            return 1;
        }

        Console.WriteLine("UI Workflow Runner");
        Console.WriteLine($"  workflow : {Path.GetFullPath(options.WorkflowFile)}");
        Console.WriteLine($"  dry-run  : {options.DryRun}");
        Console.WriteLine($"  report   : {(options.ReportPath is null ? "(none)" : Path.GetFullPath(options.ReportPath))}");
        Console.WriteLine();

        WorkflowDefinition workflow;
        try
        {
            workflow = WorkflowLoader.Load(options.WorkflowFile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load workflow: {ex.Message}");
            return 2;
        }

        UIA3Automation? automation = null;

        try
        {
            automation = new UIA3Automation();

            var targetWindow = TargetWindowResolver.Resolve(automation, workflow.Target);

            var runner = new WorkflowRunner(
                workflow,
                automation,
                targetWindow,
                dryRun: options.DryRun,
                continueOnError: options.ContinueOnError,
                verbose: options.Verbose);

            var report = runner.Run(options.WorkflowFile);

            if (options.ReportPath is not null)
            {
                report.WriteTo(options.ReportPath);
                Console.WriteLine($"Report written to '{Path.GetFullPath(options.ReportPath)}'.");
            }

            return report.IsSuccess ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Runner failed: {ex.Message}");
            return 4;
        }
        finally
        {
            automation?.Dispose();
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: UiWorkflowRunner --file <workflow.yaml> [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  --file    <path>   YAML workflow definition (required).");
        Console.Error.WriteLine("  --dry-run          Resolve every target locator but skip the actual actions.");
        Console.Error.WriteLine("  --report  <path>   Write a JSON run report to this path.");
        Console.Error.WriteLine("  --verbose          Print per-step locator details.");
        Console.Error.WriteLine("  --continue-on-error  Keep running after a step fails (default aborts).");
    }

    private sealed record Options(
        string WorkflowFile,
        bool DryRun,
        string? ReportPath,
        bool Verbose,
        bool ContinueOnError)
    {
        public static Options? Parse(string[] args)
        {
            string? file = null;
            string? report = null;
            bool dry = false, verbose = false, cont = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--file":
                    case "-f":
                        if (++i >= args.Length) return null;
                        file = args[i];
                        break;

                    case "--report":
                    case "-r":
                        if (++i >= args.Length) return null;
                        report = args[i];
                        break;

                    case "--dry-run":
                        dry = true;
                        break;

                    case "--verbose":
                    case "-v":
                        verbose = true;
                        break;

                    case "--continue-on-error":
                        cont = true;
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

            if (string.IsNullOrWhiteSpace(file))
            {
                Console.Error.WriteLine("Missing required --file argument.");
                return null;
            }

            return new Options(file, dry, report, verbose, cont);
        }
    }
}

/// <summary>
/// Locates (or launches and locates) the target window for a workflow.
/// </summary>
internal static class TargetWindowResolver
{
    public static AutomationElement Resolve(UIA3Automation automation, TargetConfig target)
    {
        var processName = target.Process
            ?? throw new InvalidOperationException("target.process must be specified.");

        var process = FindProcessWithWindow(processName);
        if (process is null && target.StartIfNotRunning is not null)
        {
            process = Launch(target.StartIfNotRunning, processName);
        }
        if (process is null)
        {
            throw new InvalidOperationException(
                $"No running process named '{processName}' has a visible window. Start it first or configure 'startIfNotRunning'.");
        }

        Console.WriteLine($"Target process: {process.ProcessName} (pid {process.Id})");

        var window = FindWindow(automation, process.Id, target.WindowTitle,
            timeout: TimeSpan.FromSeconds(10));
        if (window is null)
        {
            throw new InvalidOperationException(
                $"Could not find a window for '{processName}'" +
                (string.IsNullOrEmpty(target.WindowTitle) ? "." : $" with title containing '{target.WindowTitle}'."));
        }

        Console.WriteLine($"Target window : \"{Safe(() => window.Name)}\" (AutomationId='{Safe(() => window.AutomationId)}')");
        Console.WriteLine();
        return window;
    }

    private static Process? FindProcessWithWindow(string name)
    {
        return Process
            .GetProcessesByName(name)
            .Where(p =>
            {
                try { return !p.HasExited && p.MainWindowHandle != IntPtr.Zero; }
                catch { return false; }
            })
            .OrderBy(p => SafeStartTime(p))
            .FirstOrDefault();
    }

    private static DateTime SafeStartTime(Process p)
    {
        try { return p.StartTime; }
        catch { return DateTime.MaxValue; }
    }

    private static Process Launch(StartIfNotRunningConfig spec, string processName)
    {
        if (string.IsNullOrWhiteSpace(spec.Path))
        {
            throw new InvalidOperationException("target.startIfNotRunning.path is required when the process isn't already running.");
        }

        var info = new ProcessStartInfo
        {
            FileName        = Path.GetFullPath(spec.Path),
            Arguments       = spec.Arguments ?? string.Empty,
            UseShellExecute = true,
        };
        if (!string.IsNullOrWhiteSpace(spec.WorkingDirectory))
        {
            info.WorkingDirectory = Path.GetFullPath(spec.WorkingDirectory);
        }

        Console.WriteLine($"Launching '{info.FileName}'...");
        var started = Process.Start(info)
            ?? throw new InvalidOperationException($"Failed to launch '{info.FileName}'.");

        var wait = DurationParser.ParseOrDefault(spec.WaitForReady, TimeSpan.FromSeconds(10));
        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                started.Refresh();
                if (started.MainWindowHandle != IntPtr.Zero) return started;
            }
            catch
            {
                // Sometimes the launcher proxy exits and the real process is
                // a sibling. Re-scan by name as a fallback.
                var sibling = FindProcessWithWindow(processName);
                if (sibling is not null) return sibling;
            }
            Thread.Sleep(200);
        }

        throw new InvalidOperationException($"Process '{processName}' started but never produced a visible window within {wait}.");
    }

    private static AutomationElement? FindWindow(
        UIA3Automation automation, int pid, string? titleSubstring, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var desktop  = automation.GetDesktop();

        while (DateTime.UtcNow < deadline)
        {
            AutomationElement[] children;
            try { children = desktop.FindAllChildren(); }
            catch { children = Array.Empty<AutomationElement>(); }

            foreach (var w in children)
            {
                int wpid;
                try { wpid = w.Properties.ProcessId.ValueOrDefault; }
                catch { continue; }
                if (wpid != pid) continue;

                if (!string.IsNullOrEmpty(titleSubstring))
                {
                    var name = Safe(() => w.Name) ?? string.Empty;
                    if (name.IndexOf(titleSubstring, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }
                return w;
            }
            Thread.Sleep(200);
        }
        return null;
    }

    private static string? Safe(Func<string?> f)
    {
        try { return f(); } catch { return null; }
    }
}
