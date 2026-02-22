using Microsoft.Extensions.DependencyInjection;

namespace AtlasHub.Localization;

public static class Loc
{
    public static LocalizationService Svc => App.Services.GetRequiredService<LocalizationService>();
}
