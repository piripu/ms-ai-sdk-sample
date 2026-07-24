using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Triggers;

/// <summary>An incoming chat message that starts (or continues) a chat-driven workflow.</summary>
public sealed record ChatTriggerPayload
{
    public ChatTriggerPayload()
    {
    }

    [SetsRequiredMembers]
    public ChatTriggerPayload(string message, string? sessionId = null, string? userId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Message = message;
        SessionId = sessionId;
        UserId = userId;
    }

    /// <summary>The user's chat message text.</summary>
    public required string Message { get; init; }

    /// <summary>Correlates messages belonging to one conversation.</summary>
    public string? SessionId { get; init; }

    public string? UserId { get; init; }

    /// <summary>Additional transport metadata (channel ids, locale, …).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// A trigger fired by an incoming chat message — the natural entry point in front of an agent. A chat
/// trigger is effectively a specialized webhook: <see cref="FromWebhook"/> lifts a JSON webhook
/// request (<c>{"message": ..., "sessionId": ..., "userId": ...}</c>) into a
/// <see cref="ChatTriggerPayload"/>.
/// </summary>
public sealed class ChatTriggerNode : TriggerNode<ChatTriggerPayload, ChatTriggerPayload>
{
    public ChatTriggerNode(NodeDescriptor? descriptor = null)
        : base(descriptor ?? DefaultDescriptor)
    {
    }

    public static NodeDescriptor DefaultDescriptor { get; } = new(
        NodeId.BuiltIn("chat-trigger"),
        "Chat Trigger",
        "Starts a workflow from an incoming chat message; a chat trigger is a specialized webhook.");

    public override ValueTask<ChatTriggerPayload> ExecuteAsync(ChatTriggerPayload input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ValueTask.FromResult(input);
    }

    /// <summary>Converts a JSON webhook request into a chat payload (the "chat is a webhook" simplification).</summary>
    public static ChatTriggerPayload FromWebhook(WebhookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ParseJsonBody() is not JsonObject body)
        {
            throw new FormatException("The webhook body must be a JSON object to be treated as a chat message.");
        }

        var message = body["message"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new FormatException("The webhook body must contain a non-empty 'message' property.");
        }

        return new ChatTriggerPayload(
            message,
            sessionId: body["sessionId"]?.GetValue<string>(),
            userId: body["userId"]?.GetValue<string>());
    }
}
