using System.Globalization;
using System.Threading.Tasks;
using AtlasHub.Localization;
using AtlasHub.Models;
using AtlasHub.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AtlasHub.ViewModels;

public sealed partial class LiveChannelItemVm : ObservableObject
{
    private readonly LogoCacheService _logos;

    public LiveChannel Channel { get; }

    [ObservableProperty] private string? _logoPath;

    // EPG Now/Next
    [ObservableProperty] private string? _nowTitle;
    [ObservableProperty] private string? _nowTimeRange;
    [ObservableProperty] private string? _nextTitle;
    [ObservableProperty] private bool _hasEpg;

    public string Name => Channel.Name;
    public string CategoryName => Channel.CategoryName;
    public string StreamUrl => Channel.StreamUrl;

    // XAML'de Group kullanıldığı için backward compatible alias
    public string Group => CategoryName;

    // Monogram + state
    public string Monogram =>
        string.IsNullOrWhiteSpace(Name)
            ? "?"
            : Name.Trim()[0].ToString().ToUpperInvariant();

    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoPath);

    // Next satırı lokalleşmiş prefix ile
    public string? NextLine =>
        string.IsNullOrWhiteSpace(NextTitle)
            ? null
            : string.Format(
                CultureInfo.CurrentCulture,
                Loc.Svc["LiveTv.Channel.NextPrefix"],
                NextTitle);

    public LiveChannelItemVm(LiveChannel channel, LogoCacheService logos)
    {
        Channel = channel;
        _logos = logos;
        _ = LoadLogoAsync();
    }

    partial void OnLogoPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasLogo));
    }

    private async Task LoadLogoAsync()
    {
        LogoPath = await _logos.GetCachedPathAsync(Channel.LogoUrl);
        // HasLogo tetikleniyor (partial)
    }

    /// <summary>
    /// EPG Now/Next bilgisini kanal item'ına yazar.
    /// </summary>
    public void UpdateNowNext(EpgProgram? now, EpgProgram? next)
    {
        if (now is null && next is null)
        {
            HasEpg = false;
            NowTitle = null;
            NowTimeRange = null;
            NextTitle = null;
            return;
        }

        HasEpg = true;

        if (now is not null)
        {
            NowTitle = now.Title ?? Loc.Svc["LiveTv.Channel.FallbackNowTitle"];
            NowTimeRange =
                $"{now.StartUtc.ToLocalTime():HH:mm} – {now.EndUtc.ToLocalTime():HH:mm}";
        }
        else
        {
            NowTitle = null;
            NowTimeRange = null;
        }

        if (next is not null)
        {
            NextTitle = next.Title ?? Loc.Svc["LiveTv.Channel.FallbackNextTitle"];
        }
        else
        {
            NextTitle = null;
        }

        // NextLine property’si değişmiş olsun
        OnPropertyChanged(nameof(NextLine));
    }
}