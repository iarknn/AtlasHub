using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtlasHub.Localization;
using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class LiveTvService
{
    private readonly ProviderService _providers;

    public LiveTvService(ProviderService providers)
    {
        _providers = providers;
    }

    /// <summary>
    /// Seçili profil için:
    /// - Scope listesi (ALL + provider'lar)
    /// - providerId -> CatalogSnapshot sözlüğü
    /// </summary>
    public async Task<(List<ProviderScope> scopes, Dictionary<string, CatalogSnapshot> catalogs)> LoadForProfileAsync(string profileId)
    {
        var enabledProviders = await _providers
            .GetEnabledProvidersForProfileAsync(profileId)
            .ConfigureAwait(false);

        var scopes = new List<ProviderScope>
        {
            new("ALL", Loc.Svc["LiveTv.Scope.AllSources"])
        };

        var catalogs = new Dictionary<string, CatalogSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in enabledProviders)
            scopes.Add(new ProviderScope(p.Id, p.Name));

        // Katalogları limitli paralel yükle (disk IO'yu patlatmadan hızlandır)
        const int parallelism = 4;
        using var gate = new SemaphoreSlim(parallelism, parallelism);

        var tasks = enabledProviders.Select(async provider =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var catalog = await _providers.GetCatalogAsync(provider.Id).ConfigureAwait(false);
                return (providerId: provider.Id, catalog);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var (providerId, catalog) in results)
        {
            if (catalog is not null)
                catalogs[providerId] = catalog;
        }

        return (scopes, catalogs);
    }

    public static List<string> BuildCategoryNames(string scopeKey, Dictionary<string, CatalogSnapshot> catalogs)
    {
        if (catalogs.Count == 0) return new List<string>();

        if (scopeKey == "ALL")
        {
            return catalogs.Values
                .SelectMany(c => c.Categories.Select(x => x.Name))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!catalogs.TryGetValue(scopeKey, out var catalog) || catalog.Categories is null || catalog.Categories.Count == 0)
            return new List<string>();

        return catalog.Categories
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<LiveChannel> BuildChannels(string scopeKey, string categoryName, Dictionary<string, CatalogSnapshot> catalogs)
    {
        if (catalogs.Count == 0 || string.IsNullOrWhiteSpace(categoryName))
            return new List<LiveChannel>();

        IEnumerable<LiveChannel> all;

        if (scopeKey == "ALL")
        {
            all = catalogs.Values.SelectMany(c => c.Channels);
        }
        else if (catalogs.TryGetValue(scopeKey, out var catalog) && catalog.Channels is not null)
        {
            all = catalog.Channels;
        }
        else
        {
            all = Enumerable.Empty<LiveChannel>();
        }

        return all
            .Where(ch => ch.CategoryName.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(ch => ch.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}