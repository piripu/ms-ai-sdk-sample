using Microsoft.Extensions.AI;
using Ovi.Runtime.Host.Middleware;
using Ovi.Runtime.Host.Timeouts;
using Ovi.Runtime.Host.Triggers;
using Ovi.Sdk.Agents;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Packaging;
using Ovi.Sdk.Tools;
using Ovi.Sdk.Workflows;

namespace Ovi.Runtime.Host;

/// <summary>
/// Builds a <see cref="WorkflowHost"/>: pick exactly one definition source (<see cref="UseWorkflow"/>
/// or <see cref="UseAgent"/>), register the <see cref="INode"/> instances it needs
/// (<see cref="MapNode(INode)"/>), configure middleware/triggers/timeouts, then <see cref="Build"/>.
/// </summary>
public sealed class WorkflowHostBuilder
{
    private readonly NodeRegistry _registry = new();
    private readonly List<IRunMiddleware> _runMiddleware = [];
    private readonly List<INodeMiddleware> _nodeMiddleware = [];
    private readonly List<ITriggerStrategy> _triggers = [];
    private readonly WorkflowHostOptions _options = new();
    private WorkflowDefinition? _definition;
    private IServiceProvider? _runtimeServices;
    private IChatClient? _chatClient;
    private bool _isAgentHost;
    private bool _useDefaultMiddleware = true;

    /// <summary>Hosts a full workflow definition.</summary>
    public WorkflowHostBuilder UseWorkflow(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        _isAgentHost = false;
        return this;
    }

    /// <summary>
    /// Hosts a bare agent: wraps <paramref name="definition"/> into a one-node workflow (see
    /// <see cref="AgentWorkflowFactory.Wrap"/>) and registers <paramref name="agentNode"/> as that
    /// node's instance. <see cref="RunRequest.Prompt"/>/<see cref="RunRequest.Messages"/> become the
    /// entry node's input, and — if <see cref="WithChatClient"/> was called — the run's
    /// <see cref="AgentChatFeature"/> carries conversation history that persists across calls to this
    /// host, so agent nodes can read "current chat history" and its last message.
    /// </summary>
    public WorkflowHostBuilder UseAgent(AgentDefinition definition, AgentNode agentNode)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(agentNode);

