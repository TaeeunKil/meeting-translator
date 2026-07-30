using System.Text.Json;

namespace MeetingTranslator.Models;

public sealed class AppSettings
{
    public CaptionSourceKind CaptionSource { get; set; } = CaptionSourceKind.WindowsLiveCaptions;
    public TranslationProviderKind TranslationProvider { get; set; } = TranslationProviderKind.FreeGoogle;
    public string ProjectId { get; set; } = "";
    public string CredentialsPath { get; set; } = "";
    public string SourceLanguage { get; set; } = "en-US";
    public string TargetLanguage { get; set; } = "ko";
    public string QwenBaseUrl { get; set; } = "http://172.30.1.57:8400/v1";
    public string QwenModel { get; set; } = "qwen3.5-27b";
    public bool QwenFallbackToFreeGoogle { get; set; } = true;
    public int MonthlyCharacterLimit { get; set; } = 490_000;

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetingTranslator");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
