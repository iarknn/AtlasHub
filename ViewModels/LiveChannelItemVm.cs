using CommunityToolkit.Mvvm.ComponentModel;
using AtlasHub.Models;
using AtlasHub.Services;

namespace AtlasHub.ViewModels;

public sealed partial class LiveChannelItemVm : ObservableObject
{
    private readonly LogoCacheService _logos;

    public LiveChannel Channel { get; }

    [ObservableProperty] private string? _logoPath;

    public string Name => Channel.Name;
    public string CategoryName => Channel.CategoryName;
    public string StreamUrl => Channel.StreamUrl;

    // NEW: Monogram + state
    public string Monogram
        => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[0].ToString().ToUpperInvariant();

    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoPath);

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
}
