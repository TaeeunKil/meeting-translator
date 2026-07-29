using MeetingTranslator.Models;
using MeetingTranslator.Services;

namespace MeetingTranslator.Tests;

public class MeetingExporterTests
{
    [Fact]
    public async Task CsvExport_EscapesCommasAndQuotes()
    {
        var path = Path.GetTempFileName();
        var entries = new[] {
            new TranscriptEntry(1, "m", DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                AudioSource.Microphone, "Hello, \"team\"", "안녕하세요", .95)
        };
        await MeetingExporter.ExportCsvAsync(entries, path);
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("\"Hello, \"\"team\"\"\"", text);
        File.Delete(path);
    }

    [Fact]
    public async Task MarkdownExport_ContainsBothLanguages()
    {
        var path = Path.GetTempFileName();
        var entries = new[] {
            new TranscriptEntry(1, "m", DateTimeOffset.Now, AudioSource.SystemAudio,
                "Ship Friday", "금요일 배포", .9)
        };
        await MeetingExporter.ExportMarkdownAsync(entries, path, "테스트 회의");
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("Ship Friday", text);
        Assert.Contains("금요일 배포", text);
        File.Delete(path);
    }
}
