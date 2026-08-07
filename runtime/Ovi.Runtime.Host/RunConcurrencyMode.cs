namespace Ovi.Runtime.Host;

/// <summary>How <see cref="WorkflowHost.RunAsync"/> behaves when a run is already in flight.</summary>
public enum RunConcurrencyMode
{
    /// <summary>
    /// A second concurrent call waits its turn behind a single-run gate (a <see cref="System.Threading.SemaphoreSlim"/>
    /// under the hood), so at most one run of this host executes at a time.
    /// </summary>
    Serialize,

    /// <summary>
    /// A second concurrent call is handed the in-flight run's result task instead of
    /// starting a new one. Only makes sense when concurrent triggers are known to be equivalent (e.g.
    /// several schedule ticks piling up) — the joining call's own <see cref="RunRequest"/> is ignored
    /// if it differs from the in-flight run's.
    /// </summary>
    JoinInFlight,
}
