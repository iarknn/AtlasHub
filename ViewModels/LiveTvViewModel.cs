using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AtlasHub.Localization;
using AtlasHub.Models;
using AtlasHub.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtlasHub.ViewModels;

public sealed partial class LiveTvViewModel : ViewModelBase, IDisposable
{
    private readonly AppState _state;
    private readonly LiveTvService _liveTv;
    private readonly LogoCacheService _logos;
    private readonly PlayerService _player;
    private readonly EpgRepository _epgRepo;
    private readonly EpgService _epg;
    private readonly LiveEpgTickerService _ticker;
    private readonly AppEventBus _bus;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Task? _initialLoadTask;

    private Dictionary<string, CatalogSnapshot> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EpgSnapshot?> _epgCache = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _channelsCts;
    private CancellationTokenSource? _nowNextCts;

    private bool _suppressScopeChanged;
    private bool _suppressCategoryChanged;

    public ObservableCollection<ProviderScope> Scopes { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<LiveChannelItemVm> Channels { get; } = new();
    public ObservableCollection<EpgTimelineItemVm> Timeline { get; } = new();

    [ObservableProperty] private ProviderScope? _selectedScope;
    [ObservableProperty] private string? _selectedCategory;
    [ObservableProperty] private LiveChannelItemVm? _selectedChannel;
    [ObservableProperty] private EpgTimelineItemVm? _selectedTimelineItem;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;

    private double _selectedProgramProgress;
    public double SelectedProgramProgress
    {
        get => _selectedProgramProgress;
        set => SetProperty(ref _selectedProgramProgress, value);
    }

    public bool HasSelectedProgram => SelectedTimelineItem is not null;
    public string SelectedProgramTitle => SelectedTimelineItem?.Title ?? string.Empty;

    public string SelectedProgramTimeRange =>
        SelectedTimelineItem is null
            ? string.Empty
            : $"{SelectedTimelineItem.Program.StartUtc.ToLocalTime():HH:mm} – {SelectedTimelineItem.Program.EndUtc.ToLocalTime():HH:mm}";

    public string SelectedProgramDescription =>
        string.IsNullOrWhiteSpace(SelectedTimelineItem?.Description)
            ? Loc.Svc["LiveTv.Program.NoDescription"]
            : SelectedTimelineItem!.Description!;

    public bool IsSelectedProgramNow => SelectedTimelineItem?.IsNow == true;

    public string SelectedProgramRemainingText
    {
        get
        {
            var p = SelectedTimelineItem?.Program;
            if (p is null) return string.Empty;

            var remaining = p.EndUtc - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return Loc.Svc["LiveTv.Program.RemainingEnded"];

            if (remaining.TotalHours >= 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    Loc.Svc["LiveTv.Program.RemainingHoursMinutes"],
                    remaining.Hours,
                    remaining.Minutes);
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                Loc.Svc["LiveTv.Program.RemainingMinutes"],
                remaining.Minutes);
        }
    }

    public LiveTvViewModel(
        AppState state,
        LiveTvService liveTv,
        LogoCacheService logos,
        PlayerService player,
        EpgRepository epgRepo,
        EpgService epg,
        LiveEpgTickerService ticker,
        AppEventBus bus)
    {
        _state = state;
        _liveTv = liveTv;
        _logos = logos;
        _player = player;
        _epgRepo = epgRepo;
        _epg = epg;
        _ticker = ticker;
        _bus = bus;

        _bus.ProvidersChanged += OnProvidersChanged;
        _ticker.Tick += OnTickerTick;

        // ❗Startup freeze azaltmak için: ctor'da Refresh çağırmıyoruz.
    }

