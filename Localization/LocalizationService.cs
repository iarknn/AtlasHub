using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace AtlasHub.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly LanguagePackRepository _packs;

    // Aktif birleşik sözlük (TR built-in + JSON override)
    private Dictionary<string, string> _current = new(StringComparer.OrdinalIgnoreCase);

    // Tek built-in sözlük: Türkçe
    private static readonly Dictionary<string, string> BuiltInTr = BuildTr();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Uygulamanın aktif kültürü (sadece formatlama + JSON pack seçimi için).
    /// Metinlerin temeli her zaman Türkçe built-in sözlüktür.
    /// </summary>
    public CultureInfo CurrentCulture { get; private set; } = new("tr-TR");

    public LocalizationService(LanguagePackRepository packs)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
        Rebuild();
    }

    /// <summary>
    /// XAML'de {loc:Loc Key=Some.Key} => Loc.Svc["Some.Key"]
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return _current.TryGetValue(key, out var value)
                ? value
                : key; // fallback: key'i göster, eksik çeviri anlaşılır olsun
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
        // 1) Temelde her zaman TR sözlük var
        var merged = new Dictionary<string, string>(BuiltInTr, StringComparer.OrdinalIgnoreCase);

        // 2) İlgili kültür için JSON pack varsa üzerine yaz
        var pack = _packs.Load(CurrentCulture);
        foreach (var kvp in pack)
            merged[kvp.Key] = kvp.Value;

        _current = merged;
        RaiseAllChanged();
    }

    private void RaiseAllChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        // Indexer bindingleri için WPF konvansiyonu: "Item[]"
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    // -------------------
    // Built-in TR sözlük
    // -------------------

    private static Dictionary<string, string> BuildTr()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // App / Nav
            ["App.Title"] = "Atlas Hub",
            ["Nav.LiveTv"] = "Canlı TV",
            ["Nav.Movies"] = "Filmler",
            ["Nav.Series"] = "Diziler",
            ["Nav.Sources"] = "Kaynaklar",
            ["Nav.Settings"] = "Ayarlar",
            ["Nav.StatusHeader"] = "Durum",
            ["Nav.SprintStatus"] = "Sprint 1: Live",

            // Settings page
            ["Settings.Title"] = "Ayarlar",
            ["Settings.Subtitle"] = "Dil ve uygulama tercihleri",
            ["Settings.Language.SectionTitle"] = "Dil",
            ["Settings.Language.SectionDescription"] =
                "Atlas Hub varsayılan olarak Türkçe gelir.\n" +
                "Diğer diller topluluk tarafından dil paketi olarak eklenebilir.",
            ["Settings.Language.Label"] = "Uygulama dili",
            ["Settings.Language.Reload"] = "Dilleri yenile",
            ["Settings.Language.Apply"] = "Uygula",
            ["Settings.Language.CommunityTitle"] = "Topluluk dil paketleri",
            ["Settings.Language.CommunityDescription"] =
                "Ek diller JSON tabanlı dil paketleri ile eklenir.",
            ["Settings.Language.CommunityPath"] = @"%AppData%\AtlasHub\lang\*.json",

            // Dil listesi için suffix'ler
            ["Settings.Language.BuiltInSuffix"] = "(Dahili)",
            ["Settings.Language.CommunitySuffix"] = "(Topluluk)",

            // Placeholder (Movies / Series)
            ["Placeholder.Generic.Line1"] = "Bu modül henüz hazır değil.",
            ["Placeholder.Generic.Line2"] =
                "Önümüzdeki sprintlerde film ve dizi kütüphanesi bu ekranda açılacak.",
            ["Placeholder.Generic.FeaturesTitle"] = "Planlanan özellikler:",
            ["Placeholder.Generic.Feature1"] = "Kaynaklardan gelen film / dizi listeleri",
            ["Placeholder.Generic.Feature2"] = "Tür, yıl, dil ve etiket filtreleri",
            ["Placeholder.Generic.Feature3"] = "Devam ettiğin içerikleri profil bazlı takip",
            ["Placeholder.Generic.Footer"] =
                "Şimdilik canlı TV ve kaynak yönetimi öncelikli.\n" +
                "Bu ekran Sprint 2–3’te canlanacak.",

            // Live TV page – UI
            ["LiveTv.PlayerTitle"] = "Oynatıcı",
            ["LiveTv.ProgramDetailsTitle"] = "Program Detayı",
            ["LiveTv.TimelineTitle"] = "Timeline",
            ["LiveTv.Search.Category"] = "Kategori ara...",
            ["LiveTv.Search.Channel"] = "Kanal ara...",
            ["LiveTv.VideoModeLabel"] = "Görüntü",
            ["LiveTv.VolumeLabel"] = "Ses",
            ["LiveTv.FullscreenTooltip"] = "Tam ekran (ESC ile çık)",
            ["LiveTv.RefreshButton"] = "Yenile",
            ["LiveTv.Scope.AllSources"] = "Tüm Kaynaklar",

            // Live TV – durum / metinler
            ["LiveTv.Status.ProfileNotSelected"] = "Profil seçilmedi.",
            ["LiveTv.Status.Loading"] = "Yükleniyor...",
            ["LiveTv.Status.NoActiveSource"] =
                "Etkin kaynak yok.\nSources ekranından kaynak ekleyin/etkinleştirin.",
            ["LiveTv.Status.Ready"] = "Hazır",
            ["LiveTv.Status.LoadErrorPrefix"] = "Yükleme hatası: {0}",
            ["LiveTv.Status.EpgErrorPrefix"] = "EPG hatası: {0}",

            ["LiveTv.Program.NoDescription"] = "Açıklama yok",
            ["LiveTv.Program.RemainingEnded"] = "Bitti",
            ["LiveTv.Program.RemainingHoursMinutes"] = "Kalan: {0} sa {1} dk",
            ["LiveTv.Program.RemainingMinutes"] = "Kalan: {0} dk",

            // Live TV – kanal listesi
            ["LiveTv.Channel.FallbackNowTitle"] = "Program",
            ["LiveTv.Channel.FallbackNextTitle"] = "Sonraki program",
            ["LiveTv.Channel.NextPrefix"] = "Sonra: {0}",

            // Providers – status
            ["Providers.Status.ProfileNotSelected"] = "Profil seçili değil.",
            ["Providers.Status.XmltvSaved"] = "XMLTV kaydedildi.",
            ["Providers.Status.AddM3uErrorPrefix"] = "Kaynak eklenemedi: {0}",
            ["Providers.Status.RefreshErrorPrefix"] = "Katalog yenilenemedi: {0}",
            ["Providers.Status.UpdateErrorPrefix"] = "Kaynak güncellenemedi: {0}",
            ["Providers.Status.ReloadErrorPrefix"] = "Sağlayıcılar yenilenemedi: {0}",

            // Providers – validation errors
            ["Providers.Error.M3uNameRequired"] = "Kaynak adı boş olamaz.",
            ["Providers.Error.M3uSourceRequired"] = "M3U URL veya dosya yolu girilmelidir.",
            ["Providers.Error.M3uFileNotFound"] = "M3U dosyası bulunamadı.",
            ["Providers.Error.M3uUrlOrFileMissing"] = "M3U URL veya dosya yolu boş.",

            // Providers – UI
            ["Providers.Title"] = "Kaynaklar",
            ["Providers.Subtitle"] = "M3U ve EPG sağlayıcılarını yönet",
            ["Providers.ReloadButton"] = "Yenile",
            ["Providers.List.Header"] = "Sağlayıcılar",
            ["Providers.List.Empty"] = "Henüz kaynak eklenmemiş.",

            ["Providers.Add.SectionTitle"] = "Yeni M3U kaynağı ekle",
            ["Providers.Add.NameLabel"] = "Görünen ad",
            ["Providers.Add.M3uUrlLabel"] = "M3U URL",
            ["Providers.Add.EnableForProfileLabel"] = "Bu profil için etkin",
            ["Providers.Add.AddButton"] = "Kaynak ekle",

            ["Providers.Epg.SectionTitle"] = "EPG (XMLTV)",
            ["Providers.Epg.XmltvUrlLabel"] = "XMLTV URL",
            ["Providers.Epg.SaveButton"] = "EPG ayarını kaydet",

            ["Providers.Http.SectionTitle"] = "Gelişmiş HTTP ayarları",
            ["Providers.Http.UserAgentLabel"] = "User-Agent",
            ["Providers.Http.RefererLabel"] = "Referer",
            ["Providers.Http.TimeoutLabel"] = "Timeout (sn)",

            ["Providers.Selected.SectionTitle"] = "Seçili kaynak",
            ["Providers.Selected.EnabledForProfileLabel"] = "Bu profil için etkin",
            ["Providers.Selected.RefreshCatalogButton"] = "Kanal listesini yenile",
            ["Providers.Selected.DeleteButton"] = "Bu kaynağı sil",
            ["Providers.Selected.NameLabel"] = "Ad",
            ["Providers.Selected.M3uUrlLabel"] = "M3U URL",
            ["Providers.Selected.SaveButton"] = "Değişiklikleri kaydet",

            // Providers – toast
            ["Providers.Toast.ProviderDeleted"] = "Kaynak silindi.",
            ["Providers.Toast.ProviderAdded"] = "Kaynak eklendi.",
            ["Providers.Toast.ProviderUpdated"] = "Kaynak güncellendi.",
            ["Providers.Toast.EpgNotFound"] = "EPG bulunamadı (x-tvg-url yok).",
            ["Providers.Toast.CatalogUpdated"] = "Katalog güncellendi.",
            ["Providers.Toast.EpgLoadFailed"] =
                "EPG indirilemedi (XMLTV değil / erişim engeli).",
            ["Providers.Toast.EpgLoadedSummary"] =
                "EPG yüklendi: {0} program, {1} kanal",
            ["Providers.Toast.EpgMergeSummary"] =
                "EPG: OK={0}, DL_FAIL={1}, PARSE_FAIL={2}, NOT_XML={3}, Programs={4}, Channels={5}"
        };

        return d;
    }
}