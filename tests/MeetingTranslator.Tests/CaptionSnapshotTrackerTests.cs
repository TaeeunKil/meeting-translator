using MeetingTranslator.Services;

namespace MeetingTranslator.Tests;

public class CaptionSnapshotTrackerTests
{
    [Fact]
    public void ShortRetainedSentence_DoesNotOverwriteOffscreenHistory()
    {
        var tracker = new CaptionSnapshotTracker();
        var first = tracker.Accept("A sufficiently long first sentence.").Single().Segment;
        var second = tracker.Accept("A sufficiently long first sentence. Next.").Last().Segment;
        var rolled = tracker.Accept("Next. Further discussion.");
        Assert.DoesNotContain(rolled, u => u.Segment.UtteranceId == first.UtteranceId);
        Assert.DoesNotContain(rolled, u => u.Segment.IsRemoved);
        Assert.Equal("Further discussion.", rolled.Last().Segment.Text);
        Assert.NotEqual(second.UtteranceId, rolled.Last().Segment.UtteranceId);
    }

    [Fact]
    public void DeletedSuffix_RetractsEveryRemovedRowIncludingTheFirst()
    {
        var tracker = new CaptionSnapshotTracker();
        tracker.Accept("Alpha.");
        var second = tracker.Accept("Alpha. Beta.").Last().Segment;
        var third = tracker.Accept("Alpha. Beta. Gamma.").Last().Segment;
        var updates = tracker.Accept("Alpha.");
        Assert.Contains(updates, u => u.Segment.UtteranceId == second.UtteranceId && u.Segment.IsRemoved);
        Assert.Contains(updates, u => u.Segment.UtteranceId == third.UtteranceId && u.Segment.IsRemoved);
        Assert.DoesNotContain(updates, u => !u.Segment.IsRemoved && string.IsNullOrWhiteSpace(u.Segment.Text));
    }

    [Fact]
    public void InternalPunctuationRevision_UpdatesTheExistingRow()
    {
        var tracker = new CaptionSnapshotTracker();
        var first = tracker.Accept("Earlier history. Yeah, because then there is a good design like a good communication between the two parties").Single();
        var second = tracker.Accept("Earlier history. Yeah, because then there is AA. Good design like a good communication between the two parties");
        var third = tracker.Accept("Earlier history. Yeah, because then there is AA, good design like a good communication between the two parties.");
        Assert.All(second.Concat(third), u => Assert.Equal(first.Segment.UtteranceId, u.Segment.UtteranceId));
        Assert.Contains("AA, good design", third.Last().Segment.Text);
    }

    [Fact]
    public void RemovedSentenceBoundary_MergesRowsAndRetractsTheObsoleteRow()
    {
        var tracker = new CaptionSnapshotTracker();
        var first = tracker.Accept("This is a.").Single().Segment;
        var split = tracker.Accept("This is a. Better design");
        var extra = split.Last().Segment;
        Assert.NotEqual(first.UtteranceId, extra.UtteranceId);
        var merged = tracker.Accept("This is a better design.");
        Assert.Contains(merged, u => u.Segment.IsRemoved && u.Segment.UtteranceId == extra.UtteranceId);
        Assert.Equal("This is a better design.", merged.Last().Segment.Text);
        Assert.Equal(first.UtteranceId, merged.Last().Segment.UtteranceId);
    }

    [Fact]
    public void RevisionOfPreviousSentence_KeepsBothIdentitiesAndUpdatesOnlyChangedText()
    {
        var tracker = new CaptionSnapshotTracker();
        var first = tracker.Accept("The output is ten.").Single().Segment;
        var second = tracker.Accept("The output is ten. Next topic").Last().Segment;
        var updates = tracker.Accept("The output is twelve. Next topic");
        var corrected = Assert.Single(updates);
        Assert.True(corrected.Final);
        Assert.Equal(first.UtteranceId, corrected.Segment.UtteranceId);
        Assert.Equal("The output is twelve.", corrected.Segment.Text);
        var tail = tracker.Accept("The output is twelve. Next topic grows").Last();
        Assert.Equal(second.UtteranceId, tail.Segment.UtteranceId);
    }

    [Fact]
    public void NewSpeechAfterFinishedSentence_RemainsSeparateEvenWithSameWords()
    {
        var tracker = new CaptionSnapshotTracker();
        var first = tracker.Accept("Thank you.").Single().Segment;
        var updates = tracker.Accept("Thank you. Thank you.");
        Assert.Equal(2, updates.Count);
        Assert.True(updates[0].Final);
        Assert.NotEqual(first.UtteranceId, updates[1].Segment.UtteranceId);
        Assert.Empty(tracker.Accept("Thank you. Thank you."));
    }

    [Fact]
    public void HistoryRollsOff_WithoutRemovingSavedRowsOrLosingPendingPrefix()
    {
        var tracker = new CaptionSnapshotTracker();
        const string firstText = "A sufficiently long complete first sentence.";
        const string secondText = "A sufficiently long second sentence is still growing";
        tracker.Accept(firstText);
        var second = tracker.Accept(firstText + " " + secondText).Last().Segment;
        var updates = tracker.Accept(secondText + " with more words");
        Assert.DoesNotContain(updates, u => u.Segment.IsRemoved);
        Assert.Equal(second.UtteranceId, updates.Last().Segment.UtteranceId);
        Assert.Equal(secondText + " with more words", updates.Last().Segment.Text);
    }
}
