using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host.Middleware;

/// <summary>Invokes the rest of the node pipeline.</summary>
public delegate ValueTask NodeMiddlewareDelegate(WorkflowExecutionContext context);

/// <summary>
/// Cross-cutting behavior wrapped around one node's execution as <see cref="WorkflowHost"/> walks
/// the workflow graph — the per-node counterpart of <see cref="IRunMiddleware"/>, and a
/// pipeline-composable generalization of <c>Ovi.Sdk.Nodes.LoggingNode</c> (which remains usable
/// standalone; it just isn't wired through this pipeline). Implementations read/write the node's
/// input and output through <see cref="NodeIoFeature"/> instead of taking them as parameters, so a
/// node middleware needs nothing but the context.
/// </summary>
/// <remarks>
/// <see cref="WorkflowHostBuilder.UseNodeMiddleware"/> registers middleware in the order it should
/// run for every node the host executes. Two defaults are always present unless removed:
/// <see cref="TimingNodeMiddleware"/> and <see cref="NodeTimeoutMiddleware"/>.
/// </remarks>
public interface INodeMiddleware
{
    ValueTask InvokeAsync(WorkflowExecutionContext context, NodeMiddlewareDelegate next);
}
