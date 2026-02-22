using AtlasHub.Models;
using System.Linq;

namespace AtlasHub.Services;

public sealed class LiveTvService
{
    private readonly ProviderService _providers;
    public LiveTvService(ProviderService providers) => _providers = providers;

    public async Task<(List<ProviderScope> scopes, Dictionary<string, CatalogSnapshot> catalogs)> LoadForProfileAsync(string profileId)
    {
        var enabled = await _providers.GetEnabledProvidersForProfileAsync(profileId);

        var scopes = new List<ProviderScope> { new("ALL", "Tüm Kaynaklar") };
        var catalogs = new Dictionary<string, CatalogSnapshot>();

        foreach (var p in enabled)
        {
            scopes.Add(new ProviderScope(p.Id, p.Name));
            var cat = await _providers.GetCatalogAsync(p.Id);
            if (cat is not null) catalogs[p.Id] = cat;
        }

        return (scopes, catalogs);
    }

    public static List<string> BuildCategoryNames(string scopeKey, Dictionary<string, CatalogSnapshot> catalogs)
    {
        if (scopeKey == "ALL")
            return catalogs.Values.SelectMany(c => c.Categories.Select(x => x.Name))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                         .ToList();

        return catalogs.TryGetValue(scopeKey, out var cat)
            ? cat.Categories.Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
    }

    public static List<LiveChannel> BuildChannels(string scopeKey, string categoryName, Dictionary<string, CatalogSnapshot> catalogs)
    {
        IEnumerable<LiveChannel> all = scopeKey == "ALL"
            ? catalogs.Values.SelectMany(c => c.Channels)
            : (catalogs.TryGetValue(scopeKey, out var cat) ? cat.Channels : Enumerable.Empty<LiveChannel>());

        return all.Where(ch => ch.CategoryName.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                  .OrderBy(ch => ch.Name, StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }
}
