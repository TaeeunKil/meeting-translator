using MeetingTranslator.Models;
using MeetingTranslator.Services;

namespace MeetingTranslator.Tests;

public class InterimTranslationPolicyTests
{
    [Fact]
    public void SelectCandidate_FreeGoogle_WaitsForThreeCompletedWords()
    {
        var policy = new InterimTranslationPolicy();
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

        var tooShort = policy.SelectCandidate(
            new CaptionSegment("terry hi how", UtteranceId: 1),
            TranslationProviderKind.FreeGoogle,
            now,
            now);
        var candidate = policy.SelectCandidate(
            new CaptionSegment("terry hi how are", UtteranceId: 1),
            TranslationProviderKind.FreeGoogle,
            now,
            now);

        Assert.Null(tooShort);
        Assert.Equal("terry hi how", candidate);
    }

    [Fact]
    public void SelectCandidate_FreeGoogle_RequiresThreeAdditionalWords()
    {
        var policy = new InterimTranslationPolicy();
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

        policy.SelectCandidate(
            new CaptionSegment("terry hi how are", UtteranceId: 1),
            TranslationProviderKind.FreeGoogle,
            now,
            now);

        var oneAdditionalWord = policy.SelectCandidate(
            new CaptionSegment("terry hi how are you", UtteranceId: 1),
            TranslationProviderKind.FreeGoogle,
            now.AddSeconds(1),
            now.AddSeconds(1));
        var threeAdditionalWords = policy.SelectCandidate(
            new CaptionSegment("terry hi how are you doing this today", UtteranceId: 1),
            TranslationProviderKind.FreeGoogle,
            now.AddSeconds(2),
            now.AddSeconds(2));

        Assert.Null(oneAdditionalWord);
        Assert.Equal("terry hi how are you doing this", threeAdditionalWords);
    }

    [Fact]
    public void SelectCandidate_IdleCaption_IncludesLastWord()
    {
        var policy = new InterimTranslationPolicy();
        var changedAt = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

        var candidate = policy.SelectCandidate(
            new CaptionSegment("terry hi", UtteranceId: 1),
            TranslationProviderKind.FreeGoogle,
            changedAt,
            changedAt + InterimTranslationPolicy.IdleTranslationDelay);

        Assert.Equal("terry hi", candidate);
    }

    [Theory]
    [InlineData("ter", "terry")]
    [InlineData("terry", "terry hi")]
    [InlineData("We can start now", "We can start new")]
    public void IsContinuation_GroupsGrowingOrCorrectedCaptions(
        string previous,
        string current)
    {
        Assert.True(WindowsLiveCaptionsService.IsContinuation(previous, current));
    }

    [Fact]
    public void IsContinuation_RejectsANewSentence()
    {
        Assert.False(WindowsLiveCaptionsService.IsContinuation(
            "Terry, hi everyone",
            "The quarterly numbers"));
    }
}
