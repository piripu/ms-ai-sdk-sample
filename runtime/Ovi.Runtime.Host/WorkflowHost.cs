using Microsoft.Extensions.AI;
using Ovi.Runtime.Host.Middleware;
using Ovi.Runtime.Host.Triggers;
using Ovi.Sdk.Agents;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Workflows;

namespace Ovi.Runtime.Host;

/// <summary>
/// The single entry point of a hosted workflow (or agent). A host is bound to exactly one
/// <see cref="WorkflowGraph"/> for its lifetime — there is no <c>id</c> parameter anywhere on this
/// type, because "which workflow" was answered at <see cref="WorkflowHostBuilder.Build"/> time.
/// Manual calls, normalized webhooks, and <see cref="ITriggerStrategy"/> firings (e.g. a schedule)
/// all go through the same <see cref="RunAsync"/> — the "unify schedule and caller-fired" design —
/// with the same <see cref="RunRequest"/>/<see cref="RunResult"/> shape.
/// </summary>
public sealed class WorkflowHost : IAsyncDisposable
{
    private readonly WorkflowGraph _graph;
    private readonly INodeRegistry _registry;
    private readonly RunMiddlewareDelegate _runPipeline;
    private readonly NodeMiddlewareDelegate _nodePipeline;
    private readonly IServiceProvider? _runtimeServices;
    private readonly WorkflowHostOptions _options;
    private readonly IReadOnlyList<ITriggerStrategy> _triggers;
    private readonly bool _isAgentHost;
    private readonly IChatClient? _chatClient;
    private readonly List<ChatMessage> _chatHistory = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _inFlightLock = new();
    private Task<RunResult>? _inFlight;
    private CancellationTokenSource? _triggerCts;

    internal WorkflowHost(
        WorkflowGraph graph,
        INodeRegistry registry,
        IReadOnlyList<IRunMiddleware> runMiddleware,
        IReadOnlyList<INodeMiddleware> nodeMiddleware,
        IServiceProvider? runtimeServices,
        WorkflowHostOptions options,
        IReadOnlyList<ITriggerStrategy> triggers,
        bool isAgentHost,
        IChatClient? chatClient)
    {
        _graph = graph;
        _registry = registry;
        _runtimeServices = runtimeServices;
        _options = options;
        _triggers = triggers;
        _isAgentHost = isAgentHost;
        _chatClient = chatClient;

        _nodePipeline = ComposeNodePipeline(nodeMiddleware, ExecuteNodeAsync);
        _runPipeline = ComposeRunPipeline(runMiddleware, ExecuteGraphAsync);
    }

    /// <summary>Starts building a host — the only way to construct one.</summary>
    public static WorkflowHostBuilder CreateBuilder() => new();

    /// <summary>The validated graph this host runs.</summary>
    public WorkflowGraph Graph => _graph;

