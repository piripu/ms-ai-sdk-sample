namespace Ovi.Runtime.Host.Triggers;

/// <summary>
/// A background-firing trigger: something that calls <see cref="WorkflowHost.RunAsync"/> on its own,
/// without a caller present (a schedule, an event subscription, …). Manual and webhook triggers are
/// <b>not</b> strategies — they're just direct calls to the always-public <c>RunAsync</c>, which is
/// what makes them "always there" with no configuration. Strategies are the opt-in, configurable
/// part: <see cref="WorkflowHostBuilder.WithTrigger"/> registers zero or more of them.
/// </summary>
public interface ITriggerStrategy
{
    /// <summary>Starts firing <paramref name="host"/> according to this strategy, until <paramref name="cancellationToken"/> is cancelled or <see cref="StopAsync"/> is called.</summary>
    Task StartAsync(WorkflowHost host, CancellationToken cancellationToken);

    /// <summary>Stops firing; safe to call even if <see cref="StartAsync"/> was never called.</summary>
    Task StopAsync(CancellationToken cancellationToken);
}
