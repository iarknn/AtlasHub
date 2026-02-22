using System.IO;
using System.Text.Json;
using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class CatalogRepository
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public CatalogRepository(AppPaths paths) => _paths = paths;

    private string PathFor(string providerId) => Path.Combine(_paths.CatalogRoot, $"{providerId}.json");

    public async Task SaveAsync(CatalogSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, _json);
        await File.WriteAllTextAsync(PathFor(snapshot.ProviderId), json);
    }

    public async Task<CatalogSnapshot?> LoadAsync(string providerId)
    {
        var file = PathFor(providerId);
        if (!File.Exists(file)) return null;
        var json = await File.ReadAllTextAsync(file);
        return JsonSerializer.Deserialize<CatalogSnapshot>(json, _json);
    }

    public Task DeleteAsync(string providerId)
    {
        var file = PathFor(providerId);
        if (File.Exists(file))
            File.Delete(file);

        return Task.CompletedTask;
    }
}
