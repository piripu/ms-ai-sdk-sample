namespace Ovi.Sdk.Nodes;

/// <summary>
/// The shared context every node executes against. It carries the runtime's services, the three
/// scopes of serializable state, cancellation, and information about the current workflow run.
/// </summary>
/// <remarks>
/// State scopes:
/// <list type="bullet">
/// <item><see cref="WorkflowState"/> — shared per-run state; lives for a single workflow run.</item>
/// <item><see cref="GlobalState"/> — persisted across runs, shared by the whole runtime.</item>
/// <item><see cref="InstanceState"/> — persisted across runs, scoped to the node's package instance.</item>
/// </list>
/// Run capabilities compose onto the context as typed <b>features</b>
/// (<see cref="SetFeature{TFeature}"/>/<see cref="GetFeature{TFeature}"/>) rather than through
/// context subclassing, so a run can carry any combination of capabilities (chat, memory, …).
/// Derived contexts such as <c>AgentWorkflowExecutionContext</c> in <c>Ovi.Sdk.Agents</c> are thin
/// sugar that registers a feature.
/// </remarks>
public class WorkflowExecutionContext
{
    private readonly Dictionary<Type, object> _features;

    public WorkflowExecutionContext(
        WorkflowInfo workflow,
        IServiceProvider? runtimeServices = null,
        IStateStore? workflowState = null,
        IStateStore? globalState = null,
        IStateStore? instanceState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        Workflow = workflow;
        RuntimeServices = runtimeServices ?? NullServiceProvider.Instance;
        WorkflowState = workflowState ?? new InMemoryStateStore();
        GlobalState = globalState ?? new InMemoryStateStore();
        InstanceState = instanceState ?? new InMemoryStateStore();
        CancellationToken = cancellationToken;
        Properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        _features = [];
    }

    /// <summary>
    /// Copies another context. Derived contexts use this to wrap a base context while sharing its
    /// state stores, <see cref="Properties"/> bag and features.
    /// </summary>
    protected WorkflowExecutionContext(WorkflowExecutionContext other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Workflow = other.Workflow;
        RuntimeServices = other.RuntimeServices;
        WorkflowState = other.WorkflowState;
        GlobalState = other.GlobalState;
        InstanceState = other.InstanceState;
        CancellationToken = other.CancellationToken;
        Properties = other.Properties;
        _features = other._features;
    }

    /// <summary>Information about the workflow and the current run.</summary>
    public WorkflowInfo Workflow { get; }

    /// <summary>The runtime's service provider, used to resolve services nodes depend on.</summary>
    public IServiceProvider RuntimeServices { get; }

    /// <summary>Serializable state shared by all nodes within the current run.</summary>
    public IStateStore WorkflowState { get; }

    /// <summary>Serializable state persisted across runs and shared runtime-wide.</summary>
    public IStateStore GlobalState { get; }

    /// <summary>Serializable state persisted across runs, scoped to the node's package instance.</summary>
    public IStateStore InstanceState { get; }

    /// <summary>Signals cancellation of the current run.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// A non-serialized bag for anything else the runtime wants to flow with the execution
    /// ("others as necessary"). Shared with derived contexts created via the copy constructor.
    /// </summary>
    public IDictionary<string, object?> Properties { get; }

    /// <summary>Gets a typed capability attached to this run, or <see langword="null"/> when absent.</summary>
    public TFeature? GetFeature<TFeature>() where TFeature : class =>
        _features.TryGetValue(typeof(TFeature), out var feature) ? (TFeature)feature : null;

    /// <summary>Gets a typed capability attached to this run, throwing when absent.</summary>
    public TFeature GetRequiredFeature<TFeature>() where TFeature : class =>
        GetFeature<TFeature>()
        ?? throw new InvalidOperationException($"No feature of type {typeof(TFeature)} is attached to the execution context.");

    /// <summary>
    /// Attaches (or replaces) a typed capability on this run. Features are shared with contexts
    /// created from this one via the copy constructor.
    /// </summary>
    public void SetFeature<TFeature>(TFeature feature) where TFeature : class
    {
        ArgumentNullException.ThrowIfNull(feature);
        _features[typeof(TFeature)] = feature;
    }

    /// <summary>Resolves an optional service from <see cref="RuntimeServices"/>.</summary>
    public T? GetService<T>() where T : class => RuntimeServices.GetService(typeof(T)) as T;

    /// <summary>Resolves a required service from <see cref="RuntimeServices"/>, throwing when absent.</summary>
    public T GetRequiredService<T>() where T : class =>
        GetService<T>()
        ?? throw new InvalidOperationException($"No service of type {typeof(T)} is registered in RuntimeServices.");

    /// <summary>
    /// Starts a builder for constructing contexts by hand — the entry point for testing nodes
    /// atomically, outside any workflow engine.
    /// </summary>
    public static WorkflowExecutionContextBuilder CreateBuilder() => new();

    private sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();

        private NullServiceProvider()
        {
        }

        public object? GetService(Type serviceType) => null;
    }
}
