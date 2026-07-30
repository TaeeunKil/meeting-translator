namespace MeetingTranslator.Models;

public enum AudioSource { SystemAudio, Microphone }

public sealed record TranscriptEntry(
    long Id,
    string MeetingId,
    DateTimeOffset Timestamp,
    AudioSource Source,
    string OriginalText,
    string TranslatedText,
    double Confidence,
    string? SpeakerName = null);
