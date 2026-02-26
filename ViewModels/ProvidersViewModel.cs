using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AtlasHub.Localization;
using AtlasHub.Models;
using AtlasHub.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtlasHub.ViewModels;

public partial class ProvidersViewModel : ViewModelBase, IDisposable
{
    private readonly AppState _state;
    private readonly ProviderService _service;
    private readonly ProviderEpgRepository _epgCfgRepo;
    private readonly AppEventBus _bus;

    // Komutlar + reload tek şerit → ProvidersChanged event'i asla boşa gitmez
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Kendi komutlarımız sırasında gelen ProvidersChanged → double reload olmasın
    private int _suppressProvidersChanged;

    // Seçim değişim yarışlarını engelle
    private int _selectionStamp;

    public ObservableCollection<ProviderSource> Providers { get; } = new();

    [ObservableProperty] private ProviderSource? _selected;

    // Yeni kaynak ekleme alanı
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _m3uUrl = "";

    // Seçili kaynağı düzenleme alanı (XAML bunu bağlıyor olabilir)
    [ObservableProperty] private string _selectedName = "";
    [ObservableProperty] private string _selectedM3uUrl = "";

    // XMLTV
    [ObservableProperty] private string _xmltvUrl = "";

    // HTTP (hem ekleme hem düzenleme için)
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

