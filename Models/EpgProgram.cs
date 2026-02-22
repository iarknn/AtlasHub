namespace AtlasHub.Models;

public sealed record EpgProgram(
    string ChannelId,
    string Title,
    string? Description,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc
);
