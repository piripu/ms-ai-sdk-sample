using System.Diagnostics;
using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host.Middleware;

/// <summary>Default node middleware that records each node's execution duration into <see cref="RunTimingFeature.NodeDurations"/>.</summary>
public sealed class TimingNodeMiddleware : INodeMiddleware
{
    public async ValueTask InvokeAsync(WorkflowExecutionContext context, NodeMiddlewareDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var nodeIo = context.GetRequiredFeature<NodeIoFeature>();
        var timing = context.GetRequiredFeature<RunTimingFeature>();
        var started = Stopwatch.GetTimestamp();

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            timing.NodeDurations[nodeIo.Key] = Stopwatch.GetElapsedTime(started);
        }
    }
}
