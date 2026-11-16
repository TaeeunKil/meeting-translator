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

    private readonly Channel<CaptionSegment> _finalizedCaptions =
        Channel.CreateUnbounded<CaptionSegment>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

    private CancellationTokenSource? _cancellation;
    private Task? _pollingTask;
    private Task? _processingTask;
    private AutomationElement? _window;
    private AutomationElement? _captionsTextBlock;
    private CaptionSegment? _pendingCaption;
    private long _nextUtteranceId = 1;

    public event Func<CaptionSegment, Task>? FinalTranscript;
    public event Action<CaptionSegment>? InterimTranscript;

    public async Task StartAsync(CancellationToken token)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _window = await FindOrLaunchWindowAsync(_cancellation.Token);
        _pollingTask = Task.Run(() => PollAsync(_cancellation.Token), _cancellation.Token);
        _processingTask = Task.Run(ProcessFinalizedAsync);
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
                var latestCaption = ExtractLatestCaption(fullText);
                if (!string.IsNullOrWhiteSpace(latestCaption))
                    ProcessCaptionUpdate(latestCaption);
            }

            await Task.Delay(40, token);
        }
    }

    internal void ProcessCaptionUpdate(string latestCaption)
    {
        if (_pendingCaption is null)
        {
            _pendingCaption = NewSegment(latestCaption);
        }
        else if (IsContinuation(_pendingCaption.Text, latestCaption))
        {
            _pendingCaption = _pendingCaption with { Text = latestCaption };
        }
        else
        {
            QueueFinal(_pendingCaption);
            _pendingCaption = NewSegment(latestCaption);
        }

        InterimTranscript?.Invoke(_pendingCaption);
    }

    private CaptionSegment NewSegment(string text) =>
        new(text, UtteranceId: _nextUtteranceId++);

    private void QueueFinal(CaptionSegment segment) =>
        _finalizedCaptions.Writer.TryWrite(segment);

    private async Task ProcessFinalizedAsync()
    {
        await foreach (var segment in _finalizedCaptions.Reader.ReadAllAsync())
        {
            if (FinalTranscript is not null)
                await FinalTranscript(segment);
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

    internal static bool IsContinuation(string previous, string current)
    {
        if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(current))
            return false;

        // Live Captions revises punctuation as speech grows. Character-position
        // similarity treats a single inserted comma as a completely new utterance.
        // Normalize only for comparison; retain the original display/export text.
        previous = ComparisonText(previous);
        current = ComparisonText(current);
        if (previous.Length == 0 || current.Length == 0)
            return false;

        if (previous.StartsWith(current, StringComparison.Ordinal) ||
            current.StartsWith(previous, StringComparison.Ordinal))
            return true;

        var previousWords = previous.Split(' ');
        var currentWords = current.Split(' ');
        var count = Math.Min(previousWords.Length, currentWords.Length);
        if (count < 4)
        {
            var previousPrefix = string.Join(' ', previousWords.Take(count));
            var currentPrefix = string.Join(' ', currentWords.Take(count));
            return previousPrefix.Length >= 5 &&
                   WithinOneCharacterEdit(previousPrefix, currentPrefix);
        }

        // Small word corrections are common before the caption stabilizes.
        // Compare just the shared-length prefix so appending speech is free,
        // while keeping unrelated statements separate (at most two edits).
        var allowedEdits = Math.Min(2, Math.Max(1, count / 5));
        var costs = Enumerable.Range(0, count + 1).ToArray();
        for (var row = 1; row <= count; row++)
        {
            var next = new int[count + 1];
            next[0] = row;
            for (var column = 1; column <= count; column++)
            {
                var substitution = previousWords[row - 1] == currentWords[column - 1] ? 0 : 1;
                next[column] = Math.Min(
                    Math.Min(next[column - 1] + 1, costs[column] + 1),
                    costs[column - 1] + substitution);
            }
            if (next.Min() > allowedEdits)
                return false;
            costs = next;
        }
        return costs[count] <= allowedEdits;
    }

    private static bool WithinOneCharacterEdit(string previous, string current)
    {
        if (Math.Abs(previous.Length - current.Length) > 1)
            return false;
        var previousIndex = 0;
        var currentIndex = 0;
        var edits = 0;
        while (previousIndex < previous.Length && currentIndex < current.Length)
        {
            if (previous[previousIndex] == current[currentIndex])
            {
                previousIndex++;
                currentIndex++;
                continue;
            }
            if (++edits > 1)
                return false;
            if (previous.Length >= current.Length)
                previousIndex++;
            if (current.Length >= previous.Length)
                currentIndex++;
        }
        return edits + (previous.Length - previousIndex) +
               (current.Length - currentIndex) <= 1;
    }

    private static string ComparisonText(string text) =>
        Regex.Replace(
            Regex.Replace(text.Replace("'", "").Replace("’", ""), @"[^\p{L}\p{N}\s]", " "),
            @"\s+", " ").Trim().ToUpperInvariant();

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is null)
            return;

        _cancellation.Cancel();

        try
        {
            if (_pollingTask is not null)
                await _pollingTask;
        }
        catch (OperationCanceledException)
        {
        }

        if (_pendingCaption is not null)
        {
            QueueFinal(_pendingCaption);
            _pendingCaption = null;
        }

        _finalizedCaptions.Writer.TryComplete();
        if (_processingTask is not null)
            await _processingTask;

        _cancellation.Dispose();
        _cancellation = null;
    }

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"[^.!?。！？\n]+[.!?。！？]?")]
    private static partial Regex SentenceRegex();
}
