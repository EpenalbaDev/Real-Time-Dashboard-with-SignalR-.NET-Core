namespace RealTimeDashboard.Services;

public interface ILocalizationService
{
    string CurrentLanguage { get; }
    string Get(string key);
    Task InitializeAsync();
    Task SetLanguageAsync(string lang);
    event Action? OnLanguageChanged;
}
