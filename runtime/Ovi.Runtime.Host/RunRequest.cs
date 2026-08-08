using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Ovi.Runtime.Host;

/// <summary>
/// The one request shape <see cref="WorkflowHost.RunAsync"/> accepts, regardless of what triggered
/// it — a manual call, a normalized webhook, or a schedule tick. Carrying <see cref="Prompt"/>/
/// <see cref="Messages"/> and <see cref="State"/> on a single type is what makes a single entry
/// point possible: callers stop branching on trigger type before they call in.
/// </summary>
public sealed record RunRequest
{
    /// <summary>What triggered this run.</summary>
    public TriggerKind TriggerKind { get; init; } = TriggerKind.Manual;

    /// <summary>
    /// The payload handed to the workflow's entry node when it is <i>not</i> agent-shaped (see
    /// <see cref="Prompt"/>/<see cref="Messages"/> for the agent case). Left <see langword="null"/>
    /// for agent-wrapped hosts.
    /// </summary>
    public object? Input { get; init; }

    /// <summary>A plain-text prompt, for hosts running an agent (wired into <see cref="Ovi.Sdk.Agents.AgentChatFeature"/>).</summary>
    public string? Prompt { get; init; }

    /// <summary>Fully formed chat messages, for hosts running an agent. Combined with <see cref="Prompt"/> the same way <c>AgentRequest</c> combines them.</summary>
    public IReadOnlyList<ChatMessage>? Messages { get; init; }

    /// <summary>Additional state merged into the run's <c>WorkflowState</c> before execution starts.</summary>
    public JsonObject? State { get; init; }

    /// <summary>Free-form metadata that flows through to <see cref="RunDiagnostics"/> and middleware; not interpreted by the host.</summary>
    public JsonObject? Metadata { get; init; }

    /// <summary>Caller-supplied id for correlating a request with its logs/diagnostics.</summary>
    public string? CorrelationId { get; init; }

    public static implicit operator RunRequest(string prompt) => new() { Prompt = prompt };
}
