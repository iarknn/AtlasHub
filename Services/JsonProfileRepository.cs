using System.IO;
using System.Text.Json;
using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class JsonProfileRepository : IProfileRepository
{
    private readonly AppPaths _paths;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonProfileRepository(AppPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<Profile>> GetAllAsync()
    {
        var list = await ReadAsync();
        return list.OrderBy(p => p.CreatedUtc).ToList();
    }

    public async Task<Profile> CreateAsync(string name, string avatarKey)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Profil adı boş olamaz.");

        var list = await ReadAsync();

        var profile = new Profile(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            AvatarKey: avatarKey,
            CreatedUtc: DateTimeOffset.UtcNow.ToString("O")
        );

        list.Add(profile);
        await WriteAsync(list);

        return profile;
    }

    public async Task DeleteAsync(string profileId)
    {
        var list = await ReadAsync();
        list.RemoveAll(p => p.Id == profileId);
        await WriteAsync(list);
    }

    private async Task<List<Profile>> ReadAsync()
    {
        if (!File.Exists(_paths.ProfilesJsonPath))
            return new List<Profile>();

        var json = await File.ReadAllTextAsync(_paths.ProfilesJsonPath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<Profile>();

        return JsonSerializer.Deserialize<List<Profile>>(json, _jsonOptions) ?? new List<Profile>();
    }

    private async Task WriteAsync(List<Profile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, _jsonOptions);
        await File.WriteAllTextAsync(_paths.ProfilesJsonPath, json);
    }
}
