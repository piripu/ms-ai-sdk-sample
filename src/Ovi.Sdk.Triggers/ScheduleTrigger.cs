using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Triggers;

/// <summary>One firing of a <see cref="ScheduleTriggerNode"/>.</summary>
public sealed record ScheduleTick
{
    /// <summary>When the schedule intended this firing to happen.</summary>
    public DateTimeOffset ScheduledAt { get; init; }

    /// <summary>When the firing actually happened.</summary>
    public DateTimeOffset FiredAt { get; init; }

    /// <summary>True when the tick was fired by hand (testing / "run now") instead of by the schedule.</summary>
    public bool IsManual { get; init; }

    /// <summary>Creates a manual tick stamped with the given (or current) time.</summary>
    public static ScheduleTick Manual(DateTimeOffset? at = null)
    {
        var timestamp = at ?? DateTimeOffset.UtcNow;
        return new ScheduleTick { ScheduledAt = timestamp, FiredAt = timestamp, IsManual = true };
    }
}

/// <summary>
/// A trigger fired on a <see cref="Triggers.Schedule"/> — a fixed interval or a cron expression. The
/// runtime owns the actual timer/scheduler; the SDK contract is the schedule itself (with
/// <see cref="Schedule.GetNextOccurrence"/> for planning) and the <see cref="ScheduleTick"/> payload
/// each firing delivers. Like every trigger it can be fired manually, e.g. with
/// <see cref="ScheduleTick.Manual"/>.
/// </summary>
public class ScheduleTriggerNode : TriggerNode<ScheduleTick, ScheduleTick>
{
    public ScheduleTriggerNode(Schedule schedule, NodeDescriptor? descriptor = null)
        : base(descriptor ?? DefaultDescriptor)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        Schedule = schedule;
    }

    public static NodeDescriptor DefaultDescriptor { get; } = new(
        NodeId.BuiltIn("schedule-trigger"),
        "Schedule Trigger",
        "Starts a workflow on a schedule: a fixed interval or a cron expression.");

    /// <summary>When this trigger fires.</summary>
    public Schedule Schedule { get; }

    /// <summary>The next time this trigger fires after <paramref name="after"/>.</summary>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after) => Schedule.GetNextOccurrence(after);

    public override ValueTask<ScheduleTick> ExecuteAsync(ScheduleTick input, WorkflowExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ValueTask.FromResult(input);
    }
}
