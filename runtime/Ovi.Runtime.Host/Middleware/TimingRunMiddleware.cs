using System.Diagnostics;
using Ovi.Runtime.Host.Timeouts;
using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host.Middleware;

/// <summary>
/// Default run middleware that records total run duration into <see cref="RunTimingFeature"/> (the
/// source <see cref="WorkflowHost"/> reads to build <see cref="RunDiagnostics"/>) and, when a
/// timeout policy is supplied, feeds it the completed duration so the next run's timeout can adapt.
/// </summary>
public sealed class TimingRunMiddleware(IRunTimeoutPolicy? timeoutPolicy = null) : IRunMiddleware
{
    public async ValueTask InvokeAsync(WorkflowExecutionContext context, RunMiddlewareDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var timing = context.GetRequiredFeature<RunTimingFeature>();
        timing.StartedAt = DateTimeOffset.UtcNow;
        var started = Stopwatch.GetTimestamp();

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            timing.Duration = Stopwatch.GetElapsedTime(started);
            timeoutPolicy?.RecordRunDuration(timing.Duration);
        }
    }
}
