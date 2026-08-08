namespace Ovi.Runtime.Host;

/// <summary>
/// What kind of trigger produced a <see cref="RunRequest"/>. Every kind flows through the same
/// <see cref="WorkflowHost.RunAsync"/> entry point and the same request/response shape — this is
/// metadata for middleware and diagnostics, not a dispatch switch.
/// </summary>
public enum TriggerKind
{
    /// <summary>A caller invoked <see cref="WorkflowHost.RunAsync"/> directly (includes webhook-originated calls).</summary>
    Manual,

    /// <summary>The request was normalized from an inbound HTTP request.</summary>
    Webhook,

    /// <summary>A <see cref="Triggers.ScheduleTriggerStrategy"/> fired the run on its configured schedule.</summary>
    Schedule,
}
