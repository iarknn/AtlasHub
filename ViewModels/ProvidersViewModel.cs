using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AtlasHub.Localization;
using AtlasHub.Models;
using AtlasHub.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtlasHub.ViewModels;

public partial class ProvidersViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly ProviderService _service;
    private readonly ProviderEpgRepository _epgCfgRepo;
    private readonly AppEventBus _bus;

    public ObservableCollection<ProviderSource> Providers { get; } = new();

    [ObservableProperty] private ProviderSource? _selected;

    // Yeni kaynak ekleme alanı
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _m3uUrl = "";

    // Seçili kaynağı düzenleme alanı
    [ObservableProperty] private string _selectedName = "";
    [ObservableProperty] private string _selectedM3uUrl = "";

    // XMLTV
    [ObservableProperty] private string _xmltvUrl = "";

    // HTTP (hem ekleme hem düzenleme için kullanıyoruz)
    [ObservableProperty] private string _userAgent = "AtlasHub/1.0 (Windows; WPF)";
    [ObservableProperty] private string _referer = "";
    [ObservableProperty] private int _timeoutSeconds = 30;

    [ObservableProperty] private bool _enableForProfile = true;
    [ObservableProperty] private bool _isEnabledForProfile;

    [ObservableProperty] private bool _isBusy;
    public bool IsNotBusy => !IsBusy;

    [ObservableProperty] private string _status = "";

    public ProvidersViewModel(
        AppState state,
        ProviderService service,
        ProviderEpgRepository epgCfgRepo,
        AppEventBus bus)
    {
        _state = state;
        _service = service;
        _epgCfgRepo = epgCfgRepo;
        _bus = bus;

        _bus.ProvidersChanged += OnProvidersChanged;

        _ = ReloadAsync();
    }

    private void OnProvidersChanged(object? sender, EventArgs e)
        => _ = ReloadAsync();

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnSelectedChanged(ProviderSource? value)
    {
        if (value is null)
        {
            SelectedName = "";
            SelectedM3uUrl = "";
            XmltvUrl = "";
            UserAgent = "AtlasHub/1.0 (Windows; WPF)";
            Referer = "";
            TimeoutSeconds = 30;
            IsEnabledForProfile = false;
            return;
        }

        SelectedName = value.Name ?? "";

        SelectedM3uUrl = value.M3u?.M3uUrl
                         ?? value.M3u?.M3uFilePath
                         ?? "";

        UserAgent = value.Http?.UserAgent
                    ?? "AtlasHub/1.0 (Windows; WPF)";
        Referer = value.Http?.Referer ?? "";
        TimeoutSeconds = value.Http?.TimeoutSeconds > 0
            ? value.Http.TimeoutSeconds
            : 30;

        _ = SyncSelectedEnabledAsync();
        _ = LoadSelectedEpgConfigAsync();
    }

    // -----------------------
    // Commands
    // -----------------------

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            // 1) Veri çekme arka planda
            var list = await _service.GetProvidersAsync().ConfigureAwait(false);

            // 2) Koleksiyon ve Selected sadece UI thread'de
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Providers.Clear();
                foreach (var p in list)
                    Providers.Add(p);

                if (Selected is not null)
                {
                    var keep = Providers.FirstOrDefault(p => p.Id == Selected.Id);
                    Selected = keep ?? Providers.FirstOrDefault();
                }
                else
                {
                    Selected = Providers.FirstOrDefault();
                }
            });

            await SyncSelectedEnabledAsync().ConfigureAwait(false);
            await LoadSelectedEpgConfigAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var fmt = Loc.Svc["Providers.Status.ReloadErrorPrefix"];
            var msg = string.Format(CultureInfo.CurrentCulture, fmt, ex.Message);
            await Application.Current.Dispatcher.InvokeAsync(() => Status = msg);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddM3uAsync()
    {
        if (_state.CurrentProfile is null)
        {
            Status = Loc.Svc["Providers.Status.ProfileNotSelected"];
            return;
        }

        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var http = new ProviderHttpConfig(
                UserAgent: string.IsNullOrWhiteSpace(UserAgent) ? null : UserAgent.Trim(),
                Referer: string.IsNullOrWhiteSpace(Referer) ? null : Referer.Trim(),
                Headers: null,
                TimeoutSeconds: TimeoutSeconds);

            await _service.AddM3uProviderAsync(
                profileId: _state.CurrentProfile.Id,
                name: NewName,
                m3uUrl: M3uUrl,
                m3uFilePath: null,
                enableForProfile: EnableForProfile,
                http: http).ConfigureAwait(false);

            await ReloadAsync().ConfigureAwait(false);

            NewName = "";
            M3uUrl = "";
            XmltvUrl = "";
        }
        catch (Exception ex)
        {
            var fmt = Loc.Svc["Providers.Status.AddM3uErrorPrefix"];
            Status = string.Format(CultureInfo.CurrentCulture, fmt, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveXmltvAsync()
    {
        if (Selected is null) return;
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            await _epgCfgRepo.SetForProviderAsync(
                Selected.Id,
                XmltvUrl,
                null).ConfigureAwait(false);

            Status = Loc.Svc["Providers.Status.XmltvSaved"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (Selected is null) return;
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            await _service.DeleteProviderAsync(Selected.Id).ConfigureAwait(false);
            await ReloadAsync().ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshCatalogAsync()
    {
        if (Selected is null) return;
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            await _service.RefreshCatalogAsync(Selected).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var fmt = Loc.Svc["Providers.Status.RefreshErrorPrefix"];
            Status = string.Format(CultureInfo.CurrentCulture, fmt, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveSelectedProviderAsync()
    {
        if (Selected is null) return;
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var newName = SelectedName?.Trim() ?? "";
            var newM3u = SelectedM3uUrl?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException(Loc.Svc["Providers.Error.M3uNameRequired"]);

            if (string.IsNullOrWhiteSpace(newM3u))
                throw new ArgumentException(Loc.Svc["Providers.Error.M3uSourceRequired"]);

            var isUrl = newM3u.StartsWith("http", StringComparison.OrdinalIgnoreCase);

            var m3uCfg = new ProviderM3uConfig(
                M3uUrl: isUrl ? newM3u : null,
                M3uFilePath: isUrl ? null : newM3u);

            var httpCfg = new ProviderHttpConfig(
                UserAgent: string.IsNullOrWhiteSpace(UserAgent) ? null : UserAgent.Trim(),
                Referer: string.IsNullOrWhiteSpace(Referer) ? null : Referer.Trim(),
                Headers: Selected.Http?.Headers,
                TimeoutSeconds: TimeoutSeconds);

            var updated = Selected with
            {
                Name = newName,
                M3u = m3uCfg,
                Http = httpCfg
            };

            await _service.UpdateProviderAsync(updated).ConfigureAwait(false);

            await ReloadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var fmt = Loc.Svc["Providers.Status.UpdateErrorPrefix"];
            Status = string.Format(CultureInfo.CurrentCulture, fmt, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync()
    {
        if (_state.CurrentProfile is null)
        {
            Status = Loc.Svc["Providers.Status.ProfileNotSelected"];
            return;
        }

        if (Selected is null) return;
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            await _service.SetEnabledAsync(
                _state.CurrentProfile.Id,
                Selected.Id,
                !IsEnabledForProfile).ConfigureAwait(false);

            await SyncSelectedEnabledAsync().ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // -----------------------
    // Internal helpers
    // -----------------------

    private async Task SyncSelectedEnabledAsync()
    {
        if (_state.CurrentProfile is null || Selected is null)
        {
            IsEnabledForProfile = false;
            return;
        }

        var links = await _service.GetLinksAsync().ConfigureAwait(false);
        var link = links.FirstOrDefault(x =>
            x.ProfileId == _state.CurrentProfile.Id &&
            x.ProviderId == Selected.Id);

        IsEnabledForProfile = link?.IsEnabled ?? false;
    }

    private async Task LoadSelectedEpgConfigAsync()
    {
        if (Selected is null)
        {
            XmltvUrl = "";
            return;
        }

        var cfg = await _epgCfgRepo.GetForProviderAsync(Selected.Id).ConfigureAwait(false);
        XmltvUrl = cfg?.XmltvUrl ?? "";
    }
}