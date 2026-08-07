using Ovi.Runtime.Host.Process;
using Ovi.Runtime.Host.Tests.Support;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Workflows;
using Xunit;

namespace Ovi.Runtime.Host.Tests;

public class HostProcessManagerTests
{
    [Fact]
    public async Task RequestShutdown_drives_ready_through_draining_to_stopped()
    {
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(SingleNodeWorkflow("upper", NodeId.BuiltIn("upper")))
            .MapNode(TestNodes.Upper())
            .Build();

        await using var manager = new HostProcessManager(host, shutdownGracePeriod: TimeSpan.FromSeconds(5));
        var states = new List<HostLifecycleState>();
        manager.StateChanged += (_, state) => states.Add(state);

        var runUntilShutdown = manager.RunUntilShutdownAsync();
        Assert.Equal(HostLifecycleState.Ready, manager.State);

        manager.RequestShutdown();
        await runUntilShutdown;

        Assert.Equal(HostLifecycleState.Stopped, manager.State);
        Assert.Equal([HostLifecycleState.Ready, HostLifecycleState.Draining, HostLifecycleState.Stopped], states);
    }

    [Fact]
    public async Task RequestShutdown_is_safe_to_call_more_than_once()
    {
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(SingleNodeWorkflow("upper", NodeId.BuiltIn("upper")))
            .MapNode(TestNodes.Upper())
            .Build();

        await using var manager = new HostProcessManager(host);
        var transitions = 0;
        manager.StateChanged += (_, _) => transitions++;

        manager.RequestShutdown();
        manager.RequestShutdown();
        manager.RequestShutdown();

        Assert.Equal(1, transitions);
        Assert.True(manager.ShutdownRequested.IsCancellationRequested);
    }

    [Fact]
    public async Task Drain_waits_for_an_in_flight_run_before_stopping()
    {
        var ready = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(SingleNodeWorkflow("gate", NodeId.BuiltIn("gate")))
            .MapNode(TestNodes.Gate("gate", ready, release))
            .Build();

        await using var manager = new HostProcessManager(host, shutdownGracePeriod: TimeSpan.FromSeconds(5));

        var run = host.RunAsync(new RunRequest { Input = "x" });
        await ready.Task;

        var runUntilShutdown = manager.RunUntilShutdownAsync();
        manager.RequestShutdown();

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Equal(HostLifecycleState.Draining, manager.State); // still waiting on the in-flight run

        release.SetResult();
        await run;
        await runUntilShutdown;

        Assert.Equal(HostLifecycleState.Stopped, manager.State);
    }

    [Fact]
    public async Task An_elapsed_grace_period_still_stops_the_manager()
    {
        var ready = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(SingleNodeWorkflow("gate", NodeId.BuiltIn("gate")))
            .MapNode(TestNodes.Gate("gate", ready, release))
            .Build();

        await using var manager = new HostProcessManager(host, shutdownGracePeriod: TimeSpan.FromMilliseconds(50));

        var run = host.RunAsync(new RunRequest { Input = "x" });
        await ready.Task;

        manager.RequestShutdown();
        await manager.RunUntilShutdownAsync();

        Assert.Equal(HostLifecycleState.Stopped, manager.State);

        release.SetResult();
        await run;
    }

    private static WorkflowDefinition SingleNodeWorkflow(string key, NodeId nodeType) => new(
        NodeId.Parse($"acme/{key}@1.0.0"),
        key,
        nodes: [new WorkflowNodeDefinition(key, nodeType)]);
}
