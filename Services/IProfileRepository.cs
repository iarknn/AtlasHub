using AtlasHub.Models;

namespace AtlasHub.Services;

public interface IProfileRepository
{
    Task<IReadOnlyList<Profile>> GetAllAsync();
    Task<Profile> CreateAsync(string name, string avatarKey);
    Task DeleteAsync(string profileId);
}
