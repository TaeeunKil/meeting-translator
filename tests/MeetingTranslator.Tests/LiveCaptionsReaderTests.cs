using MeetingTranslator.Services;

namespace MeetingTranslator.Tests;

public class LiveCaptionsReaderTests
{
    [Fact]
    public void ExtractLatestSentence_ReturnsIncompleteTail()
    {
        var result = LiveCaptionsReader.ExtractLatestSentence(
            "Welcome to the meeting.\r\nWe need to ship Friday");
        Assert.Equal("We need to ship Friday", result);
    }

    [Fact]
    public void ExtractLatestSentence_NormalizesLineBreaks()
    {
        var result = LiveCaptionsReader.ExtractLatestSentence("Hello\r\nworld");
        Assert.Equal("Hello world", result);
    }
}
