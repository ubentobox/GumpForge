using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GumpForge.Core.Models;

/// <summary>
/// Represents a single page within a gump document.
/// Page 0 is the "always visible" layer; pages 1+ are switched via buttons.
/// </summary>
public partial class GumpPage : ObservableObject
{
    [ObservableProperty] private int _pageNumber;
    [ObservableProperty] private string _name = string.Empty;

    public ObservableCollection<GumpElement> Elements { get; } = [];

    public GumpPage() { }
    public GumpPage(int pageNumber) => PageNumber = pageNumber;
}

/// <summary>
/// Supported emulator targets for code generation and parsing.
/// </summary>
public enum EmulatorTarget
{
    ServUO,
    RunUO,
    ModernUO,
    Sphere,
    ClassicAssist,
    TazUO
}

/// <summary>
/// Represents a custom asset imported by the user (PNG → gump ID mapping).
/// </summary>
public partial class CustomAssetEntry : ObservableObject
{
    [ObservableProperty] private int _gumpId;
    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _fileHash = string.Empty;
    [ObservableProperty] private string _tag = "Custom";
}

/// <summary>
/// Root document model. Observable, serializable, and the single source of truth
/// for both canvas rendering and code generation.
/// </summary>
public partial class GumpDocument : ObservableObject
{
    [ObservableProperty] private string _name = "Untitled";
    [ObservableProperty] private string _gumpClassName = "MyGump";
    [ObservableProperty] private string _namespace = "Server.Gumps";
    [ObservableProperty] private int _canvasWidth = 800;
    [ObservableProperty] private int _canvasHeight = 600;
    [ObservableProperty] private int _gumpX = 100;
    [ObservableProperty] private int _gumpY = 100;
    [ObservableProperty] private EmulatorTarget _targetEmulator = EmulatorTarget.ServUO;

    // Gump behavior flags (matching the RunUO Gump base class)
    [ObservableProperty] private bool _isDraggable = true;
    [ObservableProperty] private bool _isClosable = true;
    [ObservableProperty] private bool _isResizable;
    [ObservableProperty] private bool _isDisposable = true;

    /// <summary>All pages in the document. Page 0 is always present.</summary>
    public ObservableCollection<GumpPage> Pages { get; } = [new GumpPage(0)];

    /// <summary>Custom assets imported by the user.</summary>
    public ObservableCollection<CustomAssetEntry> CustomAssets { get; } = [];

    /// <summary>Path to the project file on disk, if saved.</summary>
    [ObservableProperty] private string? _filePath;

    /// <summary>Whether the document has unsaved changes.</summary>
    [ObservableProperty] private bool _isDirty;

    /// <summary>
    /// Gets all elements across all pages, recursively flattening groups.
    /// Groups are editor-only containers; their children are the "real" elements.
    /// </summary>
    public IEnumerable<GumpElement> GetAllElements()
    {
        foreach (var page in Pages)
            foreach (var element in page.Elements)
                foreach (var flat in FlattenElement(element))
                    yield return flat;
    }

    /// <summary>
    /// Recursively flattens an element — if it's a group, yields its children instead.
    /// </summary>
    public static IEnumerable<GumpElement> FlattenElement(GumpElement element)
    {
        if (element is GumpGroup group)
        {
            foreach (var child in group.Children)
                foreach (var flat in FlattenElement(child))
                    yield return flat;
        }
        else
        {
            yield return element;
        }
    }

    /// <summary>
    /// Gets or creates a page with the specified number.
    /// </summary>
    public GumpPage GetOrCreatePage(int pageNumber)
    {
        var page = Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
        if (page is null)
        {
            page = new GumpPage(pageNumber);
            Pages.Add(page);
        }
        return page;
    }
}
