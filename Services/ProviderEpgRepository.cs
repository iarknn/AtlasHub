using AtlasHub.Models;
using System.IO;
using System.Text.Json;

namespace AtlasHub.Services;

public sealed class ProviderEpgRepository
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static string BaseDir
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasHub", "epg");

    private static string PathFile
        => Path.Combine(BaseDir, "provider_epg.json");

    public async Task<List<ProviderEpgConfig>> GetAllAsync()
    {
        Directory.CreateDirectory(BaseDir);
        if (!File.Exists(PathFile)) return new List<ProviderEpgConfig>();

        var json = await File.ReadAllTextAsync(PathFile);
        return JsonSerializer.Deserialize<List<ProviderEpgConfig>>(json, _json) ?? new List<ProviderEpgConfig>();
    }

    public async Task SaveAllAsync(List<ProviderEpgConfig> list)
    {
        Directory.CreateDirectory(BaseDir);
        var json = JsonSerializer.Serialize(list, _json);
        await File.WriteAllTextAsync(PathFile, json);
    }

    public async Task<ProviderEpgConfig?> GetForProviderAsync(string providerId)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(x => x.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SetForProviderAsync(string providerId, string? xmltvUrl, string? xmltvFilePath)
    {
        var all = await GetAllAsync();
        var existing = all.FirstOrDefault(x => x.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        var cleanUrl = string.IsNullOrWhiteSpace(xmltvUrl) ? null : xmltvUrl.Trim();
        var cleanFile = string.IsNullOrWhiteSpace(xmltvFilePath) ? null : xmltvFilePath.Trim();

        if (existing is null)
            all.Add(new ProviderEpgConfig(providerId, cleanUrl, cleanFile));
        else
        {
            all.Remove(existing);
            all.Add(existing with { XmltvUrl = cleanUrl, XmltvFilePath = cleanFile });
        }

        await SaveAllAsync(all);
    }
}
