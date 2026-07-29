using Google.Cloud.Speech.V1;
using Google.Protobuf;
using MeetingTranslator.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingTranslator.Services;

public sealed class AudioCaptureService : IAsyncDisposable
{
    private readonly SpeechClient _speechClient;
    private readonly List<CancellationTokenSource> _cancellations = [];
    private readonly List<IDisposable> _captures = [];

    public AudioCaptureService() => _speechClient = SpeechClient.Create();

    public event Func<AudioSource, string, double, Task>? FinalTranscript;
    public event Action<AudioSource, string>? InterimTranscript;

    public Task StartAsync(bool systemAudio, bool microphone, string language, CancellationToken token)
    {
        if (systemAudio) StartSystem(language, token);
        if (microphone) StartMicrophone(language, token);
        return Task.CompletedTask;
    }

    private void StartSystem(string language, CancellationToken token)
    {
        var capture = new WasapiLoopbackCapture();
        StartCapture(capture, AudioSource.SystemAudio, language, token);
    }

    private void StartMicrophone(string language, CancellationToken token)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        var capture = new WasapiCapture(device);
        StartCapture(capture, AudioSource.Microphone, language, token);
    }

    private void StartCapture(WasapiCapture capture, AudioSource source, string language, CancellationToken outerToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        _cancellations.Add(linked);
        _captures.Add(capture);

        var targetFormat = new WaveFormat(16000, 16, 1);
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5)
        };
        capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        capture.StartRecording();

        _ = Task.Run(async () =>
        {
            while (!linked.IsCancellationRequested)
            {
                try
                {
                    await RunStreamingSessionAsync(buffer, capture.WaveFormat, targetFormat, source, language,
                        linked.Token);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(1000, linked.Token); }
            }
        }, linked.Token);
    }

    private async Task RunStreamingSessionAsync(BufferedWaveProvider buffer, WaveFormat inputFormat,
        WaveFormat targetFormat, AudioSource source, string language, CancellationToken token)
    {
        var sample = buffer.ToSampleProvider();
        if (inputFormat.Channels > 1) sample = new StereoToMonoSampleProvider(sample);
        var resampled = new WdlResamplingSampleProvider(sample, targetFormat.SampleRate);
        var provider = new SampleToWaveProvider16(resampled);

        using var stream = _speechClient.StreamingRecognize();
        await stream.WriteAsync(new StreamingRecognizeRequest
        {
            StreamingConfig = new StreamingRecognitionConfig
            {
                Config = new RecognitionConfig
                {
                    Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                    SampleRateHertz = 16000,
                    LanguageCode = language,
                    EnableAutomaticPunctuation = true,
                    Model = "latest_long"
                },
                InterimResults = true
            }
        });

        var responseTask = Task.Run(async () =>
        {
            await foreach (var response in stream.GetResponseStream().WithCancellation(token))
            foreach (var result in response.Results)
            {
                var alternative = result.Alternatives.FirstOrDefault();
                if (alternative is null) continue;
                if (result.IsFinal && FinalTranscript is not null)
                    await FinalTranscript(source, alternative.Transcript.Trim(), alternative.Confidence);
                else
                    InterimTranscript?.Invoke(source, alternative.Transcript.Trim());
            }
        }, token);

        var bytes = new byte[3200];
        var sessionEnd = DateTime.UtcNow.AddMinutes(4);
        while (!token.IsCancellationRequested && DateTime.UtcNow < sessionEnd)
        {
            var count = provider.Read(bytes, 0, bytes.Length);
            if (count > 0)
                await stream.WriteAsync(new StreamingRecognizeRequest
                    { AudioContent = ByteString.CopyFrom(bytes, 0, count) });
            else
                await Task.Delay(20, token);
        }
        await stream.WriteCompleteAsync();
        await responseTask;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var cts in _cancellations) cts.Cancel();
        foreach (var capture in _captures)
        {
            if (capture is WasapiCapture wasapi) wasapi.StopRecording();
            capture.Dispose();
        }
        foreach (var cts in _cancellations) cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
