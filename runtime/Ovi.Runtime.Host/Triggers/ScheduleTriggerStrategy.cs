using System.Text.Json.Nodes;
using Ovi.Sdk.Triggers;

namespace Ovi.Runtime.Host.Triggers;

/// <summary>
/// The one concrete background-firing trigger: wraps an <c>Ovi.Sdk.Triggers.Schedule</c> (interval
/// or cron) and calls <see cref="WorkflowHost.RunAsync"/> on each occurrence — through the very same
/// entry point a manual or webhook caller uses, which is what "unifying schedule and caller-fired"
/// means in practice. The SDK contract stays exactly <see cref="Schedule"/>; this strategy is the
/// runtime's timer loop around it.
/// </summary>
public sealed class ScheduleTriggerStrategy : ITriggerStrategy
{
    private readonly Schedule _schedule;
    private readonly Func<DateTimeOffset> _clock;
    private CancellationTokenSource? _stopCts;
    private Task? _loop;

    public ScheduleTriggerStrategy(Schedule schedule, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        _schedule = schedule;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task StartAsync(WorkflowHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);

        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunLoopAsync(host, _stopCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopCts is null)
        {
            return;
        }

        await _stopCts.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: either our own stop signal or the caller's token.
            }
        }
    }

    private async Task RunLoopAsync(WorkflowHost host, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _clock();
            var next = _schedule.GetNextOccurrence(now);
            if (next is null)
            {
                return;
            }

            var delay = next.Value - now;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var request = new RunRequest
                {
                    TriggerKind = TriggerKind.Schedule,
                    Metadata = new JsonObject { ["scheduledAt"] = next.Value.ToString("O") },
                };

                await host.RunAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
