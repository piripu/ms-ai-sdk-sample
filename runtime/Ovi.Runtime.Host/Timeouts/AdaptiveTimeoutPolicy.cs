namespace Ovi.Runtime.Host.Timeouts;

/// <summary>
/// A global timeout that normalizes to the workflow's actual run-time distribution instead of one
/// fixed guess. Before <see cref="_minSamplesForAdaptation"/> runs have completed it returns
/// <see cref="_fallback"/>; from then on the timeout is <c>mean + multiplier * stdDev</c> over a
/// rolling window of recent run durations, with the multiplier shrinking toward
/// <see cref="_minMultiplier"/> as more samples accumulate — phi-accrual-inspired in spirit (the
/// suspicion threshold tightens as the distribution becomes better known), not a literal port of the
/// phi-accrual failure detector, which reasons about heartbeat inter-arrival times rather than run
/// durations. The result is always clamped to <c>[minTimeout, maxTimeout]</c> so one freak slow run
/// can't blow the budget out permanently, and a burst of freak runs only shifts it gradually because
/// the window is bounded.
/// </summary>
public sealed class AdaptiveTimeoutPolicy : IRunTimeoutPolicy
{
    private readonly object _gate = new();
    private readonly Queue<double> _samplesMs = new();
    private readonly TimeSpan _fallback;
    private readonly TimeSpan _min;
    private readonly TimeSpan _max;
    private readonly int _minSamplesForAdaptation;
    private readonly int _windowSize;
    private readonly double _initialMultiplier;
    private readonly double _minMultiplier;

    public AdaptiveTimeoutPolicy(
        TimeSpan? fallbackTimeout = null,
        TimeSpan? minTimeout = null,
        TimeSpan? maxTimeout = null,
        int minSamplesForAdaptation = 3,
        int windowSize = 20,
        double initialMultiplier = 4.0,
        double minMultiplier = 2.0)
    {
        _fallback = fallbackTimeout ?? TimeSpan.FromSeconds(30);
        _min = minTimeout ?? TimeSpan.FromSeconds(5);
        _max = maxTimeout ?? TimeSpan.FromMinutes(10);

        ArgumentOutOfRangeException.ThrowIfLessThan(_max, _min);
        ArgumentOutOfRangeException.ThrowIfLessThan(minSamplesForAdaptation, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, minSamplesForAdaptation);
        ArgumentOutOfRangeException.ThrowIfLessThan(initialMultiplier, minMultiplier);

        _minSamplesForAdaptation = minSamplesForAdaptation;
        _windowSize = windowSize;
        _initialMultiplier = initialMultiplier;
        _minMultiplier = minMultiplier;
    }

    public TimeSpan GetTimeout()
    {
        lock (_gate)
        {
            if (_samplesMs.Count < _minSamplesForAdaptation)
            {
                return _fallback;
            }

            var mean = _samplesMs.Average();
            var variance = _samplesMs.Count < 2
                ? 0
                : _samplesMs.Sum(sample => (sample - mean) * (sample - mean)) / (_samplesMs.Count - 1);
            var stdDev = Math.Sqrt(variance);

            // The multiplier shrinks from initialMultiplier toward minMultiplier as samples accumulate.
            var multiplier = _minMultiplier + ((_initialMultiplier - _minMultiplier) / _samplesMs.Count);
            var timeout = TimeSpan.FromMilliseconds(mean + (multiplier * stdDev));

            return Clamp(timeout, _min, _max);
        }
    }

    public void RecordRunDuration(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        lock (_gate)
        {
            _samplesMs.Enqueue(duration.TotalMilliseconds);
            while (_samplesMs.Count > _windowSize)
            {
                _samplesMs.Dequeue();
            }
        }
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;
}
