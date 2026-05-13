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

        try
        {
            // Initialize TextMate with Dark+ theme for VS Code-like highlighting
            var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
            _textMateInstallation = editor.InstallTextMate(registryOptions);

            // Set C# grammar for syntax highlighting
            var csharpLanguage = registryOptions.GetLanguageByExtension(".cs");
            if (csharpLanguage is not null)
            {
                string scopeName = registryOptions.GetScopeByLanguageId(csharpLanguage.Id);
                _textMateInstallation.SetGrammar(scopeName);
            }
        }
        catch
        {
            // TextMate setup failed — editor still works, just no highlighting
        }

        // Style the line number margin
        editor.Foreground = Avalonia.Media.Brushes.White;
        editor.LineNumbersForeground = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#555"));

        // Sync ViewModel → Editor when GeneratedCode changes
        if (DataContext is MainWindowViewModel vm)
        {
            // Initial sync
            editor.Text = vm.GeneratedCode ?? string.Empty;

            // Listen for ViewModel code changes
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.GeneratedCode) && !_isUpdatingEditor)
                {
                    _isUpdatingEditor = true;
                    editor.Text = vm.GeneratedCode ?? string.Empty;
                    _isUpdatingEditor = false;
                }
            };

            // Listen for editor text changes → ViewModel
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

        // Don't process shortcuts if a TextBox is focused
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;

        switch (e.Key)
        {
            // F5 = export PNG
            case Key.F5:
                _ = ExportCanvasAsPng(vm);
                e.Handled = true;
                break;
            // F6 = export to MUL
            case Key.F6:
                vm.ExportToMulCommand.Execute(null);
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