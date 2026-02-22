namespace AtlasHub.Models;

public sealed record ProviderSource(
    string Id,
    string Name,
    string Type,
    ProviderM3uConfig M3u,
    ProviderHttpConfig Http,
    string CreatedUtc
);
