using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using AtlasHub.Services;

namespace AtlasHub.Localization;

public sealed class LanguagePackRepository
{
    private readonly AppPaths _paths;

    // cultureName -> dictionary
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public LanguagePackRepository(AppPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// İstediğin kültür için JSON dil paketini yükler (cache'li).
    /// </summary>
    public Dictionary<string, string> Load(CultureInfo culture)
    {
        if (culture is null) throw new ArgumentNullException(nameof(culture));

        var name = culture.Name;
        return _cache.GetOrAdd(name, _ => LoadInternal(name));
    }

    /// <summary>
    /// %AppData%\AtlasHub\lang altındaki *.json dosyalarının culture adlarını döner.
    /// </summary>
    public IReadOnlyList<string> GetAvailableCultureFiles()
    {
        if (!Directory.Exists(_paths.LangRoot))
            return Array.Empty<string>();

        return Directory
            .GetFiles(_paths.LangRoot, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    public void ClearCache() => _cache.Clear();

    private Dictionary<string, string> LoadInternal(string cultureName)
    {
        var file = Path.Combine(_paths.LangRoot, $"{cultureName}.json");

        if (!File.Exists(file))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(file);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            return dict is not null
                ? new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Bozuk JSON => sessizce boş sözlük
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}