using System;
using System.Diagnostics;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using UiWorkflowRunner.Reporting;
using UiWorkflowRunner.Workflow;

namespace UiWorkflowRunner.Execution;

/// <summary>
/// Executes the steps of a <see cref="WorkflowDefinition"/> against a live
/// target window. Each step is retried per <c>defaults.retry</c> on transient
/// UIA failures; persistent failures abort the run unless
/// <paramref name="continueOnError"/> is set.
/// </summary>
internal sealed class WorkflowRunner
{
    private readonly WorkflowDefinition _workflow;
    private readonly UIA3Automation _automation;
    private readonly AutomationElement _rootWindow;
    private readonly bool _dryRun;
    private readonly bool _continueOnError;
    private readonly bool _verbose;

    public WorkflowRunner(
        WorkflowDefinition workflow,
        UIA3Automation automation,
        AutomationElement rootWindow,
        bool dryRun,
        bool continueOnError,
        bool verbose)
    {
        _workflow        = workflow;
        _automation      = automation;
        _rootWindow      = rootWindow;
        _dryRun          = dryRun;
        _continueOnError = continueOnError;
        _verbose         = verbose;
    }

    public RunReport Run(string workflowFilePath)
    {
        var defaultTimeout    = DurationParser.ParseOrDefault(_workflow.Defaults.Timeout, TimeSpan.FromSeconds(5));
        var pauseBetweenSteps = DurationParser.ParseOrDefault(_workflow.Defaults.PauseBetweenSteps, TimeSpan.FromMilliseconds(150));
        var retryCount        = Math.Max(0, _workflow.Defaults.Retry);

        var ctx = new StepContext(
            _automation,
            _rootWindow,
            new ElementLocator(_rootWindow),
            defaultTimeout,
            retryCount,
            pauseBetweenSteps,
            _verbose);

        var report = new RunReport
        {
            WorkflowName = _workflow.Name,
            WorkflowFile = workflowFilePath,
            TotalSteps   = _workflow.Steps.Count,
        };

        Console.WriteLine($"Workflow: {_workflow.Name ?? "(unnamed)"}    steps: {_workflow.Steps.Count}");
        if (_dryRun)
        {
            Console.WriteLine("(dry-run: locators will be resolved but no actions will be performed)");
        }
        Console.WriteLine();

        for (int i = 0; i < _workflow.Steps.Count; i++)
        {
            var step      = _workflow.Steps[i];
            var label     = step.Id is { Length: > 0 } id ? $"[{id}]" : $"#{i + 1}";
            var stepRec   = new StepRecord { Index = i, Id = step.Id, Action = step.Action };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                ExecuteWithRetries(ctx, step, retryCount);
                stopwatch.Stop();
                stepRec.Status     = "ok";
                stepRec.DurationMs = stopwatch.ElapsedMilliseconds;
                report.Succeeded++;
                Console.WriteLine($"  ok    {label,-8} {step.Action,-18} ({stopwatch.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                stepRec.Status     = "failed";
                stepRec.DurationMs = stopwatch.ElapsedMilliseconds;
                stepRec.Error      = ex.Message;
                report.Failed++;
                Console.Error.WriteLine($"  FAIL  {label,-8} {step.Action,-18} ({stopwatch.ElapsedMilliseconds} ms): {ex.Message}");

                if (!_continueOnError)
                {
                    report.Steps.Add(stepRec);
                    SkipRemaining(report, i + 1);
                    break;
                }
            }
            finally
            {
                report.Steps.Add(stepRec);
            }

            if (pauseBetweenSteps > TimeSpan.Zero)
            {
                Thread.Sleep(pauseBetweenSteps);
            }
        }

        report.FinishedAt = DateTimeOffset.Now;
        Console.WriteLine();
        Console.WriteLine(
            $"Done: {report.Succeeded} ok, {report.Failed} failed, {report.Skipped} skipped " +
            $"({(report.FinishedAt - report.StartedAt).TotalSeconds:0.##}s).");
        return report;
    }

    private void ExecuteWithRetries(StepContext ctx, StepDefinition step, int retryCount)
    {
        var action = StepActionRegistry.Resolve(step.Action);

        if (_dryRun)
        {
            // For dry-run we still resolve any target locator so the user
            // gets immediate feedback that the YAML is wired up correctly,
            // but we never actually perform the side-effect.
            if (step.Target is not null)
            {
                _ = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
            }
            return;
        }

        Exception? last = null;
        for (int attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                action.Execute(ctx, step);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt == retryCount)
                {
                    break;
                }
                Thread.Sleep(TimeSpan.FromMilliseconds(250));
            }
        }
        throw last ?? new InvalidOperationException("Step failed for unknown reasons.");
    }

    private static void SkipRemaining(RunReport report, int startIndex)
    {
        var totalToSkip = report.TotalSteps - startIndex;
        if (totalToSkip <= 0) return;
        report.Skipped += totalToSkip;
        Console.WriteLine($"  Aborting: {totalToSkip} subsequent step(s) skipped (use --continue-on-error to keep going).");
    }
}