    /// <summary>Shell açıldıktan sonra LiveTV yükünü başlatmak için (idempotent).</summary>
    public Task EnsureLoadedAsync()
        => _initialLoadTask ??= RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_state.CurrentProfile is null)
        {
            StatusText = Loc.Svc["LiveTv.Status.ProfileNotSelected"];
            return;
        }

        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await OnUIAsync(() =>
            {
                IsBusy = true;
                StatusText = Loc.Svc["LiveTv.Status.Loading"];
            }).ConfigureAwait(false);

            var (scopes, catalogs) = await _liveTv.LoadForProfileAsync(_state.CurrentProfile.Id).ConfigureAwait(false);

            _catalogs = catalogs;
            _epgCache.Clear();

            await OnUIAsync(() =>
            {
                Scopes.Clear();
                foreach (var s in scopes) Scopes.Add(s);

                _suppressScopeChanged = true;
                SelectedScope = Scopes.FirstOrDefault();
                _suppressScopeChanged = false;

                BuildCategories(autoSelectFirst: false);

                StatusText =
                    Scopes.Count == 0
                        ? Loc.Svc["LiveTv.Status.NoActiveSource"]
                        : Loc.Svc["LiveTv.Status.Ready"];
            }).ConfigureAwait(false);

            // UI çizildikten sonra ilk kategori seçimi + channel build (fire & forget ama _= ile CS4014 yok)
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SelectedScope is null) return;
                if (Categories.Count == 0) return;

                _suppressCategoryChanged = true;
                SelectedCategory = Categories.FirstOrDefault();
                _suppressCategoryChanged = false;

                _ = BuildChannelsAsync();
            }), DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            await OnUIAsync(() =>
            {
                StatusText = string.Format(
                    CultureInfo.CurrentCulture,
                    Loc.Svc["LiveTv.Status.LoadErrorPrefix"],
                    ex.Message);
            }).ConfigureAwait(false);
        }
        finally
        {
            await OnUIAsync(() => IsBusy = false).ConfigureAwait(false);
            _refreshGate.Release();
        }
    }

    [RelayCommand]
    private void SelectTimelineItem(EpgTimelineItemVm? item)
    {
        if (item is null) return;

        foreach (var t in Timeline)
            t.IsSelected = ReferenceEquals(t, item);

        SelectedTimelineItem = item;
        SelectedProgramProgress = item.Progress;

        OnPropertyChanged(nameof(HasSelectedProgram));
        OnPropertyChanged(nameof(SelectedProgramTitle));
        OnPropertyChanged(nameof(SelectedProgramTimeRange));
        OnPropertyChanged(nameof(SelectedProgramDescription));
        OnPropertyChanged(nameof(IsSelectedProgramNow));
        OnPropertyChanged(nameof(SelectedProgramRemainingText));
    }

    partial void OnSelectedScopeChanged(ProviderScope? value)
    {
        if (_suppressScopeChanged) return;
        BuildCategories(autoSelectFirst: true);
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
        if (_suppressCategoryChanged) return;
        _ = BuildChannelsAsync(); // fire & forget bilinçli
    }

    partial void OnSelectedChannelChanged(LiveChannelItemVm? value)
        => _ = OnSelectedChannelChangedAsync(value);

    private void BuildCategories(bool autoSelectFirst)
    {
        Categories.Clear();
        Channels.Clear();
        Timeline.Clear();

        SelectedCategory = null;
        SelectedChannel = null;
        SelectedTimelineItem = null;
        SelectedProgramProgress = 0;

        if (SelectedScope is null) return;

        var names = LiveTvService.BuildCategoryNames(SelectedScope.Key, _catalogs);
        foreach (var n in names) Categories.Add(n);

        if (!autoSelectFirst) return;

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (SelectedScope is null) return;
            if (Categories.Count == 0) return;
            SelectedCategory = Categories.FirstOrDefault();
        }), DispatcherPriority.Background);
    }

    private async Task BuildChannelsAsync()
    {
        _channelsCts?.Cancel();
        _channelsCts?.Dispose();
        _channelsCts = new CancellationTokenSource();
        var ct = _channelsCts.Token;

        var scopeKey = SelectedScope?.Key;
        var category = SelectedCategory;

        await OnUIAsync(() =>
        {
            Channels.Clear();
            Timeline.Clear();

            SelectedChannel = null;
            SelectedTimelineItem = null;
            SelectedProgramProgress = 0;
        }).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(scopeKey) || string.IsNullOrWhiteSpace(category))
            return;

        List<LiveChannel> channels;
        try
        {
            channels = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return LiveTvService.BuildChannels(scopeKey!, category!, _catalogs);
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        const int batchSize = 200;

        for (int i = 0; i < channels.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = channels.Skip(i).Take(batchSize).ToList();

            await OnUIAsync(() =>
            {
                foreach (var ch in batch)
                    Channels.Add(new LiveChannelItemVm(ch, _logos));
            }, DispatcherPriority.Background).ConfigureAwait(false);

            // ✅ CS0176 fix: Dispatcher.Yield yerine UI dispatcher'a "boş iş" post edip await ediyoruz.
            // Bu hem compile eder hem de UI'nın mesaj kuyruğunu işlemesine izin verir.
            await UiYieldAsync(DispatcherPriority.Background, ct).ConfigureAwait(false);
        }

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ct.IsCancellationRequested) return;
            if (SelectedScope?.Key != scopeKey) return;
            if (!string.Equals(SelectedCategory, category, StringComparison.OrdinalIgnoreCase)) return;

            SelectedChannel = Channels.FirstOrDefault();
        }), DispatcherPriority.ApplicationIdle);

        _ = PopulateNowNextForChannelsAsync(ct);
    }

    private async Task PopulateNowNextForChannelsAsync(CancellationToken ct)
    {
        _nowNextCts?.Cancel();
        _nowNextCts?.Dispose();
        _nowNextCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _nowNextCts.Token;

        List<LiveChannelItemVm> vmSnapshot =
            await Application.Current.Dispatcher.InvokeAsync(() => Channels.ToList()).Task.ConfigureAwait(false);

        if (vmSnapshot.Count == 0) return;

        const int firstBatchCount = 250;
        await ComputeAndApplyNowNextAsync(vmSnapshot.Take(firstBatchCount).ToList(), token).ConfigureAwait(false);

        if (vmSnapshot.Count > firstBatchCount)
        {
            var rest = vmSnapshot.Skip(firstBatchCount).ToList();
            const int batchSize = 250;

            for (int i = 0; i < rest.Count; i += batchSize)
            {
                token.ThrowIfCancellationRequested();
                var batch = rest.Skip(i).Take(batchSize).ToList();

                await ComputeAndApplyNowNextAsync(batch, token).ConfigureAwait(false);
                await Task.Delay(1, token).ConfigureAwait(false);
            }
        }
    }

    private async Task ComputeAndApplyNowNextAsync(List<LiveChannelItemVm> items, CancellationToken ct)
    {
        if (items.Count == 0) return;

        var providerIds = items
            .Select(x => x.Channel.ProviderId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var pid in providerIds)
        {
            ct.ThrowIfCancellationRequested();

            if (_epgCache.ContainsKey(pid)) continue;
            var snap = await _epgRepo.LoadAsync(pid).ConfigureAwait(false);
            _epgCache[pid] = snap;
        }

        var nowUtc = DateTimeOffset.UtcNow;

        var results = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var arr = new (EpgProgram? now, EpgProgram? next)[items.Count];

            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var chVm = items[i];
                var pid = chVm.Channel.ProviderId;

                if (string.IsNullOrWhiteSpace(pid) ||
                    !_epgCache.TryGetValue(pid, out var snap) ||
                    snap is null)
                {
                    arr[i] = (null, null);
                    continue;
                }

                arr[i] = _epg.GetNowNext(snap, chVm.Channel, nowUtc);
            }

            return arr;
        }, ct).ConfigureAwait(false);

        await OnUIAsync(() =>
        {
            for (int i = 0; i < items.Count; i++)
                items[i].UpdateNowNext(results[i].now, results[i].next);
        }, DispatcherPriority.Background).ConfigureAwait(false);
    }

    private async Task OnSelectedChannelChangedAsync(LiveChannelItemVm? value)
    {
        if (value is null)
        {
            await OnUIAsync(() =>
            {
                Timeline.Clear();
                SelectedTimelineItem = null;
                SelectedProgramProgress = 0;
                OnPropertyChanged(nameof(HasSelectedProgram));
            }).ConfigureAwait(false);

            _ticker.Stop();
            return;
        }

        try { _player.Play(value.StreamUrl); } catch { /* ignore */ }

        await LoadTimelineForChannelAsync(value).ConfigureAwait(false);
        _ticker.Start();
    }

    private async Task LoadTimelineForChannelAsync(LiveChannelItemVm channelVm)
    {
        await OnUIAsync(() =>
        {
            Timeline.Clear();
            SelectedTimelineItem = null;
            SelectedProgramProgress = 0;
        }).ConfigureAwait(false);

        var providerId = channelVm.Channel.ProviderId;
        if (string.IsNullOrWhiteSpace(providerId))
        {
            await OnUIAsync(() => OnPropertyChanged(nameof(HasSelectedProgram))).ConfigureAwait(false);
            return;
        }

        try
        {
            if (!_epgCache.TryGetValue(providerId, out var epgSnap))
            {
                epgSnap = await _epgRepo.LoadAsync(providerId).ConfigureAwait(false);
                _epgCache[providerId] = epgSnap;
            }

            if (epgSnap is null)
            {
                await OnUIAsync(() => OnPropertyChanged(nameof(HasSelectedProgram))).ConfigureAwait(false);
                return;
            }

            var nowUtc = DateTimeOffset.UtcNow;

            var rawItems = _epg.GetTimelineItems(
                epgSnap,
                channelVm.Channel,
                nowUtc,
                pastWindow: TimeSpan.FromHours(2),
                futureWindow: TimeSpan.FromHours(6));

            var items = DeduplicateTimelineItems(rawItems);
            if (items.Count == 0)
            {
                await OnUIAsync(() => OnPropertyChanged(nameof(HasSelectedProgram))).ConfigureAwait(false);
                return;
            }

            var nowLocal = DateTimeOffset.Now;

            await OnUIAsync(() =>
            {
                foreach (var (program, isNow, progressInt) in items)
                {
                    var vm = new EpgTimelineItemVm(program)
                    {
                        IsNow = isNow,
                        Progress = progressInt,
                        IsSelected = false
                    };

                    vm.IsNow = vm.IsNowAt(nowLocal);
                    vm.Progress = vm.IsNow ? vm.GetProgressPercent(nowLocal) : vm.Progress;

                    Timeline.Add(vm);
                }

                var nowItem = Timeline.FirstOrDefault(x => x.IsNow) ?? Timeline.FirstOrDefault();
                if (nowItem is not null)
                    SelectTimelineItem(nowItem);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await OnUIAsync(() =>
            {
                StatusText = string.Format(
                    CultureInfo.CurrentCulture,
                    Loc.Svc["LiveTv.Status.EpgErrorPrefix"],
                    ex.Message);
            }).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<(EpgProgram program, bool isNow, int progress)> DeduplicateTimelineItems(
        IReadOnlyList<(EpgProgram program, bool isNow, int progress)> items)
    {
        if (items.Count == 0) return items;

        var grouped = items.GroupBy(x => new
        {
            x.program.ChannelId,
            x.program.StartUtc,
            x.program.EndUtc,
            Title = NormalizeTitle(x.program.Title)
        });

        var result = new List<(EpgProgram program, bool isNow, int progress)>();

        foreach (var g in grouped)
        {
            var chosen = g
                .OrderByDescending(x => x.isNow)
                .ThenBy(x => (x.program.Title ?? string.Empty).Length)
                .First();

            result.Add(chosen);
        }

        result.Sort((a, b) => a.program.StartUtc.CompareTo(b.program.StartUtc));
        return result;
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        var s = title.Trim();
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);

        return s.ToUpperInvariant();
    }

    private void OnProvidersChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = RefreshAsync();
            return;
        }

        dispatcher.BeginInvoke(new Action(() => _ = RefreshAsync()));
    }

    private void OnTickerTick()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            UpdateTickerOnUi();
            return;
        }

        dispatcher.BeginInvoke(new Action(UpdateTickerOnUi));
    }

    private void UpdateTickerOnUi()
    {
        if (SelectedChannel is null) return;
        if (Timeline.Count == 0) return;

        var nowLocal = DateTimeOffset.Now;
        var nowUtc = DateTimeOffset.UtcNow;

        foreach (var item in Timeline)
        {
            var isNow = item.IsNowAt(nowLocal);
            item.IsNow = isNow;
            item.Progress = isNow ? item.GetProgressPercent(nowLocal) : 0;
        }

        var selected = SelectedTimelineItem;
        var selectedEnded = selected is not null && selected.Program.EndUtc <= nowUtc;

        if (selected is null || selectedEnded)
        {
            var nowItem = Timeline.FirstOrDefault(i => i.IsNow) ?? Timeline.FirstOrDefault();
            if (nowItem is not null)
                SelectTimelineItem(nowItem);
        }
        else
        {
            SelectedProgramProgress = selected.Progress;
            OnPropertyChanged(nameof(IsSelectedProgramNow));
            OnPropertyChanged(nameof(SelectedProgramRemainingText));
        }
    }

    private static Task OnUIAsync(Action action, DispatcherPriority priority = DispatcherPriority.DataBind)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, priority).Task;
    }

    private static Task UiYieldAsync(DispatcherPriority priority, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return Task.Delay(1, ct);

        // UI kuyruğuna düşük öncelikte bir "no-op" bırakıp await etmek: UI'nın nefes almasını sağlar.
        return dispatcher.InvokeAsync(static () => { }, priority).Task;
    }

    public void Dispose()
    {
        _channelsCts?.Cancel();
        _channelsCts?.Dispose();

        _nowNextCts?.Cancel();
        _nowNextCts?.Dispose();

        _bus.ProvidersChanged -= OnProvidersChanged;
        _ticker.Tick -= OnTickerTick;
        _ticker.Stop();

        _refreshGate.Dispose();
    }
}