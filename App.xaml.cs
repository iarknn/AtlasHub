using System;
using System.Globalization;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

using AtlasHub.Localization;
using AtlasHub.Services;
using AtlasHub.ViewModels;
using AtlasHub.Views;

namespace AtlasHub;

public partial class App : Application
{
    private static ServiceProvider? _services;

    public static IServiceProvider Services
        => _services ?? throw new InvalidOperationException("DI container başlatılmadı.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Core
        services.AddSingleton<AppState>();
        services.AddSingleton<AppEventBus>();

        // Paths / Profiles
        services.AddSingleton<AppPaths>();
        services.AddSingleton<IProfileRepository, JsonProfileRepository>();

        // Localization
        services.AddSingleton<LanguagePackRepository>();
        services.AddSingleton<LocalizationService>();

        // Persistence / repositories
        services.AddSingleton<ProviderRepository>();
        services.AddSingleton<ProfileProviderRepository>();
        services.AddSingleton<CatalogRepository>();

        // EPG repositories
        services.AddSingleton<ProviderEpgRepository>();
        services.AddSingleton<EpgRepository>();

        // Import / parse
        services.AddSingleton<M3uImporter>();
        services.AddSingleton<XmlTvParser>();

        // Downloader
        services.AddSingleton<XmlTvDownloader>();

        // Services
        services.AddSingleton<ProviderService>();
        services.AddSingleton<LiveTvService>();
        services.AddSingleton<LogoCacheService>();
        services.AddSingleton<EpgService>();
        services.AddSingleton<LiveEpgTickerService>();

        // Settings (projende varsa)
        services.AddSingleton<SettingsService>();

        // Player
        services.AddSingleton<PlayerService>();

        // ViewModels
        services.AddTransient<ShellViewModel>();
        services.AddTransient<ProfilePickerViewModel>();
        services.AddTransient<ProvidersViewModel>();
        services.AddTransient<LiveTvViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Views / Windows
        services.AddTransient<MainWindow>();
        services.AddTransient<ProfilePickerWindow>();

        _services = services.BuildServiceProvider();

        // TR default
        var loc = Services.GetRequiredService<LocalizationService>();
        loc.SetCulture(new CultureInfo("tr-TR"));

        // First window
        var pickerWindow = Services.GetRequiredService<ProfilePickerWindow>();
        pickerWindow.DataContext = Services.GetRequiredService<ProfilePickerViewModel>();
        pickerWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Services.GetService<PlayerService>()?.Dispose(); } catch { }
        try { _services?.Dispose(); } catch { }

        base.OnExit(e);
    }
}
