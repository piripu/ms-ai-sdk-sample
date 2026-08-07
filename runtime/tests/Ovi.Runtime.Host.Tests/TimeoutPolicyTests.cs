using Ovi.Runtime.Host.Timeouts;
using Xunit;

namespace Ovi.Runtime.Host.Tests;

public class TimeoutPolicyTests
{
    [Fact]
    public void Fixed_policy_always_returns_the_same_timeout()
    {
        var policy = new FixedTimeoutPolicy(TimeSpan.FromSeconds(5));
        policy.RecordRunDuration(TimeSpan.FromSeconds(50));

        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetTimeout());
    }

    [Fact]
    public void Adaptive_policy_returns_the_fallback_before_enough_samples()
    {
        var policy = new AdaptiveTimeoutPolicy(fallbackTimeout: TimeSpan.FromSeconds(30), minSamplesForAdaptation: 3);

        policy.RecordRunDuration(TimeSpan.FromSeconds(1));
        policy.RecordRunDuration(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetTimeout());
    }

    [Fact]
    public void Adaptive_policy_normalizes_to_the_observed_distribution_once_enough_samples_exist()
    {
        var policy = new AdaptiveTimeoutPolicy(
            fallbackTimeout: TimeSpan.FromMinutes(5),
            minTimeout: TimeSpan.FromMilliseconds(1),
            maxTimeout: TimeSpan.FromMinutes(10),
            minSamplesForAdaptation: 3);

        for (var i = 0; i < 5; i++)
        {
            policy.RecordRunDuration(TimeSpan.FromMilliseconds(100));
        }

        var timeout = policy.GetTimeout();

        // Constant durations => zero stddev => the timeout converges to (about) the mean, nowhere near the fallback.
        Assert.True(timeout < TimeSpan.FromSeconds(1), $"expected a timeout well under the 5-minute fallback, got {timeout}");
        Assert.True(timeout >= TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Adaptive_policy_widens_the_timeout_when_run_durations_are_volatile()
    {
        var stable = new AdaptiveTimeoutPolicy(minSamplesForAdaptation: 3, minTimeout: TimeSpan.FromMilliseconds(1), maxTimeout: TimeSpan.FromMinutes(10));
        var volatilePolicy = new AdaptiveTimeoutPolicy(minSamplesForAdaptation: 3, minTimeout: TimeSpan.FromMilliseconds(1), maxTimeout: TimeSpan.FromMinutes(10));

        foreach (var duration in new[] { 100, 100, 100, 100, 100 })
        {
            stable.RecordRunDuration(TimeSpan.FromMilliseconds(duration));
        }

        foreach (var duration in new[] { 50, 500, 50, 500, 50 })
        {
            volatilePolicy.RecordRunDuration(TimeSpan.FromMilliseconds(duration));
        }

        Assert.True(volatilePolicy.GetTimeout() > stable.GetTimeout());
    }

    [Fact]
    public void Adaptive_policy_clamps_to_the_configured_band()
    {
        var policy = new AdaptiveTimeoutPolicy(
            minTimeout: TimeSpan.FromSeconds(10),
            maxTimeout: TimeSpan.FromSeconds(20),
            minSamplesForAdaptation: 3);

        for (var i = 0; i < 3; i++)
        {
            policy.RecordRunDuration(TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetTimeout());
    }

    [Fact]
    public void Recording_a_negative_duration_throws()
    {
        var policy = new AdaptiveTimeoutPolicy();
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.RecordRunDuration(TimeSpan.FromSeconds(-1)));
    }
}
