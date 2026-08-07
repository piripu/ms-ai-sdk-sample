namespace Ovi.Runtime.Host.Timeouts;

/// <summary>
/// Per-node timeout configuration, keyed by the node's workflow instance key (see
/// <c>WorkflowNodeDefinition.Key</c>). Complements the host's global <see cref="IRunTimeoutPolicy"/>:
/// a node can have both a global run budget and its own tighter (or looser) limit. Enforced by
/// <see cref="Middleware.NodeTimeoutMiddleware"/>.
/// </summary>
public sealed class NodeTimeoutOptions
{
    private readonly Dictionary<string, TimeSpan> _timeouts = new(StringComparer.Ordinal);

    /// <summary>Applied to any node without a specific entry; <see langword="null"/> means "no per-node timeout by default".</summary>
    public TimeSpan? DefaultTimeout { get; set; }

    /// <summary>Sets the timeout for a specific node instance, overriding <see cref="DefaultTimeout"/> for it.</summary>
    public NodeTimeoutOptions SetTimeout(string nodeKey, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeouts[nodeKey] = timeout;
        return this;
    }

    /// <summary>The effective timeout for a node instance, or <see langword="null"/> when none applies.</summary>
    public TimeSpan? GetTimeout(string nodeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        return _timeouts.TryGetValue(nodeKey, out var timeout) ? timeout : DefaultTimeout;
    }
}
