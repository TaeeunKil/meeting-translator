using System.Net.Http;
using System.Text.Json;

namespace MeetingTranslator.Services;

public sealed class FreeGoogleTranslationService : ITranslationService
{
    private static readonly HttpClient Client = CreateClient();

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string sourceLanguage,
        CancellationToken cancellationToken = default)
    {
        var source = Uri.EscapeDataString(sourceLanguage.Split('-')[0]);
        var target = Uri.EscapeDataString(targetLanguage.Split('-')[0]);
        var query = Uri.EscapeDataString(text);
        var url = "https://clients5.google.com/translate_a/t" +
                  $"?client=dict-chrome-ex&sl={source}&tl={target}&q={query}";

        using var response = await Client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var payload = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        var root = payload.RootElement;
        var translated = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
            ? ReadFirstTranslation(root[0])
            : null;

        if (string.IsNullOrWhiteSpace(translated))
            throw new InvalidOperationException("무료 Google 번역 응답을 해석할 수 없습니다.");

        return translated;
    }

    private static string? ReadFirstTranslation(JsonElement first)
    {
        if (first.ValueKind == JsonValueKind.String)
            return first.GetString();

        if (first.ValueKind == JsonValueKind.Array &&
            first.GetArrayLength() > 0 &&
            first[0].ValueKind == JsonValueKind.String)
            return first[0].GetString();

        return null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MeetingTranslator/1.0");
        return client;
    }
}
