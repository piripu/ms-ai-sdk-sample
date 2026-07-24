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
        public override ValueTask<string> ExecuteAsync(string input, WorkflowExecutionContext context)
        {
            var invocations = context.WorkflowState.GetValueOrDefault<int>("invocations");
            context.WorkflowState.SetValue("invocations", invocations + 1);
            return ValueTask.FromResult(input.ToUpperInvariant());
        }
    }

    private sealed class DoubleNode() : Node<int, int>(
        new NodeDescriptor(NodeId.BuiltIn("double"), "Double", "Doubles a number."))
    {
        public override ValueTask<int> ExecuteAsync(int input, WorkflowExecutionContext context) =>
            ValueTask.FromResult(input * 2);
    }

    [Fact]
    public async Task An_node_can_be_executed_on_its_own()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        var op = new UppercaseNode();

        var result = await op.ExecuteAsync("hello", context);

        Assert.Equal("HELLO", result);
        Assert.Equal(1, context.WorkflowState.GetValueOrDefault<int>("invocations"));
    }

    [Fact]
    public async Task The_untyped_surface_bridges_to_the_typed_implementation()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        INode op = new UppercaseNode();

        Assert.Equal(typeof(string), op.InputType);
        Assert.Equal(typeof(string), op.OutputType);
        Assert.Equal("HELLO", await op.ExecuteAsync("hello", context));
    }

    [Fact]
    public async Task The_untyped_surface_rejects_wrong_input_types()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        INode stringNode = new UppercaseNode();
        INode intNode = new DoubleNode();

        await Assert.ThrowsAsync<ArgumentException>(() => stringNode.ExecuteAsync(42, context).AsTask());

        // Null can only be rejected when the input type is a non-nullable value type; reference-type
        // nullability is erased at runtime.
        await Assert.ThrowsAsync<ArgumentNullException>(() => intNode.ExecuteAsync(null, context).AsTask());
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

        Assert.Equal("olleh", await typed.ExecuteAsync("hello", context));
        Assert.Equal("reverse", reverse.Id.Name);

        // The untyped runtime surface comes along for free.
        INode untyped = reverse;
        Assert.Equal("cba", await untyped.ExecuteAsync("abc", context));
    }
}
