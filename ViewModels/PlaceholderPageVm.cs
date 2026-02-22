using CommunityToolkit.Mvvm.ComponentModel;

namespace AtlasHub.ViewModels;

/// <summary>
/// Sprint 1–2 için geçici sayfalar (Filmler / Diziler)
/// </summary>
public sealed partial class PlaceholderPageVm : ViewModelBase
{
    [ObservableProperty]
    private string _title;

    public PlaceholderPageVm(string title)
    {
        _title = title;
    }
}
