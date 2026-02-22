namespace AtlasHub.Models;

public sealed record ProviderHttpConfig(
    string? UserAgent,
    string? Referer,
    Dictionary<string, string>? Headers,
    int TimeoutSeconds
);
