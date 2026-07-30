using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

public static class TranslationServiceFactory
{
    public static ITranslationService Create(AppSettings settings)
    {
        return settings.TranslationProvider switch
        {
            TranslationProviderKind.FreeGoogle => new FreeGoogleTranslationService(),
            TranslationProviderKind.GoogleCloud => CreateGoogleCloud(settings),
            TranslationProviderKind.Qwen => new QwenTranslationService(
                settings.QwenBaseUrl,
                settings.QwenModel,
                settings.QwenFallbackToFreeGoogle
                    ? new FreeGoogleTranslationService()
                    : null),
            _ => throw new ArgumentOutOfRangeException(
                nameof(settings.TranslationProvider),
                settings.TranslationProvider,
                "지원하지 않는 번역 엔진입니다.")
        };
    }

    private static ITranslationService CreateGoogleCloud(AppSettings settings)
    {
        Environment.SetEnvironmentVariable(
            "GOOGLE_APPLICATION_CREDENTIALS",
            settings.CredentialsPath);
        return new GoogleTranslationService();
    }
}
