using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UiWorkflowRunner.Workflow;

/// <summary>
/// Parses human-friendly duration strings used throughout the workflow YAML
/// (e.g. "500ms", "5s", "2m", "1h"). Plain numbers are interpreted as
/// milliseconds for convenience.
/// </summary>
internal static class DurationParser
{
    private static readonly Regex Pattern = new(
        @"^\s*(?<value>\d+(\.\d+)?)\s*(?<unit>ms|s|m|h)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static TimeSpan Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Duration value is empty.");
        }

        var m = Pattern.Match(text);
        if (!m.Success)
        {
            throw new FormatException($"Invalid duration '{text}'. Use values like '500ms', '5s', '2m', '1h'.");
        }

        var value = double.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture);
        var unit = m.Groups["unit"].Value.ToLowerInvariant();

        return unit switch
        {
            ""   => TimeSpan.FromMilliseconds(value),
            "ms" => TimeSpan.FromMilliseconds(value),
            "s"  => TimeSpan.FromSeconds(value),
            "m"  => TimeSpan.FromMinutes(value),
            "h"  => TimeSpan.FromHours(value),
            _    => throw new FormatException($"Unknown duration unit '{unit}'."),
        };
    }

    public static TimeSpan ParseOrDefault(string? text, TimeSpan fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : Parse(text);
}
