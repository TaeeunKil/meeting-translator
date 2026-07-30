using System.Text;
using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

public static class MeetingExporter
{
    public static async Task ExportCsvAsync(IEnumerable<TranscriptEntry> entries, string path)
    {
        var sb = new StringBuilder("timestamp,source,speaker,original,translated,confidence\r\n");
        foreach (var e in entries)
            sb.AppendLine(string.Join(",", Csv(e.Timestamp.ToString("O")), Csv(Label(e.Source)),
                Csv(e.SpeakerName ?? ""), Csv(e.OriginalText), Csv(e.TranslatedText),
                e.Confidence.ToString("0.000")));
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
    }

    public static async Task ExportMarkdownAsync(IEnumerable<TranscriptEntry> entries, string path,
        string title)
    {
        var list = entries.ToList();
        var sb = new StringBuilder()
            .AppendLine($"# {title}").AppendLine()
            .AppendLine($"- 시작: {list.FirstOrDefault()?.Timestamp:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"- 종료: {list.LastOrDefault()?.Timestamp:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"- 발화 수: {list.Count}").AppendLine()
            .AppendLine("## 대화 기록").AppendLine();
        foreach (var e in list)
            sb.AppendLine($"### {e.Timestamp:HH:mm:ss} · {SpeakerLabel(e)}")
                .AppendLine().AppendLine($"> {e.OriginalText}")
                .AppendLine().AppendLine(e.TranslatedText).AppendLine();
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Label(AudioSource source) => source == AudioSource.Microphone ? "나" : "상대방";
    private static string SpeakerLabel(TranscriptEntry entry) =>
        string.IsNullOrWhiteSpace(entry.SpeakerName)
            ? Label(entry.Source)
            : entry.SpeakerName;
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
