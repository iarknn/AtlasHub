namespace AtlasHub.Models;

public sealed record EpgSnapshot(
    string ProviderId,
    string CreatedUtc,
    List<EpgProgram> Programs,
    List<EpgChannel>? Channels = null
);
