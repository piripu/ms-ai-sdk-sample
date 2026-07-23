using Microsoft.Extensions.AI;
using Ovi.Sdk.Agents;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Tests.Support;
using Ovi.Sdk.Tools;
using Xunit;

namespace Ovi.Sdk.Tests;

public class AgentNodeTests
{
    private static NodeDescriptor Descriptor { get; } = new(
        NodeId.Parse("acme/assistant@1.0.0"), "Assistant", "A helpful assistant.");

    [Fact]
    public async Task Sends_instructions_and_prompt_to_the_chat_client()
    {
        var chatClient = FakeChatClient.RespondingWith("Hi there!");
        var agent = new AgentNode(Descriptor, "You are terse.", chatClient: chatClient);
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        var response = await agent.ExecuteAsync("Hello?", context);

        Assert.Equal("Hi there!", response.Text);
        Assert.NotNull(response.RawResponse);

        var (messages, _) = Assert.Single(chatClient.Calls);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal("You are terse.", messages[0].Text);
        Assert.Equal(ChatRole.User, messages[^1].Role);
        Assert.Equal("Hello?", messages[^1].Text);
    }

    [Fact]
    public async Task Resolves_the_chat_client_from_runtime_services()
    {
        var chatClient = FakeChatClient.RespondingWith("From services.");
        var agent = new AgentNode(Descriptor);
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithService<IChatClient>(chatClient)
            .Build();

        var response = await agent.ExecuteAsync("Hello?", context);

        Assert.Equal("From services.", response.Text);
    }

    [Fact]
    public async Task Fails_clearly_when_no_chat_client_is_available()
    {
        var agent = new AgentNode(Descriptor);
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.ExecuteAsync("Hello?", context).AsTask());
        Assert.Contains("chat client", error.Message);
    }

    [Fact]
    public async Task Empty_requests_are_rejected()
    {
        var agent = new AgentNode(Descriptor, chatClient: FakeChatClient.RespondingWith("unused"));
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => agent.ExecuteAsync(new AgentRequest(), context).AsTask());
    }

    [Fact]
    public async Task Exposes_tools_to_the_model_as_aifunctions()
    {
        var chatClient = FakeChatClient.RespondingWith("done");
        var echoTool = DelegateTool.Create("./echo", "Echo", "Echoes text back.", (string text) => text);
        var agent = new AgentNode(Descriptor, tools: [echoTool], chatClient: chatClient, autoInvokeFunctions: false);

        await agent.ExecuteAsync("run", WorkflowExecutionContext.CreateBuilder().Build());

        var (_, options) = Assert.Single(chatClient.Calls);
        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(options!.Tools!));
        Assert.Equal("echo", function.Name);
        Assert.Equal("Echoes text back.", function.Description);
    }

    [Fact]
    public async Task Automatically_invokes_tool_calls()
    {
        var invoked = false;
        var addTool = DelegateTool.Create("./add", "Add", "Adds two integers.", (int a, int b) =>
        {
            invoked = true;
            return a + b;
        });

        var toolCallTurn = new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent("call-1", "add", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 })]);
        var chatClient = new FakeChatClient(
            new ChatResponse(toolCallTurn),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "The sum is 3.")));

        var agent = new AgentNode(Descriptor, tools: [addTool], chatClient: chatClient);
        var response = await agent.ExecuteAsync("What is 1 + 2?", WorkflowExecutionContext.CreateBuilder().Build());

        Assert.True(invoked);
        Assert.Equal("The sum is 3.", response.Text);
        Assert.Equal(2, chatClient.Calls.Count);
        Assert.Contains(
            chatClient.Calls[1].Messages,
            message => message.Contents.OfType<FunctionResultContent>().Any());
    }

    [Fact]
    public async Task Maintains_chat_history_through_the_agent_context()
    {
        var chatClient = FakeChatClient.RespondingWith("First answer.", "Second answer.");
        var agent = new AgentNode(Descriptor, "Be helpful.");
        var context = new AgentWorkflowExecutionContext(
            WorkflowExecutionContext.CreateBuilder().Build(),
            chatClient);

        var first = await agent.ExecuteAsync("First question?", context);
        Assert.Equal("First answer.", first.Text);
        Assert.Equal(2, context.ChatHistory.Count); // user turn + assistant turn

        var second = await agent.ExecuteAsync("Second question?", context);
        Assert.Equal("Second answer.", second.Text);
        Assert.Equal(4, context.ChatHistory.Count);

        // The second call must include: system + prior user/assistant turns + the new user turn.
        Assert.Equal(4, chatClient.Calls[1].Messages.Count);
        Assert.Equal("First question?", chatClient.Calls[1].Messages[1].Text);
    }

    [Fact]
    public void From_definition_resolves_tools_from_the_catalog()
    {
        var definition = new AgentDefinition(
            NodeId.Parse("acme/researcher@1.0.0"),
            "Researcher",
            instructions: "Research.",
            tools: [new ToolReference(NodeId.Parse("acme/web-search@2.0.0"))]);

        // The catalog holds a newer version; resolution falls back to the version-insensitive match.
        var catalog = new ToolCatalog
        {
            DelegateTool.Create("acme/web-search@2.1.0", "Web Search", "Searches the web.", (string query) => query),
        };

        var agent = AgentNode.FromDefinition(definition, catalog);

        Assert.Equal(definition.Id, agent.Id);
        Assert.Equal("Research.", agent.Instructions);
        Assert.Single(agent.Tools);
    }

    [Fact]
    public void From_definition_fails_on_unresolvable_tools()
    {
        var definition = new AgentDefinition(
            NodeId.Parse("acme/researcher@1.0.0"),
            "Researcher",
            tools: [new ToolReference(NodeId.Parse("acme/missing@1.0.0"))]);

        var withEmptyCatalog = Assert.Throws<InvalidOperationException>(
            () => AgentNode.FromDefinition(definition, new ToolCatalog()));
        Assert.Contains("acme/missing@1.0.0", withEmptyCatalog.Message);

        Assert.Throws<InvalidOperationException>(() => AgentNode.FromDefinition(definition));
    }
}
