using System.Collections.ObjectModel;
using GumpForge.Core.Commands;
using GumpForge.Core.Models;
using GumpForge.Core.Services;
using GumpForge.App.Services;
using GumpForge.Generators;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GumpForge.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // Document
    [ObservableProperty] private GumpDocument _document = new();
    [ObservableProperty] private string _title = "GumpForge — Gump Editor";
    [ObservableProperty] private int _activePage;

    // Services
    public UndoStack UndoStack { get; } = new();
    public SelectionManager Selection { get; } = new();
    public AssetCache Cache { get; } = new();
    private AssetLoadingService? _assetLoader;

    // Panels
    public AssetBrowserViewModel AssetBrowser { get; }
    public CanvasViewModel Canvas { get; }
    public LayersViewModel Layers { get; }
    public PropertiesViewModel Properties { get; }
    public CodePanelViewModel CodePanel { get; }

    // Code generators
    private readonly List<IGumpCodeGenerator> _generators =
    [
        new ServUoGenerator(),
        new RunUoGenerator(),
        new ModernUoGenerator(),
        new SphereGenerator(),
        new ClassicAssistGenerator()
    ];

    // Generated code per tab
    [ObservableProperty] private string _generatedCode = string.Empty;
    [ObservableProperty] private string _runUoCode = string.Empty;
    [ObservableProperty] private string _modernUoCode = string.Empty;
    [ObservableProperty] private string _sphereCode = string.Empty;
    [ObservableProperty] private string _classicAssistCode = string.Empty;
    [ObservableProperty] private int _activeCodeTab;

    // Validation problems
    public ObservableCollection<GumpProblem> Problems { get; } = [];
    [ObservableProperty] private int _problemCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _errorCount;

    // Client data path
    [ObservableProperty] private string? _clientDataPath;
    [ObservableProperty] private string _statusMessage = "Ready";

    // Active shard profile
    [ObservableProperty] private ShardProfile? _activeProfile;

    public MainWindowViewModel()
    {
        AssetBrowser = new AssetBrowserViewModel();
        AssetBrowser.OnPlaceAsset = AddGumpFromAsset;
        Canvas = new CanvasViewModel(Document, Selection, UndoStack);
        Layers = new LayersViewModel(Document, Selection);
        Properties = new PropertiesViewModel(Selection, UndoStack);
        CodePanel = new CodePanelViewModel(_generators, Document);

        // Regenerate code whenever the document changes
        Document.PropertyChanged += (_, _) => RegenerateCode();

        RegenerateCode();

        // Auto-load assets from known data paths
        _ = TryAutoLoadAssetsAsync();
    }

    partial void OnActivePageChanged(int value)
    {
        Canvas.ActivePage = value;
    }

    /// <summary>
    /// Apply a loaded ShardProfile to the editor — preferences, client data path, etc.
    /// Called after construction when a profile is selected.
    /// </summary>
    public void ApplyProfile(ShardProfile profile)
    {
        ActiveProfile = profile;

        // Apply editor preferences
        var prefs = profile.Preferences;
        Canvas.GridSize = prefs.GridSize;
        Canvas.ShowGrid = prefs.GridVisible;
        Canvas.SnapToGrid = true; // Snap is on, resolution is stored in profile
        Canvas.ShowRulers = prefs.ShowRulers;

        // Apply default canvas size to new documents
        Document.CanvasWidth = prefs.DefaultCanvasWidth;
        Document.CanvasHeight = prefs.DefaultCanvasHeight;

        // Load client data if path is set
        if (!string.IsNullOrEmpty(profile.ClientDataPath) && Directory.Exists(profile.ClientDataPath))
        {
            ClientDataPath = profile.ClientDataPath;
            _ = LoadAssetsFromPathAsync(profile.ClientDataPath);
        }

        Title = $"GumpForge — {profile.ProfileName}";
        StatusMessage = $"Profile loaded: {profile.ProfileName}";

        // Pass profile to asset browser for metadata search
        AssetBrowser.Profile = profile;
    }

    private async Task TryAutoLoadAssetsAsync()
    {
        // Search common locations for UO client data
        string[] searchPaths =
        [
            // Relative to where the editor is run from (workspace)
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Ultima-Adventures", "Client", "Data Files"),
            // Direct workspace path
            @"E:\Projects\gump-editor\Ultima-Adventures\Client\Data Files",
            // Other common locations
            @"C:\Ultima-Adventures\Client\Data Files",
            @"D:\Ultima Online\Ultima Adventures\Data Files",
        ];

        foreach (var path in searchPaths)
        {
            var resolved = Path.GetFullPath(path);
            if (Directory.Exists(resolved))
            {
                var indexFile = Directory.GetFiles(resolved)
                    .FirstOrDefault(f => Path.GetFileName(f).Equals("Gumpidx.mul", StringComparison.OrdinalIgnoreCase));
                if (indexFile is not null)
                {
                    ClientDataPath = resolved;
                    await LoadAssetsFromPathAsync(resolved);
                    return;
                }
            }
        }
    }

    [RelayCommand]
    private async Task LoadAssetsFromPathAsync(string path)
    {
        // Initialize the singleton AssetManager for canvas rendering
        var mgr = AssetManager.Instance;
        mgr.LoadFromFolder(path);

        // Load thumbnails into the asset browser
        _assetLoader = new AssetLoadingService(AssetBrowser, Cache);
        await _assetLoader.LoadFromClientFolderAsync(path);

        var format = mgr.DataFormat ?? "??";
        Title = $"GumpForge — Gump Editor ({AssetBrowser.TotalAssets} assets via {format})";
        StatusMessage = $"✅ Loaded {AssetBrowser.TotalAssets} assets from {format} files"
            + (mgr.HasHues ? $" | {mgr.HueCount} hues" : "")
            + (mgr.HasCliloc ? $" | {mgr.ClilocCount} cliloc" : "")
            + (mgr.HasFonts ? " | fonts" : "");

        // Auto-tag assets if a profile is active
        if (ActiveProfile is not null)
        {
            AutoTagger.TagAssets(ActiveProfile, path);
            ApplyMetadataToThumbnails();
        }
    }

    /// <summary>
    /// Copies display names and tags from the active profile metadata onto thumbnails.
    /// </summary>
    private void ApplyMetadataToThumbnails()
    {
        if (ActiveProfile is null) return;

        foreach (var thumb in AssetBrowser.AllThumbnails)
        {
            if (ActiveProfile.AssetMetadata.TryGetValue(thumb.GumpId, out var meta))
            {
                thumb.DisplayName = meta.DisplayName;
                thumb.Tags = [..meta.Tags, ..meta.AutoTags];
            }
        }
    }

    [RelayCommand]
    private void NewDocument()
    {
        Document = new GumpDocument();
        UndoStack.Clear();
        Selection.ClearSelection();
        Canvas.Document = Document;
        Layers.Document = Document;
        CodePanel.Document = Document;
        RegenerateCode();
        Title = "GumpForge — Untitled";
    }

    /// <summary>
    /// Available gump templates for "New from Template".
    /// </summary>
    public List<GumpTemplate> Templates { get; } = GumpTemplateLibrary.GetTemplates();

    [RelayCommand]
    private void NewFromTemplate(GumpTemplate template)
    {
        if (template?.CreateDocument is null) return;

        Document = template.CreateDocument();
        Document.PropertyChanged += (_, _) => RegenerateCode();
        UndoStack.Clear();
        Selection.ClearSelection();
        Canvas.Document = Document;
        Layers.Document = Document;
        CodePanel.Document = Document;
        RegenerateCode();
        Title = $"GumpForge — {template.Name}";
        StatusMessage = $"Created from template: {template.Name}";
    }

    [RelayCommand]
    private async Task SaveDocument()
    {
        if (string.IsNullOrEmpty(Document.FilePath))
        {
            await SaveDocumentAs();
            return;
        }
        await GumpForge.Core.Serialization.ProjectSerializer.SaveAsync(Document, Document.FilePath);

        // Also save the active profile if there is one
        if (ActiveProfile is not null)
            await GumpForge.Core.Serialization.ProfileSerializer.SaveAsync(ActiveProfile);

        Title = $"GumpForge — {Path.GetFileNameWithoutExtension(Document.FilePath)}";
    }

    [RelayCommand]
    private async Task SaveDocumentAs()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save Gump Project",
                DefaultExtension = "gumpproj",
                FileTypeChoices =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Gump Project") { Patterns = ["*.gumpproj"] },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = ["*"] }
                ],
                SuggestedFileName = Document.GumpClassName
            });

        if (file is not null)
        {
            var path = file.Path.LocalPath;
            await GumpForge.Core.Serialization.ProjectSerializer.SaveAsync(Document, path);
            Title = $"GumpForge — {Path.GetFileNameWithoutExtension(path)}";
        }
    }

    [RelayCommand]
    private async Task OpenDocument()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Open Gump Project",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Gump Project") { Patterns = ["*.gumpproj"] },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = ["*"] }
                ]
            });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            var doc = await GumpForge.Core.Serialization.ProjectSerializer.LoadAsync(path);
            Document = doc;
            UndoStack.Clear();
            Selection.ClearSelection();
            Canvas.Document = Document;
            Layers.Document = Document;
            CodePanel.Document = Document;
            RegenerateCode();
            Title = $"GumpForge — {Path.GetFileNameWithoutExtension(path)}";
        }
    }

    [RelayCommand]
    private async Task OpenClientFolder()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Select UO Client Data Folder",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            ClientDataPath = path;
            await LoadAssetsFromPathAsync(path);
        }
    }

    [RelayCommand]
    private void ImportFromCode()
    {
        var parser = new GumpForge.Parsers.ServUoParser();
        var code = CodePanel.IsEditMode ? CodePanel.EditText : GeneratedCode;
        if (string.IsNullOrWhiteSpace(code)) return;

        var result = parser.Parse(code);
        if (result.Document is not null)
        {
            Document = result.Document;
            Document.PropertyChanged += (_, _) => RegenerateCode();
            UndoStack.Clear();
            Selection.ClearSelection();
            ActivePage = 0;
            Canvas.Document = Document;
            Canvas.ActivePage = 0;
            Canvas.Zoom = 1.0;
            Layers.Document = Document;
            CodePanel.Document = Document;
            OnPropertyChanged(nameof(Document));
            RegenerateCode();

            int totalElements = Document.GetAllElements().Count();
            Title = $"GumpForge — {Document.GumpClassName} ({totalElements} elements, imported)";
            CodePanel.ParseErrors = string.Empty;
        }
    }

    /// <summary>
    /// Export the current canvas content to the loaded MUL files at a specified gump ID.
    /// This writes the gump art as RLE-compressed ARGB1555 data.
    /// </summary>
    [RelayCommand]
    private void ExportToMul()
    {
        var mgr = AssetManager.Instance;
        if (!mgr.IsLoaded)
        {
            StatusMessage = "⚠ Load client data first (File > Open Client Folder)";
            return;
        }

        // For now, use the first background element's gump ID as the target,
        // or allow the user to specify via a property
        var elements = Document.GetAllElements().ToList();
        if (elements.Count == 0)
        {
            StatusMessage = "⚠ No elements to export";
            return;
        }

        // Use a custom gump ID range starting from 50000 (above standard UO range)
        int targetId = 50000;

        // Try to find a background or image element to use its ID
        var firstBg = elements.OfType<GumpForge.Core.Models.GumpBackground>().FirstOrDefault();
        var firstImg = elements.OfType<GumpForge.Core.Models.GumpImage>().FirstOrDefault();
        if (firstBg is not null) targetId = firstBg.GumpId;
        else if (firstImg is not null) targetId = firstImg.GumpId;

        try
        {
            // Render canvas to pixel data
            int w = Document.CanvasWidth;
            int h = Document.CanvasHeight;
            byte[] pixels = RenderDocumentToPixels(w, h);

            mgr.SaveGump(targetId, pixels, w, h);
            StatusMessage = $"✅ Exported to MUL as gump 0x{targetId:X4} ({w}×{h})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"⛔ Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Render the current document to raw RGBA8888 pixel data (for MUL export).
    /// </summary>
    private byte[] RenderDocumentToPixels(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        var mgr = AssetManager.Instance;

        foreach (var element in Document.GetAllElements())
        {
            if (!element.IsVisible) continue;

            // Get the bitmap for this element
            Avalonia.Media.Imaging.WriteableBitmap? bitmap = null;
            switch (element)
            {
                case GumpForge.Core.Models.GumpBackground bg:
                    bitmap = mgr.GetBitmap(bg.GumpId);
                    break;
                case GumpForge.Core.Models.GumpImage img:
                    bitmap = img.Hue > 0 ? mgr.GetHuedBitmap(img.GumpId, img.Hue) : mgr.GetBitmap(img.GumpId);
                    break;
                case GumpForge.Core.Models.GumpButton btn:
                    bitmap = mgr.GetBitmap(btn.NormalId);
                    break;
                case GumpForge.Core.Models.GumpCheck chk:
                    bitmap = mgr.GetBitmap(chk.InactiveId);
                    break;
                case GumpForge.Core.Models.GumpRadio radio:
                    bitmap = mgr.GetBitmap(radio.InactiveId);
                    break;
            }

            if (bitmap is null) continue;

            // Composite the bitmap onto the output pixels
            using var fb = bitmap.Lock();
            int srcW = (int)bitmap.Size.Width;
            int srcH = (int)bitmap.Size.Height;
            int dstX = element.X;
            int dstY = element.Y;

            unsafe
            {
                byte* srcPtr = (byte*)fb.Address;
                int srcStride = fb.RowBytes;

                for (int sy = 0; sy < srcH; sy++)
                {
                    int dy = dstY + sy;
                    if (dy < 0 || dy >= height) continue;

                    for (int sx = 0; sx < srcW; sx++)
                    {
                        int dx = dstX + sx;
                        if (dx < 0 || dx >= width) continue;

                        int srcIdx = sy * srcStride + sx * 4;
                        byte sb = srcPtr[srcIdx];
                        byte sg = srcPtr[srcIdx + 1];
                        byte sr = srcPtr[srcIdx + 2];
                        byte sa = srcPtr[srcIdx + 3];

                        if (sa == 0) continue;

                        int dstIdx = (dy * width + dx) * 4;

                        if (sa == 255)
                        {
                            // BGRA source → RGBA output
                            pixels[dstIdx] = sr;
                            pixels[dstIdx + 1] = sg;
                            pixels[dstIdx + 2] = sb;
                            pixels[dstIdx + 3] = 255;
                        }
                        else
                        {
                            // Alpha blend
                            float a = sa / 255f;
                            pixels[dstIdx] = (byte)(sr * a + pixels[dstIdx] * (1 - a));
                            pixels[dstIdx + 1] = (byte)(sg * a + pixels[dstIdx + 1] * (1 - a));
                            pixels[dstIdx + 2] = (byte)(sb * a + pixels[dstIdx + 2] * (1 - a));
                            pixels[dstIdx + 3] = (byte)Math.Min(255, sa + pixels[dstIdx + 3]);
                        }
                    }
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// Import a custom PNG image as a gump art entry in the MUL files.
    /// User selects a PNG file and provides a target gump ID.
    /// </summary>
    [RelayCommand]
    private async Task ImportCustomAsset()
    {
        var mgr = AssetManager.Instance;
        if (!mgr.IsLoaded)
        {
            StatusMessage = "⚠ Load client data first (File > Open Client Folder)";
            return;
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel is null) return;

        // Open file picker for PNG images
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Import Custom Gump Art (PNG)",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("PNG Images") { Patterns = ["*.png"] },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Images") { Patterns = ["*.png", "*.bmp", "*.jpg"] }
                ]
            });

        if (files.Count == 0) return;

        try
        {
            var filePath = files[0].Path.LocalPath;

            // Load the PNG and convert to RGBA8888
            using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(filePath);
            int w = image.Width;
            int h = image.Height;
            byte[] pixels = new byte[w * h * 4];

            image.CopyPixelDataTo(pixels);

            // Use a default target ID in the custom range (50000+)
            // Find the next available ID starting from 50000
            int targetId = 50000;
            while (mgr.HasGump(targetId) && targetId < 65535)
                targetId++;

            mgr.SaveGump(targetId, pixels, w, h);

            StatusMessage = $"✅ Imported '{Path.GetFileName(filePath)}' as gump 0x{targetId:X4} ({w}×{h})";

            // Refresh asset browser if the path is loaded
            if (ClientDataPath is not null)
            {
                await LoadAssetsFromPathAsync(ClientDataPath);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"⛔ Import failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Undo()
    {
        UndoStack.Undo();
        RegenerateCode();
    }

    [RelayCommand]
    private void Redo()
    {
        UndoStack.Redo();
        RegenerateCode();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (!Selection.HasSelection) return;
        var page = Document.Pages.FirstOrDefault(p => p.PageNumber == ActivePage);
        if (page is null) return;

        var commands = Selection.SelectedElements
            .Select(e => new RemoveElementCommand(page, e))
            .Cast<IEditCommand>()
            .ToList();

        if (commands.Count > 0)
        {
            UndoStack.Execute(new BatchCommand(commands, "Delete elements"));
            Selection.ClearSelection();
            RegenerateCode();
        }
    }

    // ═══════════ CLIPBOARD ═══════════
    private List<GumpElement> _clipboard = [];

    [RelayCommand]
    private void CopySelected()
    {
        if (!Selection.HasSelection) return;
        _clipboard = Selection.SelectedElements
            .Select(e => e.Clone())
            .ToList();
    }

    [RelayCommand]
    private void CutSelected()
    {
        CopySelected();
        DeleteSelected();
    }

    [RelayCommand]
    private void PasteElements()
    {
        if (_clipboard.Count == 0) return;
        var page = Document.GetOrCreatePage(ActivePage);

        // Offset pasted elements slightly so they don't overlap originals
        var pasted = _clipboard.Select(e =>
        {
            var clone = e.Clone();
            clone.X += 20;
            clone.Y += 20;
            return clone;
        }).ToList();

        var commands = pasted
            .Select(e => (IEditCommand)new AddElementCommand(page, e))
            .ToList();
        UndoStack.Execute(new BatchCommand(commands, "Paste"));
        Selection.SelectMany(pasted);
        RegenerateCode();
    }

    [RelayCommand]
    private void DuplicateSelected()
    {
        CopySelected();
        PasteElements();
    }

    [RelayCommand]
    private void SelectAll()
    {
        var page = Document.Pages.FirstOrDefault(p => p.PageNumber == ActivePage);
        if (page is null) return;
        Selection.SelectMany(page.Elements.ToList());
    }

    // ═══════════ ALIGNMENT ═══════════

    [RelayCommand]
    private void AlignLeft()
    {
        if (Selection.SelectedElements.Count < 2) return;
        int minX = Selection.SelectedElements.Min(e => e.X);
        var commands = Selection.SelectedElements
            .Where(e => e.X != minX)
            .Select(e => (IEditCommand)new MoveElementCommand(e, minX, e.Y))
            .ToList();
        if (commands.Count > 0) { UndoStack.Execute(new BatchCommand(commands, "Align Left")); RegenerateCode(); }
    }

    [RelayCommand]
    private void AlignRight()
    {
        if (Selection.SelectedElements.Count < 2) return;
        int maxRight = Selection.SelectedElements.Max(e => e.X + e.Width);
        var commands = Selection.SelectedElements
            .Select(e => (IEditCommand)new MoveElementCommand(e, maxRight - e.Width, e.Y))
            .Where(_ => true)
            .ToList();
        if (commands.Count > 0) { UndoStack.Execute(new BatchCommand(commands, "Align Right")); RegenerateCode(); }
    }

    [RelayCommand]
    private void AlignTop()
    {
        if (Selection.SelectedElements.Count < 2) return;
        int minY = Selection.SelectedElements.Min(e => e.Y);
        var commands = Selection.SelectedElements
            .Where(e => e.Y != minY)
            .Select(e => (IEditCommand)new MoveElementCommand(e, e.X, minY))
            .ToList();
        if (commands.Count > 0) { UndoStack.Execute(new BatchCommand(commands, "Align Top")); RegenerateCode(); }
    }

    [RelayCommand]
    private void AlignBottom()
    {
        if (Selection.SelectedElements.Count < 2) return;
        int maxBottom = Selection.SelectedElements.Max(e => e.Y + e.Height);
        var commands = Selection.SelectedElements
            .Select(e => (IEditCommand)new MoveElementCommand(e, e.X, maxBottom - e.Height))
            .ToList();
        if (commands.Count > 0) { UndoStack.Execute(new BatchCommand(commands, "Align Bottom")); RegenerateCode(); }
    }

    [RelayCommand]
    private void AlignCenterH()
    {
        if (Selection.SelectedElements.Count < 2) return;
        int centerX = Selection.SelectedElements.Sum(e => e.X + e.Width / 2) / Selection.SelectedElements.Count;
        var commands = Selection.SelectedElements
            .Select(e => (IEditCommand)new MoveElementCommand(e, centerX - e.Width / 2, e.Y))
            .ToList();
        if (commands.Count > 0) { UndoStack.Execute(new BatchCommand(commands, "Align Center H")); RegenerateCode(); }
    }

    [RelayCommand]
    private void AlignCenterV()
    {
        if (Selection.SelectedElements.Count < 2) return;
        int centerY = Selection.SelectedElements.Sum(e => e.Y + e.Height / 2) / Selection.SelectedElements.Count;
        var commands = Selection.SelectedElements
            .Select(e => (IEditCommand)new MoveElementCommand(e, e.X, centerY - e.Height / 2))
            .ToList();
        if (commands.Count > 0) { UndoStack.Execute(new BatchCommand(commands, "Align Center V")); RegenerateCode(); }
    }

    [RelayCommand]
    private void DistributeH()
    {
        if (Selection.SelectedElements.Count < 3) return;
        var sorted = Selection.SelectedElements.OrderBy(e => e.X).ToList();

        // Calculate the total span from leftmost left-edge to rightmost right-edge
        int spanLeft = sorted.First().X;
        int spanRight = sorted.Max(e => e.X + e.Width);
        int totalSpan = spanRight - spanLeft;

        // Calculate total width consumed by all elements
        int totalWidths = sorted.Sum(e => e.Width);

        // Total gap space to distribute among (n-1) gaps
        double totalGap = totalSpan - totalWidths;
        double gapSize = totalGap / (sorted.Count - 1);

        var commands = new List<IEditCommand>();
        double currentX = spanLeft;
        for (int i = 0; i < sorted.Count; i++)
        {
            int newX = (int)Math.Round(currentX);
            if (i > 0 && i < sorted.Count - 1 && sorted[i].X != newX) // Don't move first/last
                commands.Add(new MoveElementCommand(sorted[i], newX, sorted[i].Y));
            currentX += sorted[i].Width + gapSize;
        }
        if (commands.Count > 0) { UndoStack.Execute(new BatchCommand(commands, "Distribute Horizontally")); RegenerateCode(); }
    }

    [RelayCommand]
    private void DistributeV()
    {
        if (Selection.SelectedElements.Count < 3) return;
        var sorted = Selection.SelectedElements.OrderBy(e => e.Y).ToList();

        // Calculate the total span from topmost top-edge to bottommost bottom-edge
        int spanTop = sorted.First().Y;
        int spanBottom = sorted.Max(e => e.Y + e.Height);
        int totalSpan = spanBottom - spanTop;

        // Calculate total height consumed by all elements
        int totalHeights = sorted.Sum(e => e.Height);

        // Total gap space to distribute among (n-1) gaps
        double totalGap = totalSpan - totalHeights;
        double gapSize = totalGap / (sorted.Count - 1);

        var commands = new List<IEditCommand>();
        double currentY = spanTop;
        for (int i = 0; i < sorted.Count; i++)
        {
            int newY = (int)Math.Round(currentY);
            if (i > 0 && i < sorted.Count - 1 && sorted[i].Y != newY) // Don't move first/last
                commands.Add(new MoveElementCommand(sorted[i], sorted[i].X, newY));
            currentY += sorted[i].Height + gapSize;
        }
        if (commands.Count > 0) { UndoStack.Execute(new BatchCommand(commands, "Distribute Vertically")); RegenerateCode(); }
    }

    [RelayCommand]
    private void GroupSelected()
    {
        if (Selection.SelectedElements.Count < 2) return;
        var page = Document.Pages.FirstOrDefault(p => p.PageNumber == ActivePage);
        if (page is null) return;

        var elements = Selection.SelectedElements.ToList();

        // Calculate bounding box
        int gx = elements.Min(e => e.X);
        int gy = elements.Min(e => e.Y);
        int gw = elements.Max(e => e.X + e.Width) - gx;
        int gh = elements.Max(e => e.Y + e.Height) - gy;

        var group = new GumpGroup
        {
            Name = $"Group_{page.Elements.Count}",
            X = gx, Y = gy, Width = gw, Height = gh,
            Page = ActivePage
        };

        // Move children into group and make coordinates relative
        foreach (var el in elements)
        {
            group.Children.Add(el);
            page.Elements.Remove(el);
        }

        page.Elements.Add(group);
        Selection.ClearSelection();
        Selection.Select(group);
        RegenerateCode();
    }

    [RelayCommand]
    private void UngroupSelected()
    {
        if (!Selection.HasSingleSelection) return;
        if (Selection.PrimarySelection is not GumpGroup group) return;
        var page = Document.Pages.FirstOrDefault(p => p.PageNumber == ActivePage);
        if (page is null) return;

        // Remove group, add children back with absolute coordinates
        page.Elements.Remove(group);
        Selection.ClearSelection();

        foreach (var child in group.Children)
        {
            page.Elements.Add(child);
            Selection.ToggleSelection(child);
        }

        RegenerateCode();
    }

    // ═══════════ Z-ORDER ═══════════

    [RelayCommand]
    private void BringToFront()
    {
        if (!Selection.HasSingleSelection) return;
        var page = Document.Pages.FirstOrDefault(p => p.PageNumber == ActivePage);
        if (page is null) return;
        var el = Selection.PrimarySelection!;
        int idx = page.Elements.IndexOf(el);
        if (idx >= 0 && idx < page.Elements.Count - 1)
        {
            UndoStack.Execute(new ReorderElementCommand(page, el, page.Elements.Count - 1));
            RegenerateCode();
        }
    }

    [RelayCommand]
    private void SendToBack()
    {
        if (!Selection.HasSingleSelection) return;
        var page = Document.Pages.FirstOrDefault(p => p.PageNumber == ActivePage);
        if (page is null) return;
        var el = Selection.PrimarySelection!;
        int idx = page.Elements.IndexOf(el);
        if (idx > 0)
        {
            UndoStack.Execute(new ReorderElementCommand(page, el, 0));
            RegenerateCode();
        }
    }

    [RelayCommand]
    private void BringForward()
    {
        if (!Selection.HasSingleSelection) return;
        var page = Document.Pages.FirstOrDefault(p => p.PageNumber == ActivePage);
        if (page is null) return;
        var el = Selection.PrimarySelection!;
        int idx = page.Elements.IndexOf(el);
        if (idx >= 0 && idx < page.Elements.Count - 1)
        {
            UndoStack.Execute(new ReorderElementCommand(page, el, idx + 1));
            RegenerateCode();
        }
    }

    [RelayCommand]
    private void SendBackward()
    {
        if (!Selection.HasSingleSelection) return;
        var page = Document.Pages.FirstOrDefault(p => p.PageNumber == ActivePage);
        if (page is null) return;
        var el = Selection.PrimarySelection!;
        int idx = page.Elements.IndexOf(el);
        if (idx > 0)
        {
            UndoStack.Execute(new ReorderElementCommand(page, el, idx - 1));
            RegenerateCode();
        }
    }

    // ═══════════ VIEW ═══════════

    [RelayCommand]
    private void ToggleGrid() => Canvas.ShowGrid = !Canvas.ShowGrid;

    [RelayCommand]
    private void ToggleRulers() => Canvas.ShowRulers = !Canvas.ShowRulers;

    [RelayCommand]
    private void ToggleSnap() => Canvas.SnapToGrid = !Canvas.SnapToGrid;

    [RelayCommand]
    private void ClearGuides() => Canvas.ClearAllGuides();

    [RelayCommand]
    private void ZoomIn() => Canvas.Zoom = Math.Min(Canvas.Zoom * 1.25, 5.0);

    [RelayCommand]
    private void ZoomOut() => Canvas.Zoom = Math.Max(Canvas.Zoom / 1.25, 0.1);

    [RelayCommand]
    private void ZoomFit() => Canvas.Zoom = 1.0; // TODO: calculate fit

    [RelayCommand]
    private void ZoomReset() => Canvas.Zoom = 1.0;

    // ═══════════ CODE SYNC ═══════════

    [RelayCommand]
    private void ApplyCode()
    {
        var code = CodePanel.IsEditMode ? CodePanel.EditText : GeneratedCode;
        if (string.IsNullOrWhiteSpace(code)) return;

        var parser = new GumpForge.Parsers.ServUoParser();
        var result = parser.Parse(code);
        if (result.Document is not null)
        {
            // Resolve element dimensions from actual gump art
            ResolveElementDimensions(result.Document);

            // Replace the document entirely
            Document = result.Document;
            Document.PropertyChanged += (_, _) => RegenerateCode();

            // Reset all subsystem references
            UndoStack.Clear();
            Selection.ClearSelection();
            ActivePage = 0;
            Canvas.Document = Document;
            Canvas.ActivePage = 0;
            Canvas.Zoom = 1.0;
            Layers.Document = Document;
            CodePanel.Document = Document;

            // Force canvas repaint
            OnPropertyChanged(nameof(Document));
            RegenerateCode();

            // Report success with warnings
            int totalElements = Document.GetAllElements().Count();
            int totalPages = Document.Pages.Count;
            Title = $"GumpForge — {Document.GumpClassName} ({totalElements} elements, {totalPages} pages)";

            if (result.Warnings.Count > 0)
            {
                CodePanel.ParseErrors = $"⚠ {result.Warnings.Count} warnings:\n" +
                    string.Join("\n", result.Warnings.Select(w => $"  Line {w.Line}: {w.Message}"));
            }
            else
            {
                CodePanel.ParseErrors = string.Empty;
            }
        }
        else
        {
            CodePanel.ParseErrors = string.Join("\n",
                result.Errors.Select(e => $"❌ Line {e.Line}: {e.Message}"));
        }
    }

    [RelayCommand]
    private void AddBackground()
    {
        AddElement(new GumpBackground
        {
            Name = "Background", X = 0, Y = 0, Width = 300, Height = 200,
            GumpId = 0x2436
        });
    }

    [RelayCommand]
    private void AddImage()
    {
        AddElement(new GumpImage
        {
            Name = "Image", X = 50, Y = 50, Width = 44, Height = 44,
            GumpId = 0x15A9
        });
    }

    [RelayCommand]
    private void AddButton()
    {
        AddElement(new GumpButton
        {
            Name = "Button", X = 50, Y = 50, Width = 40, Height = 40,
            NormalId = 0xFA5, PressedId = 0xFA7,
            ButtonId = 1, ButtonType = GumpButtonType.Reply
        });
    }

    [RelayCommand]
    private void AddLabel()
    {
        AddElement(new GumpLabel
        {
            Name = "Label", X = 50, Y = 50, Width = 100, Height = 20,
            Text = "Label Text", Hue = 0x480
        });
    }

    [RelayCommand]
    private void AddHtmlRegion()
    {
        AddElement(new GumpHtml
        {
            Name = "HtmlRegion", X = 50, Y = 50, Width = 200, Height = 150,
            Text = "<p>HTML content here</p>", HasBackground = true, HasScrollbar = true
        });
    }

    [RelayCommand]
    private void AddTextEntry()
    {
        AddElement(new GumpTextEntry
        {
            Name = "TextEntry", X = 50, Y = 50, Width = 150, Height = 20,
            EntryId = 1, InitialText = ""
        });
    }

    [RelayCommand]
    private void AddAlphaRegion()
    {
        AddElement(new GumpAlphaRegion
        {
            Name = "AlphaRegion", X = 50, Y = 50, Width = 200, Height = 150
        });
    }

    [RelayCommand]
    private void AddCheck()
    {
        AddElement(new GumpCheck
        {
            Name = "Checkbox", X = 50, Y = 50, Width = 30, Height = 30,
            InactiveId = 0xD2, ActiveId = 0xD3, SwitchId = 1
        });
    }

    [RelayCommand]
    private void AddSampleElements()
    {
        var page = Document.GetOrCreatePage(0);
        var commands = new List<IEditCommand>
        {
            new AddElementCommand(page, new GumpBackground
            {
                Name = "MainBG", X = 0, Y = 0, Width = 400, Height = 350,
                GumpId = 0x2436
            }),
            new AddElementCommand(page, new GumpAlphaRegion
            {
                Name = "Overlay", X = 10, Y = 10, Width = 380, Height = 330
            }),
            new AddElementCommand(page, new GumpLabel
            {
                Name = "Title", X = 20, Y = 15, Width = 200, Height = 20,
                Text = "Sample Gump Dialog", Hue = 0x480
            }),
            new AddElementCommand(page, new GumpHtml
            {
                Name = "Description", X = 20, Y = 45, Width = 360, Height = 200,
                Text = "This is a sample gump with multiple elements.\nYou can select, drag, and resize them.",
                HasBackground = false, HasScrollbar = true
            }),
            new AddElementCommand(page, new GumpButton
            {
                Name = "OkButton", X = 150, Y = 290, Width = 40, Height = 40,
                NormalId = 0xFA5, PressedId = 0xFA7,
                ButtonId = 1, ButtonType = GumpButtonType.Reply
            }),
            new AddElementCommand(page, new GumpButton
            {
                Name = "CancelButton", X = 250, Y = 290, Width = 40, Height = 40,
                NormalId = 0xFB1, PressedId = 0xFB3,
                ButtonId = 0, ButtonType = GumpButtonType.Reply
            }),
            new AddElementCommand(page, new GumpLabel
            {
                Name = "OkLabel", X = 195, Y = 295, Width = 40, Height = 20,
                Text = "OK", Hue = 0x480
            }),
            new AddElementCommand(page, new GumpLabel
            {
                Name = "CancelLabel", X = 290, Y = 295, Width = 60, Height = 20,
                Text = "Cancel", Hue = 0x480
            }),
        };

        UndoStack.Execute(new BatchCommand(commands, "Add sample elements"));
        RegenerateCode();
    }

    private void AddElement(GumpElement element)
    {
        // Auto-name if not already named meaningfully
        if (string.IsNullOrEmpty(element.Name) || element.Name == element.ElementType)
        {
            element.Name = GenerateElementName(element.ElementType);
        }

        var page = Document.GetOrCreatePage(ActivePage);
        UndoStack.Execute(new AddElementCommand(page, element));
        Selection.Select(element);
        RegenerateCode();
    }

    private int _elementCounter;

    private string GenerateElementName(string type)
    {
        _elementCounter++;
        return $"{type}_{_elementCounter}";
    }

    /// <summary>
    /// Adds a gump image element from an Asset Browser thumbnail.
    /// Auto-sizes the element from the actual bitmap dimensions.
    /// </summary>
    public void AddGumpFromAsset(AssetThumbnail thumb)
    {
        AddElement(new GumpImage
        {
            Name = $"Gump_0x{thumb.GumpId:X4}",
            GumpId = thumb.GumpId,
            X = 50, Y = 50,
            Width = thumb.Width,
            Height = thumb.Height
        });
    }

    /// <summary>
    /// Adds a gump image at a specific canvas position (used by drag-and-drop).
    /// </summary>
    public void AddGumpFromAssetAtPosition(AssetThumbnail thumb, int x, int y)
    {
        AddElement(new GumpImage
        {
            Name = $"Gump_0x{thumb.GumpId:X4}",
            GumpId = thumb.GumpId,
            X = x, Y = y,
            Width = thumb.Width,
            Height = thumb.Height
        });
    }

    /// <summary>
    /// Resolves hardcoded default element dimensions to actual gump art sizes.
    /// Called after code parsing to fix 44x44, 40x40, 30x30 placeholder sizes.
    /// </summary>
    private void ResolveElementDimensions(GumpDocument doc)
    {
        var mgr = AssetManager.Instance;
        if (!mgr.IsLoaded) return;

        foreach (var page in doc.Pages)
        {
            foreach (var element in page.Elements)
            {
                int gumpId = -1;
                bool isDefaultSize = false;

                switch (element)
                {
                    case GumpImage img:
                        gumpId = img.GumpId;
                        // The parser uses 44x44 as default for AddImage
                        isDefaultSize = img.Width == 44 && img.Height == 44;
                        break;
                    case GumpButton btn:
                        gumpId = btn.NormalId;
                        // The parser uses 40x40 as default for AddButton
                        isDefaultSize = btn.Width == 40 && btn.Height == 40;
                        break;
                    case GumpCheck chk:
                        gumpId = chk.InactiveId;
                        isDefaultSize = chk.Width == 30 && chk.Height == 30;
                        break;
                    case GumpRadio radio:
                        gumpId = radio.InactiveId;
                        isDefaultSize = radio.Width == 30 && radio.Height == 30;
                        break;
                    case GumpItem item:
                        gumpId = item.ItemId;
                        isDefaultSize = item.Width == 44 && item.Height == 44;
                        break;
                }

                if (gumpId >= 0 && isDefaultSize)
                {
                    var dims = mgr.GetDimensions(gumpId);
                    if (dims is { Width: > 0, Height: > 0 })
                    {
                        element.Width = dims.Value.Width;
                        element.Height = dims.Value.Height;
                    }
                }
            }
        }
    }

    // ── Page management ──────────────────────────────────────────

    public int TotalPages => Document.Pages.Count;

    [RelayCommand]
    private void AddPage()
    {
        int nextNum = Document.Pages.Max(p => p.PageNumber) + 1;
        Document.GetOrCreatePage(nextNum);
        ActivePage = nextNum;
        OnPropertyChanged(nameof(TotalPages));
        RegenerateCode();
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (ActivePage == 0) return; // Can't remove page 0
        var page = Document.Pages.FirstOrDefault(p => p.PageNumber == ActivePage);
        if (page is not null)
        {
            Document.Pages.Remove(page);
            ActivePage = 0;
            OnPropertyChanged(nameof(TotalPages));
            RegenerateCode();
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        var pages = Document.Pages.OrderBy(p => p.PageNumber).ToList();
        var idx = pages.FindIndex(p => p.PageNumber == ActivePage);
        if (idx < pages.Count - 1)
            ActivePage = pages[idx + 1].PageNumber;
    }

    [RelayCommand]
    private void PrevPage()
    {
        var pages = Document.Pages.OrderBy(p => p.PageNumber).ToList();
        var idx = pages.FindIndex(p => p.PageNumber == ActivePage);
        if (idx > 0)
            ActivePage = pages[idx - 1].PageNumber;
    }

    // ── Additional insert commands ───────────────────────────────

    [RelayCommand]
    private void AddRadio()
    {
        AddElement(new GumpRadio
        {
            X = 50, Y = 50, Width = 30, Height = 30,
            InactiveId = 0xD0, ActiveId = 0xD1, SwitchId = 1
        });
    }

    [RelayCommand]
    private void AddImageTiled()
    {
        AddElement(new GumpImageTiled
        {
            X = 50, Y = 50, Width = 200, Height = 100,
            GumpId = 0x2436
        });
    }

    [RelayCommand]
    private void AddItem()
    {
        AddElement(new GumpItem
        {
            X = 50, Y = 50, Width = 44, Height = 44,
            ItemId = 0x1BDD
        });
    }

    private void RegenerateCode()
    {
        var opts = new GenerationOptions
        {
            Namespace = Document.Namespace,
            ClassName = Document.GumpClassName,
            UseHexIds = true
        };

        foreach (var gen in _generators)
        {
            var code = gen.Generate(Document, opts);
            switch (gen.TargetName)
            {
                case "ServUO": GeneratedCode = code; break;
                case "RunUO": RunUoCode = code; break;
                case "ModernUO": ModernUoCode = code; break;
                case "Sphere": SphereCode = code; break;
                case "ClassicAssist": ClassicAssistCode = code; break;
            }
        }

        // Validate the document
        RefreshProblems();
    }

    private void RefreshProblems()
    {
        Problems.Clear();
        var results = GumpValidator.Validate(Document);
        foreach (var p in results)
            Problems.Add(p);
        ProblemCount = results.Count;
        WarningCount = results.Count(p => p.Severity == ProblemSeverity.Warning);
        ErrorCount = results.Count(p => p.Severity == ProblemSeverity.Error);
    }
}

public partial class AssetBrowserViewModel : ViewModelBase
{
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private int _filterIdStart;
    [ObservableProperty] private int _filterIdEnd = 65535;
    [ObservableProperty] private bool _showCustomOnly;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalAssets;
    [ObservableProperty] private AssetThumbnail? _selectedThumbnail;
    [ObservableProperty] private string _filterTag = string.Empty;
    [ObservableProperty] private string? _filterCollectionId;

    /// <summary>Reference to the active shard profile for metadata lookups.</summary>
    public ShardProfile? Profile { get; set; }

    /// <summary>All loaded thumbnails (unfiltered master list).</summary>
    public ObservableCollection<AssetThumbnail> AllThumbnails { get; } = [];

    /// <summary>Filtered view bound to the UI ListBox.</summary>
    public ObservableCollection<AssetThumbnail> Thumbnails { get; } = [];

    /// <summary>Currently selected thumbnails for multi-select bulk operations.</summary>
    public ObservableCollection<AssetThumbnail> SelectedThumbnails { get; } = [];

    /// <summary>Callback invoked when user double-clicks a thumbnail to place it on canvas.</summary>
    public Action<AssetThumbnail>? OnPlaceAsset { get; set; }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnFilterIdStartChanged(int value) => ApplyFilter();
    partial void OnFilterIdEndChanged(int value) => ApplyFilter();
    partial void OnShowCustomOnlyChanged(bool value) => ApplyFilter();
    partial void OnFilterTagChanged(string value) => ApplyFilter();
    partial void OnFilterCollectionIdChanged(string? value) => ApplyFilter();

    public void ApplyFilter()
    {
        Thumbnails.Clear();

        foreach (var thumb in AllThumbnails)
        {
            // ID range filter
            if (thumb.GumpId < FilterIdStart || thumb.GumpId > FilterIdEnd)
                continue;

            // Custom only (IDs >= 30000 are typically custom)
            if (ShowCustomOnly && thumb.GumpId < 30000)
                continue;

            // Collection filter
            if (!string.IsNullOrEmpty(FilterCollectionId) && Profile is not null)
            {
                var collection = Profile.Collections.FirstOrDefault(c => c.Id == FilterCollectionId);
                if (collection is not null && !collection.AssetIds.Contains(thumb.GumpId))
                    continue;
            }

            // Tag filter
            if (!string.IsNullOrWhiteSpace(FilterTag) && Profile is not null)
            {
                var meta = Profile.AssetMetadata.GetValueOrDefault(thumb.GumpId);
                if (meta is null)
                    continue;
                var tagMatch = meta.Tags.Concat(meta.AutoTags)
                    .Any(t => t.Contains(FilterTag, StringComparison.OrdinalIgnoreCase));
                if (!tagMatch)
                    continue;
            }

            // Text filter — matches hex ID, decimal ID, display name, or tags
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                var ft = FilterText.Trim();
                bool matchesHex = thumb.Label.Contains(ft, StringComparison.OrdinalIgnoreCase);
                bool matchesDec = thumb.GumpId.ToString().Contains(ft);

                // Also search display name and tags from profile
                bool matchesMeta = false;
                if (Profile is not null && Profile.AssetMetadata.TryGetValue(thumb.GumpId, out var meta2))
                {
                    matchesMeta = (!string.IsNullOrEmpty(meta2.DisplayName) &&
                                   meta2.DisplayName.Contains(ft, StringComparison.OrdinalIgnoreCase)) ||
                                  meta2.Tags.Any(t => t.Contains(ft, StringComparison.OrdinalIgnoreCase)) ||
                                  meta2.AutoTags.Any(t => t.Contains(ft, StringComparison.OrdinalIgnoreCase));
                }

                // Also check collection names
                if (!matchesMeta && Profile is not null)
                {
                    matchesMeta = Profile.Collections
                        .Where(c => c.AssetIds.Contains(thumb.GumpId))
                        .Any(c => c.Name.Contains(ft, StringComparison.OrdinalIgnoreCase));
                }

                if (!matchesHex && !matchesDec && !matchesMeta)
                    continue;
            }

            Thumbnails.Add(thumb);
        }
    }

    [RelayCommand]
    private void PlaceSelectedAsset()
    {
        if (SelectedThumbnail is not null)
            OnPlaceAsset?.Invoke(SelectedThumbnail);
    }

    // ── Single-asset commands ─────────────────────────────

    /// <summary>
    /// Creates a new collection and adds it to the profile.
    /// </summary>
    [RelayCommand]
    private void CreateCollection(string name)
    {
        if (Profile is null || string.IsNullOrWhiteSpace(name)) return;

        var collection = new AssetCollection { Name = name.Trim() };
        Profile.Collections.Add(collection);
    }

    /// <summary>
    /// Adds the currently selected asset to a collection.
    /// </summary>
    [RelayCommand]
    private void AddToCollection(string collectionId)
    {
        if (Profile is null || SelectedThumbnail is null || string.IsNullOrEmpty(collectionId)) return;

        var collection = Profile.Collections.FirstOrDefault(c => c.Id == collectionId);
        if (collection is not null && !collection.AssetIds.Contains(SelectedThumbnail.GumpId))
        {
            collection.AssetIds.Add(SelectedThumbnail.GumpId);
        }
    }

    /// <summary>
    /// Removes an asset from a collection.
    /// </summary>
    [RelayCommand]
    private void RemoveFromCollection(string collectionId)
    {
        if (Profile is null || SelectedThumbnail is null || string.IsNullOrEmpty(collectionId)) return;

        var collection = Profile.Collections.FirstOrDefault(c => c.Id == collectionId);
        collection?.AssetIds.Remove(SelectedThumbnail.GumpId);
    }

    /// <summary>
    /// Adds a user tag to the selected asset.
    /// </summary>
    [RelayCommand]
    private void AddTag(string tag)
    {
        if (Profile is null || SelectedThumbnail is null || string.IsNullOrWhiteSpace(tag)) return;

        var cleanTag = tag.Trim().ToLowerInvariant();
        if (!Profile.AssetMetadata.TryGetValue(SelectedThumbnail.GumpId, out var meta))
        {
            meta = new AssetMeta { GumpId = SelectedThumbnail.GumpId };
            Profile.AssetMetadata[SelectedThumbnail.GumpId] = meta;
        }

        if (!meta.Tags.Contains(cleanTag))
        {
            meta.Tags.Add(cleanTag);
            SelectedThumbnail.Tags = [..meta.Tags, ..meta.AutoTags];
        }
    }

    /// <summary>
    /// Removes a user tag from the selected asset.
    /// </summary>
    [RelayCommand]
    private void RemoveTag(string tag)
    {
        if (Profile is null || SelectedThumbnail is null || string.IsNullOrWhiteSpace(tag)) return;

        if (Profile.AssetMetadata.TryGetValue(SelectedThumbnail.GumpId, out var meta))
        {
            meta.Tags.Remove(tag.Trim().ToLowerInvariant());
            SelectedThumbnail.Tags = [..meta.Tags, ..meta.AutoTags];
        }
    }

    /// <summary>
    /// Removes an auto-tag from the selected asset and suppresses it permanently.
    /// </summary>
    [RelayCommand]
    private void RemoveAutoTag(string tag)
    {
        if (Profile is null || SelectedThumbnail is null || string.IsNullOrWhiteSpace(tag)) return;

        if (Profile.AssetMetadata.TryGetValue(SelectedThumbnail.GumpId, out var meta))
        {
            AutoTagger.SuppressAutoTag(meta, tag);
            SelectedThumbnail.Tags = [..meta.Tags, ..meta.AutoTags];
        }
    }

    /// <summary>
    /// Sets a display name for the selected asset.
    /// </summary>
    [RelayCommand]
    private void SetDisplayName(string name)
    {
        if (Profile is null || SelectedThumbnail is null) return;

        if (!Profile.AssetMetadata.TryGetValue(SelectedThumbnail.GumpId, out var meta))
        {
            meta = new AssetMeta { GumpId = SelectedThumbnail.GumpId };
            Profile.AssetMetadata[SelectedThumbnail.GumpId] = meta;
        }

        meta.DisplayName = name?.Trim() ?? string.Empty;
        SelectedThumbnail.DisplayName = meta.DisplayName;
    }

    // ── Bulk / multi-select commands ──────────────────────

    /// <summary>
    /// Adds a tag to all currently selected assets.
    /// </summary>
    [RelayCommand]
    private void BulkAddTag(string tag)
    {
        if (Profile is null || string.IsNullOrWhiteSpace(tag)) return;
        var cleanTag = tag.Trim().ToLowerInvariant();

        foreach (var thumb in SelectedThumbnails)
        {
            if (!Profile.AssetMetadata.TryGetValue(thumb.GumpId, out var meta))
            {
                meta = new AssetMeta { GumpId = thumb.GumpId };
                Profile.AssetMetadata[thumb.GumpId] = meta;
            }
            if (!meta.Tags.Contains(cleanTag))
            {
                meta.Tags.Add(cleanTag);
                thumb.Tags = [..meta.Tags, ..meta.AutoTags];
            }
        }
    }

    /// <summary>
    /// Removes a tag from all currently selected assets.
    /// </summary>
    [RelayCommand]
    private void BulkRemoveTag(string tag)
    {
        if (Profile is null || string.IsNullOrWhiteSpace(tag)) return;
        var cleanTag = tag.Trim().ToLowerInvariant();

        foreach (var thumb in SelectedThumbnails)
        {
            if (Profile.AssetMetadata.TryGetValue(thumb.GumpId, out var meta))
            {
                meta.Tags.Remove(cleanTag);
                if (meta.AutoTags.Contains(cleanTag))
                    AutoTagger.SuppressAutoTag(meta, cleanTag);
                thumb.Tags = [..meta.Tags, ..meta.AutoTags];
            }
        }
    }

    /// <summary>
    /// Adds all selected assets to a collection.
    /// </summary>
    [RelayCommand]
    private void BulkAddToCollection(string collectionId)
    {
        if (Profile is null || string.IsNullOrEmpty(collectionId)) return;

        var collection = Profile.Collections.FirstOrDefault(c => c.Id == collectionId);
        if (collection is null) return;

        foreach (var thumb in SelectedThumbnails)
        {
            if (!collection.AssetIds.Contains(thumb.GumpId))
                collection.AssetIds.Add(thumb.GumpId);
        }
    }

    /// <summary>
    /// Removes all selected assets from a collection.
    /// </summary>
    [RelayCommand]
    private void BulkRemoveFromCollection(string collectionId)
    {
        if (Profile is null || string.IsNullOrEmpty(collectionId)) return;

        var collection = Profile.Collections.FirstOrDefault(c => c.Id == collectionId);
        if (collection is null) return;

        foreach (var thumb in SelectedThumbnails)
            collection.AssetIds.Remove(thumb.GumpId);
    }
}

public class AssetThumbnail
{
    public int GumpId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Label => $"0x{GumpId:X4}";
    public string SizeLabel => $"{Width}×{Height}";
    public byte[]? PixelData { get; init; }

    /// <summary>User-assigned display name from profile metadata.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Combined tags (user + auto) for display.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Display label: uses DisplayName if set, otherwise hex ID.</summary>
    public string DisplayLabel => !string.IsNullOrEmpty(DisplayName) ? DisplayName : Label;

    private Avalonia.Media.Imaging.WriteableBitmap? _bitmap;
    public Avalonia.Media.Imaging.WriteableBitmap? Bitmap
    {
        get
        {
            if (_bitmap is null && PixelData is not null)
                _bitmap = Helpers.BitmapHelper.CreateThumbnail(PixelData, Width, Height, 56);
            return _bitmap;
        }
    }
}

public partial class CanvasViewModel : ViewModelBase
{
    [ObservableProperty] private GumpDocument _document;
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private double _panX;
    [ObservableProperty] private double _panY;
    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private int _gridSize = 10;
    [ObservableProperty] private bool _snapToGrid = true;
    [ObservableProperty] private bool _showRulers = true;
    [ObservableProperty] private string _toolMode = "Select"; // Select, Pan
    [ObservableProperty] private int _activePage;

    public SelectionManager Selection { get; }
    public UndoStack UndoStack { get; }

    /// <summary>User-defined vertical guide lines (canvas X positions).</summary>
    public ObservableCollection<int> UserGuidesX { get; } = [];

    /// <summary>User-defined horizontal guide lines (canvas Y positions).</summary>
    public ObservableCollection<int> UserGuidesY { get; } = [];

    public CanvasViewModel(GumpDocument document, SelectionManager selection, UndoStack undoStack)
    {
        _document = document;
        Selection = selection;
        UndoStack = undoStack;
    }

    /// <summary>Add a vertical guide at the given canvas X position.</summary>
    public void AddVerticalGuide(int x)
    {
        if (!UserGuidesX.Contains(x))
            UserGuidesX.Add(x);
    }

    /// <summary>Add a horizontal guide at the given canvas Y position.</summary>
    public void AddHorizontalGuide(int y)
    {
        if (!UserGuidesY.Contains(y))
            UserGuidesY.Add(y);
    }

    /// <summary>Remove a guide near the given canvas position (within tolerance).</summary>
    public bool RemoveGuideNear(int x, int y, int tolerance = 5)
    {
        for (int i = UserGuidesX.Count - 1; i >= 0; i--)
        {
            if (Math.Abs(UserGuidesX[i] - x) <= tolerance)
            {
                UserGuidesX.RemoveAt(i);
                return true;
            }
        }
        for (int i = UserGuidesY.Count - 1; i >= 0; i--)
        {
            if (Math.Abs(UserGuidesY[i] - y) <= tolerance)
            {
                UserGuidesY.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Clear all user guides.</summary>
    public void ClearAllGuides()
    {
        UserGuidesX.Clear();
        UserGuidesY.Clear();
    }
}

public partial class LayersViewModel : ViewModelBase
{
    [ObservableProperty] private GumpDocument _document;
    public SelectionManager Selection { get; }

    public LayersViewModel(GumpDocument document, SelectionManager selection)
    {
        _document = document;
        Selection = selection;
    }
}

public partial class PropertiesViewModel : ViewModelBase
{
    public SelectionManager Selection { get; }
    public UndoStack UndoStack { get; }

    [ObservableProperty] private ObservableCollection<ElementPropertyItem> _elementProperties = [];

    public PropertiesViewModel(SelectionManager selection, UndoStack undoStack)
    {
        Selection = selection;
        UndoStack = undoStack;

        // Refresh on any selection change (single or multi-select)
        Selection.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SelectionManager.PrimarySelection) or nameof(SelectionManager.HasSingleSelection) or nameof(SelectionManager.HasSelection))
                RefreshProperties();
        };

        // Also refresh when the collection itself changes (multi-select add/remove)
        Selection.SelectedElements.CollectionChanged += (_, _) => RefreshProperties();
    }

    public void RefreshProperties()
    {
        ElementProperties.Clear();
        var selected = Selection.SelectedElements;

        if (selected.Count == 0) return;

        // Multi-select: show shared common properties
        if (selected.Count > 1)
        {
            RefreshMultiSelectProperties(selected);
            return;
        }

        var el = Selection.PrimarySelection!;

        switch (el)
        {
            case GumpBackground bg:
                ElementProperties.Add(new("Gump ID", $"0x{bg.GumpId:X4}", "gumpid", v => bg.GumpId = ParseHexOrInt(v)));
                break;
            case GumpImage img:
                ElementProperties.Add(new("Gump ID", $"0x{img.GumpId:X4}", "gumpid", v => img.GumpId = ParseHexOrInt(v)));
                ElementProperties.Add(new("Hue", img.Hue.ToString(), "hue", v => img.Hue = ParseInt(v)));
                break;
            case GumpButton btn:
                ElementProperties.Add(new("Normal ID", $"0x{btn.NormalId:X4}", "normalid", v => btn.NormalId = ParseHexOrInt(v)));
                ElementProperties.Add(new("Pressed ID", $"0x{btn.PressedId:X4}", "pressedid", v => btn.PressedId = ParseHexOrInt(v)));
                ElementProperties.Add(new("Button ID", btn.ButtonId.ToString(), "buttonid", v => btn.ButtonId = ParseInt(v)));
                ElementProperties.Add(new("Type", btn.ButtonType.ToString(), "buttontype", v =>
                    { if (Enum.TryParse<GumpButtonType>(v, true, out var bt)) btn.ButtonType = bt; }));
                ElementProperties.Add(new("Param", btn.Param.ToString(), "param", v => btn.Param = ParseInt(v)));
                break;
            case GumpLabel label:
                ElementProperties.Add(new("Text", label.Text, "text", v => label.Text = v));
                ElementProperties.Add(new("Hue", label.Hue.ToString(), "hue", v => label.Hue = ParseInt(v)));
                ElementProperties.Add(new("Font", label.Font.ToString(), "font", v => label.Font = ParseInt(v)));
                break;
            case GumpHtml html:
                ElementProperties.Add(new("Text", html.Text, "text", v => html.Text = v));
                ElementProperties.Add(new("Background", html.HasBackground.ToString(), "hasbg", v =>
                    { if (bool.TryParse(v, out var b)) html.HasBackground = b; }));
                ElementProperties.Add(new("Scrollbar", html.HasScrollbar.ToString(), "hasscroll", v =>
                    { if (bool.TryParse(v, out var b)) html.HasScrollbar = b; }));
                break;
            case GumpTextEntry entry:
                ElementProperties.Add(new("Entry ID", entry.EntryId.ToString(), "entryid", v => entry.EntryId = ParseInt(v)));
                ElementProperties.Add(new("Init Text", entry.InitialText, "text", v => entry.InitialText = v));
                ElementProperties.Add(new("Hue", entry.Hue.ToString(), "hue", v => entry.Hue = ParseInt(v)));
                ElementProperties.Add(new("Max Length", entry.MaxLength.ToString(), "maxlen", v => entry.MaxLength = ParseInt(v)));
                break;
            case GumpCheck chk:
                ElementProperties.Add(new("Inactive ID", $"0x{chk.InactiveId:X4}", "inactiveid", v => chk.InactiveId = ParseHexOrInt(v)));
                ElementProperties.Add(new("Active ID", $"0x{chk.ActiveId:X4}", "activeid", v => chk.ActiveId = ParseHexOrInt(v)));
                ElementProperties.Add(new("Switch ID", chk.SwitchId.ToString(), "switchid", v => chk.SwitchId = ParseInt(v)));
                break;
            case GumpRadio radio:
                ElementProperties.Add(new("Inactive ID", $"0x{radio.InactiveId:X4}", "inactiveid", v => radio.InactiveId = ParseHexOrInt(v)));
                ElementProperties.Add(new("Active ID", $"0x{radio.ActiveId:X4}", "activeid", v => radio.ActiveId = ParseHexOrInt(v)));
                ElementProperties.Add(new("Switch ID", radio.SwitchId.ToString(), "switchid", v => radio.SwitchId = ParseInt(v)));
                ElementProperties.Add(new("Group ID", radio.GroupId.ToString(), "groupid", v => radio.GroupId = ParseInt(v)));
                break;
            case GumpItem item:
                ElementProperties.Add(new("Item ID", $"0x{item.ItemId:X4}", "itemid", v => item.ItemId = ParseHexOrInt(v)));
                ElementProperties.Add(new("Hue", item.Hue.ToString(), "hue", v => item.Hue = ParseInt(v)));
                break;
            case GumpHtmlLocalized loc:
                ElementProperties.Add(new("Cliloc ID", loc.ClilocId.ToString(), "clilocid", v => loc.ClilocId = ParseInt(v)));
                ElementProperties.Add(new("Args", loc.Args, "args", v => loc.Args = v));
                ElementProperties.Add(new("Color", loc.Color.ToString(), "color", v => loc.Color = ParseInt(v)));
                break;
            case GumpImageTiled tiled:
                ElementProperties.Add(new("Gump ID", $"0x{tiled.GumpId:X4}", "gumpid", v => tiled.GumpId = ParseHexOrInt(v)));
                break;
            case GumpGroup grp:
                ElementProperties.Add(new("Children", grp.Children.Count.ToString(), "children"));
                break;
        }
    }

    /// <summary>
    /// When multiple elements are selected, show common properties that can be batch-edited.
    /// Changing a value applies to ALL selected elements.
    /// </summary>
    private void RefreshMultiSelectProperties(IReadOnlyCollection<GumpElement> selected)
    {
        var list = selected.ToList();
        string countLabel = $"{list.Count} elements selected";

        // Info header
        ElementProperties.Add(new("Selection", countLabel, "info"));

        // Show types
        var types = list.Select(e => e.ElementType).Distinct().ToList();
        ElementProperties.Add(new("Types", string.Join(", ", types), "types"));

        // X — batch set X for all
        bool sameX = list.All(e => e.X == list[0].X);
        ElementProperties.Add(new("X", sameX ? list[0].X.ToString() : "—", "batch_x", v =>
        {
            int val = ParseInt(v);
            foreach (var el in list) el.X = val;
        }));

        // Y — batch set Y for all
        bool sameY = list.All(e => e.Y == list[0].Y);
        ElementProperties.Add(new("Y", sameY ? list[0].Y.ToString() : "—", "batch_y", v =>
        {
            int val = ParseInt(v);
            foreach (var el in list) el.Y = val;
        }));

        // Width — batch set Width for all
        bool sameW = list.All(e => e.Width == list[0].Width);
        ElementProperties.Add(new("Width", sameW ? list[0].Width.ToString() : "—", "batch_w", v =>
        {
            int val = ParseInt(v);
            foreach (var el in list) el.Width = val;
        }));

        // Height — batch set Height for all
        bool sameH = list.All(e => e.Height == list[0].Height);
        ElementProperties.Add(new("Height", sameH ? list[0].Height.ToString() : "—", "batch_h", v =>
        {
            int val = ParseInt(v);
            foreach (var el in list) el.Height = val;
        }));

        // If all are the same type with Hue, show Hue
        if (list.All(e => e is GumpImage or GumpLabel or GumpTextEntry or GumpItem))
        {
            ElementProperties.Add(new("Hue", "—", "batch_hue", v =>
            {
                int val = ParseInt(v);
                foreach (var el in list)
                {
                    switch (el)
                    {
                        case GumpImage img: img.Hue = val; break;
                        case GumpLabel lbl: lbl.Hue = val; break;
                        case GumpTextEntry te: te.Hue = val; break;
                        case GumpItem item: item.Hue = val; break;
                    }
                }
            }));
        }
    }

    private static int ParseHexOrInt(string v)
    {
        v = v.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(v.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hex))
                return hex;
        }
        return int.TryParse(v, out var i) ? i : 0;
    }

    private static int ParseInt(string v) => int.TryParse(v.Trim(), out var i) ? i : 0;
}

public partial class ElementPropertyItem : ObservableObject
{
    public string Label { get; init; }
    public string Key { get; init; }

    private string _value;
    private readonly Action<string>? _setter;
    private bool _isSetting;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value || _isSetting) return;
            _isSetting = true;
            SetProperty(ref _value, value);
            _setter?.Invoke(value);
            _isSetting = false;
        }
    }

    /// <param name="label">Display label for this property</param>
    /// <param name="value">Initial string value</param>
    /// <param name="key">Internal key for identification</param>
    /// <param name="setter">Optional callback to write value back to the model</param>
    public ElementPropertyItem(string label, string value, string key, Action<string>? setter = null)
    {
        Label = label;
        _value = value;
        Key = key;
        _setter = setter;
    }
}

public partial class CodePanelViewModel : ViewModelBase
{
    [ObservableProperty] private GumpDocument _document;
    [ObservableProperty] private int _activeTab;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private string _editText = string.Empty;
    [ObservableProperty] private string _parseErrors = string.Empty;

    public IReadOnlyList<IGumpCodeGenerator> Generators { get; }

    public CodePanelViewModel(List<IGumpCodeGenerator> generators, GumpDocument document)
    {
        Generators = generators;
        _document = document;
    }
}
