namespace Ovi.Sdk.Nodes;

/// <summary>
/// Builds <see cref="WorkflowExecutionContext"/> instances with sensible defaults, so a single
/// node can be executed — and unit tested — without a workflow runtime. Every unset piece falls
/// back to an empty in-memory equivalent.
/// </summary>
public sealed class WorkflowExecutionContextBuilder
{
    private readonly Dictionary<Type, object> _services = [];
    private readonly List<Action<WorkflowExecutionContext>> _featureSetters = [];
    private WorkflowInfo? _workflow;
    private IServiceProvider? _runtimeServices;
    private IStateStore? _workflowState;
    private IStateStore? _globalState;
    private IStateStore? _instanceState;
    private CancellationToken _cancellationToken;

    public WorkflowExecutionContextBuilder WithWorkflow(WorkflowInfo workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        _workflow = workflow;
        return this;
    }

    public WorkflowExecutionContextBuilder WithWorkflow(string workflowId, string? workflowName = null, string? runId = null) =>
        WithWorkflow(new WorkflowInfo(
            workflowId,
            runId ?? Guid.NewGuid().ToString("n"),
            workflowName)
        {
            StartedAt = DateTimeOffset.UtcNow,
        });

    /// <summary>Uses <paramref name="services"/> as the context's service provider. Services registered via <see cref="WithService{TService}"/> take precedence.</summary>
    public WorkflowExecutionContextBuilder WithRuntimeServices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _runtimeServices = services;
        return this;
    }

    /// <summary>Registers a single service instance, without requiring a DI container.</summary>
    public WorkflowExecutionContextBuilder WithService<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        _services[typeof(TService)] = instance;
        return this;
    }

    /// <summary>Attaches a typed capability to the built context (see <see cref="WorkflowExecutionContext.SetFeature{TFeature}"/>).</summary>
    public WorkflowExecutionContextBuilder WithFeature<TFeature>(TFeature feature) where TFeature : class
    {
        ArgumentNullException.ThrowIfNull(feature);
        _featureSetters.Add(context => context.SetFeature(feature));
        return this;
    }

    public WorkflowExecutionContextBuilder WithWorkflowState(IStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _workflowState = store;
        return this;
    }

    public WorkflowExecutionContextBuilder WithGlobalState(IStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _globalState = store;
        return this;
    }

    public WorkflowExecutionContextBuilder WithInstanceState(IStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _instanceState = store;
        return this;
    }

    public WorkflowExecutionContextBuilder WithCancellationToken(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        return this;
    }

    public WorkflowExecutionContext Build()
    {
        var workflow = _workflow ?? new WorkflowInfo(
            workflowId: "adhoc-workflow",
            runId: Guid.NewGuid().ToString("n"))
        {
            StartedAt = DateTimeOffset.UtcNow,
        };

        IServiceProvider? services = _services.Count > 0
            ? new DictionaryServiceProvider(new Dictionary<Type, object>(_services), _runtimeServices)
            : _runtimeServices;

        var context = new WorkflowExecutionContext(
            workflow,
            services,
            _workflowState,
            _globalState,
            _instanceState,
            _cancellationToken);

        foreach (var attachFeature in _featureSetters)
        {
            attachFeature(context);
        }

        return context;
    }

    private sealed class DictionaryServiceProvider(
        IReadOnlyDictionary<Type, object> services,
        IServiceProvider? fallback) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            services.TryGetValue(serviceType, out var service) ? service : fallback?.GetService(serviceType);
    }
}
