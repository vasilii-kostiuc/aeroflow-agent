using AeroFlow.Agent.Contracts;

namespace AeroFlow.Agent.Tests;

public sealed class StopPlaybackCommandTests
{
    [Fact]
    public void ParsesContract()
    {
        var command = StopPlaybackCommand.FromJson("""
        {
          "command": "audio.stop",
          "messageId": "m-1",
          "jobId": "j-1",
          "occurredAt": "2026-07-17T10:00:00+00:00",
          "schemaVersion": 1
        }
        """);

        Assert.Equal("m-1", command.MessageId);
        Assert.Equal("j-1", command.JobId);
    }

    [Fact]
    public void RejectsWrongCommandName()
    {
        Assert.Throws<ContractViolationException>(() =>
            StopPlaybackCommand.FromJson("""{ "command": "audio.play_sequence", "messageId": "m-1", "jobId": "j-1" }"""));
    }

    [Fact]
    public void RejectsMissingJobId()
    {
        Assert.Throws<ContractViolationException>(() =>
            StopPlaybackCommand.FromJson("""{ "command": "audio.stop", "messageId": "m-1" }"""));
    }

    [Fact]
    public void RejectsInvalidJson()
    {
        Assert.Throws<ContractViolationException>(() => StopPlaybackCommand.FromJson("not json"));
    }

    [Fact]
    public void InterruptedEventCarriesDiscriminatorAndJobId()
    {
        var json = AgentEvent.Interrupted("j-1").ToJson();

        Assert.Contains("\"event\":\"audio.playback_interrupted\"", json);
        Assert.Contains("\"jobId\":\"j-1\"", json);
    }
}
