using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
/// <see cref="ChatTriggerPayload"/>, reporting malformed bodies as <see cref="ValidationError"/>
/// failures instead of throwing.
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

    public override ValueTask<Result<ChatTriggerPayload>> ExecuteAsync(ChatTriggerPayload input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ValueTask.FromResult(Result<ChatTriggerPayload>.Success(input));
    }

    /// <summary>Converts a JSON webhook request into a chat payload (the "chat is a webhook" simplification).</summary>
    public static Result<ChatTriggerPayload> FromWebhook(WebhookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        JsonNode? body;
        try
        {
            body = request.ParseJsonBody();
        }
        catch (JsonException exception)
        {
            return new ValidationError("The webhook body is not valid JSON.", exception.Message);
        }

        if (body is not JsonObject json)
        {
            return new ValidationError("The webhook body must be a JSON object to be treated as a chat message.");
        }

        if (json["message"] is not JsonValue messageValue
            || !messageValue.TryGetValue<string>(out var message)
            || string.IsNullOrWhiteSpace(message))
        {
            return new ValidationError("The webhook body must contain a non-empty string 'message' property.");
        }

        return new ChatTriggerPayload(
            message,
            sessionId: json["sessionId"] is JsonValue session && session.TryGetValue<string>(out var sessionId) ? sessionId : null,
            userId: json["userId"] is JsonValue user && user.TryGetValue<string>(out var userId) ? userId : null);
    }
}
