using System.Diagnostics;
using System.Windows.Automation;

namespace MeetingTranslator.Services;

public sealed class LiveCaptionsReader : IDisposable
{
    private AutomationElement? _window;
    private AutomationElement? _textElement;
    private CancellationTokenSource? _cancellation;
    private bool _startedByUs;

    public event Action<string>? InterimCaption;
    public event Func<string, Task>? FinalCaption;

    public async Task StartAsync(CancellationToken token)
    {
        _window = FindExistingWindow();
        if (_window is null)
        {
            Process.Start(new ProcessStartInfo("LiveCaptions") { UseShellExecute = true });
            _startedByUs = true;
            for (var i = 0; i < 50 && _window is null; i++)
            {
                await Task.Delay(100, token);
                _window = FindExistingWindow();
            }
        }
        if (_window is null)
            throw new InvalidOperationException("Windows 실시간 캡션을 찾지 못했습니다. Win+Ctrl+L로 먼저 실행해 보세요.");

        _textElement = FindTextElement(_window) ??
            throw new InvalidOperationException("실시간 캡션 텍스트 영역을 찾지 못했습니다.");
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _ = Task.Run(() => PollAsync(_cancellation.Token), _cancellation.Token);
    }

    private async Task PollAsync(CancellationToken token)
    {
        var previous = "";
        var pending = "";
        var lastChange = DateTime.UtcNow;

        while (!token.IsCancellationRequested)
        {
            string current;
            try
            {
                current = (_textElement?.Current.Name ?? "").Trim();
            }
            catch (ElementNotAvailableException)
            {
                _window = FindExistingWindow();
                _textElement = _window is null ? null : FindTextElement(_window);
                await Task.Delay(250, token);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current) && current != previous)
            {
                previous = current;
                pending = ExtractLatestSentence(current);
                lastChange = DateTime.UtcNow;
                InterimCaption?.Invoke(pending);
            }
            else if (!string.IsNullOrWhiteSpace(pending) &&
                     DateTime.UtcNow - lastChange >= TimeSpan.FromMilliseconds(900))
            {
                var finalized = pending;
                pending = "";
                if (FinalCaption is not null) await FinalCaption(finalized);
            }
            await Task.Delay(40, token);
        }
    }

    public static string ExtractLatestSentence(string fullText)
    {
        var normalized = string.Join(" ", fullText.Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var endings = new[] { '.', '?', '!', '。', '？', '！' };
        var end = normalized.Length > 0 && endings.Contains(normalized[^1])
            ? normalized[..^1].LastIndexOfAny(endings)
            : normalized.LastIndexOfAny(endings);
        return end >= 0 ? normalized[(end + 1)..].Trim() : normalized.Trim();
    }

    private static AutomationElement? FindExistingWindow()
    {
        foreach (var process in Process.GetProcessesByName("LiveCaptions"))
        {
            var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id);
            var window = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
            if (window is not null) return window;
        }
        return null;
    }

    private static AutomationElement? FindTextElement(AutomationElement window) =>
        window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptionsTextBlock"));

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        // 사용자가 이미 실행해 둔 Live Captions는 절대 종료하지 않는다.
        // 우리가 실행했더라도 Windows 접근성 기능은 사용자가 계속 쓸 수 있게 유지한다.
        _ = _startedByUs;
    }
}
