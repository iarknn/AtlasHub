using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AtlasHub.Services;

namespace AtlasHub.Localization;

/// <summary>
/// %AppData%\AtlasHub\lang\*.json içindeki dil paketlerini okur.
/// - Anahtar: LocalizationService'deki TR temel anahtarlarla aynı
/// - Değer: Hedef dilde çeviri (örn. en-US)
/// LocalizationService her zaman önce TR built-in sözlüğü,
/// ardından buradan gelen sözlüğü kullanır (override).
/// </summary>
public sealed class LanguagePackRepository
{
    private readonly AppPaths _paths;

    // Kılavuz: cultureName -> (key -> value) sözlüğü
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public LanguagePackRepository(AppPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Verilen kültür için JSON dil paketini yükler.
    /// Dosya yoksa veya parse edilemezse boş sözlük döner.
    /// </summary>
    public Dictionary<string, string> Load(CultureInfo culture)
    {
        var name = culture.Name; // örn: "en-US", "de-DE"
        return _cache.GetOrAdd(name, _ => LoadInternal(name));
    }

    /// <summary>
    /// Dil seçim ekranında göstermek için mevcut JSON dosyalarının isimlerini verir.
    /// Örn: ["en-US", "de-DE"]
    /// </summary>
    public IReadOnlyList<string> GetAvailableCultureFiles()
    {
        if (!Directory.Exists(_paths.LangRoot))
            return Array.Empty<string>();

        return Directory.GetFiles(_paths.LangRoot, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!) // yukarıda null filtreledik
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    /// <summary>Cache'i temizler (örneğin "Dilleri yenile" düğmesinden sonra).</summary>
    public void ClearCache() => _cache.Clear();

    private Dictionary<string, string> LoadInternal(string cultureName)
    {
        var file = Path.Combine(_paths.LangRoot, $"{cultureName}.json");
        if (!File.Exists(file))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(file);

            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            return raw is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Bozuk JSON ise hiçbir şeyi bozmayalım, sadece pack'i yok say.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}