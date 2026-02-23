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

    public LiveTvService(ProviderService providers) => _providers = providers;

    public async Task<(List<ProviderScope> scopes, Dictionary<string, ProviderCatalog> catalogs)> LoadForProfileAsync(string profileId)
    {
        var enabled = await _providers.GetEnabledProvidersForProfileAsync(profileId);

        var scopes = new List<ProviderScope>
        {
            new("ALL", Loc.Svc["LiveTv.Scope.AllSources"])
        };

        var catalogs = new Dictionary<string, ProviderCatalog>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in enabled)
        {
            scopes.Add(new ProviderScope(p.Id, p.Name));
            var cat = await _providers.GetCatalogAsync(p.Id);
            if (cat is not null)
                catalogs[p.Id] = cat;
        }

        return (scopes, catalogs);
    }

    public static List<string> BuildCategoryNames(
        string scopeKey,
        Dictionary<string, ProviderCatalog> catalogs)
    {
        if (scopeKey == "ALL")
        {
            return catalogs.Values
                .SelectMany(c => c.Categories.Select(x => x.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return catalogs.TryGetValue(scopeKey, out var cat)
            ? cat.Categories
                .Select(x => x.Name)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();
    }

    public static List<LiveChannel> BuildChannels(
        string scopeKey,
        string categoryName,
        Dictionary<string, ProviderCatalog> catalogs)
    {
        IEnumerable<LiveChannel> all =
            scopeKey == "ALL"
                ? catalogs.Values.SelectMany(c => c.Channels)
                : (catalogs.TryGetValue(scopeKey, out var cat)
                    ? cat.Channels
                    : Enumerable.Empty<LiveChannel>());

        return all
            .Where(ch => ch.CategoryName.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(ch => ch.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}