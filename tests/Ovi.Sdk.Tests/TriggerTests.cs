using Ovi.Sdk.Operators;
using Ovi.Sdk.Triggers;
using Xunit;

namespace Ovi.Sdk.Tests;

public class TriggerTests
{
    private static WorkflowExecutionContext NewContext() => WorkflowExecutionContext.CreateBuilder().Build();

    [Fact]
    public async Task Manual_triggers_pass_their_payload_through()
    {
        var trigger = new ManualTriggerOperator<string>();

        var output = await trigger.FireAsync("start!", NewContext());

        Assert.Equal("start!", output);
        Assert.True(trigger.Id.IsBuiltIn);
    }

    [Fact]
    public async Task Webhook_triggers_deliver_the_request_into_the_workflow()
    {
        var trigger = new WebhookTriggerOperator();
        var request = new WebhookRequest
        {
            Path = "/hooks/orders",
            Body = """{"orderId": 7}""",
            ContentType = "application/json",
        };

        var output = await trigger.FireAsync(request, NewContext());

        Assert.Same(request, output);
        Assert.Equal(7, output.ParseJsonBody()!["orderId"]!.GetValue<int>());
        Assert.Null(new WebhookRequest().ParseJsonBody());
    }

    [Fact]
    public async Task Chat_triggers_normalize_webhook_requests()
    {
        var webhook = new WebhookRequest
        {
            Body = """{"message": "Hello!", "sessionId": "s-1", "userId": "u-9"}""",
            ContentType = "application/json",
        };

        var payload = ChatTriggerOperator.FromWebhook(webhook);

        Assert.Equal("Hello!", payload.Message);
        Assert.Equal("s-1", payload.SessionId);
        Assert.Equal("u-9", payload.UserId);

        var output = await new ChatTriggerOperator().FireAsync(payload, NewContext());
        Assert.Same(payload, output);
    }

    [Fact]
    public void Chat_triggers_reject_bodies_that_are_not_chat_messages()
    {
        Assert.Throws<FormatException>(() => ChatTriggerOperator.FromWebhook(new WebhookRequest { Body = "42" }));
        Assert.Throws<FormatException>(() => ChatTriggerOperator.FromWebhook(new WebhookRequest { Body = """{"note": "no message"}""" }));
    }

    [Fact]
    public void Interval_schedules_compute_the_next_occurrence()
    {
        var schedule = Schedule.FromInterval(TimeSpan.FromMinutes(5));
        var after = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(ScheduleKind.Interval, schedule.Kind);
        Assert.Equal(after.AddMinutes(5), schedule.GetNextOccurrence(after));
        Assert.Throws<ArgumentOutOfRangeException>(() => Schedule.FromInterval(TimeSpan.Zero));
    }

    [Fact]
    public void Cron_schedules_compute_the_next_occurrence()
    {
        var schedule = Schedule.FromCron("*/15 * * * *");
        var after = new DateTimeOffset(2026, 1, 1, 0, 7, 0, TimeSpan.Zero);

        Assert.Equal(ScheduleKind.Cron, schedule.Kind);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 15, 0, TimeSpan.Zero), schedule.GetNextOccurrence(after));
    }

    [Fact]
    public void Invalid_cron_expressions_are_rejected_eagerly() =>
        Assert.ThrowsAny<FormatException>(() => Schedule.FromCron("not a cron"));

    [Fact]
    public async Task Schedule_triggers_can_be_fired_manually()
    {
        var trigger = new ScheduleTriggerOperator(Schedule.FromInterval(TimeSpan.FromHours(1)));
        var tick = ScheduleTick.Manual();

        var output = await trigger.FireAsync(tick, NewContext());

        Assert.True(output.IsManual);
        Assert.Equal(tick.FiredAt, output.FiredAt);

        var after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(after.AddHours(1), trigger.GetNextOccurrence(after));
    }

    [Fact]
    public void Triggers_are_operators_with_built_in_ids()
    {
        Assert.IsAssignableFrom<ITriggerOperator>(new ManualTriggerOperator<string>());
        Assert.IsAssignableFrom<IOperator>(new WebhookTriggerOperator());

        Assert.True(ManualTriggerOperator<string>.DefaultDescriptor.Id.IsBuiltIn);
        Assert.True(WebhookTriggerOperator.DefaultDescriptor.Id.IsBuiltIn);
        Assert.True(ChatTriggerOperator.DefaultDescriptor.Id.IsBuiltIn);
        Assert.True(ScheduleTriggerOperator.DefaultDescriptor.Id.IsBuiltIn);
    }
}
