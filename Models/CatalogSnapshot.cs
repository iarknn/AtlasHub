namespace AtlasHub.Models;

public sealed record CatalogSnapshot(
    string ProviderId,
    string ProviderName,
    string CreatedUtc,
    List<LiveCategory> Categories,
    List<LiveChannel> Channels
);
