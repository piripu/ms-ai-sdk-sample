namespace Ovi.Runtime.Host;

/// <summary>
/// Timing and correlation data collected while a run executed. Populated by the default
/// <c>TimingRunMiddleware</c>/<c>TimingNodeMiddleware</c> — see
/// <see cref="Middleware.TimingRunMiddleware"/> and <see cref="Middleware.TimingNodeMiddleware"/>.
/// </summary>
public sealed record RunDiagnostics
{
    /// <summary>When the run started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Total wall-clock duration of the run, including middleware.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Per-node execution duration, keyed by the node's workflow instance key.</summary>
    public IReadOnlyDictionary<string, TimeSpan> NodeDurations { get; init; } =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal);

    /// <summary>Echoes <see cref="RunRequest.CorrelationId"/>, when the caller supplied one.</summary>
    public string? CorrelationId { get; init; }
}
