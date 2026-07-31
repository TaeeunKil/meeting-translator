namespace MeetingTranslator.Models;

public sealed record CaptionSegment(
    string Text,
    string? SpeakerName = null,
    double Confidence = 1.0,
    long UtteranceId = 0);