    public void Dispose()
    {
        _bus.ProvidersChanged -= OnProvidersChanged;
        _gate.Dispose();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    private void OnProvidersChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _suppressProvidersChanged) > 0)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = ReloadAsync();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => _ = ReloadAsync()));
        }
    }

    partial void OnSelectedChanged(ProviderSource? value)
    {
        ApplySelectedToEditor(value);

        var stamp = Interlocked.Increment(ref _selectionStamp);
        _ = SyncSelectedEnabledAsync(stamp, value);
        _ = LoadSelectedEpgConfigAsync(stamp, value);
    }

    private void ApplySelectedToEditor(ProviderSource? value)
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
        SelectedM3uUrl = value.M3u.M3uUrl ?? value.M3u.M3uFilePath ?? "";

        UserAgent = string.IsNullOrWhiteSpace(value.Http.UserAgent)
            ? "AtlasHub/1.0 (Windows; WPF)"
            : value.Http.UserAgent!;

        Referer = value.Http.Referer ?? "";
        TimeoutSeconds = value.Http.TimeoutSeconds > 0 ? value.Http.TimeoutSeconds : 30;
    }

    private async Task OnUIAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    private IDisposable SuppressProvidersChangedScope()
    {
        Interlocked.Increment(ref _suppressProvidersChanged);
        return new ActionDisposable(() => Interlocked.Decrement(ref _suppressProvidersChanged));
    }

    private sealed class ActionDisposable : IDisposable
    {
        private Action? _a;
        public ActionDisposable(Action a) => _a = a;
        public void Dispose() => Interlocked.Exchange(ref _a, null)?.Invoke();
    }

    private async Task RunExclusiveAsync(Func<Task> action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await OnUIAsync(() => IsBusy = true).ConfigureAwait(false);
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await OnUIAsync(() => Status = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            await OnUIAsync(() => IsBusy = false).ConfigureAwait(false);
            _gate.Release();
        }
    }

    // -----------------------
    // Commands
    // -----------------------

    [RelayCommand]
    private Task ReloadAsync()
        => RunExclusiveAsync(() => ReloadCoreAsync(preferId: Selected?.Id));

    [RelayCommand]
    private Task AddM3uAsync()
        => RunExclusiveAsync(async () =>
        {
            if (_state.CurrentProfile is null)
            {
                await OnUIAsync(() => Status = Loc.Svc["Providers.Status.ProfileNotSelected"]).ConfigureAwait(false);
                return;
            }

            using var _ = SuppressProvidersChangedScope();

            var http = new ProviderHttpConfig(
                UserAgent: string.IsNullOrWhiteSpace(UserAgent) ? null : UserAgent.Trim(),
                Referer: string.IsNullOrWhiteSpace(Referer) ? null : Referer.Trim(),
                Headers: null,
                TimeoutSeconds: TimeoutSeconds
            );

            var provider = await _service.AddM3uProviderAsync(
                profileId: _state.CurrentProfile.Id,
                name: NewName,
                m3uUrl: M3uUrl,
                m3uFilePath: null,
                enableForProfile: EnableForProfile,
                http: http).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(XmltvUrl))
            {
                await _epgCfgRepo.SetForProviderAsync(provider.Id, XmltvUrl, null).ConfigureAwait(false);
            }

            await OnUIAsync(() =>
            {
                NewName = "";
                M3uUrl = "";
                XmltvUrl = "";
            }).ConfigureAwait(false);

            // Deterministik: yeni ekleneni seçerek reload
            await ReloadCoreAsync(preferId: provider.Id).ConfigureAwait(false);
        });

    [RelayCommand]
    private Task DeleteSelectedAsync()
        => RunExclusiveAsync(async () =>
        {
            if (Selected is null) return;

            using var _ = SuppressProvidersChangedScope();

            var deletingId = Selected.Id;
            await _service.DeleteProviderAsync(deletingId).ConfigureAwait(false);

            await ReloadCoreAsync(preferId: null).ConfigureAwait(false);
        });

    [RelayCommand]
    private Task RefreshCatalogAsync()
        => RunExclusiveAsync(async () =>
        {
            if (Selected is null) return;

            using var _ = SuppressProvidersChangedScope();

            var keepId = Selected.Id;
            await _service.RefreshCatalogAsync(Selected).ConfigureAwait(false);

            // Servis ProvidersChanged atsa da biz burada deterministic reload yapıyoruz
            await ReloadCoreAsync(preferId: keepId).ConfigureAwait(false);
        });

    [RelayCommand]
    private Task SaveXmltvAsync()
        => RunExclusiveAsync(async () =>
        {
            if (Selected is null) return;

            await _epgCfgRepo.SetForProviderAsync(Selected.Id, XmltvUrl, null).ConfigureAwait(false);
            await OnUIAsync(() => Status = Loc.Svc["Providers.Status.XmltvSaved"]).ConfigureAwait(false);
        });

    [RelayCommand]
    private Task SaveSelectedProviderAsync()
        => RunExclusiveAsync(async () =>
        {
            if (Selected is null) return;

            using var _ = SuppressProvidersChangedScope();

            var newName = (SelectedName ?? "").Trim();
            var newM3u = (SelectedM3uUrl ?? "").Trim();

            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException(Loc.Svc["Providers.Error.M3uNameRequired"]);

            if (string.IsNullOrWhiteSpace(newM3u))
                throw new ArgumentException(Loc.Svc["Providers.Error.M3uSourceRequired"]);

            var isUrl = newM3u.StartsWith("http", StringComparison.OrdinalIgnoreCase);

            var m3uCfg = new ProviderM3uConfig(
                M3uUrl: isUrl ? newM3u : null,
                M3uFilePath: isUrl ? null : newM3u
            );

            var httpCfg = new ProviderHttpConfig(
                UserAgent: string.IsNullOrWhiteSpace(UserAgent) ? null : UserAgent.Trim(),
                Referer: string.IsNullOrWhiteSpace(Referer) ? null : Referer.Trim(),
                Headers: Selected.Http.Headers,
                TimeoutSeconds: TimeoutSeconds
            );

            var updated = Selected with
            {
                Name = newName,
                M3u = m3uCfg,
                Http = httpCfg
            };

            await _service.UpdateProviderAsync(updated).ConfigureAwait(false);
            await ReloadCoreAsync(preferId: updated.Id).ConfigureAwait(false);
        });

    [RelayCommand]
    private Task ToggleEnabledAsync()
        => RunExclusiveAsync(async () =>
        {
            if (_state.CurrentProfile is null)
            {
                await OnUIAsync(() => Status = Loc.Svc["Providers.Status.ProfileNotSelected"]).ConfigureAwait(false);
                return;
            }

            if (Selected is null) return;

            using var _ = SuppressProvidersChangedScope();

            await _service.SetEnabledAsync(
                _state.CurrentProfile.Id,
                Selected.Id,
                !IsEnabledForProfile).ConfigureAwait(false);

            var stamp = Interlocked.Increment(ref _selectionStamp);
            await SyncSelectedEnabledAsync(stamp, Selected).ConfigureAwait(false);
        });

    // -----------------------
    // Internal helpers
    // -----------------------

    private async Task ReloadCoreAsync(string? preferId)
    {
        var keepId = preferId;

        var list = await _service.GetProvidersAsync().ConfigureAwait(false);

        await OnUIAsync(() =>
        {
            Providers.Clear();
            foreach (var p in list)
                Providers.Add(p);

            if (!string.IsNullOrWhiteSpace(keepId))
            {
                Selected =
                    Providers.FirstOrDefault(x => x.Id.Equals(keepId, StringComparison.OrdinalIgnoreCase))
                    ?? Providers.FirstOrDefault();
            }
            else
            {
                Selected = Providers.FirstOrDefault();
            }

            Status = string.Empty;

            // Eğer Selected value-equality yüzünden değişmediyse, editor alanlarını yine de güncelleyelim
            ApplySelectedToEditor(Selected);
        }).ConfigureAwait(false);

        // SelectedChanged tetiklenmese bile bunlar doğru kalsın
        var stamp = Interlocked.Increment(ref _selectionStamp);
        await SyncSelectedEnabledAsync(stamp, Selected).ConfigureAwait(false);
        await LoadSelectedEpgConfigAsync(stamp, Selected).ConfigureAwait(false);
    }

    private async Task SyncSelectedEnabledAsync(int stamp, ProviderSource? selectedSnapshot)
    {
        if (_state.CurrentProfile is null || selectedSnapshot is null)
        {
            await OnUIAsync(() => IsEnabledForProfile = false).ConfigureAwait(false);
            return;
        }

        var links = await _service.GetLinksAsync().ConfigureAwait(false);
        var link = links.FirstOrDefault(x =>
            x.ProfileId.Equals(_state.CurrentProfile.Id, StringComparison.OrdinalIgnoreCase) &&
            x.ProviderId.Equals(selectedSnapshot.Id, StringComparison.OrdinalIgnoreCase));

        if (stamp != Volatile.Read(ref _selectionStamp))
            return;

        await OnUIAsync(() =>
        {
            if (Selected is null || !Selected.Id.Equals(selectedSnapshot.Id, StringComparison.OrdinalIgnoreCase))
                return;

            IsEnabledForProfile = link?.IsEnabled ?? false;
        }).ConfigureAwait(false);
    }

    private async Task LoadSelectedEpgConfigAsync(int stamp, ProviderSource? selectedSnapshot)
    {
        if (selectedSnapshot is null)
        {
            await OnUIAsync(() => XmltvUrl = "").ConfigureAwait(false);
            return;
        }

        var cfg = await _epgCfgRepo.GetForProviderAsync(selectedSnapshot.Id).ConfigureAwait(false);

        if (stamp != Volatile.Read(ref _selectionStamp))
            return;

        await OnUIAsync(() =>
        {
            if (Selected is null || !Selected.Id.Equals(selectedSnapshot.Id, StringComparison.OrdinalIgnoreCase))
                return;

            XmltvUrl = cfg?.XmltvUrl ?? "";
        }).ConfigureAwait(false);
    }
}