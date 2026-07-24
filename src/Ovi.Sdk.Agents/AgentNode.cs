using Microsoft.Extensions.AI;
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
/// then <see cref="AgentWorkflowExecutionContext.ChatClient"/>, then an <see cref="IChatClient"/>
/// registered in <see cref="WorkflowExecutionContext.RuntimeServices"/>. Any
/// <see cref="IChatClient"/> works: an Ollama client, a cloud provider's client, or the SDK's own
/// (intentionally incomplete) <see cref="MultipartHttpChatClient"/>.
/// </remarks>
public class AgentNode : Node<AgentRequest, AgentResponse>
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
    /// references through <paramref name="toolCatalog"/>.
    /// </summary>
    public static AgentNode FromDefinition(
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
                throw new InvalidOperationException(
                    $"Tool '{reference.Id}' referenced by agent '{definition.Id}' could not be resolved"
                    + (toolCatalog is null ? " because no tool catalog was provided." : "."));
            }

            tools.Add(tool);
        }

        return new AgentNode(definition.ToDescriptor(), definition.Instructions, tools, chatClient, autoInvokeFunctions);
    }

    public override async ValueTask<AgentResponse> ExecuteAsync(AgentRequest input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        var agentContext = context as AgentWorkflowExecutionContext;
        var client = ChatClient
            ?? agentContext?.ChatClient
            ?? context.GetService<IChatClient>()
            ?? throw new InvalidOperationException(
                $"Agent '{Id}' has no chat client. Provide one on the node, use an AgentWorkflowExecutionContext, or register an IChatClient in RuntimeServices.");

        var requestMessages = BuildRequestMessages(input);

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(Instructions))
        {
            messages.Add(new ChatMessage(ChatRole.System, Instructions));
        }

        if (agentContext is not null)
        {
            messages.AddRange(agentContext.ChatHistory);
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

        var response = await client.GetResponseAsync(messages, options, context.CancellationToken).ConfigureAwait(false);

        if (agentContext is not null)
        {
            foreach (var message in requestMessages)
            {
                agentContext.ChatHistory.Add(message);
            }

            foreach (var message in response.Messages)
            {
                agentContext.ChatHistory.Add(message);
            }
        }

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

        return messages.Count > 0
            ? messages
            : throw new ArgumentException("The agent request must contain a prompt or at least one message.", nameof(input));
    }

    private static IChatClient EnsureFunctionInvocation(IChatClient client, WorkflowExecutionContext context) =>
        client.GetService(typeof(FunctionInvokingChatClient)) is not null
            ? client
            : client.AsBuilder().UseFunctionInvocation().Build(context.RuntimeServices);
}
