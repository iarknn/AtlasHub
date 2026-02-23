using Microsoft.Extensions.DependencyInjection;

namespace AtlasHub.Localization;

public static class Loc
{
    /// <summary>
    /// XAML ve C# tarafında tek giriş noktası:
    /// Loc.Svc["Key"]
    /// </summary>
    public static LocalizationService Svc
        => App.Services.GetRequiredService<LocalizationService>();
}