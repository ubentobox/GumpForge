using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace GumpForge.Core.Models;

/// <summary>
/// Root model for a Shard Profile — stores editor preferences, client paths,
/// asset metadata (tags, aliases), collection definitions, and auto-tag rules.
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
    /// User-editable auto-tag rules. Each maps an ID range to a tag name.
    /// Populated with defaults on first creation.
    /// </summary>
    public ObservableCollection<TagRule> TagRules { get; set; } = [];

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
    /// Tags applied automatically from data sources (containers.txt, tiledata.mul, ID ranges).
    /// </summary>
    public List<string> AutoTags { get; set; } = [];

    /// <summary>
    /// Auto-tags that the user explicitly removed. These are permanently suppressed
    /// and will not be re-applied when the auto-tagger runs again.
    /// </summary>
    public List<string> SuppressedAutoTags { get; set; } = [];
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

    /// <summary>
    /// Tags that automatically include matching assets in this collection.
    /// Effective members = AssetIds ∪ {assets matching AutoIncludeTags} − ExcludedAssetIds.
    /// </summary>
    public List<string> AutoIncludeTags { get; set; } = [];

    /// <summary>
    /// Assets manually removed from this collection. Prevents auto-include from re-adding them.
    /// </summary>
    public List<int> ExcludedAssetIds { get; set; } = [];
}

/// <summary>
/// An auto-tag rule that maps a gump ID range to a tag.
/// Users can edit, disable, add, and remove rules.
/// </summary>
public partial class TagRule : ObservableObject
{
    [ObservableProperty] private int _startId;
    [ObservableProperty] private int _endId;
    [ObservableProperty] private string _tag = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
}
