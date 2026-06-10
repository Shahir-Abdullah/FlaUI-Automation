using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using UiWorkflowRunner.Workflow;

namespace UiWorkflowRunner.Execution;

internal interface IStepAction
{
    string Name { get; }
    void Execute(StepContext ctx, StepDefinition step);
}

/// <summary>
/// Registry of every action keyword accepted in a workflow YAML file.
/// </summary>
internal static class StepActionRegistry
{
    private static readonly Dictionary<string, IStepAction> Map =
        new(StringComparer.OrdinalIgnoreCase);

    static StepActionRegistry()
    {
        Register(new ClickAction());
        Register(new MouseClickAction());
        Register(new SetTextAction());
        Register(new AppendTextAction());
        Register(new ClearTextAction());
        Register(new SelectComboItemAction());
        Register(new SetCheckAction());
        Register(new FocusAction());
        Register(new KeysAction());
        Register(new WaitForAction());
        Register(new WaitForTextAction());
        Register(new AssertAction());
        Register(new SleepAction());
        Register(new ScreenshotAction());
    }

    private static void Register(IStepAction action) => Map[action.Name] = action;

    public static IStepAction Resolve(string actionName)
    {
        if (Map.TryGetValue(actionName, out var action))
        {
            return action;
        }
        throw new InvalidOperationException(
            $"Unknown action '{actionName}'. Valid actions: {string.Join(", ", Map.Keys)}.");
    }
}

// =====================================================================
// Click / mouse / focus
// =====================================================================

internal sealed class ClickAction : IStepAction
{
    public string Name => "click";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
        element = ResolveActionableElement(element);

        var invoke = element.Patterns.Invoke.PatternOrDefault;
        if (invoke is not null)
        {
            invoke.Invoke();
            return;
        }

        var selectionItem = element.Patterns.SelectionItem.PatternOrDefault;
        if (selectionItem is not null)
        {
            selectionItem.Select();
            return;
        }

        // Final fallback: real mouse click. Useful for elements that don't
        // implement InvokePattern (some WinForms / custom controls).
        element.Click();
    }

    /// <summary>
    /// If the located element is a container (e.g. a DataGridCell that wraps
    /// a Button), drill in to the first invokable descendant.
    /// </summary>
    private static AutomationElement ResolveActionableElement(AutomationElement element)
    {
        if (element.Patterns.Invoke.IsSupported || element.Patterns.SelectionItem.IsSupported)
        {
            return element;
        }
        var invokable = element.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
        return invokable ?? element;
    }
}

internal sealed class MouseClickAction : IStepAction
{
    public string Name => "mouseClick";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
        var rect    = element.BoundingRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new InvalidOperationException(
                $"Element has no clickable bounding rectangle. Locator: {ElementLocator.Describe(step.Target!)}");
        }

        var centre = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        Mouse.LeftClick(centre);
    }
}

internal sealed class FocusAction : IStepAction
{
    public string Name => "focus";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
        element.Focus();
    }
}

// =====================================================================
// Text input
// =====================================================================

internal sealed class SetTextAction : IStepAction
{
    public string Name => "setText";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
        var text    = step.Value?.ToString() ?? string.Empty;

        // Prefer ValuePattern: writes directly through UIA without keystrokes.
        var value = element.Patterns.Value.PatternOrDefault;
        if (value is not null && !value.IsReadOnly.ValueOrDefault)
        {
            value.SetValue(text);
            return;
        }

        // Fallback to the keyboard via the TextBox wrapper (handles
        // focus + select-all + type).
        element.AsTextBox().Enter(text);
    }
}

internal sealed class AppendTextAction : IStepAction
{
    public string Name => "appendText";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
        var text    = step.Value?.ToString() ?? string.Empty;

        element.Focus();
        Wait.UntilInputIsProcessed(null);
        Keyboard.Press(VirtualKeyShort.END);
        Keyboard.Release(VirtualKeyShort.END);
        Keyboard.Type(text);
    }
}

internal sealed class ClearTextAction : IStepAction
{
    public string Name => "clearText";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));

        var value = element.Patterns.Value.PatternOrDefault;
        if (value is not null && !value.IsReadOnly.ValueOrDefault)
        {
            value.SetValue(string.Empty);
            return;
        }

        element.AsTextBox().Enter(string.Empty);
    }
}

// =====================================================================
// Combo / checkbox / toggle
// =====================================================================

internal sealed class SelectComboItemAction : IStepAction
{
    public string Name => "selectComboItem";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
        var combo   = element.AsComboBox();

        switch (step.Value)
        {
            case null:
                throw new InvalidOperationException("selectComboItem requires a 'value' (item text or zero-based index).");
            case int i:
                combo.Select(i);
                return;
            case long l:
                combo.Select(checked((int)l));
                return;
            default:
                var text = step.Value.ToString() ?? string.Empty;
                if (int.TryParse(text, out var index))
                {
                    combo.Select(index);
                }
                else
                {
                    combo.Select(text);
                }
                return;
        }
    }
}

internal sealed class SetCheckAction : IStepAction
{
    public string Name => "setCheck";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
        var desired = ParseBool(step.Value);

