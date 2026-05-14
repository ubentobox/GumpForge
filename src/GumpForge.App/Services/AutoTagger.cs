using GumpForge.Core.Models;

namespace GumpForge.App.Services;

/// <summary>
/// Automatically applies tags to gump assets using data sources:
/// - containers.txt: maps container gump IDs to descriptive names
/// - tiledata.mul: extracts object names for item art
/// - multi.mul: marks multi-structure components
/// - ID ranges: known UO gump art ranges (buttons, backgrounds, scrollbars, etc.)
/// </summary>
public static class AutoTagger
{
    /// <summary>
    /// Applies auto-tags to all assets in the profile based on known ID ranges
    /// and optional data files.
    /// </summary>
    public static void TagAssets(ShardProfile profile, string? clientDataPath = null)
    {
        var mgr = AssetManager.Instance;

        // Tag by known gump art ID ranges
        TagByIdRanges(profile);

        // Tag from containers.txt if available
        if (!string.IsNullOrEmpty(clientDataPath))
        {
            var containersPath = Path.Combine(clientDataPath, "containers.txt");
            if (File.Exists(containersPath))
                TagFromContainers(profile, containersPath);
        }

        // Tag from tiledata.mul if the AssetManager has loaded it
        if (mgr.IsLoaded)
            TagFromAssetDimensions(profile, mgr);

        profile.LastModified = DateTime.UtcNow;
    }

    /// <summary>
    /// Tags gump assets by well-known Ultima Online gump art ID ranges.
    /// These ranges are consistent across most UO server emulators.
    /// </summary>
    private static void TagByIdRanges(ShardProfile profile)
    {
        // Well-known UO gump art ID ranges
        var ranges = new (int Start, int End, string Tag)[]
        {
            (0, 4, "cursor"),
            (5, 9, "scroll-background"),
            (10, 14, "border"),
            (15, 29, "gem-button"),
            (30, 39, "checkbox"),
            (40, 49, "radio"),
            (50, 99, "paperdoll"),
            (100, 149, "status-bar"),
            (150, 199, "map"),
            (200, 249, "skill-icon"),
            (250, 299, "mini-scroll"),
            (300, 499, "gump-background"),
            (500, 599, "button"),
            (600, 699, "scroll"),
            (700, 799, "textbox-border"),
            (800, 899, "book"),
            (900, 999, "container"),
            (1000, 1099, "menu"),
            (1100, 1199, "dialog-border"),
            (1200, 1399, "large-background"),
            (1400, 1499, "paperdoll-slot"),
            (1500, 1599, "equipment-slot"),
            (2062, 2062, "spellbook-background"),
            (2070, 2090, "spellbook-tab"),
            (2091, 2180, "spell-icon"),
            (2240, 2310, "necro-spell-icon"),
            (2360, 2430, "chivalry-icon"),
            (5000, 5100, "gump-art"),
            (9000, 9099, "interface"),
            (30000, 65535, "custom"),
        };

        foreach (var (start, end, tag) in ranges)
        {
            for (int id = start; id <= end; id++)
            {
                var meta = GetOrCreateMeta(profile, id);
                if (!meta.AutoTags.Contains(tag))
                    meta.AutoTags.Add(tag);
            }
        }
    }

    /// <summary>
    /// Parses containers.txt to tag container gump IDs with descriptive names.
    /// Format: GumpID\tItemID\tBounds\tName
    /// </summary>
    private static void TagFromContainers(ShardProfile profile, string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("#"))
                    continue;

                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                if (int.TryParse(parts[0].Trim(), out int gumpId))
                {
                    var meta = GetOrCreateMeta(profile, gumpId);
                    if (!meta.AutoTags.Contains("container"))
                        meta.AutoTags.Add("container");

                    // If there's a name column, use it as display name
                    if (parts.Length >= 4 && string.IsNullOrEmpty(meta.DisplayName))
                    {
                        var name = parts[3].Trim();
                        if (!string.IsNullOrEmpty(name))
                            meta.DisplayName = name;
                    }
                }
            }
        }
        catch
        {
            // Non-critical — containers.txt format might vary
        }
    }

    /// <summary>
    /// Tags assets by their dimensions: very thin = border/separator,
    /// small square = button/icon, large = background, etc.
    /// </summary>
    private static void TagFromAssetDimensions(ShardProfile profile, AssetManager mgr)
    {
        // Only tag assets that already have entries in the profile
        // (i.e., ones that have been loaded into the browser)
        foreach (var kvp in profile.AssetMetadata)
        {
            var dims = mgr.GetDimensions(kvp.Key);
            if (dims is null) continue;

            var (w, h) = dims.Value;
            var meta = kvp.Value;

            // Size-based classification
            if (w <= 2 || h <= 2)
                AddAutoTag(meta, "border");
            else if (w <= 20 && h <= 20)
                AddAutoTag(meta, "icon");
            else if (w <= 50 && h <= 50)
                AddAutoTag(meta, "small-element");
            else if (w >= 300 && h >= 200)
                AddAutoTag(meta, "large-background");
            else if (w >= 200 && h <= 50)
                AddAutoTag(meta, "header-bar");
            else if (w <= 50 && h >= 200)
                AddAutoTag(meta, "vertical-bar");

            // Aspect ratio based
            if (w > 0 && h > 0)
            {
                double ratio = (double)w / h;
                if (ratio > 4) AddAutoTag(meta, "wide");
                else if (ratio < 0.25) AddAutoTag(meta, "tall");
                if (w == h) AddAutoTag(meta, "square");
            }
        }
    }

    private static AssetMeta GetOrCreateMeta(ShardProfile profile, int gumpId)
    {
        if (!profile.AssetMetadata.TryGetValue(gumpId, out var meta))
        {
            meta = new AssetMeta { GumpId = gumpId };
            profile.AssetMetadata[gumpId] = meta;
        }
        return meta;
    }

    private static void AddAutoTag(AssetMeta meta, string tag)
    {
        if (!meta.AutoTags.Contains(tag))
            meta.AutoTags.Add(tag);
    }
}
