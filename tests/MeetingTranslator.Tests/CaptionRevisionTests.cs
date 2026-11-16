using MeetingTranslator.Models;
using MeetingTranslator.Services;

namespace MeetingTranslator.Tests;

public class CaptionRevisionTests
{
    [Fact]
    public void ShortSpellingRevision_KeepsTheSameUtterance()
    {
        var service = new WindowsLiveCaptionsService();
        var updates = new List<CaptionSegment>();
        service.InterimTranscript += updates.Add;
        service.ProcessCaptionUpdate("The inverter works");
        service.ProcessCaptionUpdate("The invertor works at full power");
        Assert.Single(updates.Select(u => u.UtteranceId).Distinct());
    }

    [Theory]
    [InlineData("My name", "My budget")]
    [InlineData("Please enable recording", "Please disable recording")]
    public void UnrelatedShortStatements_RemainSeparate(string previous, string current)
    {
        Assert.False(WindowsLiveCaptionsService.IsContinuation(previous, current));
    }

    [Fact]
    public void ScreenshotSequence_RemainsOneUtteranceWhenPunctuationChanges()
    {
        var service = new WindowsLiveCaptionsService();
        var updates = new List<CaptionSegment>();
        service.InterimTranscript += updates.Add;
        service.ProcessCaptionUpdate("Uh so you'll have");
        service.ProcessCaptionUpdate("Uh, so you'll have a certain number of Gen sets online the the TV will be doing a certain output");
        service.ProcessCaptionUpdate("Uh so you'll have a certain number of Gen sets online the the TV will be doing a certain output and the battery will");
        service.ProcessCaptionUpdate("Uh, so you'll have a certain number of Gen sets online, the the TV will be doing a certain output and the battery will be, you know, maybe charging or potentially");
        Assert.Equal(4, updates.Count);
        Assert.Single(updates.Select(u => u.UtteranceId).Distinct());
        Assert.EndsWith("charging or potentially", updates[^1].Text);
    }

    [Theory]
    [InlineData("Uh so you'll have", "Uh, so you’ll have a certain number")]
    [InlineData("We can start now", "We can start new")]
    [InlineData("The inverter is connected and running", "The inverter is connected, and running at full output.")]
    public void Continuation_IgnoresPunctuationAndSmallWordCorrections(string previous, string current)
    {
        Assert.True(WindowsLiveCaptionsService.IsContinuation(previous, current));
    }

    [Theory]
    [InlineData("Terry, hi everyone", "The quarterly numbers")]
    [InlineData("We will review the deployment plan", "We will review the budget numbers")]
    public void Continuation_SeparatesDifferentStatements(string previous, string current)
    {
        Assert.False(WindowsLiveCaptionsService.IsContinuation(previous, current));
    }

    [Fact]
    public void PreviewResult_IsRejectedIfTheDispatcherNowShowsAnotherUtterance()
    {
        var latest = new CaptionSegment("The next topic is different", UtteranceId: 2);
        Assert.False(InterimTranslationPolicy.CanApplyTranslation(1, "The next topic", latest, new HashSet<long>()));
    }

    [Fact]
    public void PreviewResult_CanUpdateOnlyTheSameUnfinalizedSource()
    {
        var latest = new CaptionSegment("We can start now and review the plan", UtteranceId: 1);
        Assert.True(InterimTranslationPolicy.CanApplyTranslation(1, "We can start now", latest, new HashSet<long>()));
        Assert.False(InterimTranslationPolicy.CanApplyTranslation(1, "We can start now", latest, new HashSet<long> { 1 }));
        Assert.False(InterimTranslationPolicy.CanApplyTranslation(1, "We should wait", latest, new HashSet<long>()));
        Assert.False(InterimTranslationPolicy.CanApplyTranslation(1, "We can start now", null, new HashSet<long>()));
    }

    [Fact]
    public void SecondScreenshot_RemovesCommaAndGrowsWithinOneUtterance()
    {
        var service = new WindowsLiveCaptionsService();
        var updates = new List<CaptionSegment>();
        service.InterimTranscript += updates.Add;
        service.ProcessCaptionUpdate("Have cases here, but rather we just wanted to have different");
        service.ProcessCaptionUpdate("Have cases here but rather we just wanted to have different scenario having some.");
        Assert.Single(updates.Select(u => u.UtteranceId).Distinct());
        Assert.EndsWith("scenario having some.", updates[^1].Text);
    }
}
