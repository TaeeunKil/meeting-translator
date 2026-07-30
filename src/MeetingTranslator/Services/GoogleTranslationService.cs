using Google.Cloud.Translation.V2;

namespace MeetingTranslator.Services;

public sealed class GoogleTranslationService : ITranslationService
{
    private readonly TranslationClient _client;

    public GoogleTranslationService() => _client = TranslationClient.Create();

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string sourceLanguage,
        CancellationToken cancellationToken = default)
    {
        var source = sourceLanguage.Split('-')[0];
        var result = await _client.TranslateTextAsync(text, targetLanguage, source);
        return result.TranslatedText;
    }
}
