using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

public interface ICaptionCaptureService : IAsyncDisposable
{
    event Func<CaptionSegment, Task>? FinalTranscript;
    event Action<CaptionSegment>? InterimTranscript;

    Task StartAsync(CancellationToken token);
}
