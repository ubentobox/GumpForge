using Avalonia.Controls;
using Avalonia.Interactivity;
using GumpForge.Core.Models;
using GumpForge.App.Services;
using GumpForge.App.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace GumpForge.App.Views;

public partial class TagCollectionWindow : Window
{
    private ShardProfile? _profile;
    private MainWindowViewModel? _vm;

    public TagCollectionWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initialize the window with a profile reference and optionally open a specific tab.
    /// </summary>
    public void Initialize(ShardProfile profile, MainWindowViewModel vm, int openTab = 0)
    {
        _profile = profile;
        _vm = vm;

        // Populate rules grid
        RulesGrid.ItemsSource = profile.TagRules;

        // Populate tag list
        RefreshTagList();

        // Populate collection list
        RefreshCollectionList();

        // Open the requested tab
        var tabControl = this.FindControl<TabControl>("TabControl");
        // TabControl items are indexed by position
    }

    // ═══════════════════════════════════════════
    // TAB 1: Auto-Tag Rules
    // ═══════════════════════════════════════════

    private void AddRule_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        _profile.TagRules.Add(new TagRule { StartId = 0, EndId = 0, Tag = "new-tag", IsEnabled = true });
    }

    private void DeleteRule_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null || RulesGrid.SelectedItem is not TagRule rule) return;
        // RulesGrid is now a ListBox
        _profile.TagRules.Remove(rule);
    }

    private void ResetRules_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        _profile.TagRules.Clear();
        foreach (var rule in AutoTagger.GetDefaultRules())
            _profile.TagRules.Add(rule);
    }

    private void RunAutoTagger_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null || _vm is null) return;
        AutoTagger.TagAssets(_profile, _vm.ClientDataPath);
        RefreshTagList();
        if (_vm is not null)
            _vm.StatusMessage = $"✅ Auto-tagged {_profile.AssetMetadata.Count} assets";
    }

    // ═══════════════════════════════════════════
    // TAB 2: Tag Manager
    // ═══════════════════════════════════════════

    private void RefreshTagList()
    {
        if (_profile is null) return;

        var allTags = new HashSet<string>();
        foreach (var meta in _profile.AssetMetadata.Values)
        {
            foreach (var t in meta.Tags) allTags.Add(t);
            foreach (var t in meta.AutoTags) allTags.Add(t);
        }

        TagList.ItemsSource = allTags.OrderBy(t => t).ToList();
    }

    private void TagList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_profile is null || TagList.SelectedItem is not string selectedTag) return;

        var assets = _profile.AssetMetadata
            .Where(kvp => kvp.Value.Tags.Contains(selectedTag) || kvp.Value.AutoTags.Contains(selectedTag))
            .Select(kvp =>
            {
                var label = string.IsNullOrEmpty(kvp.Value.DisplayName)
                    ? $"Gump #{kvp.Key} (0x{kvp.Key:X4})"
                    : $"{kvp.Value.DisplayName} (#{kvp.Key})";
                var source = kvp.Value.AutoTags.Contains(selectedTag) ? " [auto]" : " [user]";
                return label + source;
            })
            .ToList();

        TagAssetList.ItemsSource = assets;
        TagAssetCount.Text = $"\"{selectedTag}\" — {assets.Count} asset(s)";
    }

    private async void RenameTag_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null || TagList.SelectedItem is not string selectedTag) return;

        var dialog = new Window
        {
            Title = "Rename Tag",
            Width = 350, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Black
        };

        var input = new TextBox { Text = selectedTag, Margin = new Avalonia.Thickness(16) };
        var btn = new Button { Content = "Rename", Margin = new Avalonia.Thickness(16, 0) };
        var stack = new StackPanel { Margin = new Avalonia.Thickness(8) };
        stack.Children.Add(new TextBlock { Text = $"Rename \"{selectedTag}\" to:", Foreground = Avalonia.Media.Brushes.White, Margin = new Avalonia.Thickness(16, 16, 16, 4) });
        stack.Children.Add(input);
        stack.Children.Add(btn);
        dialog.Content = stack;

        string? newName = null;
        btn.Click += (_, _) => { newName = input.Text; dialog.Close(); };

        await dialog.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(newName) && newName != selectedTag)
        {
            foreach (var meta in _profile.AssetMetadata.Values)
            {
                var idx = meta.Tags.IndexOf(selectedTag);
                if (idx >= 0) meta.Tags[idx] = newName;

                var autoIdx = meta.AutoTags.IndexOf(selectedTag);
                if (autoIdx >= 0) meta.AutoTags[autoIdx] = newName;
            }
            RefreshTagList();
        }
    }

    private void DeleteTag_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null || TagList.SelectedItem is not string selectedTag) return;

        foreach (var meta in _profile.AssetMetadata.Values)
        {
            meta.Tags.Remove(selectedTag);
            meta.AutoTags.Remove(selectedTag);
        }

        RefreshTagList();
        TagAssetList.ItemsSource = null;
        TagAssetCount.Text = "Tag deleted";
    }

    private void RemoveTagFromAll_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null || TagList.SelectedItem is not string selectedTag) return;

        foreach (var meta in _profile.AssetMetadata.Values)
        {
            meta.Tags.Remove(selectedTag);
            // For auto-tags, suppress so they don't come back
            if (meta.AutoTags.Remove(selectedTag))
            {
                if (!meta.SuppressedAutoTags.Contains(selectedTag))
                    meta.SuppressedAutoTags.Add(selectedTag);
            }
        }

        RefreshTagList();
        TagAssetList.ItemsSource = null;
        TagAssetCount.Text = "Tag removed from all assets";
    }

    // ═══════════════════════════════════════════
    // TAB 3: Collection Manager
    // ═══════════════════════════════════════════

    private void RefreshCollectionList()
    {
        if (_profile is null) return;

        CollectionList.ItemsSource = null;
        CollectionList.ItemsSource = _profile.Collections
            .Select(c => $"{c.Name} ({c.AssetIds.Count} items)")
            .ToList();
    }

    private AssetCollection? GetSelectedCollection()
    {
        if (_profile is null || CollectionList.SelectedIndex < 0) return null;
        if (CollectionList.SelectedIndex >= _profile.Collections.Count) return null;
        return _profile.Collections[CollectionList.SelectedIndex];
    }

    private void CollectionList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var col = GetSelectedCollection();
        if (col is null || _profile is null) return;

        var assets = col.AssetIds.Select(id =>
        {
            _profile.AssetMetadata.TryGetValue(id, out var meta);
            return string.IsNullOrEmpty(meta?.DisplayName)
                ? $"Gump #{id} (0x{id:X4})"
                : $"{meta.DisplayName} (#{id})";
        }).ToList();

        CollectionAssetList.ItemsSource = assets;
        CollectionAssetCount.Text = $"\"{col.Name}\" — {assets.Count} asset(s)";
    }

    private void AddCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        var name = NewCollectionName.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        _profile.Collections.Add(new AssetCollection { Name = name });
        NewCollectionName.Text = string.Empty;
        RefreshCollectionList();
    }

    private async void RenameCollection_Click(object? sender, RoutedEventArgs e)
    {
        var col = GetSelectedCollection();
        if (col is null) return;

        var dialog = new Window
        {
            Title = "Rename Collection",
            Width = 350, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Black
        };

        var input = new TextBox { Text = col.Name, Margin = new Avalonia.Thickness(16) };
        var btn = new Button { Content = "Rename", Margin = new Avalonia.Thickness(16, 0) };
        var stack = new StackPanel { Margin = new Avalonia.Thickness(8) };
        stack.Children.Add(new TextBlock { Text = $"Rename \"{col.Name}\" to:", Foreground = Avalonia.Media.Brushes.White, Margin = new Avalonia.Thickness(16, 16, 16, 4) });
        stack.Children.Add(input);
        stack.Children.Add(btn);
        dialog.Content = stack;

        string? newName = null;
        btn.Click += (_, _) => { newName = input.Text; dialog.Close(); };
        await dialog.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(newName))
        {
            col.Name = newName;
            RefreshCollectionList();
        }
    }

    private void DeleteCollection_Click(object? sender, RoutedEventArgs e)
    {
        var col = GetSelectedCollection();
        if (col is null || _profile is null) return;

        // Remove collection ID from all asset metadata
        foreach (var meta in _profile.AssetMetadata.Values)
            meta.CollectionIds.Remove(col.Id);

        _profile.Collections.Remove(col);
        RefreshCollectionList();
        CollectionAssetList.ItemsSource = null;
        CollectionAssetCount.Text = "Collection deleted";
    }

    private void RemoveFromCollection_Click(object? sender, RoutedEventArgs e)
    {
        var col = GetSelectedCollection();
        if (col is null || _profile is null) return;

        var selected = CollectionAssetList.SelectedItems?.Cast<string>().ToList();
        if (selected is null || selected.Count == 0) return;

        // Parse IDs from display strings
        foreach (var item in selected)
        {
            var match = System.Text.RegularExpressions.Regex.Match(item, @"#(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
            {
                col.AssetIds.Remove(id);
                if (_profile.AssetMetadata.TryGetValue(id, out var meta))
                    meta.CollectionIds.Remove(col.Id);
            }
        }

        CollectionList_SelectionChanged(null, null!);
        RefreshCollectionList();
    }

    private void ClearCollection_Click(object? sender, RoutedEventArgs e)
    {
        var col = GetSelectedCollection();
        if (col is null || _profile is null) return;

        foreach (var id in col.AssetIds)
        {
            if (_profile.AssetMetadata.TryGetValue(id, out var meta))
                meta.CollectionIds.Remove(col.Id);
        }

        col.AssetIds.Clear();
        CollectionList_SelectionChanged(null, null!);
        RefreshCollectionList();
    }

    // ═══════════════════════════════════════════
    // Common
    // ═══════════════════════════════════════════

    private async void SaveProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        await GumpForge.Core.Serialization.ProfileSerializer.SaveAsync(_profile);
        if (_vm is not null)
            _vm.StatusMessage = $"✅ Profile saved: {_profile.ProfileName}";
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
