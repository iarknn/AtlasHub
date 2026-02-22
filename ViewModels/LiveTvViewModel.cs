using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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

    private Dictionary<string, CatalogSnapshot> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EpgSnapshot?> _epgCache = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ProviderScope> Scopes { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<LiveChannelItemVm> Channels { get; } = new();
    public ObservableCollection<EpgTimelineItemVm> Timeline { get; } = new();

    [ObservableProperty] private ProviderScope? _selectedScope;
    [ObservableProperty] private string? _selectedCategory;
    [ObservableProperty] private LiveChannelItemVm? _selectedChannel;
    [ObservableProperty] private EpgTimelineItemVm? _selectedTimelineItem;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    public bool HasSelectedProgram => SelectedTimelineItem is not null;

    public string SelectedProgramTitle => SelectedTimelineItem?.Title ?? string.Empty;

    public string SelectedProgramTimeRange =>
        SelectedTimelineItem is null
            ? string.Empty
            : $"{SelectedTimelineItem.Program.StartUtc.ToLocalTime():HH:mm} – {SelectedTimelineItem.Program.EndUtc.ToLocalTime():HH:mm}";

    public string SelectedProgramDescription =>
        string.IsNullOrWhiteSpace(SelectedTimelineItem?.Description)
            ? "Açıklama yok"
            : SelectedTimelineItem!.Description!;

    public bool IsSelectedProgramNow => SelectedTimelineItem?.IsNow == true;

    public double SelectedProgramProgress => SelectedTimelineItem?.Progress ?? 0;

    public string SelectedProgramRemainingText
    {
        get
        {
            var p = SelectedTimelineItem?.Program;
            if (p is null) return string.Empty;

            var remaining = p.EndUtc - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return "Bitti";

            if (remaining.TotalHours >= 1)
                return $"Kalan: {remaining.Hours} sa {remaining.Minutes} dk";

            return $"Kalan: {remaining.Minutes} dk";
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

        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_state.CurrentProfile is null)
        {
            StatusText = "Profil seçilmedi.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Yükleniyor...";

            var (scopes, catalogs) = await _liveTv.LoadForProfileAsync(_state.CurrentProfile.Id);
            _catalogs = catalogs;

            _epgCache.Clear();

            Scopes.Clear();
            foreach (var s in scopes)
                Scopes.Add(s);

            SelectedScope = Scopes.FirstOrDefault();

            StatusText = Scopes.Count == 0
                ? "Etkin kaynak yok. Sources ekranından kaynak ekleyin/etkinleştirin."
                : "Hazır";
        }
        catch (Exception ex)
        {
            StatusText = "Yükleme hatası: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectTimelineItem(EpgTimelineItemVm? item)
    {
        if (item is null) return;

        foreach (var t in Timeline)
            t.IsSelected = ReferenceEquals(t, item);

        SelectedTimelineItem = item;

        OnPropertyChanged(nameof(HasSelectedProgram));
        OnPropertyChanged(nameof(SelectedProgramTitle));
        OnPropertyChanged(nameof(SelectedProgramTimeRange));
        OnPropertyChanged(nameof(SelectedProgramDescription));
        OnPropertyChanged(nameof(IsSelectedProgramNow));
        OnPropertyChanged(nameof(SelectedProgramProgress));
        OnPropertyChanged(nameof(SelectedProgramRemainingText));
    }

    partial void OnSelectedScopeChanged(ProviderScope? value) => BuildCategories();

    partial void OnSelectedCategoryChanged(string? value) => BuildChannels();

    partial void OnSelectedChannelChanged(LiveChannelItemVm? value) => _ = OnSelectedChannelChangedAsync(value);

    private async Task OnSelectedChannelChangedAsync(LiveChannelItemVm? value)
    {
        if (value is null)
        {
            Timeline.Clear();
            SelectedTimelineItem = null;
            _ticker.Stop();

            OnPropertyChanged(nameof(HasSelectedProgram));
            return;
        }

        try { _player.Play(value.StreamUrl); } catch { }

        await LoadTimelineForSelectedChannelAsync();
        _ticker.Start();
    }

    private void BuildCategories()
    {
        Categories.Clear();
        Channels.Clear();
        Timeline.Clear();
        SelectedCategory = null;
        SelectedChannel = null;
        SelectedTimelineItem = null;

        if (SelectedScope is null) return;

        var names = LiveTvService.BuildCategoryNames(SelectedScope.Key, _catalogs);
        foreach (var n in names)
            Categories.Add(n);

        SelectedCategory = Categories.FirstOrDefault();
    }

    private void BuildChannels()
    {
        Channels.Clear();
        Timeline.Clear();
        SelectedChannel = null;
        SelectedTimelineItem = null;

        if (SelectedScope is null || string.IsNullOrWhiteSpace(SelectedCategory))
            return;

        var channels = LiveTvService.BuildChannels(SelectedScope.Key, SelectedCategory!, _catalogs);
        foreach (var ch in channels)
            Channels.Add(new LiveChannelItemVm(ch, _logos));

        SelectedChannel = Channels.FirstOrDefault();
    }

    private async Task LoadTimelineForSelectedChannelAsync()
    {
        Timeline.Clear();
        SelectedTimelineItem = null;

        if (SelectedChannel is null) return;

        try
        {
            var providerId = SelectedChannel.Channel.ProviderId;

            if (!_epgCache.TryGetValue(providerId, out var epgSnap))
            {
                epgSnap = await _epgRepo.LoadAsync(providerId);
                _epgCache[providerId] = epgSnap;
            }

            if (epgSnap is null)
            {
                OnPropertyChanged(nameof(HasSelectedProgram));
                return;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var items = _epg.GetTimelineItems(
                epgSnap,
                SelectedChannel.Channel,
                nowUtc,
                pastWindow: TimeSpan.FromHours(2),
                futureWindow: TimeSpan.FromHours(6));

            if (items.Count == 0)
            {
                OnPropertyChanged(nameof(HasSelectedProgram));
                return;
            }

            var nowLocal = DateTimeOffset.Now;

            foreach (var (program, isNow, progress) in items)
            {
                var vm = new EpgTimelineItemVm(program)
                {
                    IsNow = isNow,
                    Progress = progress
                };

                // güvence: local bazlı da doğrula (küçük farklar için)
                vm.IsNow = vm.IsNowAt(nowLocal);
                vm.Progress = vm.IsNow ? vm.GetProgressPercent(nowLocal) : 0;

                Timeline.Add(vm);
            }

            var nowItem = Timeline.FirstOrDefault(x => x.IsNow) ?? Timeline.FirstOrDefault();
            if (nowItem is not null)
                SelectTimelineItem(nowItem);
        }
        catch (Exception ex)
        {
            StatusText = "EPG hatası: " + ex.Message;
        }
    }

    private void OnTickerTick()
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
            OnPropertyChanged(nameof(IsSelectedProgramNow));
            OnPropertyChanged(nameof(SelectedProgramProgress));
            OnPropertyChanged(nameof(SelectedProgramRemainingText));
        }
    }

    private void OnProvidersChanged(object? sender, EventArgs e) => _ = RefreshAsync();

    public void Dispose()
    {
        _bus.ProvidersChanged -= OnProvidersChanged;
        _ticker.Tick -= OnTickerTick;
        _ticker.Stop();
    }
}