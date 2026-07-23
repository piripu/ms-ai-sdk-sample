using Microsoft.Extensions.AI;

namespace Ovi.Sdk.Tests.Support;

/// <summary>A scriptable IChatClient test double that records every call it receives.</summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new();

    public FakeChatClient(params ChatResponse[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(response);
        }
    }

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public static FakeChatClient RespondingWith(params string[] texts) =>
        new([.. texts.Select(text => new ChatResponse(new ChatMessage(ChatRole.Assistant, text)))]);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(([.. messages], options));
        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, "(no scripted response)"));
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
