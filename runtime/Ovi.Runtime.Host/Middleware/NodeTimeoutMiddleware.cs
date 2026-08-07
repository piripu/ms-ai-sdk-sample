using Microsoft.Extensions.Logging;
using Ovi.Runtime.Host.Timeouts;
using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host.Middleware;

/// <summary>
/// Default node middleware enforcing <see cref="NodeTimeoutOptions"/>. This is a <b>soft</b>
/// timeout: the node's own call is raced against the configured duration via
/// <c>Task.WaitAsync</c>, and a <see cref="ExecutionError"/> is written to
/// <see cref="NodeIoFeature.Output"/> when the timeout wins — but <see cref="WorkflowExecutionContext"/>
/// hands every node the same fixed <see cref="WorkflowExecutionContext.CancellationToken"/>, so a
/// timed-out node's own task is not force-cancelled; it keeps running in the background until it
/// finishes or the run's own cancellation fires. This is the same limitation every cooperative,
/// non-preemptive timeout in .NET has without a dedicated per-operation token.
/// </summary>
public sealed class NodeTimeoutMiddleware : INodeMiddleware
{
    private readonly NodeTimeoutOptions _options;

    public NodeTimeoutMiddleware(NodeTimeoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public async ValueTask InvokeAsync(WorkflowExecutionContext context, NodeMiddlewareDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var nodeIo = context.GetRequiredFeature<NodeIoFeature>();
        var timeout = _options.GetTimeout(nodeIo.Key);
        if (timeout is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        try
        {
            await next(context).AsTask().WaitAsync(timeout.Value, context.CancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var logger = context.GetLogger<NodeTimeoutMiddleware>();
            NodeTimeoutLog.TimedOut(logger, nodeIo.Key, timeout.Value);
            nodeIo.Output = Result<object?>.Failure(new ExecutionError(
                $"Node '{nodeIo.Key}' did not complete within its {timeout.Value} timeout. Its task may still be running in the background; the run does not wait for it."));
        }
    }
}

/// <summary>Source-generated log messages for node timeout enforcement.</summary>
internal static partial class NodeTimeoutLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Node {NodeKey} timed out after {Timeout}")]
    public static partial void TimedOut(ILogger logger, string nodeKey, TimeSpan timeout);
}
