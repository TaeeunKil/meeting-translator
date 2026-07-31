using System.Text.RegularExpressions;
using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

internal sealed partial class InterimTranslationPolicy
{
    internal static readonly TimeSpan IdleTranslationDelay =
        TimeSpan.FromMilliseconds(1200);

    private long _utteranceId;
    private string _lastRequestedText = string.Empty;
    private DateTimeOffset _lastRequestedAt = DateTimeOffset.MinValue;

    public string? SelectCandidate(
        CaptionSegment segment,
        TranslationProviderKind provider,
        DateTimeOffset changedAt,
        DateTimeOffset now)
    {
        if (segment.UtteranceId != _utteranceId)
        {
            _utteranceId = segment.UtteranceId;
            _lastRequestedText = string.Empty;
            _lastRequestedAt = DateTimeOffset.MinValue;
        }

        var text = Normalize(segment.Text);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var cadence = Cadence.For(provider);
        var isIdle = now - changedAt >= IdleTranslationDelay;
        var candidate = isIdle
            ? text
            : CompletedWords(text);

        if ((!isIdle && candidate.Length < cadence.MinimumCharacters) ||
            string.Equals(candidate, _lastRequestedText, StringComparison.Ordinal) ||
            now - _lastRequestedAt < cadence.MinimumInterval)
            return null;

        var candidateWordCount = WordCount(candidate);
        var previousWordCount = WordCount(_lastRequestedText);
        var newWordCount = Math.Max(0, candidateWordCount - previousWordCount);

        if (!isIdle && newWordCount < cadence.NewWordsPerRequest)
            return null;

        _lastRequestedText = candidate;
        _lastRequestedAt = now;
        return candidate;
    }

    internal static string CompletedWords(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrEmpty(normalized) || EndsSentence(normalized))
            return normalized;

        var lastSpace = normalized.LastIndexOf(' ');
        return lastSpace <= 0
            ? string.Empty
            : normalized[..lastSpace].TrimEnd();
    }

    private static int WordCount(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Normalize(string text) =>
        MultiSpaceRegex().Replace(text, " ").Trim();

    private static bool EndsSentence(string text) =>
        text.Length > 0 && ".!?。！？".Contains(text[^1]);

    private sealed record Cadence(
        int NewWordsPerRequest,
        int MinimumCharacters,
        TimeSpan MinimumInterval)
    {
        public static Cadence For(TranslationProviderKind provider) =>
            provider switch
            {
                TranslationProviderKind.Qwen =>
                    new(2, 8, TimeSpan.FromMilliseconds(600)),
                TranslationProviderKind.GoogleCloud =>
                    new(5, 12, TimeSpan.FromMilliseconds(1500)),
                _ =>
                    new(3, 10, TimeSpan.FromMilliseconds(900))
            };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
