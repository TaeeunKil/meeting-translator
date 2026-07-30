using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Windows.Automation;
using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

public sealed partial class TeamsCaptionsService : ICaptionCaptureService
{
    private static readonly string[] ProcessNames = ["ms-teams", "Teams"];
    private static readonly string[] CaptionTokens =
    [
        "caption", "closed-caption", "closedcaption", "subtitle",
        "live-caption", "transcript", "캡션", "자막"
    ];
    private static readonly TimeSpan StableDelay = TimeSpan.FromMilliseconds(850);

    private readonly Channel<CaptionSegment> _finalizedCaptions =
        Channel.CreateUnbounded<CaptionSegment>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

    private CancellationTokenSource? _cancellation;
    private Task? _pollingTask;
    private Task? _processingTask;
    private AutomationElement? _captionRoot;

    public event Func<CaptionSegment, Task>? FinalTranscript;
    public event Action<CaptionSegment>? InterimTranscript;

    public async Task StartAsync(CancellationToken token)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _captionRoot = await FindCaptionRootAsync(_cancellation.Token);
        _pollingTask = Task.Run(() => PollAsync(_cancellation.Token), _cancellation.Token);
        _processingTask = Task.Run(() => ProcessFinalizedAsync(_cancellation.Token), _cancellation.Token);
    }

    public static void ShowTeams()
    {
        Process.Start(new ProcessStartInfo("msteams:")
        {
            UseShellExecute = true
        });
    }

    private async Task PollAsync(CancellationToken token)
    {
        var lastSnapshot = string.Empty;
        CaptionSegment? pending = null;
        var lastCommittedKey = string.Empty;
        var changedAt = DateTimeOffset.UtcNow;
        var emptyReads = 0;

        while (!token.IsCancellationRequested)
        {
            CaptionSegment? current;
            string snapshot;

            try
            {
                (current, snapshot) = ReadLatestCaption();
            }
            catch (ElementNotAvailableException)
            {
                _captionRoot = await FindCaptionRootAsync(token);
                continue;
            }

            if (current is null)
            {
                emptyReads++;
                if (emptyReads >= 30)
                {
                    _captionRoot = await FindCaptionRootAsync(token);
                    emptyReads = 0;
                }

                await Task.Delay(100, token);
                continue;
            }

            emptyReads = 0;
            var currentKey = SegmentKey(current);
            if (!string.Equals(snapshot, lastSnapshot, StringComparison.Ordinal))
            {
                if (pending is not null &&
                    !string.Equals(pending.SpeakerName, current.SpeakerName, StringComparison.Ordinal) &&
                    !string.Equals(SegmentKey(pending), lastCommittedKey, StringComparison.Ordinal))
                {
                    _finalizedCaptions.Writer.TryWrite(pending);
                    lastCommittedKey = SegmentKey(pending);
                }

                lastSnapshot = snapshot;
                changedAt = DateTimeOffset.UtcNow;
                pending = current;

                if (!string.Equals(currentKey, lastCommittedKey, StringComparison.Ordinal))
                {
                    InterimTranscript?.Invoke(current);

                    if (EndsSentence(current.Text))
                    {
                        _finalizedCaptions.Writer.TryWrite(current);
                        lastCommittedKey = currentKey;
                        pending = null;
                    }
                }
            }
            else if (pending is not null &&
                     !string.Equals(SegmentKey(pending), lastCommittedKey, StringComparison.Ordinal) &&
                     DateTimeOffset.UtcNow - changedAt >= StableDelay)
            {
                _finalizedCaptions.Writer.TryWrite(pending);
                lastCommittedKey = SegmentKey(pending);
                pending = null;
            }

            await Task.Delay(80, token);
        }
    }

    private async Task ProcessFinalizedAsync(CancellationToken token)
    {
        await foreach (var segment in _finalizedCaptions.Reader.ReadAllAsync(token))
        {
            if (FinalTranscript is not null)
                await FinalTranscript(segment);
        }
    }

    private (CaptionSegment? Segment, string Snapshot) ReadLatestCaption()
    {
        if (_captionRoot is null)
            return (null, string.Empty);

        var lines = ReadTextLines(_captionRoot);
        if (lines.Count == 0)
            return (null, string.Empty);

        var segment = ExtractLatest(lines);
        return (segment, string.Join('\u001f', lines));
    }

    private static List<string> ReadTextLines(AutomationElement root)
    {
        var condition = new PropertyCondition(
            AutomationElement.ControlTypeProperty,
            ControlType.Text);
        var elements = root.FindAll(TreeScope.Descendants, condition);
        var lines = new List<string>();

        foreach (AutomationElement element in elements)
        {
            var name = element.Current.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            foreach (var part in name.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = Normalize(part);
                if (string.IsNullOrWhiteSpace(normalized) ||
                    IsCaptionChrome(normalized) ||
                    string.Equals(lines.LastOrDefault(), normalized, StringComparison.Ordinal))
                    continue;

                lines.Add(normalized);
            }
        }

        if (lines.Count == 0)
        {
            var rootName = Normalize(root.Current.Name);
            if (!string.IsNullOrWhiteSpace(rootName) && !IsCaptionChrome(rootName))
                lines.Add(rootName);
        }

        return lines;
    }

    internal static CaptionSegment? ExtractLatest(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return null;

        var text = lines[^1];
        string? speaker = null;

        var combined = SpeakerPrefixRegex().Match(text);
        if (combined.Success)
        {
            speaker = CleanSpeaker(combined.Groups["speaker"].Value);
            text = combined.Groups["text"].Value.Trim();
        }
        else if (lines.Count >= 2 && LooksLikeSpeaker(lines[^2]))
        {
            speaker = CleanSpeaker(lines[^2]);
        }

        return string.IsNullOrWhiteSpace(text)
            ? null
            : new CaptionSegment(text, speaker);
    }

    private static bool LooksLikeSpeaker(string value)
    {
        var candidate = CleanSpeaker(value);
        return candidate.Length is > 0 and <= 64 &&
               candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8 &&
               !EndsSentence(candidate) &&
               !candidate.Contains("caption", StringComparison.OrdinalIgnoreCase) &&
               !candidate.Contains("자막", StringComparison.Ordinal) &&
               !candidate.Contains("캡션", StringComparison.Ordinal);
    }

    private static string CleanSpeaker(string value) =>
        value.Trim().TrimEnd(':', '：');

    private static bool IsCaptionChrome(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is
            "live captions" or "captions" or "caption settings" or
            "show live captions" or "hide live captions" or
            "spoken language" or "pop out captions" or
            "라이브 캡션" or "캡션" or "캡션 설정" or
            "자막" or "자막 설정";
    }

    private static async Task<AutomationElement> FindCaptionRootAsync(CancellationToken token)
    {
        if (!HasTeamsProcess())
            ShowTeams();

        for (var attempt = 0; attempt < 150; attempt++)
        {
            token.ThrowIfCancellationRequested();

            var candidate = FindBestCaptionRoot();
            if (candidate is not null)
                return candidate;

            await Task.Delay(100, token);
        }

        throw new InvalidOperationException(
            "Teams 캡션 영역을 찾을 수 없습니다. Teams 회의에서 라이브 캡션을 켠 뒤, 가능하면 캡션을 팝아웃하고 다시 시작해 주세요.");
    }

    private static AutomationElement? FindBestCaptionRoot()
    {
        AutomationElement? best = null;
        var bestScore = 0;

        foreach (var process in GetTeamsProcesses())
        {
            var processCondition = new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                process.Id);
            var windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                processCondition);

            foreach (AutomationElement window in windows)
            {
                ScoreCandidate(window, ref best, ref bestScore, isWindow: true);

                var descendants = window.FindAll(
                    TreeScope.Descendants,
                    Condition.TrueCondition);
                foreach (AutomationElement element in descendants)
                    ScoreCandidate(element, ref best, ref bestScore, isWindow: false);
            }
        }

        return best;
    }

    private static void ScoreCandidate(
        AutomationElement element,
        ref AutomationElement? best,
        ref int bestScore,
        bool isWindow)
    {
        try
        {
            var current = element.Current;
            if (current.ControlType is null ||
                current.ControlType == ControlType.Button ||
                current.ControlType == ControlType.MenuItem)
                return;

            var searchable = $"{current.AutomationId} {current.ClassName} {current.Name}";
            if (!CaptionTokens.Any(token =>
                    searchable.Contains(token, StringComparison.OrdinalIgnoreCase)))
                return;

            var score = isWindow ? 4 : 8;
            if (CaptionTokens.Any(token =>
                    current.AutomationId.Contains(token, StringComparison.OrdinalIgnoreCase)))
                score += 10;
            if (current.ControlType == ControlType.Group ||
                current.ControlType == ControlType.Pane ||
                current.ControlType == ControlType.List ||
                current.ControlType == ControlType.Document)
                score += 5;

            var textCount = element.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Text)).Count;
            score += Math.Min(textCount, 10);

            if (score > bestScore)
            {
                best = element;
                bestScore = score;
            }
        }
        catch (ElementNotAvailableException)
        {
        }
    }

    private static bool HasTeamsProcess() => GetTeamsProcesses().Any();

    private static IEnumerable<Process> GetTeamsProcesses()
    {
        foreach (var processName in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
                yield return process;
        }
    }

    private static string SegmentKey(CaptionSegment segment) =>
        $"{segment.SpeakerName}\u001f{segment.Text}";

    private static string Normalize(string text) =>
        MultiSpaceRegex().Replace(text, " ").Trim();

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

    [GeneratedRegex(@"^(?<speaker>[^:：]{1,64})[:：]\s*(?<text>.+)$")]
    private static partial Regex SpeakerPrefixRegex();
}
