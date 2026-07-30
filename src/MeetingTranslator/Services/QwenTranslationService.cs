using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeetingTranslator.Services;

public sealed class QwenTranslationService : ITranslationService
{
    private readonly HttpClient _client;
    private readonly string _model;
    private readonly ITranslationService? _fallback;

    public QwenTranslationService(
        string baseUrl,
        string model,
        ITranslationService? fallback = null)
    {
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        _client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
        _model = model;
        _fallback = fallback;
    }

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string sourceLanguage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                model = _model,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = BuildPrompt(sourceLanguage, targetLanguage)
                    },
                    new { role = "user", content = text }
                },
                temperature = 0,
                max_tokens = 256,
                chat_template_kwargs = new { enable_thinking = false }
            };

            using var response = await _client.PostAsJsonAsync(
                "chat/completions",
                request,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            using var result = await response.Content.ReadFromJsonAsync<JsonDocument>(
                cancellationToken: cancellationToken);
            var translated = result?.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim();

            if (string.IsNullOrWhiteSpace(translated))
                throw new InvalidOperationException("Qwen이 빈 번역 결과를 반환했습니다.");

            return translated;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _fallback is not null)
        {
            return await _fallback.TranslateAsync(
                text,
                targetLanguage,
                sourceLanguage,
                cancellationToken);
        }
        catch (Exception) when (_fallback is not null)
        {
            return await _fallback.TranslateAsync(
                text,
                targetLanguage,
                sourceLanguage,
                cancellationToken);
        }
    }

    private static string BuildPrompt(string sourceLanguage, string targetLanguage)
    {
        var source = LanguageName(sourceLanguage);
        var target = LanguageName(targetLanguage);
        return $"Translate {source} into natural {target}. " +
               $"Return only the {target} translation. Do not add explanations.";
    }

    private static string LanguageName(string language) =>
        language.Split('-')[0].ToLowerInvariant() switch
        {
            "en" => "English",
            "ko" => "Korean",
            "ja" => "Japanese",
            "zh" => "Chinese",
            "es" => "Spanish",
            "fr" => "French",
            "de" => "German",
            _ => language
        };
}
