using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GumpForge.Core.Models;
using GumpForge.Core.Serialization;

namespace GumpForge.App.Views;

public partial class ProfileWindow : Window
{
    /// <summary>
    /// The selected/created profile. Null if user skipped.
    /// </summary>
    public ShardProfile? SelectedProfile { get; private set; }

    /// <summary>
    /// Discoverable profile entries (Name + Path).
    /// </summary>
    private readonly List<ProfileEntry> _profiles = [];

    public ProfileWindow()
    {
        InitializeComponent();
        LoadProfileList();

        // Set default storage path
        var storageInput = this.FindControl<TextBox>("StoragePathInput");
        if (storageInput is not null)
            storageInput.Text = ProfileSerializer.DefaultProfilesDirectory;
    }

    private void LoadProfileList()
    {
        _profiles.Clear();

        var files = ProfileSerializer.DiscoverProfiles();
        foreach (var file in files)
        {
            _profiles.Add(new ProfileEntry
            {
                Name = ProfileSerializer.GetProfileNameFromPath(file),
                Path = file
            });
        }

        var list = this.FindControl<ListBox>("ProfileList");
        if (list is not null)
        {
            list.ItemsSource = _profiles;
            if (_profiles.Count > 0)
                list.SelectedIndex = 0;
        }
    }

    private async void LoadProfile_Click(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("ProfileList");
        if (list?.SelectedItem is ProfileEntry entry)
        {
            SelectedProfile = await ProfileSerializer.LoadAsync(entry.Path);
            Close();
        }
    }

    private async void BrowseProfile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Shard Profile",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("GumpForge Profile") { Patterns = ["*.gfprofile"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            SelectedProfile = await ProfileSerializer.LoadAsync(files[0].Path.LocalPath);
            Close();
        }
    }

    private async void BrowseClientFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select UO Client Data Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var input = this.FindControl<TextBox>("ClientPathInput");
            if (input is not null)
                input.Text = folders[0].Path.LocalPath;
        }
    }

    private async void BrowseProfileStorage_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Profile Storage Directory",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var input = this.FindControl<TextBox>("StoragePathInput");
            if (input is not null)
                input.Text = folders[0].Path.LocalPath;
        }
    }

    private async void CreateProfile_Click(object? sender, RoutedEventArgs e)
    {
        var nameInput = this.FindControl<TextBox>("ProfileNameInput");
        var clientInput = this.FindControl<TextBox>("ClientPathInput");
        var storageInput = this.FindControl<TextBox>("StoragePathInput");

        var profileName = nameInput?.Text?.Trim();
        if (string.IsNullOrEmpty(profileName))
        {
            profileName = "Default";
        }

        var profile = new ShardProfile
        {
            ProfileName = profileName,
            ClientDataPath = clientInput?.Text ?? string.Empty
        };

        // Determine save path
        var storageDir = storageInput?.Text ?? ProfileSerializer.DefaultProfilesDirectory;
        var safeName = profileName.Replace(" ", "_");
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');
        var filePath = System.IO.Path.Combine(storageDir, $"{safeName}.gfprofile");

        await ProfileSerializer.SaveAsync(profile, filePath);

        SelectedProfile = profile;
        Close();
    }

    private void Skip_Click(object? sender, RoutedEventArgs e)
    {
        SelectedProfile = null;
        Close();
    }
}

/// <summary>
/// Simple display model for the profile list.
/// </summary>
public class ProfileEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
