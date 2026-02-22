using System.IO;
using System.Text.Json;
using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class ProfileProviderRepository
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public ProfileProviderRepository(AppPaths paths) => _paths = paths;

    public async Task<List<ProfileProviderLink>> GetAllAsync()
    {
        if (!File.Exists(_paths.ProfileProvidersJsonPath)) return new();
        var json = await File.ReadAllTextAsync(_paths.ProfileProvidersJsonPath);
        return JsonSerializer.Deserialize<List<ProfileProviderLink>>(json, _json) ?? new();
    }

    public async Task SaveAllAsync(List<ProfileProviderLink> links)
    {
        var json = JsonSerializer.Serialize(links, _json);
        await File.WriteAllTextAsync(_paths.ProfileProvidersJsonPath, json);
    }

    // ------------------------------------------------------------------
    // Convenience helpers (used by ProviderService)
    // ------------------------------------------------------------------

    public async Task UpsertAsync(ProfileProviderLink link)
    {
        var all = await GetAllAsync();

        // Key: (ProfileId, ProviderId)
        var idx = all.FindIndex(x =>
            string.Equals(x.ProfileId, link.ProfileId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ProviderId, link.ProviderId, StringComparison.OrdinalIgnoreCase));

        if (idx >= 0) all[idx] = link;
        else all.Add(link);

        await SaveAllAsync(all);
    }

    public async Task SetEnabledAsync(string profileId, string providerId, bool isEnabled)
    {
        var all = await GetAllAsync();
        var idx = all.FindIndex(x =>
            string.Equals(x.ProfileId, profileId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

        if (idx >= 0)
        {
            var cur = all[idx];
            all[idx] = cur with { IsEnabled = isEnabled };
        }
        else
        {
            // If there isn't a link yet, create one.
            all.Add(new ProfileProviderLink(profileId, providerId, isEnabled, SortOrder: 0));
        }

        await SaveAllAsync(all);
    }

    public async Task DeleteByProviderAsync(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;

        var all = await GetAllAsync();
        all.RemoveAll(x => string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        await SaveAllAsync(all);
    }
}
