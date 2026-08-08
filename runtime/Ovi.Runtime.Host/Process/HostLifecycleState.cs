namespace Ovi.Runtime.Host.Process;

/// <summary>The lifecycle a <see cref="HostProcessManager"/> drives a hosted process through.</summary>
public enum HostLifecycleState
{
    /// <summary>Signal handlers are not registered yet.</summary>
    Starting,

    /// <summary>Signal handlers are registered; the host is accepting runs.</summary>
    Ready,

    /// <summary>A shutdown was requested; the manager is waiting for any in-flight run to finish.</summary>
    Draining,

    /// <summary>Draining finished (or its grace period elapsed); the process should exit.</summary>
    Stopped,
}
