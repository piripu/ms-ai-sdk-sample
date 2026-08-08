using Ovi.Runtime.Host.Tests.Support;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Workflows;
using Xunit;

namespace Ovi.Runtime.Host.Tests;

public class ConcurrencyTests
{
    [Fact]
    public async Task Serialize_mode_runs_overlapping_calls_one_at_a_time()
    {
        var ready = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var definition = SingleNodeWorkflow("gate", NodeId.BuiltIn("gate"));

        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Gate("gate", ready, release))
            .WithConcurrencyMode(RunConcurrencyMode.Serialize)
            .Build();

        var firstRun = host.RunAsync(new RunRequest { Input = "a" });
        await ready.Task; // the first run is now parked inside the gated node

        var secondRunStarted = false;
        var secondRun = Task.Run(async () =>
        {
            var result = await host.RunAsync(new RunRequest { Input = "b" });
            secondRunStarted = true;
            return result;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(secondRunStarted, "the second run should still be waiting behind the single-run gate");

        release.SetResult();

        var first = await firstRun;
        var second = await secondRun;

        Assert.True(secondRunStarted);
        Assert.Equal("a", first.Output.Value);
        Assert.Equal("b", second.Output.Value);
    }

    [Fact]
    public async Task JoinInFlight_mode_hands_a_concurrent_caller_the_in_flight_runs_result()
    {
        var ready = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var definition = SingleNodeWorkflow("gate", NodeId.BuiltIn("gate"));

        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Gate("gate", ready, release))
            .WithConcurrencyMode(RunConcurrencyMode.JoinInFlight)
            .Build();

        var firstRun = host.RunAsync(new RunRequest { Input = "a" });
        await ready.Task;

        // A second call arriving while the first is still in flight is handed the same task — its
        // own (different) input is never actually run.
        var secondRun = host.RunAsync(new RunRequest { Input = "different" });

        release.SetResult();

        var first = await firstRun;
        var second = await secondRun;

        Assert.Equal("a", first.Output.Value);
        Assert.Equal(first.Diagnostics.StartedAt, second.Diagnostics.StartedAt);
    }

    private static WorkflowDefinition SingleNodeWorkflow(string key, NodeId nodeType) => new(
        NodeId.Parse($"acme/{key}@1.0.0"),
        key,
        nodes: [new WorkflowNodeDefinition(key, nodeType)]);
}
