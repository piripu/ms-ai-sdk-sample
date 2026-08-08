namespace Ovi.Runtime.Host.Timeouts;

/// <summary>A constant global timeout — for hosts that don't want adaptive behavior.</summary>
public sealed class FixedTimeoutPolicy : IRunTimeoutPolicy
{
    private readonly TimeSpan _timeout;

    public FixedTimeoutPolicy(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeout = timeout;
    }

    public TimeSpan GetTimeout() => _timeout;

    /// <summary>No-op: a fixed policy does not adapt.</summary>
    public void RecordRunDuration(TimeSpan duration)
    {
    }
}
