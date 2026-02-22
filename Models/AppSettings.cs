namespace AtlasHub.Models;

public sealed record AppSettings(
    string CultureName
)
{
    public static AppSettings Default => new("tr-TR");
}
