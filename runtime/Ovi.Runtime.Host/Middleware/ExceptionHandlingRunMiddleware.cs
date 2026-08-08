using Microsoft.Extensions.Logging;
using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host.Middleware;

/// <summary>
/// The default outermost run middleware: the one place a whole-run <c>try/catch</c> lives. Any
/// exception that escapes the rest of the pipeline (a node middleware bug, a node that threw instead
/// of returning a failed <see cref="Result{T}"/>, …) is caught here, logged, and turned into an
/// <see cref="ExecutionError"/> written to <see cref="RunIoFeature.Output"/> — callers of
/// <see cref="WorkflowHost.RunAsync"/> never see an unhandled exception. Cancellation still
/// propagates, since it is not a run failure.
/// </summary>
public sealed class ExceptionHandlingRunMiddleware : IRunMiddleware
{
    public async ValueTask InvokeAsync(WorkflowExecutionContext context, RunMiddlewareDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var io = context.GetRequiredFeature<RunIoFeature>();
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var logger = context.GetLogger<ExceptionHandlingRunMiddleware>();
            RunLog.UnhandledException(logger, exception, context.Workflow.WorkflowId, context.Workflow.RunId);
            io.Output = Result<object?>.Failure(ExecutionError.FromException(exception));
        }
    }
}

/// <summary>Source-generated log messages for run-level middleware.</summary>
internal static partial class RunLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Run {RunId} of workflow {WorkflowId} threw an unhandled exception")]
    public static partial void UnhandledException(ILogger logger, Exception exception, string workflowId, string runId);
}
