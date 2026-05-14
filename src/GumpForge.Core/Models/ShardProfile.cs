using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace GumpForge.Core.Models;

/// <summary>
/// Root model for a Shard Profile — stores editor preferences, client paths,
/// asset metadata (tags, aliases), and collection definitions.
/// Serialized as a .gfprofile JSON file.
/// </summary>
public partial class ShardProfile : ObservableObject
{
    [ObservableProperty] private string _profileName = "Default";
    [ObservableProperty] private string _clientDataPath = string.Empty;
    [ObservableProperty] private string _profileFilePath = string.Empty;
    [ObservableProperty] private DateTime _lastModified = DateTime.UtcNow;

    /// <summary>
    /// Editor layout and behavior preferences.
    /// </summary>
    [ObservableProperty] private EditorPreferences _preferences = new();

    /// <summary>
    /// Per-asset metadata (tags, display names) keyed by gump ID.
    /// </summary>
    public Dictionary<int, AssetMeta> AssetMetadata { get; set; } = [];

    /// <summary>
    /// User-defined asset collections.
    /// </summary>
    public ObservableCollection<AssetCollection> Collections { get; set; } = [];

    /// <summary>
    /// Hashes of last-known gump art entries, used to detect NEW/CHANGED assets.
    /// </summary>
    public Dictionary<int, string> AssetHashes { get; set; } = [];
}

/// <summary>
/// Editor preferences: grid, snap, padding, buffer, canvas defaults.
/// </summary>
public partial class EditorPreferences : ObservableObject
{
    [ObservableProperty] private int _gridSize = 10;
    [ObservableProperty] private bool _gridVisible = true;
    [ObservableProperty] private int _snapResolution = 5;
    [ObservableProperty] private int _defaultPaddingX = 5;
    [ObservableProperty] private int _defaultPaddingY = 5;
    [ObservableProperty] private int _defaultBufferX = 10;
    [ObservableProperty] private int _defaultBufferY = 10;
    [ObservableProperty] private int _defaultCanvasWidth = 600;
    [ObservableProperty] private int _defaultCanvasHeight = 400;
    [ObservableProperty] private bool _showRulers = true;
}

/// <summary>
/// Metadata for a single gump asset — user-assigned name, tags, and collection memberships.
/// </summary>
public class AssetMeta
{
    public int GumpId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> CollectionIds { get; set; } = [];

    /// <summary>
    /// Whether this asset was auto-tagged from data sources (containers.txt, tiledata.mul).
    /// </summary>
    public List<string> AutoTags { get; set; } = [];
}

/// <summary>
/// A user-defined asset collection with default tags.
/// </summary>
public partial class AssetCollection : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString();
    [ObservableProperty] private string _name = "Untitled Collection";
    public List<string> DefaultTags { get; set; } = [];
    public List<int> AssetIds { get; set; } = [];
}
