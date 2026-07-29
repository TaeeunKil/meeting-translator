using System.Text.Json;

namespace MeetingTranslator.Models;

public sealed class AppSettings
{
    public string ProjectId { get; set; } = "";
    public string CredentialsPath { get; set; } = "";
    public string SourceLanguage { get; set; } = "en-US";
    public string TargetLanguage { get; set; } = "ko";
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = true;

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
