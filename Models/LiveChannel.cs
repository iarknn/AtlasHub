namespace AtlasHub.Models;

public sealed record LiveChannel(
    string ProviderId,
    string ChannelId,
    string CategoryName,
    string Name,
    string? LogoUrl,
    string? TvgId,
    string StreamUrl
);
