using System.Net;
using Microsoft.Extensions.AI;
using Ovi.Sdk.Agents;
using Xunit;

namespace Ovi.Sdk.Tests;

public class MultipartHttpChatClientTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? ContentType { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    [Fact]
    public async Task Posts_the_conversation_as_multipart_form_data()
    {
        var handler = new CapturingHandler();
        using var client = new MultipartHttpChatClient(
            new Uri("https://example.test/chat"),
            new HttpClient(handler),
            modelId: "test-model");

        // The request side works; response parsing is intentionally incomplete until the
        // endpoint's wire contract is defined.
        await Assert.ThrowsAsync<NotImplementedException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal("multipart/form-data", handler.ContentType);
        Assert.NotNull(handler.Body);
        Assert.Contains("name=messages", handler.Body);
        Assert.Contains("\"hi\"", handler.Body);
        Assert.Contains("name=model", handler.Body);
        Assert.Contains("test-model", handler.Body);
    }

    [Fact]
    public void Streaming_is_not_supported_yet()
    {
        using var client = new MultipartHttpChatClient(
            new Uri("https://example.test/chat"),
            new HttpClient(new CapturingHandler()));

        Assert.Throws<NotSupportedException>(
            () => client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }
}
