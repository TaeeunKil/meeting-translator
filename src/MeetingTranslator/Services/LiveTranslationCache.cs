using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

// Owned under MainWindow's preview lock. A growing/revised source never erases
// the last completed draft; only a replacement translation or removal changes it.
internal sealed class LiveTranslationCache
{
    private readonly Dictionary<long, string?> _translations = [];

    public void Observe(CaptionSegment caption) => _translations.TryAdd(caption.UtteranceId, null);
    public void SetTranslation(CaptionSegment caption, string translated) =>
        _translations[caption.UtteranceId] = translated;
    public string GetText(long utteranceId, string fallback) =>
        _translations.GetValueOrDefault(utteranceId) ?? fallback;
    public void Remove(long utteranceId) => _translations.Remove(utteranceId);
    public void Clear() => _translations.Clear();
}
