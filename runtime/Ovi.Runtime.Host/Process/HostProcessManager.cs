using System.Runtime.InteropServices;

namespace Ovi.Runtime.Host.Process;

/// <summary>
/// The process-level counterpart of <see cref="WorkflowHost"/>: registers <c>SIGTERM</c>/<c>SIGINT</c>
/// and Ctrl+C, and drives the <see cref="HostLifecycleState"/> state machine on shutdown — waiting
/// (bounded by a grace period) for whatever run is in flight to finish via
/// <see cref="WorkflowHost.WaitForIdleAsync"/> before the process exits. No prior implementation
/// existed to mirror this against; it is a standard, documented design, not a port.
/// </summary>
public sealed class HostProcessManager : IAsyncDisposable
{
    private readonly WorkflowHost _host;
    private readonly TimeSpan _shutdownGracePeriod;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly List<PosixSignalRegistration> _signalRegistrations = [];
    private bool _started;

    public HostProcessManager(WorkflowHost host, TimeSpan? shutdownGracePeriod = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        _shutdownGracePeriod = shutdownGracePeriod ?? TimeSpan.FromSeconds(30);
        State = HostLifecycleState.Starting;
    }

    public HostLifecycleState State { get; private set; }

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public event EventHandler<HostLifecycleState>? StateChanged;

    /// <summary>Cancelled once a shutdown has been requested — by a signal, Ctrl+C, or a direct call to <see cref="RequestShutdown"/>.</summary>
    public CancellationToken ShutdownRequested => _shutdownCts.Token;

    /// <summary>Registers signal handlers and transitions to <see cref="HostLifecycleState.Ready"/>. Idempotent.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _signalRegistrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal));
        _signalRegistrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal));
        Console.CancelKeyPress += OnCancelKeyPress;

        // A shutdown requested before Start() (e.g. a test calling RequestShutdown up front) must not
        // be clobbered back to Ready.
        if (!_shutdownCts.IsCancellationRequested)
        {
            SetState(HostLifecycleState.Ready);
        }
    }

    /// <summary>
    /// Requests a graceful shutdown: moves to <see cref="HostLifecycleState.Draining"/> and cancels
    /// <see cref="ShutdownRequested"/>. Safe to call more than once (later calls are no-ops) and safe
    /// to call directly — e.g. from a test, to simulate a signal without sending a real one.
    /// </summary>
    public void RequestShutdown()
    {
        if (_shutdownCts.IsCancellationRequested)
        {
            return;
        }

        SetState(HostLifecycleState.Draining);
        _shutdownCts.Cancel();
    }

    /// <summary>
    /// Starts the manager, waits for a shutdown to be requested, then waits (bounded by the
    /// configured grace period) for the host to finish any in-flight run before moving to
    /// <see cref="HostLifecycleState.Stopped"/>. A typical process's whole lifetime, in one call.
    /// </summary>
    public async Task RunUntilShutdownAsync()
    {
        Start();
        await WaitForShutdownSignalAsync().ConfigureAwait(false);

        using var graceCts = new CancellationTokenSource(_shutdownGracePeriod);
        try
        {
            await _host.WaitForIdleAsync(graceCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The grace period elapsed with a run still in flight; shut down anyway.
        }

        SetState(HostLifecycleState.Stopped);
    }

    public ValueTask DisposeAsync()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        foreach (var registration in _signalRegistrations)
        {
            registration.Dispose();
        }

        _signalRegistrations.Clear();
        _shutdownCts.Dispose();
        return ValueTask.CompletedTask;
    }

    private Task WaitForShutdownSignalAsync()
    {
        if (_shutdownCts.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _shutdownCts.Token.Register(() => completion.TrySetResult());
        return completion.Task;
    }

    private void OnPosixSignal(PosixSignalContext context)
    {
        // We drain ourselves; suppress the runtime's default terminate-immediately behavior.
        context.Cancel = true;
        RequestShutdown();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        RequestShutdown();
    }

    private void SetState(HostLifecycleState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
