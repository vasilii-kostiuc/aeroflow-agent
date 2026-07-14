using AeroFlow.Agent.Contracts;

namespace AeroFlow.Agent.Tests;

public sealed class PlaySequenceCommandTests
{
    [Fact]
    public void ParsesContractAndFlattensGroupsBySortOrder()
    {
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        var json = $$"""
        {
          "command": "audio.play_sequence",
          "messageId": "m-1",
          "jobId": "j-1",
          "audioSequence": [
            { "languageCode": "ru", "sortOrder": 1, "items": [ { "type": "audio_asset", "audioAssetId": "{{assetB}}" } ] },
            { "languageCode": "ro-MD", "sortOrder": 0, "items": [
              { "type": "audio_asset", "audioAssetId": "{{assetA}}" },
              { "type": "pause", "durationMs": 500 }
            ] }
          ],
          "occurredAt": "2026-07-10T10:00:00+00:00",
          "schemaVersion": 1
        }
        """;

        var command = PlaySequenceCommand.FromJson(json);

        Assert.Equal("m-1", command.MessageId);
        Assert.Equal("j-1", command.JobId);
        Assert.Equal(1, command.SchemaVersion);
        // ro-MD group (sortOrder 0) plays before ru (sortOrder 1), pause kept in place.
        Assert.Equal(
            new PlaybackStep[]
            {
                new PlayAssetStep(assetA),
                new PauseStep(500),
                new PlayAssetStep(assetB),
            },
            command.Steps);
    }

    [Theory]
    [InlineData("""{ "command": "audio.stop" }""")]
    [InlineData("""{ "messageId": "m", "jobId": "j", "audioSequence": [] }""")]
    [InlineData("""{ "command": "audio.play_sequence", "jobId": "j", "audioSequence": [] }""")]
    [InlineData("""{ "command": "audio.play_sequence", "messageId": "m", "jobId": "j" }""")]
    [InlineData("not json")]
    public void RejectsNonContractBodies(string json)
    {
        Assert.Throws<ContractViolationException>(() => PlaySequenceCommand.FromJson(json));
    }

    [Fact]
    public void RejectsUnknownSequenceItemType()
    {
        var json = """
        {
          "command": "audio.play_sequence",
          "messageId": "m-1",
          "jobId": "j-1",
          "audioSequence": [
            { "languageCode": "ru", "sortOrder": 0, "items": [ { "type": "tts_text", "text": "hello" } ] }
          ]
        }
        """;

        Assert.Throws<ContractViolationException>(() => PlaySequenceCommand.FromJson(json));
    }
}
