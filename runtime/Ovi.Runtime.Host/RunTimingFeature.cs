namespace Ovi.Runtime.Host;

/// <summary>
/// Accumulates the timing data a run's <see cref="RunDiagnostics"/> is built from. Attached by
/// <see cref="WorkflowHost.RunAsync"/> alongside <see cref="RunIoFeature"/>; written by
/// <see cref="Middleware.TimingRunMiddleware"/> (<see cref="Duration"/>) and
/// <see cref="Middleware.TimingNodeMiddleware"/> (<see cref="NodeDurations"/>). Execution within a
/// single run is sequential, so no synchronization is needed here.
/// </summary>
public sealed class RunTimingFeature
{
    public DateTimeOffset StartedAt { get; set; }

    public TimeSpan Duration { get; set; }

    public Dictionary<string, TimeSpan> NodeDurations { get; } = new(StringComparer.Ordinal);
}
