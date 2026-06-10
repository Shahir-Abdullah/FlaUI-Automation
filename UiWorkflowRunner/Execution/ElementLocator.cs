using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using UiWorkflowRunner.Workflow;

namespace UiWorkflowRunner.Execution;

/// <summary>
/// Resolves a <see cref="TargetSpec"/> from a workflow step into an
/// <see cref="AutomationElement"/>. Supports three locator flavours:
///
///   1. Plain UIA properties (AutomationId / Name / ControlType / ClassName).
///   2. XPath (<c>FindFirstByXPath</c>) against the root window.
///   3. Grid cell selectors (<c>{ grid, row, column }</c>) via
///      <see cref="GridCellLocator"/>.
///
/// Lookups poll until the element appears or the supplied timeout elapses.
/// </summary>
internal sealed class ElementLocator
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

    private readonly AutomationElement _root;

    public ElementLocator(AutomationElement root)
    {
        _root = root;
    }

    /// <summary>
    /// Locates the element. Throws <see cref="InvalidOperationException"/>
    /// if it cannot be found within <paramref name="timeout"/>.
    /// </summary>
    public AutomationElement Require(TargetSpec? target, TimeSpan timeout)
    {
        if (target is null)
        {
            throw new InvalidOperationException("Step is missing a 'target' locator.");
        }

        var element = TryFind(target, timeout);
        if (element is null)
        {
            throw new InvalidOperationException(
                $"Could not locate element after {timeout.TotalSeconds:0.##}s. Locator: {Describe(target)}");
        }
        return element;
    }

    public AutomationElement? TryFind(TargetSpec target, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            try
            {
                var hit = FindOnce(target);
                if (hit is not null)
                {
                    return hit;
                }
            }
            catch (Exception)
            {
                // Swallow transient UIA failures and retry within the timeout
                // (target windows commonly rebuild parts of their tree).
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }
            Thread.Sleep(PollInterval);
        }
        while (true);
    }

    private AutomationElement? FindOnce(TargetSpec target)
    {
        // Grid cell takes priority because it composes other locators.
        if (!string.IsNullOrWhiteSpace(target.Grid))
        {
            return GridCellLocator.Find(_root, target);
        }

        if (!string.IsNullOrWhiteSpace(target.XPath))
        {
            return _root.FindFirstByXPath(target.XPath);
        }

        var conditions = BuildConditions(_root.ConditionFactory, target);
        if (conditions.Count == 0)
        {
            throw new InvalidOperationException(
                "Target locator is empty - specify at least one of: automationId, name, controlType, className, xpath, or grid.");
        }

        ConditionBase combined = conditions.Count == 1
            ? conditions[0]
            : new AndCondition(conditions.ToArray());

        return _root.FindFirstDescendant(combined);
    }

    internal static List<ConditionBase> BuildConditions(ConditionFactory cf, TargetSpec target)
    {
        var list = new List<ConditionBase>();

        if (!string.IsNullOrEmpty(target.AutomationId))
        {
            list.Add(cf.ByAutomationId(target.AutomationId));
        }
        if (!string.IsNullOrEmpty(target.Name))
        {
            list.Add(cf.ByName(target.Name));
        }
        if (!string.IsNullOrEmpty(target.ClassName))
        {
            list.Add(cf.ByClassName(target.ClassName));
        }
        if (!string.IsNullOrEmpty(target.ControlType))
        {
            list.Add(cf.ByControlType(ParseControlType(target.ControlType)));
        }

        return list;
    }

    internal static ControlType ParseControlType(string text)
    {
        if (Enum.TryParse<ControlType>(text, ignoreCase: true, out var ct))
        {
            return ct;
        }
        throw new InvalidOperationException(
            $"Unknown ControlType '{text}'. Use one of the values from FlaUI.Core.Definitions.ControlType (e.g. Button, Edit, CheckBox).");
    }

    internal static string Describe(TargetSpec t)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(t.AutomationId)) parts.Add($"automationId='{t.AutomationId}'");
        if (!string.IsNullOrEmpty(t.Name))         parts.Add($"name='{t.Name}'");
        if (!string.IsNullOrEmpty(t.ControlType))  parts.Add($"controlType='{t.ControlType}'");
        if (!string.IsNullOrEmpty(t.ClassName))    parts.Add($"className='{t.ClassName}'");
        if (!string.IsNullOrEmpty(t.XPath))        parts.Add($"xpath='{t.XPath}'");
        if (!string.IsNullOrEmpty(t.Grid))         parts.Add($"grid='{t.Grid}' row={t.Row} column={t.Column}");
        return parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
    }
}
