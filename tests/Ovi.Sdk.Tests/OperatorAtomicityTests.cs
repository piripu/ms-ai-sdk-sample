using Ovi.Sdk.Operators;
using Xunit;

namespace Ovi.Sdk.Tests;

/// <summary>
/// Demonstrates that operators are atomic: any single operator instance can be executed and asserted
/// on with a hand-built <see cref="WorkflowExecutionContext"/> — no workflow engine involved.
/// </summary>
public class OperatorAtomicityTests
{
    private sealed class UppercaseOperator() : Operator<string, string>(
        new OperatorDescriptor(OperatorId.BuiltIn("uppercase"), "Uppercase", "Uppercases the input text."))
    {
        public override ValueTask<string> ExecuteAsync(string input, WorkflowExecutionContext context)
        {
            var invocations = context.WorkflowState.GetValueOrDefault<int>("invocations");
            context.WorkflowState.SetValue("invocations", invocations + 1);
            return ValueTask.FromResult(input.ToUpperInvariant());
        }
    }

    private sealed class DoubleOperator() : Operator<int, int>(
        new OperatorDescriptor(OperatorId.BuiltIn("double"), "Double", "Doubles a number."))
    {
        public override ValueTask<int> ExecuteAsync(int input, WorkflowExecutionContext context) =>
            ValueTask.FromResult(input * 2);
    }

    [Fact]
    public async Task An_operator_can_be_executed_on_its_own()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        var op = new UppercaseOperator();

        var result = await op.ExecuteAsync("hello", context);

        Assert.Equal("HELLO", result);
        Assert.Equal(1, context.WorkflowState.GetValueOrDefault<int>("invocations"));
    }

    [Fact]
    public async Task The_untyped_surface_bridges_to_the_typed_implementation()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        IOperator op = new UppercaseOperator();

        Assert.Equal(typeof(string), op.InputType);
        Assert.Equal(typeof(string), op.OutputType);
        Assert.Equal("HELLO", await op.ExecuteAsync("hello", context));
    }

    [Fact]
    public async Task The_untyped_surface_rejects_wrong_input_types()
    {
        var context = WorkflowExecutionContext.CreateBuilder().Build();
        IOperator stringOperator = new UppercaseOperator();
        IOperator intOperator = new DoubleOperator();

        await Assert.ThrowsAsync<ArgumentException>(() => stringOperator.ExecuteAsync(42, context).AsTask());

        // Null can only be rejected when the input type is a non-nullable value type; reference-type
        // nullability is erased at runtime.
        await Assert.ThrowsAsync<ArgumentNullException>(() => intOperator.ExecuteAsync(null, context).AsTask());
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
}
