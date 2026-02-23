using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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

    public ObservableCollection<ProviderSource> Providers { get; } = new();

    [ObservableProperty] private ProviderSource? _selected;

    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _m3uUrl = "";

    // XMLTV
    [ObservableProperty] private string _xmltvUrl = "";

    // HTTP
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
        ProviderEpgRepository epgCfgRepo)
    {
        _state = state;
        _service = service;
        _epgCfgRepo = epgCfgRepo;

        _ = ReloadAsync();
    }

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnSelectedChanged(ProviderSource? value)
    {
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
            Providers.Clear();

            var list = await _service.GetProvidersAsync();
            foreach (var p in list)
                Providers.Add(p);

            Selected = Providers.FirstOrDefault();

            await SyncSelectedEnabledAsync();
            await LoadSelectedEpgConfigAsync();

            Status = string.Empty;
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
                UserAgent: string.IsNullOrWhiteSpace(UserAgent)
                    ? null
                    : UserAgent.Trim(),
                Referer: string.IsNullOrWhiteSpace(Referer)
                    ? null
                    : Referer.Trim(),
                Headers: null,
                TimeoutSeconds: TimeoutSeconds
            );

            await _service.AddM3uProviderAsync(
                profileId: _state.CurrentProfile.Id,
                name: NewName,
                m3uUrl: M3uUrl,
                m3uFilePath: null,
                enableForProfile: EnableForProfile,
                http: http);

            // yeniden yükle, yeni provider seçili olsun
            await ReloadAsync();

            if (Selected is not null)
            {
                await _epgCfgRepo.SetForProviderAsync(
                    Selected.Id,
                    XmltvUrl,
                    null);
            }

            NewName = "";
            M3uUrl = "";
            XmltvUrl = "";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
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
                null);

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
            await _service.DeleteProviderAsync(Selected.Id);
            await ReloadAsync();
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
            await _service.RefreshCatalogAsync(Selected);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
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
                !IsEnabledForProfile);

            await SyncSelectedEnabledAsync();
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

        var links = await _service.GetLinksAsync();
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

        var cfg = await _epgCfgRepo.GetForProviderAsync(Selected.Id);
        XmltvUrl = cfg?.XmltvUrl ?? "";
    }
}