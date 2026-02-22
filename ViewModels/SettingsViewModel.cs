using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AtlasHub.Localization;
using AtlasHub.Models;
using AtlasHub.Services;

namespace AtlasHub.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly LocalizationService _loc;
    private readonly LanguagePackRepository _packs;

    public ObservableCollection<LanguageOption> Languages { get; } = new();

    [ObservableProperty] private LanguageOption? _selectedLanguage;

    public SettingsViewModel(SettingsService settings, LocalizationService loc, LanguagePackRepository packs)
    {
        _settings = settings;
        _loc = loc;
        _packs = packs;

        ReloadLanguages();
        SelectedLanguage = Languages.FirstOrDefault(l => l.CultureName.Equals(_settings.Current.CultureName, StringComparison.OrdinalIgnoreCase))
                           ?? Languages.FirstOrDefault();
    }

    [RelayCommand]
    private void ReloadLanguages()
    {
        Languages.Clear();

        // Built-in: Türkçe
        Languages.Add(new LanguageOption("tr-TR", "Türkçe (Dahili)", true));

        // Community packs
        foreach (var cultureName in _packs.GetAvailableCultureFiles())
        {
            if (cultureName.Equals("tr-TR", StringComparison.OrdinalIgnoreCase))
                continue;

            Languages.Add(new LanguageOption(cultureName, $"{cultureName} (Topluluk)", false));
        }
    }

    [RelayCommand]
    private void ApplyLanguage()
    {
        if (SelectedLanguage is null) return;

        _settings.SetCulture(SelectedLanguage.CultureName);
        _packs.ClearCache();
        _loc.SetCulture(new CultureInfo(SelectedLanguage.CultureName));
    }
}
