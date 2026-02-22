using System.IO;
using System.Text.Json;
using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class SettingsService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public AppSettings Current { get; private set; }

    public SettingsService(AppPaths paths)
    {
        _paths = paths;
        Current = Load();
    }

    public void SetCulture(string cultureName)
    {
        Current = Current with { CultureName = cultureName };
        Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_paths.SettingsJsonPath))
                return AppSettings.Default;

            var json = File.ReadAllText(_paths.SettingsJsonPath);
            return JsonSerializer.Deserialize<AppSettings>(json, _json) ?? AppSettings.Default;
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, _json);
            File.WriteAllText(_paths.SettingsJsonPath, json);
        }
        catch
        {
            // ayar kaydetme hatasını şimdilik sessiz geçiyoruz (Sprint 0)
        }
    }
}
