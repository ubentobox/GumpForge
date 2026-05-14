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
}