    /// <summary>
    /// Runs the hosted workflow (or agent) once. This is the one entry point: no <c>id</c> parameter
    /// (the host is already bound to its workflow), and the same signature regardless of what
    /// triggered the call — see <see cref="RunRequest.TriggerKind"/>.
    /// </summary>
    public Task<RunResult> RunAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _options.ConcurrencyMode == RunConcurrencyMode.JoinInFlight
            ? RunJoinInFlightAsync(request, cancellationToken)
            : RunSerializedAsync(request, cancellationToken);
    }

    /// <summary>Starts every registered <see cref="ITriggerStrategy"/> (e.g. a schedule); manual/webhook calls to <see cref="RunAsync"/> need no starting.</summary>
    public async Task StartTriggersAsync(CancellationToken cancellationToken = default)
    {
        _triggerCts?.Dispose();
        _triggerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        foreach (var trigger in _triggers)
        {
            await trigger.StartAsync(this, _triggerCts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Stops every registered <see cref="ITriggerStrategy"/>. Safe to call even if none were started.</summary>
    public async Task StopTriggersAsync(CancellationToken cancellationToken = default)
    {
        foreach (var trigger in _triggers)
        {
            await trigger.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _triggerCts?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        await StopTriggersAsync().ConfigureAwait(false);
        _triggerCts?.Dispose();
        _gate.Dispose();
    }

    /// <summary>
    /// Waits for whatever run is currently in flight to finish (used by <c>HostProcessManager</c> to
    /// drain before shutting down). Completes immediately when the host is idle; never throws — a
    /// failed or cancelled in-flight run is not this caller's failure.
    /// </summary>
    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        Task<RunResult>? current;
        lock (_inFlightLock)
        {
            current = _inFlight;
        }

        if (current is null)
        {
            return;
        }

        try
        {
            await current.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The in-flight run's own outcome is irrelevant here — only that it finished.
        }
    }

    private async Task<RunResult> RunSerializedAsync(RunRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var task = ExecuteAsync(request, cancellationToken);
            lock (_inFlightLock)
            {
                _inFlight = task;
            }

            return await task.ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<RunResult> RunJoinInFlightAsync(RunRequest request, CancellationToken cancellationToken)
    {
        lock (_inFlightLock)
        {
            if (_inFlight is { IsCompleted: false } inFlight)
            {
                return inFlight;
            }

            var task = ExecuteAsync(request, cancellationToken);
            _inFlight = task;
            return task;
        }
    }

    private async Task<RunResult> ExecuteAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var timeout = _options.TimeoutPolicy?.GetTimeout();
        using var timeoutCts = timeout is { } budget ? new CancellationTokenSource(budget) : null;
        using var linkedCts = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var workflowInfo = new WorkflowInfo(_graph.Definition.Id.ToString(), Guid.NewGuid().ToString("n"), _graph.Definition.Name)
        {
            StartedAt = DateTimeOffset.UtcNow,
        };

        var contextBuilder = WorkflowExecutionContext.CreateBuilder()
            .WithWorkflow(workflowInfo)
            .WithCancellationToken(linkedCts.Token);

        if (_runtimeServices is not null)
        {
            contextBuilder = contextBuilder.WithRuntimeServices(_runtimeServices);
        }

        var context = contextBuilder.Build();

        if (request.State is not null)
        {
            foreach (var (key, value) in request.State)
            {
                context.WorkflowState.SetValue(key, value?.DeepClone());
            }
        }

        if (_isAgentHost && _chatClient is not null)
        {
            // The host owns conversation history across runs — a run-scoped feature attached fresh
            // each call, backed by a list that outlives any single run.
            context.SetFeature(new AgentChatFeature(_chatClient, _chatHistory));
        }

        var entryInput = _isAgentHost
            ? new AgentRequest { Prompt = request.Prompt, Messages = request.Messages }
            : request.Input;

        context.SetFeature(new RunIoFeature(request) { Input = entryInput });
        context.SetFeature(new RunTimingFeature());

        try
        {
            await _runPipeline(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            var timing = context.GetRequiredFeature<RunTimingFeature>();
            timing.Duration = DateTimeOffset.UtcNow - timing.StartedAt;
            return new RunResult
            {
                Output = Result<object?>.Failure(new ExecutionError($"The run exceeded its timeout ({timeout}).")),
                Diagnostics = ToDiagnostics(timing, request),
            };
        }

        var io = context.GetRequiredFeature<RunIoFeature>();
        var finalTiming = context.GetRequiredFeature<RunTimingFeature>();

        return new RunResult
        {
            Output = io.Output ?? Result<object?>.Failure(new ExecutionError("The run produced no result.")),
            Diagnostics = ToDiagnostics(finalTiming, request),
        };
    }

    private static RunDiagnostics ToDiagnostics(RunTimingFeature timing, RunRequest request) => new()
    {
        StartedAt = timing.StartedAt,
        Duration = timing.Duration,
        NodeDurations = timing.NodeDurations,
        CorrelationId = request.CorrelationId,
    };

    /// <summary>The run pipeline's terminal stage: walks <see cref="WorkflowGraph.ExecutionOrder"/>, feeding each node's output to its single downstream node.</summary>
    private async ValueTask ExecuteGraphAsync(WorkflowExecutionContext context)
    {
        var runIo = context.GetRequiredFeature<RunIoFeature>();
        var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var key in _graph.ExecutionOrder)
        {
            if (!_graph.TryGetNode(key, out var nodeDefinition) || nodeDefinition is null)
            {
                runIo.Output = Result<object?>.Failure(new ExecutionError($"Node '{key}' is missing from the graph."));
                return;
            }

            var upstream = _graph.GetUpstream(key);
            object? input;
            switch (upstream.Count)
            {
                case 0:
                    input = runIo.Input;
                    break;
                case 1:
                    input = outputs[upstream[0]];
                    break;
                default:
                    // Ovi.Sdk.Workflows connections are single-input/single-output today (documented
                    // follow-up for ports); fan-in has no defined merge semantics yet, so this host
                    // rejects it explicitly rather than guessing one.
                    runIo.Output = Result<object?>.Failure(new ValidationError(
                        $"Node '{key}' has {upstream.Count} upstream connections; WorkflowHost only " +
                        "supports single-input nodes (fan-in merge semantics are not yet defined by Ovi.Sdk.Workflows)."));
                    return;
            }

            if (!_registry.TryResolve(nodeDefinition.Node, out var node) || node is null)
            {
                runIo.Output = Result<object?>.Failure(new ResolutionError(
                    $"No node instance is registered for node type '{nodeDefinition.Node}' (workflow key '{key}').",
                    hint: "Register an INode instance for this NodeId with WorkflowHostBuilder.MapNode before building the host."));
                return;
            }

            context.SetFeature(new NodeIoFeature(node, key, input));
            await _nodePipeline(context).ConfigureAwait(false);

            var nodeIo = context.GetRequiredFeature<NodeIoFeature>();
            var result = nodeIo.Output ?? Result<object?>.Failure(new ExecutionError($"Node '{key}' produced no result."));
            if (result.IsFailure)
            {
                runIo.Output = result;
                return;
            }

            outputs[key] = result.Value;
        }

        var sinks = _graph.Definition.Nodes
            .Select(node => node.Key)
            .Where(nodeKey => _graph.GetDownstream(nodeKey).Count == 0)
            .ToArray();

        runIo.Output = sinks.Length == 1
            ? Result<object?>.Success(outputs[sinks[0]])
            : Result<object?>.Success((object?)sinks.ToDictionary(sinkKey => sinkKey, sinkKey => outputs[sinkKey], StringComparer.Ordinal));
    }

    /// <summary>The node pipeline's terminal stage: the actual untyped <c>INode.ExecuteAsync</c> call.</summary>
    private static async ValueTask ExecuteNodeAsync(WorkflowExecutionContext context)
    {
        var nodeIo = context.GetRequiredFeature<NodeIoFeature>();
        nodeIo.Output = await nodeIo.Node.ExecuteAsync(nodeIo.Input, context).ConfigureAwait(false);
    }

    private static RunMiddlewareDelegate ComposeRunPipeline(IReadOnlyList<IRunMiddleware> middleware, RunMiddlewareDelegate terminal)
    {
        var pipeline = terminal;
        for (var i = middleware.Count - 1; i >= 0; i--)
        {
            var current = middleware[i];
            var next = pipeline;
            pipeline = context => current.InvokeAsync(context, next);
        }

        return pipeline;
    }

    private static NodeMiddlewareDelegate ComposeNodePipeline(IReadOnlyList<INodeMiddleware> middleware, NodeMiddlewareDelegate terminal)
    {
        var pipeline = terminal;
        for (var i = middleware.Count - 1; i >= 0; i--)
        {
            var current = middleware[i];
            var next = pipeline;
            pipeline = context => current.InvokeAsync(context, next);
        }

        return pipeline;
    }
}
