namespace AtlasHub.Models;

public sealed record ProviderEpgConfig(
    string ProviderId,
    string? XmltvUrl,
    string? XmltvFilePath
);
