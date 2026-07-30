using MeetingTranslator.Services;

namespace MeetingTranslator.Tests;

public class TeamsCaptionsServiceTests
{
    [Fact]
    public void ExtractLatest_SeparatesSpeakerAndCaptionLines()
    {
        var result = TeamsCaptionsService.ExtractLatest(
            ["Previous speaker", "Previous caption.", "Alex Kim", "Let us begin."]);

        Assert.NotNull(result);
        Assert.Equal("Alex Kim", result.SpeakerName);
        Assert.Equal("Let us begin.", result.Text);
    }

    [Fact]
    public void ExtractLatest_ParsesCombinedSpeakerPrefix()
    {
        var result = TeamsCaptionsService.ExtractLatest(
            ["Alex Kim: Let us begin."]);

        Assert.NotNull(result);
        Assert.Equal("Alex Kim", result.SpeakerName);
        Assert.Equal("Let us begin.", result.Text);
    }
}
