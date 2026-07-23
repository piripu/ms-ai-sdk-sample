namespace Ovi.Sdk.Operators;

/// <summary>
/// The shared context every operator executes against. It carries the runtime's services, the three
/// scopes of serializable state, cancellation, and information about the current workflow run.
/// </summary>
/// <remarks>
/// State scopes:
/// <list type="bullet">
/// <item><see cref="WorkflowState"/> — shared per-run state; lives for a single workflow run.</item>
/// <item><see cref="GlobalState"/> — persisted across runs, shared by the whole runtime.</item>
/// <item><see cref="InstanceState"/> — persisted across runs, scoped to the operator's package instance.</item>
/// </list>
/// Specialized runtimes derive richer contexts from this type (e.g.
/// <c>AgentWorkflowExecutionContext</c> in <c>Ovi.Sdk.Agents</c>, which adds the run's chat client and
/// chat history).
/// </remarks>
public class WorkflowExecutionContext
{
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
    }

    /// <summary>
    /// Copies another context. Derived contexts use this to wrap a base context while sharing its
    /// state stores and <see cref="Properties"/> bag.
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
    }

    /// <summary>Information about the workflow and the current run.</summary>
    public WorkflowInfo Workflow { get; }

    /// <summary>The runtime's service provider, used to resolve services operators depend on.</summary>
    public IServiceProvider RuntimeServices { get; }

    /// <summary>Serializable state shared by all operators within the current run.</summary>
    public IStateStore WorkflowState { get; }

    /// <summary>Serializable state persisted across runs and shared runtime-wide.</summary>
    public IStateStore GlobalState { get; }

    /// <summary>Serializable state persisted across runs, scoped to the operator's package instance.</summary>
    public IStateStore InstanceState { get; }

    /// <summary>Signals cancellation of the current run.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// A non-serialized bag for anything else the runtime wants to flow with the execution
    /// ("others as necessary"). Shared with derived contexts created via the copy constructor.
    /// </summary>
    public IDictionary<string, object?> Properties { get; }

    /// <summary>Resolves an optional service from <see cref="RuntimeServices"/>.</summary>
    public T? GetService<T>() where T : class => RuntimeServices.GetService(typeof(T)) as T;

    /// <summary>Resolves a required service from <see cref="RuntimeServices"/>, throwing when absent.</summary>
    public T GetRequiredService<T>() where T : class =>
        GetService<T>()
        ?? throw new InvalidOperationException($"No service of type {typeof(T)} is registered in RuntimeServices.");

    /// <summary>
    /// Starts a builder for constructing contexts by hand — the entry point for testing operators
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
