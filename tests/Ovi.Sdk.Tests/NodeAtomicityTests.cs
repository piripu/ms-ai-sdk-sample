using Ovi.Sdk.Nodes;
using Xunit;

namespace Ovi.Sdk.Tests;

/// <summary>
/// Demonstrates that nodes are atomic: any single node instance can be executed and asserted
/// on with a hand-built <see cref="WorkflowExecutionContext"/> — no workflow engine involved.
/// </summary>
public class NodeAtomicityTests
{
    private sealed class UppercaseNode() : Node<string, string>(
        new NodeDescriptor(NodeId.BuiltIn("uppercase"), "Uppercase", "Uppercases the input text."))
    {
        public override ValueTask<Result<string>> ExecuteAsync(string input, WorkflowExecutionContext context)
        {
            var invocations = context.WorkflowState.GetValueOrDefault<int>("invocations");
            context.WorkflowState.SetValue("invocations", invocations + 1);
            return ValueTask.FromResult(Result<string>.Success(input.ToUpperInvariant()));
        }
    }

    private sealed class DoubleNode() : Node<int, int>(
        new NodeDescriptor(NodeId.BuiltIn("double"), "Double", "Doubles a number."))
    {
        public override ValueTask<Result<int>> ExecuteAsync(int input, WorkflowExecutionContext context) =>
            ValueTask.FromResult(Result<int>.Success(input * 2));
    }

    private sealed class ThrowingNode() : Node<string, string>(
        new NodeDescriptor(NodeId.BuiltIn("throwing"), "Throwing", "Always throws."))
    {
        public override ValueTask<Result<string>> ExecuteAsync(string input, WorkflowExecutionContext context) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task A_node_can_be_executed_on_its_own()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        var node = new UppercaseNode();

        var result = await node.ExecuteAsync("hello", context);

        Assert.True(result.IsSuccess);
        Assert.Equal("HELLO", result.Value);
        Assert.Equal(1, context.WorkflowState.GetValueOrDefault<int>("invocations"));
    }

    [Fact]
    public async Task The_untyped_surface_bridges_to_the_typed_implementation()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        INode node = new UppercaseNode();

        Assert.Equal(typeof(string), node.InputType);
        Assert.Equal(typeof(string), node.OutputType);

        var result = await node.ExecuteAsync("hello", context);
        Assert.Equal("HELLO", result.Value);
    }

    [Fact]
    public async Task The_untyped_surface_reports_wrong_input_types_as_validation_errors()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        INode stringNode = new UppercaseNode();
        INode intNode = new DoubleNode();

        var mismatch = await stringNode.ExecuteAsync(42, context);
        Assert.True(mismatch.IsFailure);
        var validation = Assert.IsType<ValidationError>(mismatch.Error);
        Assert.Contains("expects input of type", validation.Message);

        // Null can only be rejected when the input type is a non-nullable value type; reference-type
        // nullability is erased at runtime.
        var nullInput = await intNode.ExecuteAsync(null, context);
        Assert.True(nullInput.IsFailure);
        Assert.IsType<ValidationError>(nullInput.Error);
    }

    [Fact]
    public async Task The_untyped_surface_converts_unhandled_exceptions_to_execution_errors()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        INode node = new ThrowingNode();

        var result = await node.ExecuteAsync("input", context);

        Assert.True(result.IsFailure);
        var execution = Assert.IsType<ExecutionError>(result.Error);
        Assert.IsType<InvalidOperationException>(execution.Exception);
        Assert.Contains("boom", execution.Message);
    }

    [Fact]
    public void The_context_builder_provides_sensible_defaults()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        Assert.NotEmpty(context.Workflow.WorkflowId);
        Assert.NotEmpty(context.Workflow.RunId);
        Assert.Empty(context.WorkflowState.Keys);
        Assert.Empty(context.GlobalState.Keys);
        Assert.Empty(context.InstanceState.Keys);
        Assert.False(context.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void Services_can_be_registered_without_a_container()
    {
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithWorkflow("wf-1", "My Workflow")
            .WithService<IComparer<int>>(Comparer<int>.Default)
            .Build();

        Assert.Equal("wf-1", context.Workflow.WorkflowId);
        Assert.Same(Comparer<int>.Default, context.GetRequiredService<IComparer<int>>());
        Assert.Null(context.GetService<IFormatProvider>());
        Assert.Throws<InvalidOperationException>(() => context.GetRequiredService<IFormatProvider>());
    }

    [Fact]
    public void The_cancellation_token_flows_through_the_context()
    {
        using var cts = new CancellationTokenSource();
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithCancellationToken(cts.Token)
            .Build();

        cts.Cancel();
        Assert.True(context.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Nodes_compose_from_delegates_without_subclassing()
    {
        var reverse = Node.Create(
            new NodeDescriptor(NodeId.BuiltIn("reverse"), "Reverse", "Reverses text."),
            (string input, WorkflowExecutionContext _) => new string(input.Reverse().ToArray()));

        INode<string, string> typed = reverse;
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        Assert.Equal("olleh", (await typed.ExecuteAsync("hello", context)).Value);
        Assert.Equal("reverse", reverse.Id.Name);

        // The untyped runtime surface comes along for free.
        INode untyped = reverse;
        Assert.Equal("cba", (await untyped.ExecuteAsync("abc", context)).Value);
    }

    [Fact]
    public async Task Delegate_nodes_can_return_failures_directly()
    {
        var failing = new DelegateNode<string, string>(
            new NodeDescriptor(NodeId.BuiltIn("failing"), "Failing", "Always fails."),
            (_, _) => ValueTask.FromResult(Result<string>.Failure(new ValidationError("nope"))));

        var result = await failing.ExecuteAsync("in", WorkflowExecutionContext.CreateBuilder().Build());

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationError.ErrorCode, result.Error.Code);
    }
}
