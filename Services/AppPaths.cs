using System;
using System.IO;

namespace AtlasHub.Services;

public sealed class AppPaths
{
    public string Root { get; }
    public string ProfilesJsonPath { get; }
    public string SettingsJsonPath { get; }
    public string LangRoot { get; }

    // Sprint 1
    public string ProvidersJsonPath { get; }
    public string ProfileProvidersJsonPath { get; }
    public string CatalogRoot { get; }
    public string LogoCacheRoot { get; }


    public AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasHub"
        );
        Directory.CreateDirectory(Root);

        ProfilesJsonPath = Path.Combine(Root, "profiles.json");
        SettingsJsonPath = Path.Combine(Root, "settings.json");

        // LogoCacheRoot
        LogoCacheRoot = Path.Combine(Root, "cache", "logos");
        Directory.CreateDirectory(LogoCacheRoot);


        // 🌍 Localization
        LangRoot = Path.Combine(Root, "lang");
        Directory.CreateDirectory(LangRoot);

        // 📡 Providers (Sprint 1)
        ProvidersJsonPath = Path.Combine(Root, "providers.json");
        ProfileProvidersJsonPath = Path.Combine(Root, "profile_providers.json");

        // 🎬 Catalog (logos, posters, cached images)
        CatalogRoot = Path.Combine(Root, "catalog");
        Directory.CreateDirectory(CatalogRoot);
    }
}
