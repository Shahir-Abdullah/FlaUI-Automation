using System.Collections.Generic;

namespace UiWorkflowRunner.Workflow;

/// <summary>
/// Root document deserialised from a workflow YAML file.
/// </summary>
public sealed class WorkflowDefinition
{
    public string? Name { get; set; }
    public string? Description { get; set; }

    public TargetConfig Target { get; set; } = new();
    public DefaultsConfig Defaults { get; set; } = new();
    public List<StepDefinition> Steps { get; set; } = new();
}

/// <summary>How to find (or start) the application the workflow drives.</summary>
public sealed class TargetConfig
{
    /// <summary>Process name without ".exe" (case-insensitive).</summary>
    public string? Process { get; set; }

    /// <summary>Optional case-insensitive substring filter on the window title.</summary>
    public string? WindowTitle { get; set; }

    /// <summary>If set, launch the target process when it is not already running.</summary>
    public StartIfNotRunningConfig? StartIfNotRunning { get; set; }
}

public sealed class StartIfNotRunningConfig
{
    public string Path { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }

    /// <summary>How long to wait for a main window after launch, e.g. "10s".</summary>
    public string? WaitForReady { get; set; }
}

/// <summary>Per-run defaults applied to every step unless overridden.</summary>
public sealed class DefaultsConfig
{
    /// <summary>Per-step timeout for finding an element. e.g. "5s".</summary>
    public string? Timeout { get; set; }

    /// <summary>How many times to retry a step on transient UIA errors.</summary>
    public int Retry { get; set; }

    /// <summary>Pause inserted after every step, e.g. "200ms".</summary>
    public string? PauseBetweenSteps { get; set; }
}

/// <summary>
/// A single step in the workflow. Many properties are action-specific and will
/// be ignored for actions that don't need them.
/// </summary>
public sealed class StepDefinition
{
    /// <summary>Optional human-friendly identifier (shown in logs / reports).</summary>
    public string? Id { get; set; }

    /// <summary>Required. Action keyword - see <see cref="Execution.StepActionRegistry"/>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Locator for the element this step operates on.</summary>
    public TargetSpec? Target { get; set; }

    /// <summary>Free-form value for setText / setCheck / selectComboItem / etc.</summary>
    public object? Value { get; set; }

    /// <summary>For waitForText / assert: the expected value.</summary>
    public object? Expected { get; set; }

    /// <summary>For assert: which property to compare (default: Name).</summary>
    public string? Property { get; set; }

    /// <summary>Per-step timeout override, e.g. "3s".</summary>
    public string? Timeout { get; set; }

    /// <summary>For sleep: duration string, e.g. "750ms".</summary>
    public string? Duration { get; set; }

    /// <summary>For keys: raw keyboard string accepted by FlaUI's keyboard helper.</summary>
    public string? Keys { get; set; }

    /// <summary>For screenshot: output PNG path (relative to the working dir).</summary>
    public string? File { get; set; }
}

/// <summary>
/// Element locator. All fields are optional; the locator combines whichever
/// fields are set. Three locator flavours are supported:
///
///   1. Plain UIA properties (AutomationId / Name / ControlType / ClassName).
///   2. XPath via <see cref="XPath"/> - evaluated against the target window.
///   3. Grid cell via (Grid + Row + Column).
/// </summary>
public sealed class TargetSpec
{
    public string? AutomationId { get; set; }
    public string? Name { get; set; }
    public string? ControlType { get; set; }
    public string? ClassName { get; set; }

    public string? XPath { get; set; }

    /// <summary>AutomationId of the grid (DataGrid / list / table).</summary>
    public string? Grid { get; set; }

    /// <summary>
    /// Row selector. Accepts an int index, the strings "first" / "last",
    /// or a mapping <c>{ columnEquals: { ColumnHeader: "value" } }</c>.
    /// </summary>
    public object? Row { get; set; }

    /// <summary>Column selector. Accepts an int index or the column header text.</summary>
    public object? Column { get; set; }
}
