using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ovi.Sdk.Nodes;

/// <summary>
/// A decorator that logs node execution: Debug on start and success (with duration), Warning when
/// the node returns a failure result, Error when it throws. Attach with
/// <see cref="LoggingNodeExtensions.WithLogging{TInput, TOutput}"/>.
/// </summary>
/// <remarks>
/// The logger comes from the constructor when fixed, otherwise from
/// <see cref="WorkflowExecutionContext.GetLogger(string)"/> per execution — a no-op logger when the
/// runtime registered no <see cref="ILoggerFactory"/>, so decorated nodes cost nothing by default.
/// </remarks>
public sealed class LoggingNode<TInput, TOutput> : Node<TInput, TOutput>
{
    /// <summary>The log category all logging nodes emit under.</summary>
    public const string CategoryName = "Ovi.Sdk.Nodes.LoggingNode";

    private readonly INode<TInput, TOutput> _inner;
    private readonly ILogger? _logger;

    public LoggingNode(INode<TInput, TOutput> inner, ILogger? logger = null)
        : base(inner.Descriptor)
    {
        _inner = inner;
        _logger = logger;
    }

    public override async ValueTask<Result<TOutput>> ExecuteAsync(TInput input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var logger = _logger ?? context.GetLogger(CategoryName);
        var started = Stopwatch.GetTimestamp();
        NodeLog.Executing(logger, Id);

        try
        {
            var result = await _inner.ExecuteAsync(input, context).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (result.IsSuccess)
            {
                NodeLog.Executed(logger, Id, elapsed);
            }
            else
            {
                NodeLog.ExecutionReturnedFailure(logger, Id, result.Error.Code, result.Error.Message, elapsed);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            NodeLog.UnhandledException(logger, exception, Id);
            throw;
        }
    }
}

public static class LoggingNodeExtensions
{
    /// <summary>Wraps the node in a <see cref="LoggingNode{TInput, TOutput}"/>.</summary>
    public static LoggingNode<TInput, TOutput> WithLogging<TInput, TOutput>(
        this INode<TInput, TOutput> node,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new LoggingNode<TInput, TOutput>(node, logger);
    }
}

/// <summary>Source-generated log messages for node execution.</summary>
internal static partial class NodeLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Executing node {NodeId}")]
    public static partial void Executing(ILogger logger, NodeId nodeId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Node {NodeId} succeeded in {ElapsedMs:0.0} ms")]
    public static partial void Executed(ILogger logger, NodeId nodeId, double elapsedMs);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Node {NodeId} failed ({ErrorCode}: {ErrorMessage}) in {ElapsedMs:0.0} ms")]
    public static partial void ExecutionReturnedFailure(ILogger logger, NodeId nodeId, string errorCode, string errorMessage, double elapsedMs);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Node {NodeId} threw an unhandled exception")]
    public static partial void UnhandledException(ILogger logger, Exception exception, NodeId nodeId);
}
