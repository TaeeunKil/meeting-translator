using MeetingTranslator.Models;
using MeetingTranslator.Services;

namespace MeetingTranslator.Tests;

public class TranslationContinuityTests
{
    [Fact]
    public void CaptionGrowthAndRevision_KeepTheLastCompletedTranslation()
    {
        var state = new LiveTranslationCache();
        var first = new CaptionSegment("We plan a release", UtteranceId: 1);
        state.Observe(first);
        Assert.Equal("waiting", state.GetText(1, "waiting"));
        state.SetTranslation(first, "출시를 계획합니다.");
        state.Observe(first with {Text = "We plan a release next week"});
        Assert.Equal("출시를 계획합니다.", state.GetText(1, "waiting"));
        state.Observe(first with {Text = "We postpone the release next week"});
        Assert.Equal("출시를 계획합니다.", state.GetText(1, "waiting"));
        state.SetTranslation(first with {Text = "We postpone the release next week"}, "출시를 연기합니다.");
        Assert.Equal("출시를 연기합니다.", state.GetText(1, "waiting"));
        state.Observe(new CaptionSegment("Another utterance", UtteranceId: 2));
        Assert.Equal("waiting", state.GetText(2, "waiting"));
        state.Remove(1);
        Assert.Equal("waiting", state.GetText(1, "waiting"));
    }

    [Fact]
    public void ContinuousCorrection_TranslatesEvenWithoutAnIncreaseInWordCount()
    {
        var policy = new InterimTranslationPolicy();
        var now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        Assert.NotNull(policy.SelectCandidate(new CaptionSegment("We will deploy today now", UtteranceId: 1), TranslationProviderKind.FreeGoogle, now, now));
        var later = now.AddSeconds(1);
        Assert.NotNull(policy.SelectCandidate(new CaptionSegment("We will cancel deployment now", UtteranceId: 1), TranslationProviderKind.FreeGoogle, later, later));
    }

    [Fact]
    public void SameUtterance_CanShowCompletedDraftWhileItsSourceIsBeingRevised()
    {
        var revised = new CaptionSegment("The revised sentence keeps changing", UtteranceId: 1);
        Assert.True(InterimTranslationPolicy.CanApplyTranslation(1, "The original sentence", revised, new HashSet<long>(), allowRevisedSource: true));
        Assert.False(InterimTranslationPolicy.CanApplyTranslation(2, "The original sentence", revised, new HashSet<long>(), allowRevisedSource: true));
        Assert.False(InterimTranslationPolicy.CanApplyTranslation(1, "The original sentence", revised, new HashSet<long> {1}, allowRevisedSource: true));
    }

    [Fact]
    public void FailedPreview_CanRetryTheUnchangedCaptionAfterCooldown()
    {
        var policy = new InterimTranslationPolicy();
        var now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        var caption = new CaptionSegment("We will deploy tomorrow morning", UtteranceId: 1);
        var candidate = policy.SelectCandidate(caption, TranslationProviderKind.FreeGoogle, now, now)!;
        policy.RetryFailedRequest(caption.UtteranceId, candidate);
        Assert.Equal(candidate, policy.SelectCandidate(caption, TranslationProviderKind.FreeGoogle, now.AddSeconds(2), now.AddSeconds(2)));
    }
}