        MapNode(definition.Id, agentNode);
        UseWorkflow(AgentWorkflowFactory.Wrap(definition));
        _isAgentHost = true; // must run after UseWorkflow, which resets this flag for the general case
        return this;
    }

    /// <summary>
    /// Hosts an opened <see cref="OviPackage"/>'s default workflow or agent — "find the location of
    /// the default workflow" answered: <see cref="OviPackageManifest.DefaultEntry"/> when set, else
    /// the package's one <see cref="PackagedNodeKind.Workflow"/>/<see cref="PackagedNodeKind.Agent"/>
    /// entry when there's exactly one. A workflow entry only loads the definition — you still
    /// <see cref="MapNode(INode)"/> the node types it references; an agent entry is fully
    /// materialized into a runnable <see cref="AgentNode"/> via <paramref name="toolCatalog"/>/
    /// <paramref name="chatClient"/> the same way <see cref="AgentNode.FromDefinition"/> does.
    /// </summary>
    public WorkflowHostBuilder UsePackage(OviPackage package, IToolCatalog? toolCatalog = null, IChatClient? chatClient = null)
    {
        ArgumentNullException.ThrowIfNull(package);

        var entry = ResolveDefaultEntry(package);
        if (entry.Path is null)
        {
            throw new InvalidOperationException($"The package's default entry '{entry.Id}' has no asset path to load.");
        }

        var content = package.ReadAllText(entry.Path);
        var isJson = string.Equals(Path.GetExtension(entry.Path), ".json", StringComparison.OrdinalIgnoreCase);

        switch (entry.Kind)
        {
            case PackagedNodeKind.Workflow:
                var workflow = isJson ? WorkflowDefinitionSerializer.FromJson(content) : WorkflowDefinitionSerializer.FromYaml(content);
                if (workflow.IsFailure)
                {
                    throw new InvalidOperationException($"The package's default workflow entry is invalid: {workflow.Error}");
                }

                return UseWorkflow(workflow.Value);

            case PackagedNodeKind.Agent:
                var agentDefinition = isJson ? AgentDefinitionSerializer.FromJson(content) : AgentDefinitionSerializer.FromYaml(content);
                if (agentDefinition.IsFailure)
                {
                    throw new InvalidOperationException($"The package's default agent entry is invalid: {agentDefinition.Error}");
                }

                var agentNode = AgentNode.FromDefinition(agentDefinition.Value, toolCatalog, chatClient);
                if (agentNode.IsFailure)
                {
                    throw new InvalidOperationException($"The package's default agent could not be materialized: {agentNode.Error}");
                }

                return UseAgent(agentDefinition.Value, agentNode.Value);

            default:
                throw new InvalidOperationException(
                    $"The package's default entry '{entry.Id}' has kind '{entry.Kind}', which a WorkflowHost cannot run directly (expected Workflow or Agent).");
        }
    }

    private static PackagedNodeEntry ResolveDefaultEntry(OviPackage package)
    {
        var manifest = package.Manifest;
        if (manifest.DefaultEntry is { } defaultId)
        {
            return manifest.Nodes.FirstOrDefault(entry => entry.Id.Equals(defaultId) || entry.Id.IsSameNode(defaultId))
                ?? throw new InvalidOperationException(
                    $"Package '{manifest.PackageId}' declares DefaultEntry '{defaultId}', but no node entry with that id exists.");
        }

        var candidates = manifest.Nodes.Where(entry => entry.Kind is PackagedNodeKind.Workflow or PackagedNodeKind.Agent).ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"Package '{manifest.PackageId}' has no DefaultEntry and no Workflow/Agent entry to fall back to."),
            _ => throw new InvalidOperationException(
                $"Package '{manifest.PackageId}' has no DefaultEntry and {candidates.Length} Workflow/Agent entries; set OviPackageManifest.DefaultEntry to disambiguate."),
        };
    }

    /// <summary>Registers a node instance under its own <c>Descriptor.Id</c>.</summary>
    public WorkflowHostBuilder MapNode(INode node)
    {
        _registry.Register(node);
        return this;
    }

    /// <summary>Registers a node instance under an explicit id.</summary>
    public WorkflowHostBuilder MapNode(NodeId nodeId, INode node)
    {
        _registry.Register(nodeId, node);
        return this;
    }

    /// <summary>
    /// The chat client the host attaches to every run's <see cref="AgentChatFeature"/> when hosting an
    /// agent (see <see cref="UseAgent"/>). Without this, agent nodes still work (they fall back to
    /// their own resolution chain) but the host does not maintain cross-run conversation history.
    /// </summary>
    public WorkflowHostBuilder WithChatClient(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        return this;
    }

    /// <summary>Adds run-level middleware, appended after the built-in defaults (see <see cref="WithoutDefaultMiddleware"/>).</summary>
    public WorkflowHostBuilder UseRunMiddleware(IRunMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _runMiddleware.Add(middleware);
        return this;
    }

    /// <summary>Adds node-level middleware, appended after the built-in defaults (see <see cref="WithoutDefaultMiddleware"/>).</summary>
    public WorkflowHostBuilder UseNodeMiddleware(INodeMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _nodeMiddleware.Add(middleware);
        return this;
    }

    /// <summary>
    /// Opts out of the built-in defaults: <c>ExceptionHandlingRunMiddleware</c> + <c>TimingRunMiddleware</c>
    /// on the run pipeline, <c>TimingNodeMiddleware</c> + <c>NodeTimeoutMiddleware</c> on the node
    /// pipeline. Only the middleware explicitly added via <see cref="UseRunMiddleware"/>/
    /// <see cref="UseNodeMiddleware"/> will run — including no exception handling, unless you add your own.
    /// </summary>
    public WorkflowHostBuilder WithoutDefaultMiddleware()
    {
        _useDefaultMiddleware = false;
        return this;
    }

    /// <summary>The service provider nodes resolve collaborators from (<c>WorkflowExecutionContext.RuntimeServices</c>).</summary>
    public WorkflowHostBuilder WithRuntimeServices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _runtimeServices = services;
        return this;
    }

    public WorkflowHostBuilder WithConcurrencyMode(RunConcurrencyMode mode)
    {
        _options.ConcurrencyMode = mode;
        return this;
    }

    /// <summary>Sets the global run timeout policy (see <see cref="AdaptiveTimeoutPolicy"/>/<see cref="FixedTimeoutPolicy"/>).</summary>
    public WorkflowHostBuilder WithTimeoutPolicy(IRunTimeoutPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _options.TimeoutPolicy = policy;
        return this;
    }

    /// <summary>Sets a per-node timeout for the node with the given workflow instance key.</summary>
    public WorkflowHostBuilder WithNodeTimeout(string nodeKey, TimeSpan timeout)
    {
        _options.NodeTimeouts.SetTimeout(nodeKey, timeout);
        return this;
    }

    /// <summary>Registers a background-firing trigger (e.g. a <c>ScheduleTriggerStrategy</c>); manual/webhook calls need no registration.</summary>
    public WorkflowHostBuilder WithTrigger(ITriggerStrategy trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        _triggers.Add(trigger);
        return this;
    }

    /// <summary>Validates the definition as a graph and builds the host. Throws <see cref="InvalidOperationException"/> when the definition is missing, invalid, or uses features this host doesn't support yet (fan-in).</summary>
    public WorkflowHost Build()
    {
        if (_definition is null)
        {
            throw new InvalidOperationException(
                $"No definition was set; call {nameof(UseWorkflow)} or {nameof(UseAgent)} before {nameof(Build)}.");
        }

        var graphResult = _definition.Validate();
        if (graphResult.IsFailure)
        {
            throw new InvalidOperationException($"The workflow definition is not a valid graph: {graphResult.Error}");
        }

        var graph = graphResult.Value;
        foreach (var nodeDefinition in graph.Definition.Nodes)
        {
            if (graph.GetUpstream(nodeDefinition.Key).Count > 1)
            {
                throw new InvalidOperationException(
                    $"Node '{nodeDefinition.Key}' has {graph.GetUpstream(nodeDefinition.Key).Count} upstream connections; " +
                    "WorkflowHost only supports single-input nodes (fan-in merge semantics are not yet defined by Ovi.Sdk.Workflows).");
            }
        }

        var runMiddleware = new List<IRunMiddleware>();
        var nodeMiddleware = new List<INodeMiddleware>();
        if (_useDefaultMiddleware)
        {
            runMiddleware.Add(new ExceptionHandlingRunMiddleware());
            runMiddleware.Add(new TimingRunMiddleware(_options.TimeoutPolicy));
            nodeMiddleware.Add(new TimingNodeMiddleware());
            nodeMiddleware.Add(new NodeTimeoutMiddleware(_options.NodeTimeouts));
        }

        runMiddleware.AddRange(_runMiddleware);
        nodeMiddleware.AddRange(_nodeMiddleware);

        return new WorkflowHost(
            graph,
            _registry,
            runMiddleware,
            nodeMiddleware,
            _runtimeServices,
            _options,
            _triggers,
            _isAgentHost,
            _chatClient);
    }
}
