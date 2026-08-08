using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host;

/// <summary>
/// Carries a run's input and output on the <c>WorkflowExecutionContext</c> itself, the way
/// <c>HttpContext.Request</c>/<c>.Response</c> carry an ASP.NET Core request's input/output.
/// <see cref="WorkflowHost.RunAsync"/> attaches one per run before the middleware pipeline starts;
/// <see cref="Middleware.IRunMiddleware"/> reads <see cref="Input"/> and writes <see cref="Output"/>
/// through this feature instead of taking them as parameters — middleware only ever sees the
/// context, matching how <c>RequestDelegate</c> only ever sees <c>HttpContext</c>.
/// </summary>
public sealed class RunIoFeature
{
    public RunIoFeature(RunRequest request) => Request = request;

    /// <summary>The request that started this run.</summary>
    public RunRequest Request { get; }

    /// <summary>The value passed to the workflow's entry node. Set by the host before the pipeline runs.</summary>
    public object? Input { get; set; }

    /// <summary>The workflow's result. Set by the innermost middleware (the node-graph walker) as the pipeline unwinds.</summary>
    public Result<object?>? Output { get; set; }
}
