using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AtlasHub.ViewModels;

namespace AtlasHub.Views;

public partial class ProfilePickerWindow : Window
{
    public ProfilePickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilePickerViewModel vm)
            vm.RequestOpenShell += OpenShell;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilePickerViewModel vm)
            vm.RequestOpenShell -= OpenShell;
    }

    private void OpenShell()
    {
        // Window'u hızlıca göster
        var main = new MainWindow();
        main.Show();

        // DI resolve'u UI'yi kilitlemesin diye arka öncelikte çalıştır
        Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
        {
            var shellVm = App.Services.GetRequiredService<ShellViewModel>();
            main.DataContext = shellVm;
        }), System.Windows.Threading.DispatcherPriority.Background);

        Close();
    }
}
