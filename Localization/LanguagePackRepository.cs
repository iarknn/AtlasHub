using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AtlasHub.Services;

namespace AtlasHub.Localization;

public sealed class LanguagePackRepository
{
    private readonly AppPaths _paths;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache = new();

    public LanguagePackRepository(AppPaths paths) => _paths = paths;

    public Dictionary<string, string> Load(CultureInfo culture)
    {
        var name = culture.Name;
        return _cache.GetOrAdd(name, _ => LoadInternal(name));
    }

    public IReadOnlyList<string> GetAvailableCultureFiles()
    {
        if (!Directory.Exists(_paths.LangRoot)) return Array.Empty<string>();

        return Directory.GetFiles(_paths.LangRoot, "*.json")
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!) // null olamaz, çünkü yukarıda filtreledik
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();
    }

    public void ClearCache() => _cache.Clear();

    private Dictionary<string, string> LoadInternal(string cultureName)
    {
        var file = Path.Combine(_paths.LangRoot, $"{cultureName}.json");
        if (!File.Exists(file)) return new Dictionary<string, string>();

        try
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
