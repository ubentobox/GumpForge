using GumpForge.Core.Models;

namespace GumpForge.App.Services;

/// <summary>
/// Automatically applies tags to gump assets using data sources:
/// - User-editable TagRules (ID range → tag mappings)
/// - containers.txt: maps container gump IDs to descriptive names
/// - Dimension analysis: classifies by asset size/aspect ratio
/// Respects SuppressedAutoTags so removed tags are never re-applied.
/// </summary>
public static class AutoTagger
{
    /// <summary>
    /// Returns the default set of auto-tag rules based on well-known UO gump art ID ranges.
    /// </summary>
    public static List<TagRule> GetDefaultRules() =>
    [
        new() { StartId = 0,     EndId = 4,     Tag = "cursor" },
        new() { StartId = 5,     EndId = 9,     Tag = "scroll-background" },
        new() { StartId = 10,    EndId = 14,    Tag = "border" },
        new() { StartId = 15,    EndId = 29,    Tag = "gem-button" },
        new() { StartId = 30,    EndId = 39,    Tag = "checkbox" },
        new() { StartId = 40,    EndId = 49,    Tag = "radio" },
        new() { StartId = 50,    EndId = 99,    Tag = "paperdoll" },
        new() { StartId = 100,   EndId = 149,   Tag = "status-bar" },
        new() { StartId = 150,   EndId = 199,   Tag = "map" },
        new() { StartId = 200,   EndId = 249,   Tag = "skill-icon" },
        new() { StartId = 250,   EndId = 299,   Tag = "mini-scroll" },
        new() { StartId = 300,   EndId = 499,   Tag = "gump-background" },
        new() { StartId = 500,   EndId = 599,   Tag = "button" },
        new() { StartId = 600,   EndId = 699,   Tag = "scroll" },
        new() { StartId = 700,   EndId = 799,   Tag = "textbox-border" },
        new() { StartId = 800,   EndId = 899,   Tag = "book" },
        new() { StartId = 900,   EndId = 999,   Tag = "container" },
        new() { StartId = 1000,  EndId = 1099,  Tag = "menu" },
        new() { StartId = 1100,  EndId = 1199,  Tag = "dialog-border" },
        new() { StartId = 1200,  EndId = 1399,  Tag = "large-background" },
        new() { StartId = 1400,  EndId = 1499,  Tag = "paperdoll-slot" },
        new() { StartId = 1500,  EndId = 1599,  Tag = "equipment-slot" },
        new() { StartId = 2062,  EndId = 2062,  Tag = "spellbook-background" },
        new() { StartId = 2070,  EndId = 2090,  Tag = "spellbook-tab" },
        new() { StartId = 2091,  EndId = 2180,  Tag = "spell-icon" },
        new() { StartId = 2240,  EndId = 2310,  Tag = "necro-spell-icon" },
        new() { StartId = 2360,  EndId = 2430,  Tag = "chivalry-icon" },
        new() { StartId = 5000,  EndId = 5100,  Tag = "gump-art" },
        new() { StartId = 9000,  EndId = 9099,  Tag = "interface" },
        new() { StartId = 30000, EndId = 65535,  Tag = "custom" },
    ];

    /// <summary>
    /// Applies auto-tags to all assets in the profile.
    /// Populates default rules if none exist. Respects suppressions.
    /// </summary>
    public static void TagAssets(ShardProfile profile, string? clientDataPath = null)
    {
        // Populate default rules on first run
        if (profile.TagRules.Count == 0)
        {
            foreach (var rule in GetDefaultRules())
                profile.TagRules.Add(rule);
        }

        var mgr = AssetManager.Instance;

        // Tag by user-editable ID range rules
        TagByIdRanges(profile);

        // Tag from containers.txt if available
        if (!string.IsNullOrEmpty(clientDataPath))
        {
            var containersPath = Path.Combine(clientDataPath, "containers.txt");
            if (File.Exists(containersPath))
                TagFromContainers(profile, containersPath);
        }

        // Tag from asset dimensions if loaded
        if (mgr.IsLoaded)
            TagFromAssetDimensions(profile, mgr);

        profile.LastModified = DateTime.UtcNow;
    }

    /// <summary>
    /// Tags gump assets using the profile's editable TagRules collection.
    /// Only applies enabled rules. Respects SuppressedAutoTags.
    /// </summary>
    private static void TagByIdRanges(ShardProfile profile)
    {
        foreach (var rule in profile.TagRules)
        {
            if (!rule.IsEnabled || string.IsNullOrWhiteSpace(rule.Tag))
                continue;

            for (int id = rule.StartId; id <= rule.EndId; id++)
            {
                var meta = GetOrCreateMeta(profile, id);
                AddAutoTagIfNotSuppressed(meta, rule.Tag);
            }
        }
    }

    /// <summary>
    /// Parses containers.txt to tag container gump IDs with descriptive names.
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
                    AddAutoTagIfNotSuppressed(meta, "container");

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
    /// Tags assets by dimensions: thin = border, small = icon, large = background, etc.
    /// </summary>
    private static void TagFromAssetDimensions(ShardProfile profile, AssetManager mgr)
    {
        foreach (var kvp in profile.AssetMetadata)
        {
            var dims = mgr.GetDimensions(kvp.Key);
            if (dims is null) continue;

            var (w, h) = dims.Value;
            var meta = kvp.Value;

            if (w <= 2 || h <= 2)
                AddAutoTagIfNotSuppressed(meta, "border");
            else if (w <= 20 && h <= 20)
                AddAutoTagIfNotSuppressed(meta, "icon");
            else if (w <= 50 && h <= 50)
                AddAutoTagIfNotSuppressed(meta, "small-element");
            else if (w >= 300 && h >= 200)
                AddAutoTagIfNotSuppressed(meta, "large-background");
            else if (w >= 200 && h <= 50)
                AddAutoTagIfNotSuppressed(meta, "header-bar");
            else if (w <= 50 && h >= 200)
                AddAutoTagIfNotSuppressed(meta, "vertical-bar");

            if (w > 0 && h > 0)
            {
                double ratio = (double)w / h;
                if (ratio > 4) AddAutoTagIfNotSuppressed(meta, "wide");
                else if (ratio < 0.25) AddAutoTagIfNotSuppressed(meta, "tall");
                if (w == h) AddAutoTagIfNotSuppressed(meta, "square");
            }
        }
    }

    /// <summary>
    /// Removes an auto-tag from an asset and adds it to the suppression list
    /// so it won't be re-applied when the auto-tagger runs again.
    /// </summary>
    public static void SuppressAutoTag(AssetMeta meta, string tag)
    {
        meta.AutoTags.Remove(tag);
        if (!meta.SuppressedAutoTags.Contains(tag))
            meta.SuppressedAutoTags.Add(tag);
    }

    /// <summary>
    /// Unsuppresses an auto-tag so the next auto-tagger run can re-apply it.
    /// </summary>
    public static void UnsuppressAutoTag(AssetMeta meta, string tag)
    {
        meta.SuppressedAutoTags.Remove(tag);
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

    /// <summary>
    /// Adds an auto-tag only if it's not in the suppression list.
    /// </summary>
    private static void AddAutoTagIfNotSuppressed(AssetMeta meta, string tag)
    {
        if (meta.SuppressedAutoTags.Contains(tag))
            return;
        if (!meta.AutoTags.Contains(tag))
            meta.AutoTags.Add(tag);
    }
}
