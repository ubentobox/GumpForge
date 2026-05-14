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
        Loaded += (_, _) => InitializeCodeEditor();
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
}