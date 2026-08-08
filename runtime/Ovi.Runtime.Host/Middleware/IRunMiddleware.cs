using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host.Middleware;

/// <summary>Invokes the rest of the run pipeline.</summary>
public delegate ValueTask RunMiddlewareDelegate(WorkflowExecutionContext context);

/// <summary>
/// Cross-cutting behavior wrapped around an entire <see cref="WorkflowHost.RunAsync"/> call — the
/// ASP.NET Core <c>IMiddleware</c> shape applied to a run. Implementations read the run's input and
/// write its output through <see cref="RunIoFeature"/> (<c>context.GetRequiredFeature&lt;RunIoFeature&gt;()</c>),
/// never as method parameters, so a middleware needs nothing but the context to do its job.
/// </summary>
/// <remarks>
/// <see cref="WorkflowHostBuilder.UseRunMiddleware"/> registers middleware in the order it should
/// run; the last-registered middleware is closest to the workflow itself. Two defaults are always
/// present unless removed: <see cref="ExceptionHandlingRunMiddleware"/> (outermost) and
/// <see cref="TimingRunMiddleware"/>.
/// </remarks>
public interface IRunMiddleware
{
    ValueTask InvokeAsync(WorkflowExecutionContext context, RunMiddlewareDelegate next);
}
