using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UiWorkflowRunner.Workflow;

/// <summary>
/// Loads a <see cref="WorkflowDefinition"/> from a YAML file.
/// </summary>
internal static class WorkflowLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static WorkflowDefinition Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Workflow file not found: '{path}'.", path);
        }

        var yaml = File.ReadAllText(path);
        var workflow = Deserializer.Deserialize<WorkflowDefinition>(yaml)
            ?? throw new System.InvalidOperationException($"Workflow file '{path}' is empty.");

        Validate(workflow, path);
        return workflow;
    }

    private static void Validate(WorkflowDefinition wf, string path)
    {
        if (string.IsNullOrWhiteSpace(wf.Target.Process))
        {
            throw new System.InvalidOperationException(
                $"{path}: 'target.process' must be set (the executable name without '.exe').");
        }

        if (wf.Steps is null || wf.Steps.Count == 0)
        {
            throw new System.InvalidOperationException($"{path}: workflow has no steps.");
        }

        for (int i = 0; i < wf.Steps.Count; i++)
        {
            var step = wf.Steps[i];
            if (string.IsNullOrWhiteSpace(step.Action))
            {
                throw new System.InvalidOperationException(
                    $"{path}: step #{i + 1} is missing the required 'action' field.");
            }
        }
    }
}
