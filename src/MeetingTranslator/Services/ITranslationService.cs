namespace MeetingTranslator.Services;

public interface ITranslationService
{
    Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string sourceLanguage,
        CancellationToken cancellationToken = default);
}
