using Ovi.Runtime.Host.Middleware;
using Ovi.Runtime.Host.Tests.Support;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Workflows;
using Xunit;

namespace Ovi.Runtime.Host.Tests;

public class MiddlewareTests
{
    [Fact]
    public async Task Exception_handling_middleware_converts_unhandled_exceptions_to_execution_errors()
    {
        var definition = SingleNodeWorkflow("upper", NodeId.BuiltIn("upper"));
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Upper())
            .UseRunMiddleware(new ThrowingRunMiddleware())
            .Build();

        var result = await host.RunAsync(new RunRequest { Input = "x" });

        Assert.True(result.Output.IsFailure);
        var error = Assert.IsType<ExecutionError>(result.Output.Error);
        Assert.Contains("boom from middleware", error.Message);
    }

    [Fact]
    public async Task Without_default_middleware_a_thrown_exception_propagates()
    {
        var definition = SingleNodeWorkflow("upper", NodeId.BuiltIn("upper"));
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Upper())
            .UseRunMiddleware(new ThrowingRunMiddleware())
            .WithoutDefaultMiddleware()
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(new RunRequest { Input = "x" }));
    }

    [Fact]
    public async Task Timing_middleware_records_run_and_node_durations()
    {
        var definition = SingleNodeWorkflow("upper", NodeId.BuiltIn("upper"));
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Upper())
            .Build();

        var result = await host.RunAsync(new RunRequest { Input = "hi" });

        Assert.True(result.Diagnostics.Duration >= TimeSpan.Zero);
        Assert.True(result.Diagnostics.NodeDurations.ContainsKey("upper"));
        Assert.True(result.Diagnostics.NodeDurations["upper"] >= TimeSpan.Zero);
    }

    [Fact]
    public async Task A_node_that_throws_is_still_reported_as_an_execution_error()
    {
        // Node<,>'s own untyped bridge already converts a throwing node body into an ExecutionError
        // before it ever reaches the node middleware pipeline — confirms that path composes cleanly
        // with the host rather than double-wrapping or losing the failure.
        var definition = SingleNodeWorkflow("boom", NodeId.BuiltIn("boom"));
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Throwing("boom", new InvalidOperationException("node exploded")))
            .Build();

        var result = await host.RunAsync(new RunRequest { Input = "x" });

        Assert.True(result.Output.IsFailure);
        var error = Assert.IsType<ExecutionError>(result.Output.Error);
        Assert.Contains("node exploded", error.Message);
    }

    [Fact]
    public async Task Node_timeout_middleware_fails_fast_on_a_slow_node()
    {
        var definition = SingleNodeWorkflow("slow", NodeId.BuiltIn("slow"));
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Never("slow"))
            .WithNodeTimeout("slow", TimeSpan.FromMilliseconds(50))
            .Build();

        var result = await host.RunAsync(new RunRequest { Input = "x" });

        Assert.True(result.Output.IsFailure);
        var error = Assert.IsType<ExecutionError>(result.Output.Error);
        Assert.Contains("timeout", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_node_without_a_configured_timeout_is_unaffected_by_node_timeout_middleware()
    {
        var definition = SingleNodeWorkflow("upper", NodeId.BuiltIn("upper"));
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Upper())
            .Build(); // no WithNodeTimeout call

        var result = await host.RunAsync(new RunRequest { Input = "hi" });

        Assert.True(result.Output.IsSuccess);
        Assert.Equal("HI", result.Output.Value);
    }

    private sealed class ThrowingRunMiddleware : IRunMiddleware
    {
        public ValueTask InvokeAsync(WorkflowExecutionContext context, RunMiddlewareDelegate next) =>
            throw new InvalidOperationException("boom from middleware");
    }

    private static WorkflowDefinition SingleNodeWorkflow(string key, NodeId nodeType) => new(
        NodeId.Parse($"acme/{key}@1.0.0"),
        key,
        nodes: [new WorkflowNodeDefinition(key, nodeType)]);
}
