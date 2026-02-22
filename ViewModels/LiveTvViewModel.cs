using AtlasHub.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Linq;
using System.Windows.Media.Animation;

namespace AtlasHub.ViewModels;

public partial class LiveTvViewModel : ViewModelBase
{
    private readonly LiveEpgTickerService _ticker;

    public LiveTvViewModel(
        /* mevcut ctor parametrelerin */,
        LiveEpgTickerService ticker)
    {
        _ticker = ticker;
        _ticker.Tick += OnTickerTick;
    }

    public override void Dispose()
    {
        _ticker.Tick -= OnTickerTick;
        _ticker.Stop();
        base.Dispose();
    }

    // ---------------------------
    // Program Detail (Computed)
    // ---------------------------

    public bool HasSelectedProgram => SelectedTimelineItem != null;

    public string SelectedProgramTitle =>
        SelectedTimelineItem?.Title ?? string.Empty;

    public string SelectedProgramTimeRange =>
        SelectedTimelineItem == null
            ? string.Empty
            : $"{SelectedTimelineItem.StartUtc:HH:mm} – {SelectedTimelineItem.EndUtc:HH:mm}";

    public string SelectedProgramDescription =>
        SelectedTimelineItem?.Description ?? "Açıklama yok";

    public bool IsSelectedProgramNow =>
        SelectedTimelineItem?.IsNow == true;

    public double SelectedProgramProgress
    {
        get
        {
            var p = SelectedTimelineItem;
            if (p == null) return 0;

            var now = DateTimeOffset.UtcNow;
            var total = (p.EndUtc - p.StartUtc).TotalSeconds;
            if (total <= 0) return 0;

            var elapsed = (now - p.StartUtc).TotalSeconds;
            elapsed = Math.Clamp(elapsed, 0, total);

            return (elapsed / total) * 100;
        }
    }

    public string SelectedProgramRemainingText
    {
        get
        {
            var p = SelectedTimelineItem;
            if (p == null) return string.Empty;

            var remaining = p.EndUtc - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return "Bitti";

            if (remaining.TotalHours >= 1)
                return $"Kalan: {remaining.Hours} sa {remaining.Minutes} dk";

            return $"Kalan: {remaining.Minutes} dk";
        }
    }

    // ---------------------------
    // Ticker Logic
    // ---------------------------

    private void OnTickerTick(object? sender, EventArgs e)
    {
        if (SelectedChannel == null || Timeline == null || Timeline.Count == 0)
            return;

        OnPropertyChanged(nameof(SelectedProgramProgress));
        OnPropertyChanged(nameof(SelectedProgramRemainingText));
        OnPropertyChanged(nameof(IsSelectedProgramNow));

        var nowItem = Timeline.FirstOrDefault(i => i.IsNow);
        if (nowItem != null && nowItem != SelectedTimelineItem)
        {
            SelectedTimelineItem = nowItem;
        }
    }

    // Kanal değiştiğinde çağrılan mevcut metodunun SONUNA ekle
    private void OnSelectedChannelChanged()
    {
        _ticker.Start();
    }
}