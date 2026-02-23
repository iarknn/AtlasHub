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

namespace AtlasHub.ViewModels;

[SupportedOSPlatform("windows7.0")]
public partial class ShellViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly LiveTvViewModel _liveVm;
    private readonly ProvidersViewModel _providersVm;
    private readonly SettingsViewModel _settingsVm;
    private readonly AppEventBus _bus;
    private readonly DispatcherTimer _toastTimer;

    // Page cache (sekme değişimlerinde takılmayı azaltır)
    private readonly LiveTvPage _livePage;
    private readonly ProvidersPage _providersPage;
    private readonly SettingsPage _settingsPage;
    private readonly PlaceholderPage _moviesPage;
    private readonly PlaceholderPage _seriesPage;

    public string ProfileDisplay =>
        _state.CurrentProfile is null ? "—" : _state.CurrentProfile.Name;

    [ObservableProperty] private UserControl _currentPage;
    [ObservableProperty] private string _selectedNav = "live";

    public bool IsLive => SelectedNav == "live";
    public bool IsMovies => SelectedNav == "movies";
    public bool IsSeries => SelectedNav == "series";
    public bool IsSources => SelectedNav == "sources";
    public bool IsSettings => SelectedNav == "settings";

    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private bool _isToastVisible;

    public ShellViewModel(
        AppState state,
        LiveTvViewModel liveVm,
        ProvidersViewModel providersVm,
        SettingsViewModel settingsVm,
        AppEventBus bus)
    {
        _state = state;
        _liveVm = liveVm;
        _providersVm = providersVm;
        _settingsVm = settingsVm;
        _bus = bus;

        _bus.Toast += OnToast;

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.2)
        };
        _toastTimer.Tick += (_, __) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        // Cache pages once
        _livePage = new LiveTvPage { DataContext = _liveVm };
        _providersPage = new ProvidersPage { DataContext = _providersVm };
        _settingsPage = new SettingsPage { DataContext = _settingsVm };

        var moviesTitle = Loc.Svc["Nav.Movies"];
        var seriesTitle = Loc.Svc["Nav.Series"];

        _moviesPage = new PlaceholderPage
        {
            DataContext = new PlaceholderPageVm(moviesTitle)
        };

        _seriesPage = new PlaceholderPage
        {
            DataContext = new PlaceholderPageVm(seriesTitle)
        };

        _currentPage = _livePage;
        _selectedNav = "live";
    }

    partial void OnSelectedNavChanged(string value)
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsMovies));
        OnPropertyChanged(nameof(IsSeries));
        OnPropertyChanged(nameof(IsSources));
        OnPropertyChanged(nameof(IsSettings));
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

    [RelayCommand]
    private void GoLive()
    {
        SelectedNav = "live";
        CurrentPage = _livePage;
    }

    [RelayCommand]
    private void GoMovies()
    {
        SelectedNav = "movies";
        CurrentPage = _moviesPage;
    }

    [RelayCommand]
    private void GoSeries()
    {
        SelectedNav = "series";
        CurrentPage = _seriesPage;
    }

    [RelayCommand]
    private void GoSources()
    {
        SelectedNav = "sources";
        CurrentPage = _providersPage;
    }

    [RelayCommand]
    private void GoSettings()
    {
        SelectedNav = "settings";
        CurrentPage = _settingsPage;
    }
}