        // Grid cells are containers - look inside for the actual checkbox.
        var checkboxHost = element;
        if (!element.Patterns.Toggle.IsSupported)
        {
            var inner = element.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.CheckBox));
            if (inner is not null)
            {
                checkboxHost = inner;
            }
        }

        var checkBox = checkboxHost.AsCheckBox();
        checkBox.IsChecked = desired;
    }

    private static bool ParseBool(object? value)
    {
        return value switch
        {
            null      => throw new InvalidOperationException("setCheck requires a boolean 'value'."),
            bool b    => b,
            int i     => i != 0,
            long l    => l != 0,
            string s  => bool.TryParse(s, out var b2)
                            ? b2
                            : throw new InvalidOperationException($"setCheck 'value' must be true/false, got '{s}'."),
            _         => throw new InvalidOperationException(
                            $"setCheck 'value' must be a bool, got {value.GetType().Name}."),
        };
    }
}

// =====================================================================
// Keyboard
// =====================================================================

internal sealed class KeysAction : IStepAction
{
    public string Name => "keys";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var text = step.Keys ?? step.Value?.ToString();
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException("keys action requires a 'keys' (or 'value') field with the text to type.");
        }

        if (step.Target is not null)
        {
            var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
            element.Focus();
            Wait.UntilInputIsProcessed(null);
        }

        Keyboard.Type(text);
    }
}

// =====================================================================
// Waits / assertions / sleeps
// =====================================================================

internal sealed class WaitForAction : IStepAction
{
    public string Name => "waitFor";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
        // Optionally also wait until enabled.
        try
        {
            element.WaitUntilEnabled(ctx.ResolveTimeout(step));
        }
        catch
        {
            // Not all elements report IsEnabled; ignore.
        }
    }
}

internal sealed class WaitForTextAction : IStepAction
{
    public string Name => "waitForText";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var expected = step.Expected?.ToString() ?? step.Value?.ToString();
        if (expected is null)
        {
            throw new InvalidOperationException("waitForText requires an 'expected' (or 'value') field.");
        }

        var timeout  = ctx.ResolveTimeout(step);
        var deadline = DateTime.UtcNow + timeout;

        do
        {
            var element = ctx.Locator.TryFind(step.Target!, TimeSpan.FromMilliseconds(250));
            if (element is not null)
            {
                var actual = ReadElementText(element);
                if (string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    return;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException(
                    $"Timed out after {timeout.TotalSeconds:0.##}s waiting for text '{expected}' on {ElementLocator.Describe(step.Target!)}.");
            }
            Thread.Sleep(150);
        }
        while (true);
    }

    private static string ReadElementText(AutomationElement element)
    {
        var valuePattern = element.Patterns.Value.PatternOrDefault;
        if (valuePattern is not null)
        {
            try { return valuePattern.Value.ValueOrDefault ?? string.Empty; }
            catch { /* fall through */ }
        }
        try { return element.Name ?? string.Empty; }
        catch { return string.Empty; }
    }
}

internal sealed class AssertAction : IStepAction
{
    public string Name => "assert";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var element  = ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));
        var expected = (step.Expected ?? step.Value)?.ToString() ?? string.Empty;
        var property = (step.Property ?? "name").ToLowerInvariant();

        string actual = property switch
        {
            "name"         => SafeGet(() => element.Name),
            "value"        => SafeGet(() => element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault ?? string.Empty),
            "automationid" => SafeGet(() => element.AutomationId),
            "classname"    => SafeGet(() => element.ClassName),
            "ischecked"    => SafeGet(() => element.AsCheckBox().IsChecked?.ToString() ?? "null"),
            "isenabled"    => SafeGet(() => element.IsEnabled.ToString()),
            _              => throw new InvalidOperationException(
                                  $"Unknown 'property' '{step.Property}'. Supported: name, value, automationId, className, isChecked, isEnabled."),
        };

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Assertion failed: {property} of {ElementLocator.Describe(step.Target!)} was '{actual}', expected '{expected}'.");
        }
    }

    private static string SafeGet(Func<string?> getter)
    {
        try { return getter() ?? string.Empty; }
        catch { return string.Empty; }
    }
}

internal sealed class SleepAction : IStepAction
{
    public string Name => "sleep";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        var duration = step.Duration ?? step.Value?.ToString();
        if (string.IsNullOrWhiteSpace(duration))
        {
            throw new InvalidOperationException("sleep action requires a 'duration' field (e.g. '500ms').");
        }
        Thread.Sleep(DurationParser.Parse(duration));
    }
}

// =====================================================================
// Screenshot
// =====================================================================

internal sealed class ScreenshotAction : IStepAction
{
    public string Name => "screenshot";

    public void Execute(StepContext ctx, StepDefinition step)
    {
        if (string.IsNullOrWhiteSpace(step.File))
        {
            throw new InvalidOperationException("screenshot action requires a 'file' field with the output PNG path.");
        }

        var element = step.Target is null
            ? ctx.RootWindow
            : ctx.Locator.Require(step.Target, ctx.ResolveTimeout(step));

        var path = Path.GetFullPath(step.File);
        var dir  = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var capture = Capture.Element(element, null);
        capture.ToFile(path);
    }
}
