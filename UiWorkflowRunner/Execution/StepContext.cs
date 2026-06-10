using System;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using UiWorkflowRunner.Workflow;

namespace UiWorkflowRunner.Execution;

/// <summary>
/// Shared state passed to every action during a workflow run. Owns the live
/// automation handle plus the resolved per-run defaults so individual
/// actions don't have to deal with the workflow document directly.
/// </summary>
internal sealed class StepContext
{
    public StepContext(
        UIA3Automation automation,
        AutomationElement rootWindow,
        ElementLocator locator,
        TimeSpan defaultTimeout,
        int defaultRetry,
        TimeSpan pauseBetweenSteps,
        bool verbose)
    {
        Automation        = automation;
        RootWindow        = rootWindow;
        Locator           = locator;
        DefaultTimeout    = defaultTimeout;
        DefaultRetry      = defaultRetry;
        PauseBetweenSteps = pauseBetweenSteps;
        Verbose           = verbose;
    }

    public UIA3Automation Automation { get; }
    public AutomationElement RootWindow { get; }
    public ElementLocator Locator { get; }

    public TimeSpan DefaultTimeout { get; }
    public int DefaultRetry { get; }
    public TimeSpan PauseBetweenSteps { get; }
    public bool Verbose { get; }

    public TimeSpan ResolveTimeout(StepDefinition step) =>
        DurationParser.ParseOrDefault(step.Timeout, DefaultTimeout);
}
