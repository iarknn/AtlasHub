using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Seçili profil için etkin provider listesini ve
    /// providerId -> CatalogSnapshot sözlüğünü döner.
    /// </summary>
    public async Task<(List<ProviderScope> scopes,
                       Dictionary<string, CatalogSnapshot> catalogs)>
        LoadForProfileAsync(string profileId)
    {
        var enabledProviders = await _providers
            .GetEnabledProvidersForProfileAsync(profileId)
            .ConfigureAwait(false);

        // İlk scope her zaman "Tüm kaynaklar"
        var scopes = new List<ProviderScope>
        {
            new("ALL", Loc.Svc["LiveTv.Scope.AllSources"])
        };

        var catalogs = new Dictionary<string, CatalogSnapshot>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var provider in enabledProviders)
        {
            scopes.Add(new ProviderScope(provider.Id, provider.Name));

            var catalog = await _providers
                .GetCatalogAsync(provider.Id)
                .ConfigureAwait(false);

            if (catalog is not null)
                catalogs[provider.Id] = catalog;
        }

        return (scopes, catalogs);
    }

    /// <summary>
    /// Scope + katalog sözlüğünden kategori isimleri üretir.
    /// </summary>
    public static List<string> BuildCategoryNames(
        string scopeKey,
        Dictionary<string, CatalogSnapshot> catalogs)
    {
        if (catalogs.Count == 0)
            return new List<string>();

        // "ALL" => tüm provider'lardaki kategoriler
        if (scopeKey == "ALL")
        {
            return catalogs.Values
                .SelectMany(c => c.Categories.Select(x => x.Name))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Belirli provider scope’u
        if (!catalogs.TryGetValue(scopeKey, out var catalog) ||
            catalog.Categories is null ||
            catalog.Categories.Count == 0)
        {
            return new List<string>();
        }

        return catalog.Categories
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Scope + kategori bilgisine göre canlı kanal listesini döner.
    /// </summary>
    public static List<LiveChannel> BuildChannels(
        string scopeKey,
        string categoryName,
        Dictionary<string, CatalogSnapshot> catalogs)
    {
        if (catalogs.Count == 0 ||
            string.IsNullOrWhiteSpace(categoryName))
        {
            return new List<LiveChannel>();
        }

        IEnumerable<LiveChannel> all;

        if (scopeKey == "ALL")
        {
            all = catalogs.Values.SelectMany(c => c.Channels);
        }
        else if (catalogs.TryGetValue(scopeKey, out var catalog) &&
                 catalog.Channels is not null)
        {
            all = catalog.Channels;
        }
        else
        {
            all = Enumerable.Empty<LiveChannel>();
        }

        return all
            .Where(ch =>
                ch.CategoryName.Equals(
                    categoryName,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(ch => ch.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}