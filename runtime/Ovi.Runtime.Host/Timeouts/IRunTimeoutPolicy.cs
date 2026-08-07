namespace Ovi.Runtime.Host.Timeouts;

/// <summary>
/// Decides the global timeout budget for a run and learns from how long runs actually take. Fed by
/// <see cref="Middleware.TimingRunMiddleware"/>, which calls <see cref="RecordRunDuration"/> after
/// every run and consults <see cref="GetTimeout"/> before the next one.
/// </summary>
public interface IRunTimeoutPolicy
{
    /// <summary>The timeout to apply to the next run.</summary>
    TimeSpan GetTimeout();

    /// <summary>Records how long a completed run took, so future timeouts can adapt.</summary>
    void RecordRunDuration(TimeSpan duration);
}
