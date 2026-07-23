using CronosExpression = Cronos.CronExpression;

namespace Ovi.Sdk.Triggers;

public enum ScheduleKind
{
    /// <summary>Fires every fixed <see cref="Schedule.Interval"/>.</summary>
    Interval,

    /// <summary>Fires per a cron expression, evaluated in <see cref="Schedule.TimeZone"/>.</summary>
    Cron,
}

/// <summary>
/// When a <see cref="ScheduleTriggerOperator"/> fires: either a fixed interval or a cron expression.
/// Cron expressions are validated at construction; <see cref="GetNextOccurrence"/> lets both the
/// runtime and tests compute upcoming fire times without any scheduler infrastructure.
/// </summary>
public sealed class Schedule
{
    private readonly CronosExpression? _cron;

    private Schedule(ScheduleKind kind, TimeSpan? interval, string? cronExpression, CronosExpression? cron, TimeZoneInfo? timeZone)
    {
        Kind = kind;
        Interval = interval;
        CronExpression = cronExpression;
        _cron = cron;
        TimeZone = timeZone ?? TimeZoneInfo.Utc;
    }

    public ScheduleKind Kind { get; }

    /// <summary>The fixed interval, for <see cref="ScheduleKind.Interval"/> schedules.</summary>
    public TimeSpan? Interval { get; }

    /// <summary>The cron expression text, for <see cref="ScheduleKind.Cron"/> schedules.</summary>
    public string? CronExpression { get; }

    /// <summary>The time zone cron expressions are evaluated in (UTC by default).</summary>
    public TimeZoneInfo TimeZone { get; }

    public static Schedule FromInterval(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        return new Schedule(ScheduleKind.Interval, interval, cronExpression: null, cron: null, timeZone: null);
    }

    /// <summary>
    /// Creates a cron schedule from a standard 5-field expression (or 6-field, with
    /// <paramref name="includeSeconds"/>). Throws <see cref="FormatException"/> when invalid.
    /// </summary>
    public static Schedule FromCron(string expression, TimeZoneInfo? timeZone = null, bool includeSeconds = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var cron = CronosExpression.Parse(
            expression,
            includeSeconds ? Cronos.CronFormat.IncludeSeconds : Cronos.CronFormat.Standard);

        return new Schedule(ScheduleKind.Cron, interval: null, expression, cron, timeZone);
    }

    /// <summary>The next time the schedule fires strictly after <paramref name="after"/>, or <see langword="null"/> when it never fires again.</summary>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after) => Kind switch
    {
        ScheduleKind.Interval => after + Interval!.Value,
        _ => _cron!.GetNextOccurrence(after, TimeZone),
    };

    public override string ToString() => Kind switch
    {
        ScheduleKind.Interval => $"every {Interval}",
        _ => $"cron({CronExpression}, {TimeZone.Id})",
    };
}
