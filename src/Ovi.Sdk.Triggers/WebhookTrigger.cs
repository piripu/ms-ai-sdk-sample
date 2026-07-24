using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Triggers;

/// <summary>A transport-agnostic snapshot of an incoming HTTP request that fires a webhook trigger.</summary>
public sealed record WebhookRequest
{
    /// <summary>The HTTP method; webhooks are conventionally POSTs.</summary>
    public string Method { get; init; } = "POST";

    /// <summary>The request path below the webhook's mount point, when known.</summary>
    public string? Path { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    public IReadOnlyDictionary<string, string> Query { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>The raw request body, typically JSON.</summary>
    public string? Body { get; init; }

    public string? ContentType { get; init; }

    /// <summary>Parses <see cref="Body"/> as JSON, or returns <see langword="null"/> when the body is empty.</summary>
    public JsonNode? ParseJsonBody() =>
        string.IsNullOrWhiteSpace(Body) ? null : JsonNode.Parse(Body);
}

/// <summary>
/// A trigger fired by an incoming HTTP request. The runtime (or a test) delivers a
/// <see cref="WebhookRequest"/>; the node passes it through, and normalization into richer payloads
/// composes on top — map the request (see <see cref="ChatTriggerNode.FromWebhook"/>) or wrap it in a
/// delegate node (<c>Node.Create</c>).
/// </summary>
public sealed class WebhookTriggerNode : TriggerNode<WebhookRequest, WebhookRequest>
{
    public WebhookTriggerNode(NodeDescriptor? descriptor = null)
        : base(descriptor ?? DefaultDescriptor)
    {
    }

    public static NodeDescriptor DefaultDescriptor { get; } = new(
        NodeId.BuiltIn("webhook-trigger"),
        "Webhook Trigger",
        "Starts a workflow from an incoming HTTP request.");

    public override ValueTask<WebhookRequest> ExecuteAsync(WebhookRequest input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ValueTask.FromResult(input);
    }
}
