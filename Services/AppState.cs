using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class AppState
{
    public Profile? CurrentProfile { get; private set; }

    public void SetProfile(Profile profile) => CurrentProfile = profile;
}
