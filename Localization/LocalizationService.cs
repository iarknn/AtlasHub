using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using AtlasHub.Services;

namespace AtlasHub.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly LanguagePackRepository _packs;

    // Aktif birleşik sözlük (built-in + JSON pack override)
    private Dictionary<string, string> _current = new(StringComparer.OrdinalIgnoreCase);

    // Built-in TR / EN string’leri
    private static readonly Dictionary<string, string> BuiltInTr = BuildTr();
    private static readonly Dictionary<string, string> BuiltInEn = BuildEn();

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo CurrentCulture { get; private set; } = new("tr-TR");

    public LocalizationService(LanguagePackRepository packs)
    {
        _packs = packs;
        Rebuild();
    }

    /// <summary>
    /// XAML'de {loc Some.Key} => Loc.Svc["Some.Key"]
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return _current.TryGetValue(key, out var value)
                ? value
                : key; // fallback: key'i göster
        }
    }

    public void SetCulture(CultureInfo culture)
    {
        if (culture is null) throw new ArgumentNullException(nameof(culture));

        if (string.Equals(CurrentCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
            return;

        CurrentCulture = culture;
        Rebuild();
    }

    // -------------------
    // İç mantık
    // -------------------
    private void Rebuild()
    {
        var baseDict = GetBuiltIn(CurrentCulture);

        // Built-in kopyası
        var merged = new Dictionary<string, string>(baseDict, StringComparer.OrdinalIgnoreCase);

        // JSON language pack override (varsa)
        var pack = _packs.Load(CurrentCulture);
        foreach (var kvp in pack)
            merged[kvp.Key] = kvp.Value;

        _current = merged;

        RaiseAllChanged();
    }

    private static Dictionary<string, string> GetBuiltIn(CultureInfo culture)
    {
        var name = culture.Name;

        if (name.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
            return BuiltInTr;

        if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return BuiltInEn;

        // Diğer tüm kültürler için varsayılan EN
        return BuiltInEn;
    }

    private void RaiseAllChanged()
    {
        // Culture değiştiğini bildir
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));

        // Indexer bindingleri için WPF konvansiyonu: "Item[]"
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    // -------------------
    // Built-in sözlükler
    // -------------------
    private static Dictionary<string, string> BuildTr()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // App / Nav (ileride nav için kullanacağız)
            ["App.Title"] = "Atlas Hub",
            ["Nav.LiveTv"] = "Canlı TV",
            ["Nav.Movies"] = "Filmler",
            ["Nav.Series"] = "Diziler",
            ["Nav.Sources"] = "Kaynaklar",
            ["Nav.Settings"] = "Ayarlar",
            ["Nav.SprintStatus"] = "Sprint 1: Live TV + Kaynaklar",

            // Settings page
            ["Settings.Title"] = "Ayarlar",
            ["Settings.Subtitle"] = "Dil ve uygulama tercihleri",

            ["Settings.Language.SectionTitle"] = "Dil",
            ["Settings.Language.SectionDescription"] =
                "Atlas Hub varsayılan olarak Türkçe gelir. Diğer diller topluluk tarafından dil paketi olarak eklenebilir.",

            ["Settings.Language.Label"] = "Uygulama dili",
            ["Settings.Language.Reload"] = "Dilleri yenile",
            ["Settings.Language.Apply"] = "Uygula",

            ["Settings.Language.CommunityTitle"] = "Topluluk dil paketleri",
            ["Settings.Language.CommunityDescription"] =
                "Ek diller JSON tabanlı dil paketleri ile eklenir.",
            ["Settings.Language.CommunityPath"] = @"%AppData%\AtlasHub\lang\*.json",
        };

        return d;
    }

    private static Dictionary<string, string> BuildEn()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // App / Nav
            ["App.Title"] = "Atlas Hub",
            ["Nav.LiveTv"] = "Live TV",
            ["Nav.Movies"] = "Movies",
            ["Nav.Series"] = "Series",
            ["Nav.Sources"] = "Sources",
            ["Nav.Settings"] = "Settings",
            ["Nav.SprintStatus"] = "Sprint 1: Live TV + Sources",

            // Settings page
            ["Settings.Title"] = "Settings",
            ["Settings.Subtitle"] = "Language and application preferences",

            ["Settings.Language.SectionTitle"] = "Language",
            ["Settings.Language.SectionDescription"] =
                "Atlas Hub ships with Turkish by default. Other languages can be added by the community as language packs.",

            ["Settings.Language.Label"] = "App language",
            ["Settings.Language.Reload"] = "Reload languages",
            ["Settings.Language.Apply"] = "Apply",

            ["Settings.Language.CommunityTitle"] = "Community language packs",
            ["Settings.Language.CommunityDescription"] =
                "Additional languages are provided as JSON-based language packs.",
            ["Settings.Language.CommunityPath"] = @"%AppData%\AtlasHub\lang\*.json",
        };

        return d;
    }
}