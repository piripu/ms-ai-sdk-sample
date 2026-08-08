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

        var result = await agent.ExecuteAsync("Hello?", context);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hi there!", result.Value.Text);
        Assert.NotNull(result.Value.RawResponse);

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

        var result = await agent.ExecuteAsync("Hello?", context);

        Assert.Equal("From services.", result.Value.Text);
    }

    [Fact]
    public async Task Missing_chat_client_is_a_resolution_error()
    {
        var agent = new AgentNode(Descriptor);
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        var result = await agent.ExecuteAsync("Hello?", context);

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ResolutionError>(result.Error);
        Assert.Contains("chat client", error.Message);
        Assert.Contains("RuntimeServices", error.Hint);
    }

    [Fact]
    public async Task Empty_requests_are_validation_errors()
    {
        var agent = new AgentNode(Descriptor, chatClient: FakeChatClient.RespondingWith("unused"));
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        var result = await agent.ExecuteAsync(new AgentRequest(), context);

        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("prompt or at least one message", result.Error.Message);
    }

    [Fact]
    public async Task Chat_client_exceptions_become_execution_errors()
    {
        var agent = new AgentNode(Descriptor, chatClient: new ThrowingChatClient(new HttpRequestException("connection refused")));
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        var result = await agent.ExecuteAsync("Hello?", context);

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ExecutionError>(result.Error);
        Assert.Contains("connection refused", error.Message);
        Assert.IsType<HttpRequestException>(error.Exception);
    }

    [Fact]
    public async Task Exposes_tools_to_the_model_as_aifunctions()
    {
        var chatClient = FakeChatClient.RespondingWith("done");
        var echoTool = Tool.FromDelegate("./echo", "Echo", "Echoes text back.", (string text) => text);
        var agent = new AgentNode(Descriptor, tools: [echoTool], chatClient: chatClient, autoInvokeFunctions: false);

        var result = await agent.ExecuteAsync("run", WorkflowExecutionContext.CreateBuilder().Build());

        Assert.True(result.IsSuccess);
        var (_, options) = Assert.Single(chatClient.Calls);
        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(options!.Tools!));
        Assert.Equal("echo", function.Name);
        Assert.Equal("Echoes text back.", function.Description);
    }

    [Fact]
    public async Task Automatically_invokes_tool_calls()
    {
        var invoked = false;
        var addTool = Tool.FromDelegate("./add", "Add", "Adds two integers.", (int a, int b) =>
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
        var result = await agent.ExecuteAsync("What is 1 + 2?", WorkflowExecutionContext.CreateBuilder().Build());

        Assert.True(invoked);
        Assert.Equal("The sum is 3.", result.Value.Text);
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
        Assert.Equal("First answer.", first.Value.Text);
        Assert.Equal(2, context.ChatHistory.Count); // user turn + assistant turn

        var second = await agent.ExecuteAsync("Second question?", context);
        Assert.Equal("Second answer.", second.Value.Text);
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
            Tool.FromDelegate("acme/web-search@2.1.0", "Web Search", "Searches the web.", (string query) => query),
        };

        var result = AgentNode.FromDefinition(definition, catalog);

        Assert.True(result.IsSuccess);
        var agent = result.Value;
        Assert.Equal(definition.Id, agent.Id);
        Assert.Equal("Research.", agent.Instructions);
        Assert.Single(agent.Tools);
    }

    [Fact]
    public void From_definition_reports_unresolvable_tools_as_resolution_errors()
    {
        var definition = new AgentDefinition(
            NodeId.Parse("acme/researcher@1.0.0"),
            "Researcher",
            tools: [new ToolReference(NodeId.Parse("acme/missing@1.0.0"))]);

        var withEmptyCatalog = AgentNode.FromDefinition(definition, new ToolCatalog());
        Assert.True(withEmptyCatalog.IsFailure);
        var error = Assert.IsType<ResolutionError>(withEmptyCatalog.Error);
        Assert.Contains("acme/missing@1.0.0", error.Message);

        Assert.True(AgentNode.FromDefinition(definition).IsFailure);
    }

    [Fact]
    public async Task Chat_capability_composes_onto_any_context_as_a_feature()
    {
        var chatClient = FakeChatClient.RespondingWith("Feature answer.");
        var agent = new AgentNode(Descriptor, "Be helpful.");

        // No context subclassing: a plain context with the chat feature attached behaves like
        // an AgentWorkflowExecutionContext.
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithFeature(new AgentChatFeature(chatClient))
            .Build();

        var result = await agent.ExecuteAsync("Question?", context);

        Assert.Equal("Feature answer.", result.Value.Text);
        var chat = context.GetRequiredFeature<AgentChatFeature>();
        Assert.Equal(2, chat.ChatHistory.Count); // user turn + assistant turn
    }

    [Fact]
    public void LastMessage_reflects_the_most_recent_history_entry()
    {
        var feature = new AgentChatFeature(FakeChatClient.RespondingWith("unused"));
        Assert.Null(feature.LastMessage);

        feature.ChatHistory.Add(new ChatMessage(ChatRole.User, "Hi"));
        Assert.Equal("Hi", feature.LastMessage!.Text);

        feature.ChatHistory.Add(new ChatMessage(ChatRole.Assistant, "Hello!"));
        Assert.Equal("Hello!", feature.LastMessage!.Text);
    }

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(exception);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
