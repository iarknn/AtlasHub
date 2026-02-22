using System.IO;
using System.Text.Json;
using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class ProviderRepository
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public ProviderRepository(AppPaths paths) => _paths = paths;

    public async Task<List<ProviderSource>> GetAllAsync()
    {
        if (!File.Exists(_paths.ProvidersJsonPath)) return new();
        var json = await File.ReadAllTextAsync(_paths.ProvidersJsonPath);
        return JsonSerializer.Deserialize<List<ProviderSource>>(json, _json) ?? new();
    }

    public async Task SaveAllAsync(List<ProviderSource> providers)
    {
        var json = JsonSerializer.Serialize(providers, _json);
        await File.WriteAllTextAsync(_paths.ProvidersJsonPath, json);
    }

    // ------------------------------------------------------------------
    // Convenience helpers (used by ProviderService)
    // ------------------------------------------------------------------

    public async Task UpsertAsync(ProviderSource provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));

        var all = await GetAllAsync();
        var idx = all.FindIndex(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) all[idx] = provider;
        else all.Add(provider);

        await SaveAllAsync(all);
    }

    public async Task DeleteAsync(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;

        var all = await GetAllAsync();
        all.RemoveAll(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));
        await SaveAllAsync(all);
    }
}
