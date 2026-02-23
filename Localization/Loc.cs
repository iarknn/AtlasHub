using Microsoft.Extensions.DependencyInjection;

namespace AtlasHub.Localization;

public static class Loc
{
    /// <summary>
    /// XAML'de {loc Key} => Loc.Svc["Key"]
    /// </summary>
    public static LocalizationService Svc =>
        App.Services.GetRequiredService<LocalizationService>();
}