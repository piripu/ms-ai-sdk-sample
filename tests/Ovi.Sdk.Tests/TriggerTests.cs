using Ovi.Sdk.Nodes;
using Ovi.Sdk.Triggers;
using Xunit;

namespace Ovi.Sdk.Tests;

public class TriggerTests
{
    private static WorkflowExecutionContext NewContext() => WorkflowExecutionContext.CreateBuilder().Build();

    [Fact]
    public async Task Manual_triggers_pass_their_payload_through()
    {
        var trigger = new ManualTriggerNode<string>();

        var output = await trigger.FireAsync("start!", NewContext());

        Assert.True(output.IsSuccess);
        Assert.Equal("start!", output.Value);
        Assert.True(trigger.Id.IsBuiltIn);
    }

    [Fact]
    public async Task Webhook_triggers_deliver_the_request_into_the_workflow()
    {
        var trigger = new WebhookTriggerNode();
        var request = new WebhookRequest
        {
            Path = "/hooks/orders",
            Body = """{"orderId": 7}""",
            ContentType = "application/json",
        };

        var output = await trigger.FireAsync(request, NewContext());

        Assert.Same(request, output.Value);
        Assert.Equal(7, output.Value.ParseJsonBody()!["orderId"]!.GetValue<int>());
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

        var payload = ChatTriggerNode.FromWebhook(webhook);

        Assert.True(payload.IsSuccess);
        Assert.Equal("Hello!", payload.Value.Message);
        Assert.Equal("s-1", payload.Value.SessionId);
        Assert.Equal("u-9", payload.Value.UserId);

        var output = await new ChatTriggerNode().FireAsync(payload.Value, NewContext());
        Assert.Same(payload.Value, output.Value);
    }

    [Fact]
    public void Chat_triggers_report_bad_bodies_as_validation_errors()
    {
        var notAnObject = ChatTriggerNode.FromWebhook(new WebhookRequest { Body = "42" });
        Assert.True(notAnObject.IsFailure);
        Assert.IsType<ValidationError>(notAnObject.Error);

        var noMessage = ChatTriggerNode.FromWebhook(new WebhookRequest { Body = """{"note": "no message"}""" });
        Assert.True(noMessage.IsFailure);
        Assert.Contains("'message'", noMessage.Error.Message);

        var invalidJson = ChatTriggerNode.FromWebhook(new WebhookRequest { Body = "{not json" });
        Assert.True(invalidJson.IsFailure);
        var error = Assert.IsType<ValidationError>(invalidJson.Error);
        Assert.NotNull(error.Detail);
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
        var trigger = new ScheduleTriggerNode(Schedule.FromInterval(TimeSpan.FromHours(1)));
        var tick = ScheduleTick.Manual();

        var output = await trigger.FireAsync(tick, NewContext());

        Assert.True(output.Value.IsManual);
        Assert.Equal(tick.FiredAt, output.Value.FiredAt);

        var after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(after.AddHours(1), trigger.GetNextOccurrence(after));
    }

    [Fact]
    public void Triggers_are_nodes_with_built_in_ids()
    {
        Assert.IsAssignableFrom<ITriggerNode>(new ManualTriggerNode<string>());
        Assert.IsAssignableFrom<INode>(new WebhookTriggerNode());

        Assert.True(ManualTriggerNode<string>.DefaultDescriptor.Id.IsBuiltIn);
        Assert.True(WebhookTriggerNode.DefaultDescriptor.Id.IsBuiltIn);
        Assert.True(ChatTriggerNode.DefaultDescriptor.Id.IsBuiltIn);
        Assert.True(ScheduleTriggerNode.DefaultDescriptor.Id.IsBuiltIn);
    }
}
