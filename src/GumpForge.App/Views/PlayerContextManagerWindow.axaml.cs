using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GumpForge.Core.Models;

namespace GumpForge.App.Views;

public partial class PlayerContextManagerWindow : Window
{
    private readonly ObservableCollection<PlayerContextProfile> _profiles = [];
    private PlayerContextProfile? _selectedProfile;
    private bool _isUpdatingUi;

    public List<PlayerContextProfile> SavedProfiles { get; private set; } = [];
    public bool IsSaved { get; private set; }

    public PlayerContextManagerWindow()
    {
        InitializeComponent();
    }

    public PlayerContextManagerWindow(IEnumerable<PlayerContextProfile> initialProfiles) : this()
    {
        foreach (var p in initialProfiles)
        {
            _profiles.Add(p.Clone());
        }

        ProfileList.ItemsSource = _profiles;
        if (_profiles.Count > 0)
        {
            ProfileList.SelectedIndex = 0;
        }
    }

    private void ProfileList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedProfile = ProfileList.SelectedItem as PlayerContextProfile;

        if (_selectedProfile == null)
        {
            DetailPanel.IsVisible = false;
            PlaceholderText.IsVisible = true;
            return;
        }

        _isUpdatingUi = true;
        DetailPanel.IsVisible = true;
        PlaceholderText.IsVisible = false;

        ProfileNameText.Text = _selectedProfile.Name;
        ProfileSerialText.Text = _selectedProfile.Serial.ToString();
        VariablesList.ItemsSource = _selectedProfile.Variables;
        _isUpdatingUi = false;
    }

    private void AddProfile_Click(object? sender, RoutedEventArgs e)
    {
        var newProfile = new PlayerContextProfile
        {
            Name = $"Character {_profiles.Count + 1}",
            Serial = 0x0001000 + _profiles.Count
        };

        // Populate with common variables as a template
        newProfile.Variables.Add(new PlayerVariableEntry("Strength", "100"));
        newProfile.Variables.Add(new PlayerVariableEntry("Dexterity", "100"));
        newProfile.Variables.Add(new PlayerVariableEntry("Intelligence", "100"));
        newProfile.Variables.Add(new PlayerVariableEntry("Hits", "100/100"));
        newProfile.Variables.Add(new PlayerVariableEntry("Mana", "100/100"));
        newProfile.Variables.Add(new PlayerVariableEntry("JediLevel", "0"));
        newProfile.Variables.Add(new PlayerVariableEntry("IsMonk", "false"));

        _profiles.Add(newProfile);
        ProfileList.SelectedItem = newProfile;
    }

    private void DeleteProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedProfile != null)
        {
            int index = _profiles.IndexOf(_selectedProfile);
            _profiles.Remove(_selectedProfile);
            
            if (_profiles.Count > 0)
            {
                ProfileList.SelectedIndex = Math.Min(index, _profiles.Count - 1);
            }
        }
    }

    private void ProfileNameText_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi || _selectedProfile == null) return;
        _selectedProfile.Name = ProfileNameText.Text ?? string.Empty;
    }

    private void ProfileSerialText_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi || _selectedProfile == null) return;
        if (int.TryParse(ProfileSerialText.Text, out int serial))
        {
            _selectedProfile.Serial = serial;
        }
    }

    private void AddVariable_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedProfile != null)
        {
            _selectedProfile.Variables.Add(new PlayerVariableEntry("NewVariable", "Value"));
        }
    }

    private void DeleteVariable_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedProfile != null && VariablesList.SelectedItem is PlayerVariableEntry selectedVar)
        {
            _selectedProfile.Variables.Remove(selectedVar);
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        SavedProfiles = _profiles.ToList();
        IsSaved = true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
