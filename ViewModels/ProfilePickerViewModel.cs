using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AtlasHub.Models;
using AtlasHub.Services;

namespace AtlasHub.ViewModels;

public partial class ProfilePickerViewModel : ViewModelBase
{
    private readonly IProfileRepository _repo;
    private readonly AppState _state;

    public ObservableCollection<Profile> Profiles { get; } = new();

    [ObservableProperty] private Profile? _selectedProfile;
    [ObservableProperty] private string _newProfileName = "";
    [ObservableProperty] private string _selectedAvatarKey = "red";

    public event Action? RequestOpenShell;

    public ProfilePickerViewModel(IProfileRepository repo, AppState state)
    {
        _repo = repo;
        _state = state;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Profiles.Clear();
        foreach (var p in await _repo.GetAllAsync())
            Profiles.Add(p);

        SelectedProfile = Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private async Task CreateProfile()
    {
        var p = await _repo.CreateAsync(NewProfileName, SelectedAvatarKey);
        Profiles.Add(p);
        SelectedProfile = p;
        NewProfileName = "";
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        if (SelectedProfile is null) return;

        var id = SelectedProfile.Id;
        await _repo.DeleteAsync(id);

        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private void Continue()
    {
        if (SelectedProfile is null) return;

        _state.SetProfile(SelectedProfile);
        RequestOpenShell?.Invoke();
    }
}
