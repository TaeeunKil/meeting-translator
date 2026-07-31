using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MeetingTranslator.Models;
using MeetingTranslator.Services;
using Microsoft.Win32;

namespace MeetingTranslator;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Row> _rows = [];
    private readonly TranscriptStore _store = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly MonthlyUsageGuard _usage = new();
    private readonly object _previewLock = new();
    private readonly SemaphoreSlim _translationGate = new(1, 1);
    private InterimTranslationPolicy _interimPolicy = new();
    private readonly Dictionary<long, int> _rowIndexes = [];
    private readonly Dictionary<long, string> _rowTranslations = [];
    private readonly Dictionary<string, string> _translationCache =
        new(StringComparer.Ordinal);
    private readonly HashSet<long> _finalizedUtterances = [];
    private ICaptionCaptureService? _capture;
    private ITranslationService? _translator;
    private CancellationTokenSource? _meetingCancellation;
    private Task? _previewTask;
    private string? _meetingId;
    private CaptionSegment? _latestInterim;
    private DateTimeOffset _latestInterimChangedAt;

    public MainWindow()
    {
        InitializeComponent();
        TranscriptList.ItemsSource = _rows;

        ProjectIdBox.Text = _settings.ProjectId;
        CredentialsBox.Text = _settings.CredentialsPath;
        QwenBaseUrlBox.Text = _settings.QwenBaseUrl;
        QwenModelBox.Text = _settings.QwenModel;
        QwenFallbackCheck.IsChecked = _settings.QwenFallbackToFreeGoogle;
        if (_settings.MonthlyCharacterLimit is <= 0 or > 500_000)
            _settings.MonthlyCharacterLimit = 490_000;
        UpdateCloudUsage();
        SelectCaptionSource(_settings.CaptionSource, save: false);
        SelectProvider(_settings.TranslationProvider, save: false);

        Loaded += async (_, _) => await _store.InitializeAsync();
        Closed += async (_, _) =>
        {
            _meetingCancellation?.Cancel();
            if (_capture is not null)
                await _capture.DisposeAsync();
        };
    }

    private void WindowsCaption_Click(object sender, RoutedEventArgs e) =>
        SelectCaptionSource(CaptionSourceKind.WindowsLiveCaptions);

    private void TeamsCaption_Click(object sender, RoutedEventArgs e) =>
        SelectCaptionSource(CaptionSourceKind.MicrosoftTeams);

    private void SelectCaptionSource(CaptionSourceKind source, bool save = true)
    {
        _settings.CaptionSource = source;

        SetProviderButton(
            WindowsCaptionButton,
            source == CaptionSourceKind.WindowsLiveCaptions);
        SetProviderButton(
            TeamsCaptionButton,
            source == CaptionSourceKind.MicrosoftTeams);

        if (source == CaptionSourceKind.MicrosoftTeams)
        {
            CaptionSourceHintText.Text = "Teams 전용 · 화자 이름 감지 · 캡션 팝아웃 권장";
            CaptionSourceCardTitle.Text = "Microsoft Teams Captions";
            CaptionSourceCardDescription.Text =
                "회의에서 라이브 캡션을 켜고 가능하면 팝아웃하세요. 화면에 표시된 화자 이름과 발언을 함께 읽습니다.";
            OpenCaptionSourceButton.Content = "Teams 열기";
            CaptionSourceSubtitle.Text = "Microsoft Teams 캡션 기반 실시간 번역";
            CaptionSourceBadgeText.Text = "Teams 자막";
            EmptyStateDescription.Text = "Teams 캡션에서 화자와 문장을 읽어 번역하고 저장합니다.";
        }
        else
        {
            CaptionSourceHintText.Text = "모든 앱 · 로컬 음성 인식 · 화자 구분 없음";
            CaptionSourceCardTitle.Text = "Windows Live Captions";
            CaptionSourceCardDescription.Text =
                "자막 언어를 영어로 설정하세요. 시스템 오디오는 무료로 PC 안에서 인식됩니다.";
            OpenCaptionSourceButton.Content = "Windows 자막 열기";
            CaptionSourceSubtitle.Text = "Windows Live Captions 기반 실시간 번역";
            CaptionSourceBadgeText.Text = "Windows 자막";
            EmptyStateDescription.Text = "Windows 자막에서 완성된 문장을 읽어 번역하고 저장합니다.";
        }

        InterimText.Text = IdleCaptionText(source);

        if (save)
            _settings.Save();
    }

    private void FreeGoogle_Click(object sender, RoutedEventArgs e) =>
        SelectProvider(TranslationProviderKind.FreeGoogle);

    private void GoogleCloud_Click(object sender, RoutedEventArgs e) =>
        SelectProvider(TranslationProviderKind.GoogleCloud);

    private void Qwen_Click(object sender, RoutedEventArgs e) =>
        SelectProvider(TranslationProviderKind.Qwen);

    private void SelectProvider(TranslationProviderKind provider, bool save = true)
    {
        _settings.TranslationProvider = provider;

        FreeGooglePanel.Visibility = provider == TranslationProviderKind.FreeGoogle
            ? Visibility.Visible
            : Visibility.Collapsed;
        GoogleCloudPanel.Visibility = provider == TranslationProviderKind.GoogleCloud
            ? Visibility.Visible
            : Visibility.Collapsed;
        QwenPanel.Visibility = provider == TranslationProviderKind.Qwen
            ? Visibility.Visible
            : Visibility.Collapsed;

        SetProviderButton(FreeGoogleButton, provider == TranslationProviderKind.FreeGoogle);
        SetProviderButton(GoogleCloudButton, provider == TranslationProviderKind.GoogleCloud);
        SetProviderButton(QwenButton, provider == TranslationProviderKind.Qwen);

        ProviderBadgeText.Text = ProviderLabel(provider);
        ProviderHintText.Text = provider switch
        {
            TranslationProviderKind.FreeGoogle => "기본 엔진 · API 키 없음 · 실험용",
            TranslationProviderKind.GoogleCloud => "공식 API · 490,000자 로컬 보호 · 서비스 계정 필요",
            TranslationProviderKind.Qwen => "172.30.1.57 · qwen3.5-27b · 사고 모드 끔",
            _ => string.Empty
        };

        if (save)
            _settings.Save();
    }

    private static void SetProviderButton(Button button, bool selected)
    {
        button.Background = Brush(selected ? "#FF5600" : "#FFFFFF");
        button.Foreground = Brush(selected ? "#FFFFFF" : "#626260");
        button.BorderBrush = Brush(selected ? "#FF5600" : "#DEDAD4");
    }

    private void BrowseCredentials_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Google credentials (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
            CredentialsBox.Text = dialog.FileName;
    }

    private void OpenCaptionSource_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settings.CaptionSource == CaptionSourceKind.MicrosoftTeams)
                TeamsCaptionsService.ShowTeams();
            else
                WindowsLiveCaptionsService.ShowLiveCaptions();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "캡션 소스를 열 수 없음",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ValidateAndSaveSettings())
                return;

            _meetingId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            _meetingCancellation?.Dispose();
            _meetingCancellation = new CancellationTokenSource();
            _translator = TranslationServiceFactory.Create(_settings);
            _capture = _settings.CaptionSource == CaptionSourceKind.MicrosoftTeams
                ? new TeamsCaptionsService()
                : new WindowsLiveCaptionsService();
            _capture.InterimTranscript += HandleInterimTranscript;
            _capture.FinalTranscript += HandleFinalTranscriptAsync;

            _rows.Clear();
            lock (_previewLock)
            {
                _rowIndexes.Clear();
                _rowTranslations.Clear();
                _translationCache.Clear();
                _finalizedUtterances.Clear();
                _latestInterim = null;
                _latestInterimChangedAt = DateTimeOffset.MinValue;
                _interimPolicy = new InterimTranslationPolicy();
            }
            EmptyState.Visibility = Visibility.Visible;

            await _capture.StartAsync(_meetingCancellation.Token);
            _previewTask = Task.Run(
                () => PreviewTranslationLoopAsync(_meetingCancellation.Token),
                _meetingCancellation.Token);
            ToggleMeeting(true);
        }
        catch (Exception ex)
        {
            _meetingCancellation?.Cancel();
            if (_capture is not null)
                await _capture.DisposeAsync();
            _capture = null;
            _meetingCancellation?.Dispose();
            _meetingCancellation = null;

            MessageBox.Show(
                ex.Message,
                "회의를 시작할 수 없음",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool ValidateAndSaveSettings()
    {
        _settings.ProjectId = ProjectIdBox.Text.Trim();
        _settings.CredentialsPath = CredentialsBox.Text.Trim();
        _settings.QwenBaseUrl = QwenBaseUrlBox.Text.Trim();
        _settings.QwenModel = QwenModelBox.Text.Trim();
        _settings.QwenFallbackToFreeGoogle = QwenFallbackCheck.IsChecked == true;

        if (_settings.TranslationProvider == TranslationProviderKind.GoogleCloud &&
            (string.IsNullOrWhiteSpace(_settings.ProjectId) ||
             !File.Exists(_settings.CredentialsPath)))
        {
            MessageBox.Show(
                "Google Cloud 프로젝트 ID와 서비스 계정 JSON 파일을 설정해 주세요.",
                "Google Cloud 설정 필요",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (_settings.TranslationProvider == TranslationProviderKind.Qwen &&
            (!Uri.TryCreate(_settings.QwenBaseUrl, UriKind.Absolute, out _) ||
             string.IsNullOrWhiteSpace(_settings.QwenModel)))
        {
            MessageBox.Show(
                "Qwen 서버 주소와 모델명을 확인해 주세요.",
                "Qwen 설정 필요",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        _settings.Save();
        return true;
    }

    private void HandleInterimTranscript(CaptionSegment caption)
    {
        if (string.IsNullOrWhiteSpace(caption.Text))
            return;

        lock (_previewLock)
        {
            _latestInterim = caption;
            _latestInterimChangedAt = DateTimeOffset.UtcNow;
        }

        Dispatcher.BeginInvoke(() =>
        {
            InterimText.Text = FormatCaption(caption);
            UpsertRow(
                caption,
                TranslationFor(caption.UtteranceId, "번역할 단어를 모으는 중…"));
        });
    }

    private async Task PreviewTranslationLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                CaptionSegment? caption;
                DateTimeOffset changedAt;
                string? candidate;

                lock (_previewLock)
                {
                    caption = _latestInterim;
                    changedAt = _latestInterimChangedAt;
                    candidate = caption is null ||
                                _finalizedUtterances.Contains(caption.UtteranceId)
                        ? null
                        : _interimPolicy.SelectCandidate(
                            caption,
                            _settings.TranslationProvider,
                            changedAt,
                            DateTimeOffset.UtcNow);
                }

                if (caption is null || candidate is null)
                    continue;

                var translated = await TranslateCaptionAsync(candidate, token);
                if (translated is null)
                    continue;

                var shouldDisplay = false;
                lock (_previewLock)
                {
                    shouldDisplay =
                        !_finalizedUtterances.Contains(caption.UtteranceId) &&
                        _latestInterim?.UtteranceId == caption.UtteranceId &&
                        _latestInterim.Text.StartsWith(
                            candidate,
                            StringComparison.OrdinalIgnoreCase);

                    if (shouldDisplay)
                        _rowTranslations[caption.UtteranceId] = translated;
                }

                if (!shouldDisplay)
                    continue;

                await Dispatcher.InvokeAsync(() =>
                {
                    CaptionSegment latest;
                    lock (_previewLock)
                        latest = _latestInterim ?? caption;

                    UpsertRow(latest, translated);
                    SetRunningStatus();
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HandleFinalTranscriptAsync(CaptionSegment caption)
    {
        if (_translator is null ||
            _meetingId is null ||
            string.IsNullOrWhiteSpace(caption.Text))
            return;

        lock (_previewLock)
        {
            _finalizedUtterances.Add(caption.UtteranceId);
            if (_latestInterim?.UtteranceId == caption.UtteranceId)
                _latestInterim = null;
        }

        try
        {
            var translated = await TranslateCaptionAsync(
                caption.Text,
                _meetingCancellation?.Token ?? CancellationToken.None);
            if (translated is null)
                return;

            var entry = await _store.AddAsync(
                _meetingId,
                AudioSource.SystemAudio,
                caption.Text,
                translated,
                caption.Confidence,
                caption.SpeakerName);

            lock (_previewLock)
                _rowTranslations[caption.UtteranceId] = translated;

            await Dispatcher.InvokeAsync(() =>
            {
                UpsertRow(caption, translated, entry.Timestamp);
                InterimText.Text = "다음 문장을 기다리는 중입니다…";
                SetRunningStatus();
                Dispatcher.BeginInvoke(
                    TranscriptScroll.ScrollToEnd,
                    DispatcherPriority.Background);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateCloudUsage();
                StatusText.Text = "번역 오류";
                HeaderStatusText.Text = "오류";
                StatusDot.Fill = Brush("#C41C1C");
                LiveDot.Fill = Brush("#C41C1C");
                StatusBadge.Background = Brush("#FBE9E7");
                InterimText.Text = ex.Message;
            });
        }
    }

    private async Task<string?> TranslateCaptionAsync(
        string text,
        CancellationToken token)
    {
        if (_translator is null)
            return null;

        var cacheKey =
            $"{_settings.TranslationProvider}\u001f{_settings.SourceLanguage}\u001f" +
            $"{_settings.TargetLanguage}\u001f{text}";

        await _translationGate.WaitAsync(token);
        try
        {
            lock (_previewLock)
            {
                if (_translationCache.TryGetValue(cacheKey, out var cached))
                    return cached;
            }

            var reservedCharacters = 0;
            var translationCompleted = false;
            try
            {
                if (_settings.TranslationProvider == TranslationProviderKind.GoogleCloud)
                {
                    reservedCharacters = MonthlyUsageGuard.CountBillableCharacters(text);
                    if (!_usage.TryReserve(
                            reservedCharacters,
                            _settings.MonthlyCharacterLimit))
                    {
                        await ShowCloudLimitReachedAsync();
                        return null;
                    }

                    await Dispatcher.InvokeAsync(UpdateCloudUsage);
                }

                var translated = await _translator.TranslateAsync(
                    text,
                    _settings.TargetLanguage,
                    _settings.SourceLanguage,
                    token);
                translationCompleted = true;

                lock (_previewLock)
                    _translationCache[cacheKey] = translated;

                return translated;
            }
            catch
            {
                if (!translationCompleted && reservedCharacters > 0)
                {
                    _usage.Release(reservedCharacters);
                    await Dispatcher.InvokeAsync(UpdateCloudUsage);
                }

                throw;
            }
        }
        finally
        {
            _translationGate.Release();
        }
    }

    private string TranslationFor(long utteranceId, string fallback)
    {
        lock (_previewLock)
            return _rowTranslations.GetValueOrDefault(utteranceId, fallback);
    }

    private void UpsertRow(
        CaptionSegment caption,
        string translated,
        DateTimeOffset? timestamp = null)
    {
        var speakerName = string.IsNullOrWhiteSpace(caption.SpeakerName)
            ? CaptionSourceLabel(_settings.CaptionSource)
            : caption.SpeakerName;

        if (_rowIndexes.TryGetValue(caption.UtteranceId, out var index))
        {
            var existing = _rows[index];
            _rows[index] = new Row(
                timestamp ?? existing.Timestamp,
                caption.Text,
                translated,
                ProviderLabel(_settings.TranslationProvider),
                speakerName,
                SourceInitial(_settings.CaptionSource));
        }
        else
        {
            _rowIndexes[caption.UtteranceId] = _rows.Count;
            _rows.Add(new Row(
                timestamp ?? DateTimeOffset.Now,
                caption.Text,
                translated,
                ProviderLabel(_settings.TranslationProvider),
                speakerName,
                SourceInitial(_settings.CaptionSource)));
        }

        EmptyState.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(
            TranscriptScroll.ScrollToEnd,
            DispatcherPriority.Background);
    }

    private async Task ShowCloudLimitReachedAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            UpdateCloudUsage();
            StatusText.Text = "Cloud 보호 한도 도달";
            HeaderStatusText.Text = "호출 차단";
            StatusDot.Fill = Brush("#C41C1C");
            LiveDot.Fill = Brush("#C41C1C");
            StatusBadge.Background = Brush("#FBE9E7");
            InterimText.Text =
                $"이번 달 앱 사용량이 {_settings.MonthlyCharacterLimit:N0}자 보호 한도에 도달해 Google Cloud 호출을 중단했습니다.";
        });
    }

    private void UpdateCloudUsage()
    {
        var used = _usage.CharactersUsed;
        var limit = _settings.MonthlyCharacterLimit;
        CloudUsageText.Text = $"{used:N0} / {limit:N0}자";
        CloudUsageBar.Maximum = limit;
        CloudUsageBar.Value = Math.Min(used, limit);
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_meetingId is null)
            return;

        if (_capture is not null)
            await _capture.DisposeAsync();
        _capture = null;
        _meetingCancellation?.Cancel();
        if (_previewTask is not null)
        {
            try
            {
                await _previewTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _previewTask = null;
        _meetingCancellation?.Dispose();
        _meetingCancellation = null;

        var entries = await _store.GetMeetingAsync(_meetingId);
        var exportDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MeetingTranslator",
            "Exports");
        Directory.CreateDirectory(exportDir);

        var baseName = $"meeting-{DateTime.Now:yyyy-MM-dd-HHmm}";
        var csv = Path.Combine(exportDir, $"{baseName}.csv");
        var markdown = Path.Combine(exportDir, $"{baseName}.md");
        await MeetingExporter.ExportCsvAsync(entries, csv);
        await MeetingExporter.ExportMarkdownAsync(
            entries,
            markdown,
            $"회의록 {DateTime.Now:yyyy-MM-dd HH:mm}");

        ToggleMeeting(false);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{markdown}\"")
        {
            UseShellExecute = true
        });
        MessageBox.Show(
            $"회의록을 저장했습니다.\n\n{markdown}\n{csv}",
            "내보내기 완료");
    }

    private void ToggleMeeting(bool running)
    {
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        FreeGoogleButton.IsEnabled = !running;
        GoogleCloudButton.IsEnabled = !running;
        QwenButton.IsEnabled = !running;
        WindowsCaptionButton.IsEnabled = !running;
        TeamsCaptionButton.IsEnabled = !running;
        OpenCaptionSourceButton.IsEnabled = !running;
        ProjectIdBox.IsEnabled = !running;
        CredentialsBox.IsEnabled = !running;
        BrowseButton.IsEnabled = !running;
        QwenBaseUrlBox.IsEnabled = !running;
        QwenModelBox.IsEnabled = !running;
        QwenFallbackCheck.IsEnabled = !running;

        if (running)
        {
            SetRunningStatus();
            InterimText.Text = _settings.CaptionSource == CaptionSourceKind.MicrosoftTeams
                ? "Teams 캡션과 화자를 기다리는 중입니다…"
                : "Windows 자막을 기다리는 중입니다…";
        }
        else
        {
            StatusText.Text = "준비됨";
            HeaderStatusText.Text = "대기 중";
            StatusDot.Fill = Brush("#9C9A96");
            LiveDot.Fill = Brush("#9C9A96");
            StatusBadge.Background = Brush("#FAEEE8");
            InterimText.Text = IdleCaptionText(_settings.CaptionSource);
        }
    }

    private void SetRunningStatus()
    {
        StatusText.Text = "캡션 수신 · 번역 중";
        HeaderStatusText.Text = "실시간";
        StatusDot.Fill = Brush("#FF5600");
        LiveDot.Fill = Brush("#FF5600");
        StatusBadge.Background = Brush("#FAEEE8");
    }

    private static string ProviderLabel(TranslationProviderKind provider) =>
        provider switch
        {
            TranslationProviderKind.FreeGoogle => "무료 Google",
            TranslationProviderKind.GoogleCloud => "Google Cloud",
            TranslationProviderKind.Qwen => "사내 Qwen",
            _ => provider.ToString()
        };

    private static string CaptionSourceLabel(CaptionSourceKind source) =>
        source == CaptionSourceKind.MicrosoftTeams
            ? "Teams 캡션"
            : "Windows 캡션";

    private static string SourceInitial(CaptionSourceKind source) =>
        source == CaptionSourceKind.MicrosoftTeams ? "T" : "W";

    private static string IdleCaptionText(CaptionSourceKind source) =>
        source == CaptionSourceKind.MicrosoftTeams
            ? "회의를 시작하면 Teams 캡션과 화자가 여기에 표시됩니다."
            : "회의를 시작하면 Windows 자막이 여기에 표시됩니다.";

    private static string FormatCaption(CaptionSegment segment) =>
        string.IsNullOrWhiteSpace(segment.SpeakerName)
            ? segment.Text
            : $"{segment.SpeakerName}: {segment.Text}";

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    public sealed record Row(
        DateTimeOffset Timestamp,
        string OriginalText,
        string TranslatedText,
        string ProviderLabel,
        string SpeakerName,
        string SourceInitial);
}
