using Ovi.Runtime.Host.Tests.Support;
using Ovi.Sdk.Agents;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Workflows;
using Xunit;

namespace Ovi.Runtime.Host.Tests;

public class WorkflowHostTests
{
    [Fact]
    public async Task Runs_a_linear_multi_node_workflow_end_to_end()
    {
        var definition = new WorkflowDefinition(
            NodeId.Parse("acme/greeter@1.0.0"),
            "Greeter",
            nodes:
            [
                new WorkflowNodeDefinition("step1", NodeId.BuiltIn("upper")),
                new WorkflowNodeDefinition("step2", NodeId.BuiltIn("exclaim")),
            ],
            connections: [new WorkflowConnection("step1", "step2")]);

        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Upper())
            .MapNode(TestNodes.Exclaim())
            .Build();

        var result = await host.RunAsync(new RunRequest { Input = "hello" });

        Assert.True(result.Output.IsSuccess);
        Assert.Equal("HELLO!", result.Output.Value);
        Assert.True(result.Diagnostics.NodeDurations.ContainsKey("step1"));
        Assert.True(result.Diagnostics.NodeDurations.ContainsKey("step2"));
        Assert.True(result.Diagnostics.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task A_failing_node_short_circuits_the_run_without_running_downstream_nodes()
    {
        var downstreamRan = false;
        var definition = new WorkflowDefinition(
            NodeId.Parse("acme/pipeline@1.0.0"),
            "Pipeline",
            nodes:
            [
                new WorkflowNodeDefinition("failer", NodeId.BuiltIn("failer")),
                new WorkflowNodeDefinition("after", NodeId.BuiltIn("after")),
            ],
            connections: [new WorkflowConnection("failer", "after")]);

        var host = WorkflowHost.CreateBuilder()
            .UseWorkflow(definition)
            .MapNode(TestNodes.Failing("failer", "nope"))
            .MapNode(Node.Create<string, string>(
                new NodeDescriptor(NodeId.BuiltIn("after"), "after"),
                (input, _) => { downstreamRan = true; return input; }))
            .Build();

        var result = await host.RunAsync(new RunRequest { Input = "hello" });

        Assert.True(result.Output.IsFailure);
        Assert.IsType<ValidationError>(result.Output.Error);
        Assert.Contains("nope", result.Output.Error.Message);
        Assert.False(downstreamRan);
    }

    [Fact]
    public async Task An_unresolved_node_type_is_a_resolution_error()
    {
        var definition = SingleNodeWorkflow("missing", NodeId.BuiltIn("does-not-exist"));
        var host = WorkflowHost.CreateBuilder().UseWorkflow(definition).Build();

        var result = await host.RunAsync(new RunRequest { Input = "hello" });

        Assert.True(result.Output.IsFailure);
        Assert.IsType<ResolutionError>(result.Output.Error);
    }

    [Fact]
    public async Task Wrapped_agent_reads_prompt_and_persists_chat_history_across_runs()
    {
        var chatClient = FakeChatClient.RespondingWith("First.", "Second.");
        var definition = new AgentDefinition(NodeId.Parse("acme/bot@1.0.0"), "Bot", instructions: "Be terse.");
        var agentNode = AgentNode.FromDefinition(definition, chatClient: chatClient).Value;

        var host = WorkflowHost.CreateBuilder()
            .UseAgent(definition, agentNode)
            .WithChatClient(chatClient)
            .Build();

        var first = await host.RunAsync(new RunRequest { Prompt = "Hi" });
        Assert.True(first.Output.IsSuccess);
        Assert.Equal("First.", ((AgentResponse)first.Output.Value!).Text);

        var second = await host.RunAsync(new RunRequest { Prompt = "Again" });
        Assert.Equal("Second.", ((AgentResponse)second.Output.Value!).Text);

        Assert.Equal(2, chatClient.Calls.Count);
        Assert.True(chatClient.Calls[1].Messages.Count > chatClient.Calls[0].Messages.Count);
    }

    [Fact]
    public void Build_rejects_fan_in_workflows()
    {
        var definition = new WorkflowDefinition(
            NodeId.Parse("acme/fanin@1.0.0"),
            "Fan-in",
            nodes:
            [
                new WorkflowNodeDefinition("a", NodeId.BuiltIn("upper")),
                new WorkflowNodeDefinition("b", NodeId.BuiltIn("upper")),
                new WorkflowNodeDefinition("c", NodeId.BuiltIn("exclaim")),
            ],
            connections: [new WorkflowConnection("a", "c"), new WorkflowConnection("b", "c")]);

        var builder = WorkflowHost.CreateBuilder().UseWorkflow(definition);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_requires_a_definition()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowHost.CreateBuilder().Build());
    }

    private static WorkflowDefinition SingleNodeWorkflow(string key, NodeId nodeType) => new(
        NodeId.Parse($"acme/{key}@1.0.0"),
        key,
        nodes: [new WorkflowNodeDefinition(key, nodeType)]);
}
