namespace AtlasHub.Models;

public sealed record ProfileProviderLink(
    string ProfileId,
    string ProviderId,
    bool IsEnabled,
    int SortOrder
);
