using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UiWorkflowRunner.Reporting;

internal sealed class RunReport
{
    public string? WorkflowName { get; set; }
    public string? WorkflowFile { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public int TotalSteps { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<StepRecord> Steps { get; } = new();

    public bool IsSuccess => Failed == 0;

    public void WriteTo(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        File.WriteAllText(full, JsonSerializer.Serialize(this, options));
    }
}

internal sealed class StepRecord
{
    public int Index { get; set; }
    public string? Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = "ok"; // ok | failed | skipped
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}
