using Ovi.Runtime.Host.Timeouts;

namespace Ovi.Runtime.Host;

/// <summary>Configuration collected by <see cref="WorkflowHostBuilder"/> and consumed by <see cref="WorkflowHost"/>.</summary>
public sealed class WorkflowHostOptions
{
    /// <summary>How the host behaves when a run overlaps another; defaults to <see cref="RunConcurrencyMode.Serialize"/>.</summary>
    public RunConcurrencyMode ConcurrencyMode { get; set; } = RunConcurrencyMode.Serialize;

    /// <summary>Per-node timeout configuration, enforced by <see cref="Middleware.NodeTimeoutMiddleware"/>.</summary>
    public NodeTimeoutOptions NodeTimeouts { get; } = new();

    /// <summary>
    /// The global run timeout policy. <see langword="null"/> (the default) means no global timeout is
    /// enforced; runs still stop on external cancellation.
    /// </summary>
    public IRunTimeoutPolicy? TimeoutPolicy { get; set; }
}
