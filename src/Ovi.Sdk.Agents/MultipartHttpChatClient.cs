using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ovi.Sdk.Agents;

/// <summary>
/// A custom <see cref="IChatClient"/> skeleton that sends the conversation as an HTTP POST with
/// <see cref="MultipartFormDataContent"/> to an arbitrary endpoint.
/// </summary>
/// <remarks>
/// <para><b>Intentionally incomplete.</b> The request side works: messages are serialized to a JSON
/// part and posted alongside model/temperature parts. What is NOT implemented yet, pending a real
/// wire contract for the receiving endpoint:</para>
/// <list type="bullet">
/// <item>Mapping the endpoint's response body to a <see cref="ChatResponse"/> — override
/// <see cref="ParseResponse"/> once the contract exists.</item>
/// <item>Streaming (<see cref="GetStreamingResponseAsync"/>).</item>
/// <item>Binary parts for multimodal content (images/files from <see cref="DataContent"/>).</item>
/// </list>
/// <para><see cref="IChatClient"/> is an external contract, so this type keeps exception semantics
/// (no <c>Result</c>); agents wrap its failures into <c>ExecutionError</c> results. For a working
/// client today, use any existing <see cref="IChatClient"/> implementation — e.g. OllamaSharp's
/// client for a local Ollama server — anywhere the SDK accepts a chat client.</para>
/// </remarks>
public class MultipartHttpChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger _logger;

    public MultipartHttpChatClient(Uri endpoint, HttpClient? httpClient = null, string? modelId = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        Endpoint = endpoint;
        ModelId = modelId;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The URL conversations are posted to.</summary>
    public Uri Endpoint { get; }

    /// <summary>The default model id sent with each request (overridable per call via <see cref="ChatOptions.ModelId"/>).</summary>
    public string? ModelId { get; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        using var content = CreateRequestContent(messages, options);
        ChatClientLog.Posting(_logger, Endpoint);

        using var response = await _httpClient.PostAsync(Endpoint, content, cancellationToken).ConfigureAwait(false);
        ChatClientLog.ResponseReceived(_logger, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseResponse(body);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{nameof(MultipartHttpChatClient)} does not support streaming yet.");

    /// <summary>
    /// Builds the multipart form for a conversation: a <c>messages</c> part carrying the serialized
    /// conversation as JSON, plus optional <c>model</c> and <c>temperature</c> parts.
    /// </summary>
    protected virtual MultipartFormDataContent CreateRequestContent(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var content = new MultipartFormDataContent();

        var messageList = messages.Select(message => new { role = message.Role.Value, text = message.Text }).ToList();
        var serializedMessages = JsonSerializer.Serialize(messageList, OviJson.DefaultOptions);
        content.Add(new StringContent(serializedMessages, Encoding.UTF8, "application/json"), "messages");

        if ((options?.ModelId ?? ModelId) is { } model)
        {
            content.Add(new StringContent(model), "model");
        }

        if (options?.Temperature is { } temperature)
        {
            content.Add(new StringContent(temperature.ToString(CultureInfo.InvariantCulture)), "temperature");
        }

        ChatClientLog.RequestBuilt(_logger, messageList.Count, (options?.ModelId ?? ModelId) ?? "(none)");

        // TODO: attach DataContent (images/files) as binary parts once the endpoint contract is defined.
        return content;
    }

    /// <summary>
    /// Maps the endpoint's response body to a <see cref="ChatResponse"/>. Not implemented until the
    /// endpoint's wire contract is defined; override in a derived client to complete it.
    /// </summary>
    protected virtual ChatResponse ParseResponse(string responseBody) =>
        throw new NotImplementedException(
            $"{nameof(MultipartHttpChatClient)} has no response contract yet. Override {nameof(ParseResponse)} to map the endpoint's response to a ChatResponse.");

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>Source-generated log messages for the multipart chat client.</summary>
internal static partial class ChatClientLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Built multipart chat request with {MessageCount} messages for model {Model}")]
    public static partial void RequestBuilt(ILogger logger, int messageCount, string model);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Posting conversation to {Endpoint}")]
    public static partial void Posting(ILogger logger, Uri endpoint);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Chat endpoint responded with HTTP {StatusCode}")]
    public static partial void ResponseReceived(ILogger logger, int statusCode);
}
