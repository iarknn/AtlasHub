using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AtlasHub.Localization;
using AtlasHub.Services;
using AtlasHub.Views.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasHub.ViewModels;

[SupportedOSPlatform("windows7.0")]
public partial class ShellViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IServiceProvider _sp;
    private readonly AppEventBus _bus;

    private readonly DispatcherTimer _toastTimer;

    // Lazy pages
    private LiveTvPage? _livePage;
    private ProvidersPage? _providersPage;
    private SettingsPage? _settingsPage;

    private PlaceholderPage? _moviesPage;
    private PlaceholderPage? _seriesPage;
    private PlaceholderPage? _loadingPage;

    // Lazy VMs
    private LiveTvViewModel? _liveVm;
    private ProvidersViewModel? _providersVm;
    private SettingsViewModel? _settingsVm;

    private bool _initScheduled;

    public string ProfileDisplay => _state.CurrentProfile is null ? "—" : _state.CurrentProfile.Name;

    [ObservableProperty] private UserControl _currentPage = new UserControl();
    [ObservableProperty] private string _selectedNav = "live";

    public bool IsLive => SelectedNav == "live";
    public bool IsMovies => SelectedNav == "movies";
    public bool IsSeries => SelectedNav == "series";
    public bool IsSources => SelectedNav == "sources";
    public bool IsSettings => SelectedNav == "settings";

    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private bool _isToastVisible;

    public ShellViewModel(AppState state, IServiceProvider sp, AppEventBus bus)
    {
        _state = state;
        _sp = sp;
        _bus = bus;

        _bus.Toast += OnToast;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _toastTimer.Tick += (_, __) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        // Açılışta: önce hafif bir sayfa göster (UI hemen paintsin)
        _loadingPage = new PlaceholderPage
        {
            DataContext = new PlaceholderPageVm(Loc.Svc["LiveTv.Status.Loading"])
        };

        _currentPage = _loadingPage;
        _selectedNav = "live";

        // MainWindow görünür olduktan sonra (UI idle) LiveTV'yi lazily yükle
        ScheduleInit();
    }

    partial void OnSelectedNavChanged(string value)
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsMovies));
        OnPropertyChanged(nameof(IsSeries));
        OnPropertyChanged(nameof(IsSources));
        OnPropertyChanged(nameof(IsSettings));
    }

    private void ScheduleInit()
    {
        if (_initScheduled) return;
        _initScheduled = true;

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            // İlk açılışta Live sekmesine geçir (lazy create)
            GoLive();
        }), DispatcherPriority.ApplicationIdle);
    }

    private void OnToast(object? sender, string msg)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ToastMessage = msg;
            IsToastVisible = true;
            _toastTimer.Stop();
            _toastTimer.Start();
        });
    }

    private LiveTvPage EnsureLivePage()
    {
        _liveVm ??= _sp.GetRequiredService<LiveTvViewModel>();
        _livePage ??= new LiveTvPage { DataContext = _liveVm };

        // İlk kez oluşturulunca yüklemeyi başlat (UI bloke etmeden)
        _ = _liveVm.EnsureLoadedAsync();

        return _livePage;
    }

    private ProvidersPage EnsureProvidersPage()
    {
        _providersVm ??= _sp.GetRequiredService<ProvidersViewModel>();
        _providersPage ??= new ProvidersPage { DataContext = _providersVm };

        // ProvidersViewModel zaten kendi içinde ReloadAsync tetikliyor (mevcut repo hali).
        // Eğer ileride kaldırırsak, burada manuel tetikleyebiliriz.
        return _providersPage;
    }

    private SettingsPage EnsureSettingsPage()
    {
        _settingsVm ??= _sp.GetRequiredService<SettingsViewModel>();
        _settingsPage ??= new SettingsPage { DataContext = _settingsVm };
        return _settingsPage;
    }

    private PlaceholderPage EnsureMoviesPage()
    {
        if (_moviesPage is not null) return _moviesPage;
        var title = Loc.Svc["Nav.Movies"];
        _moviesPage = new PlaceholderPage { DataContext = new PlaceholderPageVm(title) };
        return _moviesPage;
    }

    private PlaceholderPage EnsureSeriesPage()
    {
        if (_seriesPage is not null) return _seriesPage;
        var title = Loc.Svc["Nav.Series"];
        _seriesPage = new PlaceholderPage { DataContext = new PlaceholderPageVm(title) };
        return _seriesPage;
    }

    [RelayCommand]
    private void GoLive()
    {
        SelectedNav = "live";
        CurrentPage = EnsureLivePage();
    }

    [RelayCommand]
    private void GoMovies()
    {
        SelectedNav = "movies";
        CurrentPage = EnsureMoviesPage();
    }

    [RelayCommand]
    private void GoSeries()
    {
        SelectedNav = "series";
        CurrentPage = EnsureSeriesPage();
    }

    [RelayCommand]
    private void GoSources()
    {
        SelectedNav = "sources";
        CurrentPage = EnsureProvidersPage();
    }

    [RelayCommand]
    private void GoSettings()
    {
        SelectedNav = "settings";
        CurrentPage = EnsureSettingsPage();
    }
}