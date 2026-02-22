using AtlasHub.Models;
using System.IO;
using System.Text.Json;

namespace AtlasHub.Services;

public sealed class EpgRepository
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static string BaseDir
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasHub", "epg");

    private static string FilePath(string providerId)
        => Path.Combine(BaseDir, $"epg_{providerId}.json");

    public async Task SaveAsync(EpgSnapshot snapshot)
    {
        Directory.CreateDirectory(BaseDir);
        var path = FilePath(snapshot.ProviderId);
        var json = JsonSerializer.Serialize(snapshot, _json);
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<EpgSnapshot?> LoadAsync(string providerId)
    {
        var path = FilePath(providerId);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<EpgSnapshot>(json, _json);
    }

    public Task DeleteAsync(string providerId)
    {
        var path = FilePath(providerId);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }
}
