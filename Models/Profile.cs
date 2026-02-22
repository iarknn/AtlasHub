namespace AtlasHub.Models;

public sealed record Profile(
    string Id,
    string Name,
    string AvatarKey,
    string CreatedUtc
);
