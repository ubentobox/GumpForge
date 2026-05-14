using System.Text.Json;
using System.Text.Json.Serialization;
using GumpForge.Core.Models;

namespace GumpForge.Core.Serialization;

/// <summary>
/// Handles JSON serialization of ShardProfile (.gfprofile files).
/// Default storage: %AppData%/GumpForge/Profiles/ with user-configurable override.
/// </summary>
public static class ProfileSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Gets the default profiles directory (%AppData%/GumpForge/Profiles/).
    /// </summary>
    public static string DefaultProfilesDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "GumpForge", "Profiles");
        }
    }

    /// <summary>
    /// Save a profile to disk as .gfprofile JSON.
    /// </summary>
    public static async Task SaveAsync(ShardProfile profile, string? overridePath = null)
    {
        profile.LastModified = DateTime.UtcNow;

        string filePath = overridePath ?? profile.ProfileFilePath;
        if (string.IsNullOrEmpty(filePath))
        {
            // Generate path in default directory
            Directory.CreateDirectory(DefaultProfilesDirectory);
            var safeName = SanitizeFileName(profile.ProfileName);
            filePath = Path.Combine(DefaultProfilesDirectory, $"{safeName}.gfprofile");
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        profile.ProfileFilePath = filePath;
    }

    /// <summary>
    /// Load a profile from a .gfprofile JSON file.
    /// </summary>
    public static async Task<ShardProfile?> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var json = await File.ReadAllTextAsync(filePath);
        var profile = JsonSerializer.Deserialize<ShardProfile>(json, JsonOptions);

        if (profile is not null)
            profile.ProfileFilePath = filePath;

        return profile;
    }

    /// <summary>
    /// Discover all .gfprofile files in the default profiles directory.
    /// </summary>
    public static List<string> DiscoverProfiles(string? customDir = null)
    {
        var profiles = new List<string>();
        var searchDir = customDir ?? DefaultProfilesDirectory;

        if (Directory.Exists(searchDir))
        {
            profiles.AddRange(Directory.GetFiles(searchDir, "*.gfprofile"));
        }

        return profiles;
    }

    /// <summary>
    /// Gets just the profile name from a file path without loading the full profile.
    /// </summary>
    public static string GetProfileNameFromPath(string filePath)
    {
        return Path.GetFileNameWithoutExtension(filePath);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "profile" : sanitized;
    }
}
