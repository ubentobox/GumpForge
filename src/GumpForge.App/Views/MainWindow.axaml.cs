using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using GumpForge.App.ViewModels;
using GumpForge.App.Controls;
using GumpForge.Core.Models;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace GumpForge.App.Views;

public partial class MainWindow : Window
{
    // Drag state for Asset Browser → Canvas
    private AssetThumbnail? _draggedThumbnail;
    private bool _isAssetDragging;
    private Point _assetDragStart;

    // TextMate installation for syntax highlighting
    private TextMate.Installation? _textMateInstallation;
    private bool _isUpdatingEditor;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;

        // Set up AvaloniaEdit with C# syntax highlighting
        Loaded += (_, _) =>
        {
            InitializeCodeEditor();
            WireTagsMenuHandlers();
        };
    }

    /// <summary>
    /// Wire up named Tags menu items and other named controls to their handlers.
    /// </summary>
    private void WireTagsMenuHandlers()
    {
        // Tags menu
        if (this.FindControl<MenuItem>("OpenTagManagerMenuItem") is { } tagMgr)
            tagMgr.Click += OpenTagManager_Click;
        if (this.FindControl<MenuItem>("BulkAddTagMenuItem") is { } bulkAdd)
            bulkAdd.Click += BulkAddTagMenu_Click;
        if (this.FindControl<MenuItem>("BulkRemoveTagMenuItem") is { } bulkRemove)
            bulkRemove.Click += BulkRemoveTagMenu_Click;
        if (this.FindControl<MenuItem>("RunAutoTaggerMenuItem2") is { } autoTag2)
            autoTag2.Click += RunAutoTagger_Click;
        if (this.FindControl<MenuItem>("OpenTagRulesMenuItem") is { } tagRules)
            tagRules.Click += OpenTagRules_Click;
        if (this.FindControl<MenuItem>("SaveProfileMenuItem2") is { } saveProfile2)
            saveProfile2.Click += SaveProfile_Click;

        // Tag search box — Enter key adds the typed tag or first matching tag as a filter
        if (this.FindControl<TextBox>("TagSearchBox") is { } searchBox)
        {
            searchBox.KeyDown += TagSearchBox_KeyDown;
        }

        // Rebuild filter badges whenever the FilterTags collection changes
        if (DataContext is MainWindowViewModel vm)
        {
            vm.AssetBrowser.FilterTags.CollectionChanged += (_, _) => RebuildFilterTagBadges();
        }
    }

    /// <summary>
    /// When Enter is pressed in the tag search box, add the first matching tag as a filter.
    /// </summary>
    private void TagSearchBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter) return;
        if (DataContext is not MainWindowViewModel vm) return;

        var search = vm.AssetBrowser.TagSearchText.Trim();
        if (string.IsNullOrEmpty(search)) return;

        // Find exact match first, then partial
        var match = vm.AssetBrowser.AvailableTagsFiltered
            .FirstOrDefault(t => t.Equals(search, StringComparison.OrdinalIgnoreCase))
            ?? vm.AssetBrowser.AvailableTagsFiltered.FirstOrDefault();

        if (match is not null)
        {
            vm.AssetBrowser.AddFilterTagCommand.Execute(match);
        }
        else
        {
            // Add as a custom filter tag even if not in available tags
            vm.AssetBrowser.AddFilterTagCommand.Execute(search);
        }
        e.Handled = true;
    }

    /// <summary>
    /// Rebuilds the active filter tag badges in the FilterTagBadgesPanel.
    /// </summary>
    private void RebuildFilterTagBadges()
    {
        var panel = this.FindControl<WrapPanel>("FilterTagBadgesPanel");
        if (panel is null) return;

        panel.Children.Clear();

        if (DataContext is not MainWindowViewModel vm) return;

        foreach (var tag in vm.AssetBrowser.FilterTags.ToList())
        {
            var textBlock = new TextBlock
            {
                Text = tag,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#aad6a5")),
                FontSize = 9,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var removeBtn = new Button
            {
                Content = "✕",
                FontSize = 7,
                Padding = new Thickness(2, 0),
                Margin = new Thickness(2, 0, 0, 0),
                Background = Avalonia.Media.Brushes.Transparent,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f66")),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                MinWidth = 0, MinHeight = 0
            };
            var capturedTag = tag;
            removeBtn.Click += (_, _) => vm.AssetBrowser.RemoveFilterTagCommand.Execute(capturedTag);

            var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 1 };
            stack.Children.Add(textBlock);
            stack.Children.Add(removeBtn);

            panel.Children.Add(new Border
            {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1a3a2a")),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1),
                Margin = new Thickness(1),
                Child = stack
            });
        }
    }

    private void InitializeCodeEditor()
    {
        var editor = this.FindControl<TextEditor>("ServUoEditor");
        if (editor is null) return;

        // Initialize TextMate with Dark+ theme
        RegistryOptions? registryOptions = null;
        string? csharpScopeName = null;

        try
        {
            registryOptions = new RegistryOptions(ThemeName.DarkPlus);
            _textMateInstallation = editor.InstallTextMate(registryOptions);

            var csharpLanguage = registryOptions.GetLanguageByExtension(".cs");
            if (csharpLanguage is not null)
            {
                csharpScopeName = registryOptions.GetScopeByLanguageId(csharpLanguage.Id);
                _textMateInstallation.SetGrammar(csharpScopeName);
            }
        }
        catch
        {
            // TextMate setup failed — editor still works, just no highlighting
        }

        // Style the ServUO editor
        StyleEditor(editor);

        // Initialize read-only editors with highlighting
        var readOnlyEditors = new (string Name, string Property)[]
        {
            ("RunUoEditor", nameof(MainWindowViewModel.RunUoCode)),
            ("ModernUoEditor", nameof(MainWindowViewModel.ModernUoCode)),
            ("SphereEditor", nameof(MainWindowViewModel.SphereCode)),
            ("ClassicAssistEditor", nameof(MainWindowViewModel.ClassicAssistCode)),
        };

        foreach (var (name, property) in readOnlyEditors)
        {
            var roEditor = this.FindControl<TextEditor>(name);
            if (roEditor is null) continue;

            // Apply TextMate highlighting
            if (registryOptions is not null && csharpScopeName is not null)
            {
                try
                {
                    var tm = roEditor.InstallTextMate(registryOptions);
                    tm.SetGrammar(csharpScopeName);
                }
                catch { /* non-critical */ }
            }

            StyleEditor(roEditor);
        }

        // Sync ViewModel → Editors when code properties change
        if (DataContext is MainWindowViewModel vm)
        {
            // Initial sync for ServUO
            editor.Text = vm.GeneratedCode ?? string.Empty;

            // Listen for ViewModel code changes
            vm.PropertyChanged += (_, args) =>
            {
                if (_isUpdatingEditor) return;
                _isUpdatingEditor = true;

                if (args.PropertyName == nameof(vm.GeneratedCode))
                    editor.Text = vm.GeneratedCode ?? string.Empty;

                // Sync read-only editors
                foreach (var (name, property) in readOnlyEditors)
                {
                    if (args.PropertyName == property)
                    {
                        var roEditor = this.FindControl<TextEditor>(name);
                        if (roEditor is not null)
                        {
                            var value = typeof(MainWindowViewModel).GetProperty(property)?.GetValue(vm) as string;
                            roEditor.Text = value ?? string.Empty;
                        }
                    }
                }

                _isUpdatingEditor = false;
            };

            // Listen for ServUO editor text changes → ViewModel (editable)
            editor.TextChanged += (_, _) =>
            {
                if (!_isUpdatingEditor)
                {
                    _isUpdatingEditor = true;
                    vm.GeneratedCode = editor.Text;
                    _isUpdatingEditor = false;
                }
            };
        }
    }

    private static void StyleEditor(TextEditor editor)
    {
        editor.Foreground = Avalonia.Media.Brushes.White;
        editor.LineNumbersForeground = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#555"));
    }

    /// <summary>
    /// Double-click on Asset Browser thumbnail places the gump on the canvas.
    /// </summary>
    private void AssetBrowser_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.AssetBrowser.SelectedThumbnail is not null)
        {
            vm.AddGumpFromAsset(vm.AssetBrowser.SelectedThumbnail);
        }
    }

    /// <summary>
    /// Copy the generated code to the system clipboard.
    /// </summary>
    private async void CopyCode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && !string.IsNullOrEmpty(vm.GeneratedCode))
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(vm.GeneratedCode);
        }
    }

    // ── Asset Metadata Management ──────────────────────────────

    /// <summary>
    /// Save display name when the text box loses focus.
    /// </summary>
    private void AssetDisplayName_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is TextBox tb)
        {
            vm.AssetBrowser.SetDisplayNameCommand.Execute(tb.Text ?? string.Empty);
        }
    }

    /// <summary>
    /// Add a user tag to the currently selected asset, then refresh the metadata panel.
    /// </summary>
    private void AddTag_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var input = this.FindControl<TextBox>("NewTagInput");
        if (input is null || string.IsNullOrWhiteSpace(input.Text)) return;

        vm.AssetBrowser.AddTagCommand.Execute(input.Text);
        input.Text = string.Empty;
        RefreshMetadataPanel();
    }

    /// <summary>
    /// Create a new collection.
    /// </summary>
    private void CreateCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var input = this.FindControl<TextBox>("NewCollectionInput");
        if (input is null || string.IsNullOrWhiteSpace(input.Text)) return;

        vm.AssetBrowser.CreateCollectionCommand.Execute(input.Text);
        input.Text = string.Empty;
        RefreshMetadataPanel();
    }

    // ── Metadata Panel Rendering ──────────────────────────────

    /// <summary>
    /// Rebuilds the tag badges and collection checkboxes for the selected asset.
    /// Called whenever the selection changes or tags/collections are modified.
    /// </summary>
    private void RefreshMetadataPanel()
    {
        var tagPanel = this.FindControl<WrapPanel>("TagBadgesPanel");
        var colPanel = this.FindControl<StackPanel>("CollectionCheckboxPanel");
        if (tagPanel is null || colPanel is null) return;

        tagPanel.Children.Clear();
        colPanel.Children.Clear();

        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.AssetBrowser.SelectedThumbnail is null || vm.ActiveProfile is null) return;

        var gumpId = vm.AssetBrowser.SelectedThumbnail.GumpId;
        vm.ActiveProfile.AssetMetadata.TryGetValue(gumpId, out var meta);

        // ── Build tag badges ──
        if (meta is not null)
        {
            // User tags (green badges)
            foreach (var tag in meta.Tags.ToList())
            {
                var badge = CreateTagBadge(vm, tag, isAutoTag: false);
                tagPanel.Children.Add(badge);
            }
            // Auto-tags (blue badges)
            foreach (var tag in meta.AutoTags.ToList())
            {
                var badge = CreateTagBadge(vm, tag, isAutoTag: true);
                tagPanel.Children.Add(badge);
            }
        }

        if (tagPanel.Children.Count == 0)
        {
            tagPanel.Children.Add(new TextBlock
            {
                Text = "(no tags)",
                Foreground = Avalonia.Media.Brushes.Gray,
                FontSize = 9
            });
        }

        // ── Build collection checkboxes ──
        foreach (var col in vm.ActiveProfile.Collections)
        {
            var isInCollection = col.AssetIds.Contains(gumpId);
            var cb = new CheckBox
            {
                Content = $"{col.Name} ({col.AssetIds.Count})",
                FontSize = 10,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#a5c8d6")),
                IsChecked = isInCollection,
                Tag = col.Id
            };
            cb.Click += (s, _) =>
            {
                if (s is not CheckBox checkbox || checkbox.Tag is not string collectionId) return;
                if (vm.AssetBrowser.SelectedThumbnail is null) return;

                if (checkbox.IsChecked == true)
                    vm.AssetBrowser.AddToCollectionCommand.Execute(collectionId);
                else
                    vm.AssetBrowser.RemoveFromCollectionCommand.Execute(collectionId);

                // Also update the collection memberships in meta
                if (vm.ActiveProfile.AssetMetadata.TryGetValue(vm.AssetBrowser.SelectedThumbnail.GumpId, out var m))
                {
                    if (checkbox.IsChecked == true && !m.CollectionIds.Contains(collectionId))
                        m.CollectionIds.Add(collectionId);
                    else
                        m.CollectionIds.Remove(collectionId);
                }
            };
            colPanel.Children.Add(cb);
        }

        if (colPanel.Children.Count == 0)
        {
            colPanel.Children.Add(new TextBlock
            {
                Text = "(no collections — create one below or in Tags → Manager)",
                Foreground = Avalonia.Media.Brushes.Gray,
                FontSize = 9
            });
        }
    }

    /// <summary>
    /// Creates a tag badge with clickable text (filter) and ✕ button (remove).
    /// </summary>
    private Border CreateTagBadge(MainWindowViewModel vm, string tag, bool isAutoTag)
    {
        var bgColor = isAutoTag ? "#1a2a3a" : "#1a3a2a";
        var fgColor = isAutoTag ? "#a5c8d6" : "#aad6a5";
        var label = isAutoTag ? $"{tag} ⚙" : tag;

        var textBlock = new TextBlock
        {
            Text = label,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(fgColor)),
            FontSize = 9,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        textBlock.PointerPressed += (_, args) =>
        {
            vm.AssetBrowser.AddFilterTagCommand.Execute(tag);
            args.Handled = true;
        };
        ToolTip.SetTip(textBlock, "Click to filter by this tag");

        var removeBtn = new Button
        {
            Content = "✕",
            FontSize = 8,
            Padding = new Thickness(2, 0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f66")),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            MinWidth = 0, MinHeight = 0
        };
        ToolTip.SetTip(removeBtn, isAutoTag ? "Remove auto-tag (permanent)" : "Remove tag");
        removeBtn.Click += (_, _) =>
        {
            if (isAutoTag)
                vm.AssetBrowser.RemoveAutoTagCommand.Execute(tag);
            else
                vm.AssetBrowser.RemoveTagCommand.Execute(tag);
            RefreshMetadataPanel();
        };

        var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 1 };
        stack.Children.Add(textBlock);
        stack.Children.Add(removeBtn);

        return new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(bgColor)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1),
            Margin = new Thickness(1),
            Child = stack
        };
    }

    /// <summary>
    /// Clicking an element in the Layers panel selects it on the canvas.
    /// </summary>
    private void LayerElement_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is ListBox lb && lb.SelectedItem is GumpElement el)
        {
            vm.Selection.Select(el);
        }
    }

    // ── Asset Browser Drag-and-Drop (pointer-based) ─────────────

    /// <summary>
    /// Track pointer press on asset thumbnail to start drag.
    /// </summary>
    private void AssetThumbnail_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is AssetThumbnail thumb)
        {
            _draggedThumbnail = thumb;
            _assetDragStart = e.GetPosition(this);
            _isAssetDragging = false;
        }
    }

    /// <summary>
    /// Detect drag threshold and show visual feedback.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedThumbnail is not null && !_isAssetDragging)
        {
            var pos = e.GetPosition(this);
            var delta = pos - _assetDragStart;
            if (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5)
            {
                _isAssetDragging = true;
                Cursor = new Cursor(StandardCursorType.DragCopy);
            }
        }
    }

    /// <summary>
    /// Drop asset on release if drag was active.
    /// </summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isAssetDragging && _draggedThumbnail is not null && DataContext is MainWindowViewModel vm)
        {
            var canvas = this.FindControl<GumpCanvasControl>("GumpCanvas");
            if (canvas is not null)
            {
                var pos = e.GetPosition(canvas);

                // Check if pointer is over the canvas
                if (pos.X >= 0 && pos.Y >= 0 && pos.X <= canvas.Bounds.Width && pos.Y <= canvas.Bounds.Height)
                {
                    var zoom = vm.Canvas.Zoom;
                    var bounds = canvas.Bounds;
                    double offsetX = (bounds.Width - vm.Document.CanvasWidth * zoom) / 2 + vm.Canvas.PanX;
                    double offsetY = (bounds.Height - vm.Document.CanvasHeight * zoom) / 2 + vm.Canvas.PanY;

                    int canvasX = (int)((pos.X - offsetX) / zoom);
                    int canvasY = (int)((pos.Y - offsetY) / zoom);

                    // Clamp to canvas bounds
                    canvasX = Math.Max(0, Math.Min(canvasX, vm.Document.CanvasWidth - _draggedThumbnail.Width));
                    canvasY = Math.Max(0, Math.Min(canvasY, vm.Document.CanvasHeight - _draggedThumbnail.Height));

                    vm.AddGumpFromAssetAtPosition(_draggedThumbnail, canvasX, canvasY);
                }
            }
        }

        _draggedThumbnail = null;
        _isAssetDragging = false;
        Cursor = Cursor.Default;
    }

    // ── Keyboard Shortcuts ──────────────────────────────────────

    /// <summary>
    /// Global keyboard shortcuts.
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // F-key shortcuts work even when TextBox is focused
        switch (e.Key)
        {
            case Key.F1:
                Help_Click(sender, e);
                e.Handled = true;
                return;
            case Key.F5:
                _ = ExportCanvasAsPng(vm);
                e.Handled = true;
                return;
            case Key.F6:
                vm.ExportToMulCommand.Execute(null);
                e.Handled = true;
                return;
        }

        // Don't process other shortcuts if a TextBox or TextEditor is focused
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox || focused is AvaloniaEdit.TextEditor)
            return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            // Delete key
            case Key.Delete:
                vm.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            // Ctrl+Z/Y — Undo/Redo
            case Key.Z when ctrl:
                vm.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Y when ctrl:
                vm.RedoCommand.Execute(null);
                e.Handled = true;
                break;

            // Ctrl+C/X/V/D — Clipboard
            case Key.C when ctrl:
                vm.CopySelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.X when ctrl:
                vm.CutSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.V when ctrl:
                vm.PasteElementsCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D when ctrl:
                vm.DuplicateSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            // Ctrl+A — Select All
            case Key.A when ctrl:
                vm.SelectAllCommand.Execute(null);
                e.Handled = true;
                break;

            // Ctrl+G / Ctrl+Shift+G — Group/Ungroup
            case Key.G when ctrl && shift:
                vm.UngroupSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.G when ctrl:
                vm.GroupSelectedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ── Export ───────────────────────────────────────────────────

    /// <summary>
    /// Export the current canvas as a PNG screenshot.
    /// </summary>
    private async Task ExportCanvasAsPng(MainWindowViewModel vm)
    {
        var storageProvider = StorageProvider;
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Canvas as PNG",
            DefaultExtension = "png",
            FileTypeChoices =
            [
                new FilePickerFileType("PNG Image") { Patterns = ["*.png"] }
            ],
            SuggestedFileName = $"{vm.Document.GumpClassName}_export"
        });

        if (file is null) return;

        // Find the canvas control and render it
        var canvas = this.FindControl<GumpCanvasControl>("GumpCanvas");
        if (canvas is null) return;

        // Render the control to a bitmap
        var pixelSize = new PixelSize(
            Math.Max((int)canvas.Bounds.Width, 1),
            Math.Max((int)canvas.Bounds.Height, 1));
        var renderTarget = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        renderTarget.Render(canvas);

        // Save to file
        await using var stream = await file.OpenWriteAsync();
        renderTarget.Save(stream);
    }

    private void ExportPng_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            _ = ExportCanvasAsPng(vm);
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        about.ShowDialog(this);
    }

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        var help = new HelpWindow();
        help.ShowDialog(this);
    }

    // ── Profile & Tagging Tools ────────────────────────────────

    /// <summary>
    /// Save the active shard profile to disk.
    /// </summary>
    private async void SaveProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.ActiveProfile is not null)
        {
            await GumpForge.Core.Serialization.ProfileSerializer.SaveAsync(vm.ActiveProfile);
            vm.StatusMessage = $"✅ Profile saved: {vm.ActiveProfile.ProfileName}";
        }
    }

    /// <summary>
    /// Re-run the auto-tagger on all loaded assets.
    /// </summary>
    private void RunAutoTagger_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.ActiveProfile is not null)
        {
            GumpForge.App.Services.AutoTagger.TagAssets(vm.ActiveProfile, vm.ClientDataPath);
            vm.StatusMessage = $"✅ Auto-tagged {vm.ActiveProfile.AssetMetadata.Count} assets";
        }
    }

    /// <summary>
    /// Bulk add a tag to ALL selected assets (multi-select).
    /// </summary>
    private void BulkAddTag_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var input = this.FindControl<TextBox>("NewTagInput");
        if (input is null || string.IsNullOrWhiteSpace(input.Text)) return;

        vm.AssetBrowser.BulkAddTagCommand.Execute(input.Text);
        input.Text = string.Empty;
        vm.StatusMessage = $"✅ Tag added to {vm.AssetBrowser.SelectedThumbnails.Count} assets";
        RefreshMetadataPanel();
    }

    // ── Multi-Select Sync ─────────────────────────────────

    /// <summary>
    /// Sync the SelectedThumbnails collection when multi-selection changes.
    /// </summary>
    private void AssetBrowser_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not ListBox lb) return;

        vm.AssetBrowser.SelectedThumbnails.Clear();
        foreach (var item in lb.SelectedItems!)
        {
            if (item is AssetThumbnail thumb)
                vm.AssetBrowser.SelectedThumbnails.Add(thumb);
        }

        // Update count label
        var countLabel = this.FindControl<TextBlock>("SelectedCountLabel");
        if (countLabel is not null)
        {
            var count = vm.AssetBrowser.SelectedThumbnails.Count;
            countLabel.Text = count > 1 ? $"({count} selected)" : "";
        }

        // Refresh tag badges and collection checkboxes for the newly selected asset
        RefreshMetadataPanel();
    }

    // ── Tag & Collection Manager Window ───────────────────

    /// <summary>
    /// Open the Tag & Collection Manager modal window.
    /// </summary>
    private void OpenTagManager_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.ActiveProfile is null) return;

        var win = new TagCollectionWindow();
        win.Initialize(vm.ActiveProfile, vm);
        win.ShowDialog(this);
    }

    /// <summary>
    /// Open the Tag & Collection Manager on the Rules tab.
    /// </summary>
    private void OpenTagRules_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.ActiveProfile is null) return;

        var win = new TagCollectionWindow();
        win.Initialize(vm.ActiveProfile, vm, openTab: 0);
        win.ShowDialog(this);
    }

    /// <summary>
    /// Prompt for a tag name and bulk-add to selected assets.
    /// </summary>
    private async void BulkAddTagMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.ActiveProfile is null) return;
        if (vm.AssetBrowser.SelectedThumbnails.Count == 0)
        {
            vm.StatusMessage = "⚠️ Select assets first";
            return;
        }

        var dialog = new Window
        {
            Title = "Add Tag to Selected",
            Width = 350, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Black
        };

        var input = new TextBox { PlaceholderText = "Tag name...", Margin = new Thickness(16) };
        var btn = new Button { Content = "Add Tag", Margin = new Thickness(16, 0) };
        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(new TextBlock { Text = $"Add tag to {vm.AssetBrowser.SelectedThumbnails.Count} asset(s):", Foreground = Avalonia.Media.Brushes.White, Margin = new Thickness(16, 16, 16, 4) });
        stack.Children.Add(input);
        stack.Children.Add(btn);
        dialog.Content = stack;

        string? tagName = null;
        btn.Click += (_, _) => { tagName = input.Text; dialog.Close(); };
        await dialog.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(tagName))
        {
            vm.AssetBrowser.BulkAddTagCommand.Execute(tagName);
            vm.StatusMessage = $"✅ Tag \"{tagName}\" added to {vm.AssetBrowser.SelectedThumbnails.Count} assets";
        }
    }

    /// <summary>
    /// Prompt for a tag name and bulk-remove from selected assets.
    /// </summary>
    private async void BulkRemoveTagMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.ActiveProfile is null) return;
        if (vm.AssetBrowser.SelectedThumbnails.Count == 0)
        {
            vm.StatusMessage = "⚠️ Select assets first";
            return;
        }

        var dialog = new Window
        {
            Title = "Remove Tag from Selected",
            Width = 350, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Black
        };

        var input = new TextBox { PlaceholderText = "Tag name...", Margin = new Thickness(16) };
        var btn = new Button { Content = "Remove Tag", Margin = new Thickness(16, 0) };
        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(new TextBlock { Text = $"Remove tag from {vm.AssetBrowser.SelectedThumbnails.Count} asset(s):", Foreground = Avalonia.Media.Brushes.White, Margin = new Thickness(16, 16, 16, 4) });
        stack.Children.Add(input);
        stack.Children.Add(btn);
        dialog.Content = stack;

        string? tagName = null;
        btn.Click += (_, _) => { tagName = input.Text; dialog.Close(); };
        await dialog.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(tagName))
        {
            vm.AssetBrowser.BulkRemoveTagCommand.Execute(tagName);
            vm.StatusMessage = $"✅ Tag \"{tagName}\" removed from {vm.AssetBrowser.SelectedThumbnails.Count} assets";
        }
    }

    // ── Script Analyzer ──────────────────────────────────────

    private GumpForge.ScriptAnalysis.ScriptIndexer? _scriptIndexer;
    private List<GumpForge.ScriptAnalysis.IndexedScript> _indexedScripts = [];
    private GumpForge.ScriptAnalysis.RoslynGumpAnalyzer _gumpAnalyzer = new();

    /// <summary>
    /// Opens a folder picker for the server's Scripts directory and scans for gump scripts.
    /// </summary>
    private async void OpenScriptsFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Server Scripts Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var path = folders[0].Path.LocalPath;
        var statusLabel = this.FindControl<TextBlock>("ScriptScanStatus");
        var scriptList = this.FindControl<ListBox>("ScriptFileList");

        if (statusLabel is not null) statusLabel.Text = "Scanning...";

        _scriptIndexer = new GumpForge.ScriptAnalysis.ScriptIndexer();

        var progress = new Progress<(int scanned, int found)>(p =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (statusLabel is not null)
                    statusLabel.Text = $"Scanned {p.scanned} files, found {p.found} gump scripts...";
            });
        });

        _indexedScripts = await _scriptIndexer.ScanDirectoryAsync(path, progress);

        if (statusLabel is not null)
            statusLabel.Text = $"✅ Found {_indexedScripts.Count} gump scripts in {Path.GetFileName(path)}";

        // Update tab badge
        var badge = this.FindControl<Border>("ScriptCountBadge");
        var label = this.FindControl<TextBlock>("ScriptCountLabel");
        if (badge is not null && label is not null)
        {
            badge.IsVisible = _indexedScripts.Count > 0;
            label.Text = _indexedScripts.Count.ToString();
        }

        // Populate the list
        if (scriptList is not null)
        {
            scriptList.ItemsSource = _indexedScripts.Select(s => s.DisplayName).ToList();
        }

        // Store scripts path on profile
        if (DataContext is MainWindowViewModel vm && vm.ActiveProfile is not null)
        {
            // Could store this path on profile for re-use
        }
    }

    /// <summary>
    /// When a script file is selected, run Roslyn analysis and display the results.
    /// </summary>
    private void ScriptFile_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedIndex < 0) return;
        if (lb.SelectedIndex >= _indexedScripts.Count) return;

        var script = _indexedScripts[lb.SelectedIndex];
        var header = this.FindControl<TextBlock>("ScriptAnalysisHeader");
        var panel = this.FindControl<StackPanel>("ScriptAnalysisPanel");

        if (header is null || panel is null) return;

        header.Text = $"Analyzing: {script.FileName}";
        panel.Children.Clear();

        try
        {
            var gumps = _gumpAnalyzer.AnalyzeFile(script.FilePath);

            if (gumps.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "No gump classes found in this file.\nThis file may reference gump APIs without defining a class.",
                    Foreground = Avalonia.Media.Brushes.Gray,
                    FontSize = 11,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
                return;
            }

            foreach (var gump in gumps)
            {
                BuildGumpAnalysisUI(panel, gump, script);
            }

            header.Text = $"📜 {script.FileName} — {gumps.Count} gump class(es)";
        }
        catch (Exception ex)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Analysis error: {ex.Message}",
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f66")),
                FontSize = 11,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
    }

    /// <summary>
    /// Builds the analysis detail UI for a single discovered gump class.
    /// </summary>
    private void BuildGumpAnalysisUI(StackPanel parent, GumpForge.ScriptAnalysis.DiscoveredGump gump, GumpForge.ScriptAnalysis.IndexedScript script)
    {
        var inputControls = new Dictionary<string, Control>();

        // ── Class header ──
        var classHeader = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#16213e")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 0, 0, 4)
        };
        var headerStack = new StackPanel { Spacing = 2 };
        headerStack.Children.Add(new TextBlock
        {
            Text = $"class {gump.ClassName} : {gump.BaseClass}",
            FontSize = 14, FontWeight = Avalonia.Media.FontWeight.Bold,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e94560"))
        });
        if (!string.IsNullOrEmpty(gump.Namespace))
        {
            headerStack.Children.Add(new TextBlock
            {
                Text = $"namespace {gump.Namespace}",
                FontSize = 10, Foreground = Avalonia.Media.Brushes.Gray
            });
        }
        headerStack.Children.Add(new TextBlock
        {
            Text = $"{gump.ElementCount} gump elements | {gump.Conditionals.Count} conditionals | {gump.Variables.Count} parameters",
            FontSize = 10, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5bc0de"))
        });
        classHeader.Child = headerStack;
        parent.Children.Add(classHeader);

        // ── Test Variables ──
        if (gump.Variables.Count > 0)
        {
            AddSectionHeader(parent, "🔧 Test Variables (Constructor Parameters)");
            foreach (var v in gump.Variables)
            {
                var varPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
                varPanel.Children.Add(new TextBlock
                {
                    Text = v.TypeName,
                    FontSize = 10,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5bc0de")),
                    FontFamily = new Avalonia.Media.FontFamily("Cascadia Code,Consolas,monospace")
                });
                varPanel.Children.Add(new TextBlock
                {
                    Text = v.Name,
                    FontSize = 10,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#aad6a5")),
                    FontFamily = new Avalonia.Media.FontFamily("Cascadia Code,Consolas,monospace")
                });

                // Auto-populate with active Player Context values instead of "(mock)"
                string defaultValue = v.DefaultValue;
                if (string.IsNullOrEmpty(defaultValue) || defaultValue == "(mock)")
                {
                    var mainVm = this.DataContext as GumpForge.App.ViewModels.MainWindowViewModel;
                    if (mainVm?.SelectedPlayerContext != null)
                    {
                        var tName = v.TypeName.ToLowerInvariant();
                        var pName = v.Name.ToLowerInvariant();

                        if (tName.Contains("mobile") || tName.Contains("player") || pName == "from" || pName == "owner")
                        {
                            defaultValue = mainVm.SelectedPlayerContext.Name;
                        }
                        else if (tName.Contains("item") || pName.Contains("item"))
                        {
                            var itemVar = mainVm.SelectedPlayerContext.Variables.FirstOrDefault(x => x.Name.Equals("ItemName", StringComparison.OrdinalIgnoreCase));
                            defaultValue = itemVar != null ? itemVar.Value : "Item";
                        }
                    }
                }

                // Test input control
                switch (v.Kind)
                {
                    case GumpForge.ScriptAnalysis.VariableKind.Boolean:
                        var cb = new CheckBox
                        {
                            Content = string.Format("= {0}", defaultValue),
                            IsChecked = defaultValue.ToLower() == "true",
                            FontSize = 10,
                            Foreground = Avalonia.Media.Brushes.Gray
                        };
                        varPanel.Children.Add(cb);
                        inputControls[v.Name] = cb;
                        break;

                    case GumpForge.ScriptAnalysis.VariableKind.Integer:
                        if (v.Name.Equals("serial", StringComparison.OrdinalIgnoreCase) && (defaultValue == "0" || defaultValue == ""))
                        {
                            var mainVm = this.DataContext as GumpForge.App.ViewModels.MainWindowViewModel;
                            if (mainVm?.SelectedPlayerContext != null)
                            {
                                defaultValue = mainVm.SelectedPlayerContext.Serial.ToString();
                            }
                        }
                        var numBox = new TextBox
                        {
                            Text = defaultValue,
                            Width = 60, FontSize = 10,
                            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0a0a1a")),
                            Foreground = Avalonia.Media.Brushes.White,
                            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#333"))
                        };
                        varPanel.Children.Add(numBox);
                        inputControls[v.Name] = numBox;
                        break;

                    default:
                        var txtBox = new TextBox
                        {
                            Text = defaultValue,
                            Width = 120, FontSize = 10,
                            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0a0a1a")),
                            Foreground = Avalonia.Media.Brushes.White,
                            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#333"))
                        };
                        varPanel.Children.Add(txtBox);
                        inputControls[v.Name] = txtBox;
                        break;
                }

                parent.Children.Add(varPanel);
            }
        }

        // ── Gump API Calls ──
        if (gump.GumpCalls.Count > 0)
        {
            AddSectionHeader(parent, $"📐 Gump Elements ({gump.GumpCalls.Count})");
            foreach (var call in gump.GumpCalls.Take(50)) // Cap at 50 for UI performance
            {
                var callPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };

                // Line number
                callPanel.Children.Add(new TextBlock
                {
                    Text = $"L{call.LineNumber}",
                    FontSize = 9, Width = 35,
                    Foreground = Avalonia.Media.Brushes.Gray,
                    FontFamily = new Avalonia.Media.FontFamily("Cascadia Code,Consolas,monospace")
                });

                // Method name
                callPanel.Children.Add(new TextBlock
                {
                    Text = call.MethodName,
                    FontSize = 10,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e94560")),
                    FontFamily = new Avalonia.Media.FontFamily("Cascadia Code,Consolas,monospace")
                });

                // Arguments
                callPanel.Children.Add(new TextBlock
                {
                    Text = $"({string.Join(", ", call.Arguments)})",
                    FontSize = 10,
                    Foreground = Avalonia.Media.Brushes.White,
                    FontFamily = new Avalonia.Media.FontFamily("Cascadia Code,Consolas,monospace"),
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    MaxWidth = 400
                });

                // Condition badge
                if (call.ConditionExpression is not null)
                {
                    callPanel.Children.Add(new Border
                    {
                        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3a2a1a")),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(4, 1),
                        Margin = new Thickness(4, 0, 0, 0),
                        Child = new TextBlock
                        {
                            Text = $"if {call.ConditionExpression}",
                            FontSize = 8,
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e9a045")),
                            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                            MaxWidth = 200
                        }
                    });
                }

                // Dynamic args badge
                if (call.HasDynamicArgs)
                {
                    callPanel.Children.Add(new Border
                    {
                        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2a1a3a")),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(4, 1),
                        Child = new TextBlock
                        {
                            Text = "⚡ dynamic",
                            FontSize = 8,
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#a5c8d6"))
                        }
                    });
                }

                parent.Children.Add(callPanel);
            }

            if (gump.GumpCalls.Count > 50)
            {
                parent.Children.Add(new TextBlock
                {
                    Text = $"... and {gump.GumpCalls.Count - 50} more elements",
                    FontSize = 10, Foreground = Avalonia.Media.Brushes.Gray
                });
            }
        }

        // ── Conditional Branches ──
        if (gump.Conditionals.Count > 0)
        {
            AddSectionHeader(parent, $"🔀 Conditional Branches ({gump.Conditionals.Count})");
            foreach (var cond in gump.Conditionals)
            {
                var condPanel = new Border
                {
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1a1a2e")),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 4),
                    Margin = new Thickness(0, 1)
                };
                var condStack = new StackPanel { Spacing = 2 };
                condStack.Children.Add(new TextBlock
                {
                    Text = $"if ({cond.Condition})",
                    FontSize = 10,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e9a045")),
                    FontFamily = new Avalonia.Media.FontFamily("Cascadia Code,Consolas,monospace"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
                condStack.Children.Add(new TextBlock
                {
                    Text = $"  ✓ true: {cond.TrueBranch.Count} elements  |  ✗ false: {cond.FalseBranch.Count} elements",
                    FontSize = 9, Foreground = Avalonia.Media.Brushes.Gray
                });
                condPanel.Child = condStack;
                parent.Children.Add(condPanel);
            }
        }

        // ── Referenced Files ──
        if (gump.ReferencedFiles.Count > 0)
        {
            AddSectionHeader(parent, $"🔗 Referenced Files ({gump.ReferencedFiles.Count})");
            foreach (var refFile in gump.ReferencedFiles.Distinct().Take(20))
            {
                parent.Children.Add(new TextBlock
                {
                    Text = $"  → {refFile}",
                    FontSize = 10,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#a5c8d6")),
                    FontFamily = new Avalonia.Media.FontFamily("Cascadia Code,Consolas,monospace")
                });
            }
        }

        // ── Render Button ──
        var renderBtn = new Button
        {
            Content = "▶ Render Gump to Canvas",
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e94560")),
            Foreground = Avalonia.Media.Brushes.White,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        renderBtn.Click += async (s, ev) =>
        {
            var context = new Dictionary<string, string>();
            foreach (var kvp in inputControls)
            {
                if (kvp.Value is CheckBox cb)
                {
                    context[kvp.Key] = (cb.IsChecked == true) ? "true" : "false";
                }
                else if (kvp.Value is TextBox tb)
                {
                    context[kvp.Key] = tb.Text ?? "";
                }
            }

            try
            {
                var sourceCode = await File.ReadAllTextAsync(script.FilePath);
                var parser = new GumpForge.Parsers.ServUoParser();
                
                var mainVm = this.DataContext as GumpForge.App.ViewModels.MainWindowViewModel;
                if (mainVm?.SelectedPlayerContext != null)
                {
                    // Look up AccessLevel and compute IsStaff
                    var accessLevelVar = mainVm.SelectedPlayerContext.Variables.FirstOrDefault(x => x.Name.Equals("AccessLevel", StringComparison.OrdinalIgnoreCase));
                    string accessLevel = accessLevelVar != null ? accessLevelVar.Value : "Player";
                    bool isStaff = !accessLevel.Equals("Player", StringComparison.OrdinalIgnoreCase);

                    // Copy flat context properties
                    parser.EvaluationContext["Name"] = mainVm.SelectedPlayerContext.Name;
                    parser.EvaluationContext["Serial"] = mainVm.SelectedPlayerContext.Serial.ToString();
                    parser.EvaluationContext["AccessLevel"] = accessLevel;
                    parser.EvaluationContext["IsStaff"] = isStaff.ToString().ToLower();

                    foreach (var v in mainVm.SelectedPlayerContext.Variables)
                    {
                        parser.EvaluationContext[v.Name] = v.Value;
                    }

                    // Dynamically map properties for any Mobile/Player parameters
                    var mobileParameters = gump.Variables
                        .Where(v => v.TypeName.Contains("Mobile") || v.TypeName.Contains("Player") || v.Name.Equals("from", StringComparison.OrdinalIgnoreCase) || v.Name.Equals("owner", StringComparison.OrdinalIgnoreCase))
                        .Select(v => v.Name)
                        .Concat(new[] { "from", "owner", "m_From", "m_Owner" })
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    foreach (var prefix in mobileParameters)
                    {
                        parser.EvaluationContext[prefix] = mainVm.SelectedPlayerContext.Name;
                        parser.EvaluationContext[prefix + ".Name"] = mainVm.SelectedPlayerContext.Name;
                        parser.EvaluationContext[prefix + ".Serial"] = mainVm.SelectedPlayerContext.Serial.ToString();
                        parser.EvaluationContext[prefix + ".AccessLevel"] = accessLevel;
                        parser.EvaluationContext[prefix + ".IsStaff"] = isStaff.ToString().ToLower();

                        foreach (var v in mainVm.SelectedPlayerContext.Variables)
                        {
                            parser.EvaluationContext[prefix + "." + v.Name] = v.Value;
                        }
                    }

                    // Also support Item parameters
                    var itemParameters = gump.Variables
                        .Where(v => v.TypeName.Contains("Item") || v.Name.Contains("item"))
                        .Select(v => v.Name)
                        .Concat(new[] { "item", "target", "m_Item" })
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    var itemVar = mainVm.SelectedPlayerContext.Variables.FirstOrDefault(x => x.Name.Equals("ItemName", StringComparison.OrdinalIgnoreCase));
                    string itemName = itemVar != null ? itemVar.Value : "Item";
                    var itemIdVar = mainVm.SelectedPlayerContext.Variables.FirstOrDefault(x => x.Name.Equals("ItemID", StringComparison.OrdinalIgnoreCase));
                    string itemId = itemIdVar != null ? itemIdVar.Value : "0";

                    foreach (var prefix in itemParameters)
                    {
                        parser.EvaluationContext[prefix] = itemName;
                        parser.EvaluationContext[prefix + ".Name"] = itemName;
                        parser.EvaluationContext[prefix + ".ItemID"] = itemId;
                        parser.EvaluationContext[prefix + ".Serial"] = itemId;

                        foreach (var v in mainVm.SelectedPlayerContext.Variables)
                        {
                            parser.EvaluationContext[prefix + "." + v.Name] = v.Value;
                        }
                    }
                }

                foreach (var kvp in context)
                {
                    parser.EvaluationContext[kvp.Key] = kvp.Value;
                }

                var result = parser.Parse(sourceCode);
                if (result.Document != null && mainVm != null)
                {
                    mainVm.Document = result.Document;
                    mainVm.Document.PropertyChanged += (_, _) => mainVm.RegenerateCode();
                    mainVm.UndoStack.Clear();
                    mainVm.Selection.ClearSelection();
                    mainVm.ActivePage = 0;
                    mainVm.Canvas.Document = mainVm.Document;
                    mainVm.Canvas.ActivePage = 0;
                    mainVm.Layers.Document = mainVm.Document;
                    mainVm.CodePanel.Document = mainVm.Document;

                    mainVm.ForceRepaintCanvas();
                    mainVm.RegenerateCode();
                    mainVm.LogSim(string.Format("Successfully compiled and rendered '{0}' to canvas.", gump.ClassName));
                }
                else if (result.Errors.Count > 0)
                {
                    var errs = string.Join("\n", result.Errors.Select(e => string.Format("Line {0}: {1}", e.Line, e.Message)));
                    mainVm?.LogSim(string.Format("Render error:\n{0}", errs));
                }
            }
            catch (Exception ex)
            {
                var mainVm = this.DataContext as GumpForge.App.ViewModels.MainWindowViewModel;
                mainVm?.LogSim(string.Format("Render error: {0}", ex.Message));
            }
        };
        parent.Children.Add(renderBtn);
    }

    private static void AddSectionHeader(StackPanel parent, string text)
    {
        parent.Children.Add(new Border
        {
            Height = 1,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#333")),
            Margin = new Thickness(0, 4)
        });
        parent.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e94560")),
            Margin = new Thickness(0, 2, 0, 4)
        });
    }
}