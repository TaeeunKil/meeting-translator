using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using MeetingTranslator.Models;
using MeetingTranslator.Services;
using Microsoft.Win32;

namespace MeetingTranslator;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Row> _rows = [];
    private readonly TranscriptStore _store = new();
    private AppSettings _settings = AppSettings.Load();
    private LiveCaptionsReader? _reader;
    private ITranslationService? _translator;
    private readonly MonthlyUsageGuard _usage = new();
    private CancellationTokenSource? _meetingCancellation;
    private string? _meetingId;

    public MainWindow()
    {
        InitializeComponent();
        TranscriptGrid.ItemsSource = _rows;
        CredentialsBox.Text = _settings.CredentialsPath;
        TranslationModeBox.SelectedIndex = _settings.TranslationMode == "GoogleCloud" ? 1 : 0;
        UsageText.Text = _settings.TranslationMode == "GoogleCloud"
            ? $"{_usage.CharactersUsed:N0} / {_settings.MonthlyCharacterLimit:N0}자"
            : "무료 비공식 모드 · 로컬 제한 없음";
        Loaded += async (_, _) => await _store.InitializeAsync();
    }

    private void BrowseCredentials_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Google credentials (*.json)|*.json" };
        if (dialog.ShowDialog() == true) CredentialsBox.Text = dialog.FileName;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedMode = ((System.Windows.Controls.ComboBoxItem)TranslationModeBox.SelectedItem).Tag?.ToString()
                               ?? "UnofficialGoogle";
            if (selectedMode == "GoogleCloud" && !File.Exists(CredentialsBox.Text))
            {
                MessageBox.Show("공식 Google Cloud 번역을 쓰려면 서비스 계정 JSON 파일을 설정해 주세요.",
                    "설정 필요", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _settings.CredentialsPath = CredentialsBox.Text.Trim();
            _settings.TranslationMode = selectedMode;
            _settings.Save();
            if (selectedMode == "GoogleCloud")
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", _settings.CredentialsPath);

            _meetingId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            _meetingCancellation = new();
            _translator = selectedMode == "GoogleCloud"
                ? new CloudTranslationService()
                : new UnofficialGoogleTranslationService();
            _reader = new();
            _reader.InterimCaption += text =>
                Dispatcher.Invoke(() => InterimText.Text = text);
            _reader.FinalCaption += HandleFinalCaptionAsync;
            _rows.Clear();
            await _reader.StartAsync(_meetingCancellation.Token);
            ToggleMeeting(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "시작할 수 없음", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task HandleFinalCaptionAsync(string original)
    {
        if (_translator is null || _meetingId is null || string.IsNullOrWhiteSpace(original)) return;
        try
        {
            if (_settings.TranslationMode == "GoogleCloud" &&
                !_usage.TryConsume(original.Length, _settings.MonthlyCharacterLimit))
            {
                await Dispatcher.InvokeAsync(() =>
                    StatusText.Text = "월 49만 자 보호 한도 도달 — 번역 중지");
                return;
            }
            var translated = await _translator.TranslateAsync(original, _settings.TargetLanguage,
                _settings.SourceLanguage);
            var entry = await _store.AddAsync(_meetingId, AudioSource.LiveCaptions, original, translated, 1);
            await Dispatcher.InvokeAsync(() =>
            {
                _rows.Add(new(entry.Timestamp, "Live Captions", original, translated));
                TranscriptGrid.ScrollIntoView(_rows.Last());
                InterimText.Text = "";
                UsageText.Text = _settings.TranslationMode == "GoogleCloud"
                    ? $"{_usage.CharactersUsed:N0} / {_settings.MonthlyCharacterLimit:N0}자"
                    : "무료 비공식 모드 · 로컬 제한 없음";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => StatusText.Text = $"번역 오류: {ex.Message}");
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_meetingId is null) return;
        _meetingCancellation?.Cancel();
        _reader?.Dispose();
        var entries = await _store.GetMeetingAsync(_meetingId);

        var exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MeetingTranslator", "Exports");
        Directory.CreateDirectory(exportDir);
        var baseName = $"meeting-{DateTime.Now:yyyy-MM-dd-HHmm}";
        var csv = Path.Combine(exportDir, $"{baseName}.csv");
        var markdown = Path.Combine(exportDir, $"{baseName}.md");
        await MeetingExporter.ExportCsvAsync(entries, csv);
        await MeetingExporter.ExportMarkdownAsync(entries, markdown, $"회의록 {DateTime.Now:yyyy-MM-dd HH:mm}");
        ToggleMeeting(false);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{markdown}\"") { UseShellExecute = true });
        MessageBox.Show($"회의록을 저장했습니다.\n\n{markdown}\n{csv}", "내보내기 완료");
    }

    private void ToggleMeeting(bool running)
    {
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        TranslationModeBox.IsEnabled = !running;
        CredentialsBox.IsEnabled = !running;
        BrowseButton.IsEnabled = !running;
        StatusText.Text = running ? "녹음·번역 중" : "저장 완료";
    }

    public sealed record Row(DateTimeOffset Timestamp, string SourceLabel, string OriginalText, string TranslatedText);
}
