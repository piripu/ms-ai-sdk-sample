using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Tools;

namespace Ovi.Sdk.Agents;

/// <summary>
/// An agent as a workflow node. An agent is a <see cref="Node{TInput, TOutput}"/> with extra
/// connections: the <see cref="Tools"/> it may call (exposed to the model as
/// Microsoft.Extensions.AI <see cref="AIFunction"/>s) and — in the future — memory.
/// </summary>
/// <remarks>
/// The chat client is resolved per execution, in priority order: the client fixed on this node,
/// then the context's <see cref="AgentChatFeature"/> (attached directly or via
/// <see cref="AgentWorkflowExecutionContext"/>), then an <see cref="IChatClient"/> registered in
/// <see cref="WorkflowExecutionContext.RuntimeServices"/> — a missing client is a
/// <see cref="ResolutionError"/> failure, a chat-client exception an <see cref="ExecutionError"/>.
/// Any <see cref="IChatClient"/> works: an Ollama client, a cloud provider's client, or the SDK's
/// own (intentionally incomplete) <see cref="MultipartHttpChatClient"/>.
/// </remarks>
public sealed class AgentNode : Node<AgentRequest, AgentResponse>
{
    public AgentNode(
        NodeDescriptor descriptor,
        string? instructions = null,
        IEnumerable<ITool>? tools = null,
        IChatClient? chatClient = null,
        bool autoInvokeFunctions = true)
        : base(descriptor)
    {
        Instructions = instructions;
        Tools = tools?.ToArray() ?? [];
        ChatClient = chatClient;
        AutoInvokeFunctions = autoInvokeFunctions;
    }

    /// <summary>The system instructions that steer the agent.</summary>
    public string? Instructions { get; }

    /// <summary>The tools this agent may call.</summary>
    public IReadOnlyList<ITool> Tools { get; }

    /// <summary>A chat client fixed on this node; when null, the client is resolved from the context.</summary>
    public IChatClient? ChatClient { get; }

    /// <summary>
    /// When true (default) and the agent has tools, the chat client is wrapped with
    /// Microsoft.Extensions.AI function invocation so tool calls are executed automatically.
    /// </summary>
    public bool AutoInvokeFunctions { get; }

    /// <summary>
    /// Materializes an agent from its declarative <see cref="AgentDefinition"/>, resolving tool
    /// references through <paramref name="toolCatalog"/>; an unresolvable tool yields a
    /// <see cref="ResolutionError"/> failure.
    /// </summary>
    public static Result<AgentNode> FromDefinition(
        AgentDefinition definition,
        IToolCatalog? toolCatalog = null,
        IChatClient? chatClient = null,
        bool autoInvokeFunctions = true)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var tools = new List<ITool>(definition.Tools.Count);
        foreach (var reference in definition.Tools)
        {
            if (toolCatalog is null || !toolCatalog.TryResolve(reference, out var tool))
            {
                return new ResolutionError(
                    $"Tool '{reference.Id}' referenced by agent '{definition.Id}' could not be resolved"
                    + (toolCatalog is null ? " because no tool catalog was provided." : "."),
                    hint: "Register the tool in the IToolCatalog passed to FromDefinition.");
            }

            tools.Add(tool);
        }

        return new AgentNode(definition.ToDescriptor(), definition.Instructions, tools, chatClient, autoInvokeFunctions);
    }

    public override async ValueTask<Result<AgentResponse>> ExecuteAsync(AgentRequest input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        var logger = context.GetLogger<AgentNode>();
        var chat = context.GetFeature<AgentChatFeature>();

        var (client, clientSource) =
            ChatClient is not null ? (ChatClient, "node")
            : chat?.ChatClient is { } featureClient ? (featureClient, "feature")
            : context.GetService<IChatClient>() is { } serviceClient ? ((IChatClient?)serviceClient, "services")
            : (null, "none");

        if (client is null)
        {
            AgentLog.NoChatClient(logger, Id);
            return new ResolutionError(
                $"Agent '{Id}' has no chat client.",
                hint: "Provide one on the node, attach an AgentChatFeature to the context (or use AgentWorkflowExecutionContext), or register an IChatClient in RuntimeServices.");
        }

        var requestMessages = BuildRequestMessages(input);
        if (requestMessages.Count == 0)
        {
            return new ValidationError("The agent request must contain a prompt or at least one message.");
        }

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(Instructions))
        {
            messages.Add(new ChatMessage(ChatRole.System, Instructions));
        }

        if (chat is not null)
        {
            messages.AddRange(chat.ChatHistory);
        }

        messages.AddRange(requestMessages);

        ChatOptions? options = null;
        if (Tools.Count > 0)
        {
            options = new ChatOptions
            {
                Tools = Tools.Select(AITool (tool) => tool.AsAIFunction()).ToList(),
            };

            if (AutoInvokeFunctions)
            {
                client = EnsureFunctionInvocation(client, context);
            }
        }

        AgentLog.Sending(logger, Id, messages.Count, Tools.Count, clientSource);

        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(messages, options, context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            AgentLog.ChatFailed(logger, exception, Id);
            return ExecutionError.FromException(exception);
        }

        if (chat is not null)
        {
            foreach (var message in requestMessages)
            {
                chat.ChatHistory.Add(message);
            }

            foreach (var message in response.Messages)
            {
                chat.ChatHistory.Add(message);
            }
        }

        AgentLog.Received(logger, Id, response.Text.Length);
        return new AgentResponse(response);
    }

    private static List<ChatMessage> BuildRequestMessages(AgentRequest input)
    {
        var messages = new List<ChatMessage>();
        if (input.Messages is not null)
        {
            messages.AddRange(input.Messages);
        }

        if (!string.IsNullOrWhiteSpace(input.Prompt))
        {
            messages.Add(new ChatMessage(ChatRole.User, input.Prompt));
        }

        return messages;
    }

    private static IChatClient EnsureFunctionInvocation(IChatClient client, WorkflowExecutionContext context) =>
        client.GetService(typeof(FunctionInvokingChatClient)) is not null
            ? client
            : client.AsBuilder().UseFunctionInvocation().Build(context.RuntimeServices);
}

/// <summary>Source-generated log messages for agent execution.</summary>
internal static partial class AgentLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Agent {NodeId} sending {MessageCount} messages with {ToolCount} tools (chat client from {ClientSource})")]
    public static partial void Sending(ILogger logger, NodeId nodeId, int messageCount, int toolCount, string clientSource);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Agent {NodeId} received a response of {ResponseLength} characters")]
    public static partial void Received(ILogger logger, NodeId nodeId, int responseLength);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Agent {NodeId} has no chat client")]
    public static partial void NoChatClient(ILogger logger, NodeId nodeId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Chat client call failed for agent {NodeId}")]
    public static partial void ChatFailed(ILogger logger, Exception exception, NodeId nodeId);
}
