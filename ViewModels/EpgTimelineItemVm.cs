using System;
using AtlasHub.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AtlasHub.ViewModels;

public partial class EpgTimelineItemVm : ObservableObject
{
    public EpgProgram Program { get; }

    public string Title => Program.Title ?? "";
    public string Description => Program.Description ?? "";

    public DateTimeOffset StartLocal => Program.StartUtc.ToLocalTime();
    public DateTimeOffset EndLocal => Program.EndUtc.ToLocalTime();

    public string TimeRangeText => $"{StartLocal:HH:mm} — {EndLocal:HH:mm}";

    // ✅ RENAMED: method adı property ile çakışmasın
    public bool IsNowAt(DateTimeOffset nowLocal) => nowLocal >= StartLocal && nowLocal < EndLocal;

    public double GetProgressPercent(DateTimeOffset nowLocal)
    {
        var total = (EndLocal - StartLocal).TotalSeconds;
        if (total <= 0) return 0;

        var elapsed = (nowLocal - StartLocal).TotalSeconds;
        if (elapsed < 0) elapsed = 0;
        if (elapsed > total) elapsed = total;

        return (elapsed / total) * 100.0;
    }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isNow;
    [ObservableProperty] private double _progress;

    public EpgTimelineItemVm(EpgProgram program)
    {
        Program = program;
    }
}