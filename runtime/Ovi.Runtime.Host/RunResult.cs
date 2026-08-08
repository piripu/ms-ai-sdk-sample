using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host;

/// <summary>The one response shape every <see cref="WorkflowHost.RunAsync"/> call produces.</summary>
public sealed record RunResult
{
    /// <summary>The run's outcome: the sink node's output on success, or the <see cref="Error"/> that stopped the run.</summary>
    public required Result<object?> Output { get; init; }

    /// <summary>Timing and correlation data collected while the run executed.</summary>
    public required RunDiagnostics Diagnostics { get; init; }
}
