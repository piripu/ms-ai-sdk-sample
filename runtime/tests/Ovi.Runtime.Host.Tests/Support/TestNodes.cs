using Ovi.Sdk.Nodes;

namespace Ovi.Runtime.Host.Tests.Support;

/// <summary>Small, reusable node fixtures for host tests — plain nodes, no engine, per the SDK's atomic-testing style.</summary>
internal static class TestNodes
{
    public static DelegateNode<string, string> Upper(string name = "upper") =>
        Node.Create<string, string>(
            new NodeDescriptor(NodeId.BuiltIn(name), name),
            (input, _) => input.ToUpperInvariant());

    public static DelegateNode<string, string> Exclaim(string name = "exclaim") =>
        Node.Create<string, string>(
            new NodeDescriptor(NodeId.BuiltIn(name), name),
            (input, _) => input + "!");

    public static DelegateNode<string, string> Failing(string name, string message) =>
        Node.Create<string, string>(
            new NodeDescriptor(NodeId.BuiltIn(name), name),
            (string _, WorkflowExecutionContext _) => ValueTask.FromResult(Result<string>.Failure(new ValidationError(message))));

    /// <summary>Constructed directly (not via <c>Node.Create</c>) so the throw-only lambda body isn't ambiguous between the sync/async overloads.</summary>
    public static DelegateNode<string, string> Throwing(string name, Exception exception) =>
        new(
            new NodeDescriptor(NodeId.BuiltIn(name), name),
            (string _, WorkflowExecutionContext _) => throw exception);

    /// <summary>A node whose completion is externally controlled — for concurrency/timeout tests.</summary>
    public static DelegateNode<string, string> Gate(string name, TaskCompletionSource ready, TaskCompletionSource release) =>
        Node.Create<string, string>(
            new NodeDescriptor(NodeId.BuiltIn(name), name),
            async (input, _) =>
            {
                ready.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return Result<string>.Success(input);
            });

    /// <summary>A node that outlives any timeout a test configures — for soft node-timeout tests.</summary>
    public static DelegateNode<string, string> Never(string name) =>
        Node.Create<string, string>(
            new NodeDescriptor(NodeId.BuiltIn(name), name),
            async (input, context) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), context.CancellationToken).ConfigureAwait(false);
                return Result<string>.Success(input);
            });
}
