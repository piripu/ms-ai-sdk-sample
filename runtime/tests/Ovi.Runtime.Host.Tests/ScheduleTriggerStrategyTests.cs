using Ovi.Runtime.Host.Triggers;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Triggers;
using Ovi.Sdk.Workflows;
using Xunit;

namespace Ovi.Runtime.Host.Tests;

public class ScheduleTriggerStrategyTests
{
    [Fact]
    public async Task Fires_the_host_on_each_scheduled_tick_through_the_same_entry_point_as_manual_calls()
    {
        var runCount = 0;
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(SingleNodeWorkflow("tick", NodeId.BuiltIn("tick")))
            .MapNode(CountingNode("tick", () => Interlocked.Increment(ref runCount)))
            .WithTrigger(new ScheduleTriggerStrategy(Schedule.FromInterval(TimeSpan.FromMilliseconds(20))))
            .Build();

        await host.StartTriggersAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await host.StopTriggersAsync();

        Assert.True(runCount >= 3, $"expected at least 3 ticks in 150ms at a 20ms interval, got {runCount}");
    }

    [Fact]
    public async Task StopAsync_halts_further_firings()
    {
        var runCount = 0;
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(SingleNodeWorkflow("tick", NodeId.BuiltIn("tick")))
            .MapNode(CountingNode("tick", () => Interlocked.Increment(ref runCount)))
            .WithTrigger(new ScheduleTriggerStrategy(Schedule.FromInterval(TimeSpan.FromMilliseconds(20))))
            .Build();

        await host.StartTriggersAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(60));
        await host.StopTriggersAsync();
        var countAtStop = runCount;

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Equal(countAtStop, runCount);
    }

    [Fact]
    public async Task A_schedule_that_never_fires_again_simply_stops_ticking()
    {
        // Interval schedules always have a next occurrence, so exercise this via a resolvable-but-
        // empty run window instead: stopping immediately after starting should leave the count at 0.
        var runCount = 0;
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(SingleNodeWorkflow("tick", NodeId.BuiltIn("tick")))
            .MapNode(CountingNode("tick", () => Interlocked.Increment(ref runCount)))
            .WithTrigger(new ScheduleTriggerStrategy(Schedule.FromInterval(TimeSpan.FromMinutes(10))))
            .Build();

        await host.StartTriggersAsync();
        await host.StopTriggersAsync();

        Assert.Equal(0, runCount);
    }

    private static DelegateNode<string, string> CountingNode(string name, Action increment) =>
        Node.Create<string, string>(
            new NodeDescriptor(NodeId.BuiltIn(name), name),
            (input, _) =>
            {
                increment();
                return input ?? string.Empty;
            });

    private static WorkflowDefinition SingleNodeWorkflow(string key, NodeId nodeType) => new(
        NodeId.Parse($"acme/{key}@1.0.0"),
        key,
        nodes: [new WorkflowNodeDefinition(key, nodeType)]);
}
