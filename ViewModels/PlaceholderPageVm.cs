using CommunityToolkit.Mvvm.ComponentModel;

namespace AtlasHub.ViewModels;

/// <summary>
/// Sprint 1–2 için geçici sayfalar (Filmler / Diziler).
/// Şimdilik yalnızca başlık gösteriliyor.
/// </summary>
public sealed partial class PlaceholderPageVm : ViewModelBase
{
    // CommunityToolkit 'Title' isimli observable property üretecek.
    [ObservableProperty]
    private string title;

    public PlaceholderPageVm(string title)
    {
        // Field yerine üretilen property'i kullan
        Title = title;
    }
}