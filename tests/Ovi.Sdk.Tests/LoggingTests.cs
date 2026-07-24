using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Tests.Support;
using Xunit;

namespace Ovi.Sdk.Tests;

public class LoggingTests
{
    [Fact]
    public void GetLogger_falls_back_to_a_no_op_logger_without_a_factory()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        Assert.Same(NullLogger<LoggingTests>.Instance, context.GetLogger<LoggingTests>());
        Assert.Same(NullLogger.Instance, context.GetLogger("any-category"));
    }

    [Fact]
    public void GetLogger_resolves_a_registered_factory()
    {
        var factory = new CapturingLoggerFactory();
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithService<ILoggerFactory>(factory)
            .Build();

        context.GetLogger("my-category").LogInformation("hello");

        var entry = Assert.Single(factory.Entries);
        Assert.Equal("my-category", entry.Category);
        Assert.Equal("hello", entry.Message);
    }

    [Fact]
    public async Task The_logging_decorator_records_success_with_duration()
    {
        var factory = new CapturingLoggerFactory();
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithService<ILoggerFactory>(factory)
            .Build();

        var node = Node.Create(
                new NodeDescriptor(NodeId.BuiltIn("reverse"), "Reverse", "Reverses text."),
                (string input, WorkflowExecutionContext _) => new string(input.Reverse().ToArray()))
            .WithLogging();

        var result = await node.ExecuteAsync("abc", context);

        Assert.Equal("cba", result.Value);
        Assert.Equal(2, factory.Entries.Count);
        Assert.All(factory.Entries, entry => Assert.Equal(LoggingNode<string, string>.CategoryName, entry.Category));
        Assert.Contains("Executing node ./reverse", factory.Entries[0].Message);
        Assert.Equal(LogLevel.Debug, factory.Entries[0].Level);
        Assert.Contains("succeeded in", factory.Entries[1].Message);
    }

    [Fact]
    public async Task The_logging_decorator_warns_on_failure_results()
    {
        var factory = new CapturingLoggerFactory();
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithService<ILoggerFactory>(factory)
            .Build();

        var node = new DelegateNode<string, string>(
                new NodeDescriptor(NodeId.BuiltIn("failing"), "Failing", "Always fails."),
                (_, _) => ValueTask.FromResult(Result<string>.Failure(new ValidationError("bad input"))))
            .WithLogging();

        var result = await node.ExecuteAsync("abc", context);

        Assert.True(result.IsFailure);
        var warning = Assert.Single(factory.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("failed (validation: bad input)", warning.Message);
    }

    [Fact]
    public async Task The_logging_decorator_logs_and_rethrows_exceptions()
    {
        var factory = new CapturingLoggerFactory();
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithService<ILoggerFactory>(factory)
            .Build();

        var node = new DelegateNode<string, string>(
                new NodeDescriptor(NodeId.BuiltIn("throwing"), "Throwing", "Always throws."),
                (_, _) => throw new InvalidOperationException("boom"))
            .WithLogging();

        await Assert.ThrowsAsync<InvalidOperationException>(() => node.ExecuteAsync("abc", context).AsTask());

        var error = Assert.Single(factory.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("unhandled exception", error.Message);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task The_untyped_bridge_logs_unhandled_exceptions_as_execution_errors()
    {
        var factory = new CapturingLoggerFactory();
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithService<ILoggerFactory>(factory)
            .Build();

        INode node = new DelegateNode<string, string>(
            new NodeDescriptor(NodeId.BuiltIn("throwing"), "Throwing", "Always throws."),
            (_, _) => throw new InvalidOperationException("boom"));

        var result = await node.ExecuteAsync("abc", context);

        Assert.True(result.IsFailure);
        Assert.IsType<ExecutionError>(result.Error);
        Assert.Contains(factory.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }
}
