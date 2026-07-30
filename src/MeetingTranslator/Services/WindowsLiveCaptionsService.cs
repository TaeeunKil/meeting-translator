using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Windows.Automation;
using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

public sealed partial class WindowsLiveCaptionsService : ICaptionCaptureService
{
    private const string ProcessName = "LiveCaptions";
    private const string CaptionsAutomationId = "CaptionsTextBlock";
    private static readonly TimeSpan StableDelay = TimeSpan.FromMilliseconds(700);

    private readonly Channel<string> _finalizedCaptions =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

    private CancellationTokenSource? _cancellation;
    private Task? _pollingTask;
    private Task? _processingTask;
    private AutomationElement? _window;
    private AutomationElement? _captionsTextBlock;

    public event Func<CaptionSegment, Task>? FinalTranscript;
    public event Action<CaptionSegment>? InterimTranscript;

    public async Task StartAsync(CancellationToken token)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _window = await FindOrLaunchWindowAsync(_cancellation.Token);
        _pollingTask = Task.Run(() => PollAsync(_cancellation.Token), _cancellation.Token);
        _processingTask = Task.Run(() => ProcessFinalizedAsync(_cancellation.Token), _cancellation.Token);
    }

    public static void ShowLiveCaptions()
    {
        Process.Start(new ProcessStartInfo(ProcessName)
        {
            UseShellExecute = true
        });
    }

    private async Task PollAsync(CancellationToken token)
    {
        var lastRawText = string.Empty;
        var pendingCaption = string.Empty;
        var lastCommittedCaption = string.Empty;
        var changedAt = DateTimeOffset.UtcNow;

        while (!token.IsCancellationRequested)
        {
            string fullText;
            try
            {
                fullText = ReadCaptionText();
            }
            catch (ElementNotAvailableException)
            {
                _window = await FindOrLaunchWindowAsync(token);
                _captionsTextBlock = null;
                continue;
            }

            if (string.IsNullOrWhiteSpace(fullText))
            {
                await Task.Delay(60, token);
                continue;
            }

            fullText = Normalize(fullText);
            if (!string.Equals(fullText, lastRawText, StringComparison.Ordinal))
            {
                lastRawText = fullText;
                changedAt = DateTimeOffset.UtcNow;
                pendingCaption = ExtractLatestCaption(fullText);

                if (!string.IsNullOrWhiteSpace(pendingCaption) &&
                    !string.Equals(pendingCaption, lastCommittedCaption, StringComparison.Ordinal))
                {
                    InterimTranscript?.Invoke(new CaptionSegment(pendingCaption));

                    if (EndsSentence(pendingCaption))
                    {
                        _finalizedCaptions.Writer.TryWrite(pendingCaption);
                        lastCommittedCaption = pendingCaption;
                        pendingCaption = string.Empty;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(pendingCaption) &&
                     !string.Equals(pendingCaption, lastCommittedCaption, StringComparison.Ordinal) &&
                     DateTimeOffset.UtcNow - changedAt >= StableDelay)
            {
                _finalizedCaptions.Writer.TryWrite(pendingCaption);
                lastCommittedCaption = pendingCaption;
                pendingCaption = string.Empty;
            }

            await Task.Delay(40, token);
        }
    }

    private async Task ProcessFinalizedAsync(CancellationToken token)
    {
        await foreach (var text in _finalizedCaptions.Reader.ReadAllAsync(token))
        {
            if (FinalTranscript is not null)
                await FinalTranscript(new CaptionSegment(text));
        }
    }

    private string ReadCaptionText()
    {
        if (_window is null)
            return string.Empty;

        _captionsTextBlock ??= FindElementByAutomationId(
            _window,
            CaptionsAutomationId);
        return _captionsTextBlock?.Current.Name ?? string.Empty;
    }

    private static async Task<AutomationElement> FindOrLaunchWindowAsync(CancellationToken token)
    {
        var processes = Process.GetProcessesByName(ProcessName);
        if (processes.Length == 0)
            ShowLiveCaptions();

        for (var attempt = 0; attempt < 120; attempt++)
        {
            token.ThrowIfCancellationRequested();

            foreach (var process in Process.GetProcessesByName(ProcessName))
            {
                var condition = new PropertyCondition(
                    AutomationElement.ProcessIdProperty,
                    process.Id);
                var window = AutomationElement.RootElement.FindFirst(
                    TreeScope.Children,
                    condition);
                if (window is not null)
                    return window;
            }

            await Task.Delay(100, token);
        }

        throw new InvalidOperationException(
            "Windows 라이브 캡션 창을 찾을 수 없습니다. Windows 11 라이브 캡션을 먼저 설정해 주세요.");
    }

    private static AutomationElement? FindElementByAutomationId(
        AutomationElement window,
        string automationId)
    {
        var condition = new PropertyCondition(
            AutomationElement.AutomationIdProperty,
            automationId);
        return window.FindFirst(TreeScope.Descendants, condition);
    }

    private static string Normalize(string text) =>
        MultiSpaceRegex().Replace(text.Replace("\r\n", "\n"), " ").Trim();

    private static string ExtractLatestCaption(string text)
    {
        var matches = SentenceRegex().Matches(text);
        return matches.Count == 0
            ? text.Trim()
            : matches[^1].Value.Trim();
    }

    private static bool EndsSentence(string text) =>
        text.Length > 0 && ".!?。！？".Contains(text[^1]);

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is null)
            return;

        _cancellation.Cancel();
        _finalizedCaptions.Writer.TryComplete();

        var tasks = new[] { _pollingTask, _processingTask }
            .Where(task => task is not null)
            .Cast<Task>();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }

        _cancellation.Dispose();
        _cancellation = null;
    }

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"[^.!?。！？\n]+[.!?。！？]?")]
    private static partial Regex SentenceRegex();
}
