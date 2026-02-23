using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using AtlasHub.Localization;
using AtlasHub.Models;
using AtlasHub.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtlasHub.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly LocalizationService _loc;
    private readonly LanguagePackRepository _packs;

    public ObservableCollection<LanguageOption> Languages { get; } = new();

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    public SettingsViewModel(
        SettingsService settings,
        LocalizationService loc,
        LanguagePackRepository packs)
    {
        _settings = settings;
        _loc = loc;
        _packs = packs;

        ReloadLanguages();

        SelectedLanguage =
            Languages.FirstOrDefault(l =>
                l.CultureName.Equals(_settings.Current.CultureName,
                    StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault();
    }

    [RelayCommand]
    private void ReloadLanguages()
    {
        Languages.Clear();

        // 1) Built-in: Türkçe
        var trCulture = new CultureInfo("tr-TR");
        var trBaseName = trCulture.NativeName; // örn: "Türkçe (Türkiye)"

        var builtInSuffix = _loc["Settings.Language.BuiltInSuffix"];
        var builtInDisplay = string.IsNullOrWhiteSpace(builtInSuffix)
            ? trBaseName
            : $"{trBaseName} {builtInSuffix}";

        Languages.Add(new LanguageOption("tr-TR", builtInDisplay, true));

        // 2) Topluluk JSON paketleri
        var communitySuffix = _loc["Settings.Language.CommunitySuffix"];

        foreach (var cultureName in _packs.GetAvailableCultureFiles())
        {
            if (cultureName.Equals("tr-TR", StringComparison.OrdinalIgnoreCase))
                continue;

            string baseName;

            try
            {
                var ci = new CultureInfo(cultureName);

                // NativeName: OS diline göre "English (United States)" / "Deutsch (Deutschland)" vs.
                // Eğer hep İngilizce görmek istersen EnglishName kullanabiliriz.
                baseName = ci.NativeName;
            }
            catch (CultureNotFoundException)
            {
                // Geçersiz culture koduysa dosya adını aynen göster.
                baseName = cultureName;
            }

            var display = string.IsNullOrWhiteSpace(communitySuffix)
                ? baseName
                : $"{baseName} {communitySuffix}";

            Languages.Add(new LanguageOption(cultureName, display, false));
        }
    }

    [RelayCommand]
    private void ApplyLanguage()
    {
        if (SelectedLanguage is null)
            return;

        _settings.SetCulture(SelectedLanguage.CultureName);
        _packs.ClearCache();
        _loc.SetCulture(new CultureInfo(SelectedLanguage.CultureName));
    }
}