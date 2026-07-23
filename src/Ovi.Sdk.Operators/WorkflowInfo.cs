using System.Diagnostics.CodeAnalysis;

namespace Ovi.Sdk.Operators;

/// <summary>
/// Describes the workflow (and the specific run of it) an operator is executing in.
/// </summary>
public sealed record WorkflowInfo
{
    public WorkflowInfo()
    {
    }

    [SetsRequiredMembers]
    public WorkflowInfo(string workflowId, string runId, string? workflowName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        WorkflowId = workflowId;
        RunId = runId;
        WorkflowName = workflowName;
    }

    /// <summary>The stable identifier of the workflow definition.</summary>
    public required string WorkflowId { get; init; }

    /// <summary>The identifier of this particular run of the workflow.</summary>
    public required string RunId { get; init; }

    /// <summary>The workflow's display name, when known.</summary>
    public string? WorkflowName { get; init; }

    /// <summary>When this run started.</summary>
    public DateTimeOffset StartedAt { get; init; }
}
