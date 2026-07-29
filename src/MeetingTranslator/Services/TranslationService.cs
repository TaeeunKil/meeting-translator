using System.Net.Http;
using System.Text.Json;
using Google.Cloud.Translation.V2;

namespace MeetingTranslator.Services;

public interface ITranslationService
{
    Task<string> TranslateAsync(string text, string targetLanguage, string sourceLanguage);
}

public sealed class CloudTranslationService : ITranslationService
{
    private readonly TranslationClient _client = TranslationClient.Create();

    public async Task<string> TranslateAsync(string text, string targetLanguage, string sourceLanguage)
    {
        var result = await _client.TranslateTextAsync(text, targetLanguage, sourceLanguage.Split('-')[0]);
        return result.TranslatedText;
    }
}

public sealed class UnofficialGoogleTranslationService : ITranslationService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<string> TranslateAsync(string text, string targetLanguage, string sourceLanguage)
    {
        var url = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex" +
                  $"&sl={Uri.EscapeDataString(sourceLanguage.Split('-')[0])}" +
                  $"&tl={Uri.EscapeDataString(targetLanguage)}&q={Uri.EscapeDataString(text)}";
        using var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<List<List<string>>>(body);
        return parsed?.FirstOrDefault()?.FirstOrDefault() ??
               throw new InvalidOperationException("Google 번역 응답을 읽을 수 없습니다.");
    }
}
