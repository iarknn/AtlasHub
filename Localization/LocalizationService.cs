using System.ComponentModel;
using System.Globalization;

namespace AtlasHub.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo CurrentCulture { get; private set; } = new("tr-TR");

    public void SetCulture(CultureInfo culture)
    {
        CurrentCulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
    }
}
