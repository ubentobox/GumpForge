using Avalonia.Media.Imaging;
using GumpForge.App.Helpers;
using GumpForge.Core.Services;
using GumpForge.Formats.Cliloc;
using GumpForge.Formats.Fonts;
using GumpForge.Formats.Hues;
using GumpForge.Formats.Mul;
using GumpForge.Formats.Uop;
using System.Collections.Concurrent;

namespace GumpForge.App.Services;

/// <summary>
/// Singleton service that manages access to UO client data (MUL files).
/// Provides bitmap lookup for gump IDs used by both the canvas and asset browser.
/// Thread-safe: reads happen on background threads, bitmaps are cached.
/// </summary>
public class AssetManager : IDisposable
{
    private static AssetManager? _instance;
    public static AssetManager Instance => _instance ??= new AssetManager();

    private GumpMulReader? _reader;
    private GumpUopReader? _uopReader;
    private HuesReader? _huesReader;
    private ClilocReader? _clilocReader;
    private FontsReader? _fontsReader;
    private GumpMulWriter? _writer;
    private readonly AssetCache _cache = new();
    private readonly ConcurrentDictionary<int, WriteableBitmap?> _bitmapCache = new();
    private readonly ConcurrentDictionary<(int GumpId, int HueId), WriteableBitmap?> _huedBitmapCache = new();
    private string? _dataFolder;

    public bool IsLoaded => _reader is not null || _uopReader is not null;
    public string? DataFolder => _dataFolder;

    /// <summary>Indicates the loaded data format ("MUL", "UOP", or null).</summary>
    public string? DataFormat => _reader is not null ? "MUL" : _uopReader is not null ? "UOP" : null;

    /// <summary>
    /// Initialize from a client data folder containing Gumpidx.mul and Gumpart.mul.
    /// </summary>
    public void LoadFromFolder(string dataFolder)
    {
        _dataFolder = dataFolder;

        var indexFile = FindFile(dataFolder, "Gumpidx.mul");
        var dataFile = FindFile(dataFolder, "Gumpart.mul");

        if (indexFile is not null && dataFile is not null)
        {
            // MUL files found — use legacy reader
            _reader?.Dispose();
            _reader = new GumpMulReader(indexFile, dataFile);
            _uopReader?.Dispose();
            _uopReader = null;
        }
        else
        {
            // Try UOP fallback: gumpartLegacyMUL.uop
            var uopFile = FindFile(dataFolder, "gumpartLegacyMUL.uop");
            if (uopFile is not null)
            {
                try
                {
                    _reader?.Dispose();
                    _reader = null;
                    _uopReader?.Dispose();
                    _uopReader = new GumpUopReader(uopFile);
                }
                catch
                {
                    _uopReader = null;
                    return;
                }
            }
            else
            {
                return; // No gump data found
            }
        }
        _bitmapCache.Clear();
        _huedBitmapCache.Clear();

        // Try to load hues.mul
        var huesFile = FindFile(dataFolder, "hues.mul");
        if (huesFile is not null)
        {
            try { _huesReader = new HuesReader(huesFile); }
            catch { _huesReader = null; }
        }

        // Try to load cliloc.enu
        var clilocFile = FindFile(dataFolder, "cliloc.enu");
        if (clilocFile is not null)
        {
            try { _clilocReader = new ClilocReader(clilocFile); }
            catch { _clilocReader = null; }
        }

        // Try to load fonts.mul
        var fontsFile = FindFile(dataFolder, "fonts.mul");
        if (fontsFile is not null)
        {
            try { _fontsReader = new FontsReader(fontsFile); }
            catch { _fontsReader = null; }
        }
    }

    /// <summary>
    /// Check if hues are loaded.
    /// </summary>
    public bool HasHues => _huesReader is not null;
    public int HueCount => _huesReader?.Hues.Count ?? 0;

    /// <summary>
    /// Check if cliloc is loaded.
    /// </summary>
    public bool HasCliloc => _clilocReader is not null;
    public int ClilocCount => _clilocReader?.Count ?? 0;

    /// <summary>
    /// Get the localized text for a cliloc ID.
    /// </summary>
    public string? GetClilocText(int clilocId) => _clilocReader?.GetText(clilocId);

    /// <summary>
    /// Check if fonts are loaded.
    /// </summary>
    public bool HasFonts => _fontsReader is not null;
    public int FontCount => _fontsReader?.FontCount ?? 0;

    /// <summary>
    /// Measure the pixel width of a text string in the given UO font.
    /// </summary>
    public int MeasureTextWidth(int fontId, string text) =>
        _fontsReader?.MeasureWidth(fontId, text) ?? text.Length * 8;

    /// <summary>
    /// Get the height of a UO font in pixels.
    /// </summary>
    public int GetFontHeight(int fontId) =>
        _fontsReader?.GetFontHeight(fontId) ?? 14;

    /// <summary>
    /// Save a gump entry to the MUL files. Creates a writer if needed.
    /// </summary>
    public void SaveGump(int gumpId, byte[] pixelData, int width, int height)
    {
        if (_dataFolder is null)
            throw new InvalidOperationException("No client data folder loaded.");

        var indexFile = FindFile(_dataFolder, "Gumpidx.mul");
        var dataFile = FindFile(_dataFolder, "Gumpart.mul");
        if (indexFile is null || dataFile is null)
            throw new InvalidOperationException("Cannot find MUL files.");

        _writer?.Dispose();
        _writer = new GumpMulWriter(indexFile, dataFile);
        _writer.WriteGump(gumpId, pixelData, width, height);
        _writer.Dispose();
        _writer = null;

        // Invalidate bitmap cache for this gump
        _bitmapCache.TryRemove(gumpId, out _);
    }

    /// <summary>
    /// Check if a gump ID exists in the loaded MUL files.
    /// </summary>
    public bool HasGump(int gumpId) => _reader?.HasGump(gumpId) ?? _uopReader?.HasGump(gumpId) ?? false;

    /// <summary>
    /// Remove a gump entry from the MUL files.
    /// </summary>
    public void RemoveGump(int gumpId)
    {
        if (_dataFolder is null) return;
        var indexFile = FindFile(_dataFolder, "Gumpidx.mul");
        var dataFile = FindFile(_dataFolder, "Gumpart.mul");
        if (indexFile is null || dataFile is null) return;

        using var writer = new GumpMulWriter(indexFile, dataFile);
        writer.RemoveGump(gumpId);
        _bitmapCache.TryRemove(gumpId, out _);
    }

    /// <summary>
    /// Get an Avalonia WriteableBitmap for a gump ID. Returns null if not available.
    /// Caches bitmaps after first decode.
    /// </summary>
    public WriteableBitmap? GetBitmap(int gumpId)
    {
        if (_reader is null && _uopReader is null) return null;

        if (_bitmapCache.TryGetValue(gumpId, out var cached))
            return cached;

        // Try MUL reader first, then UOP
        byte[]? pixelData = null;
        int width = 0, height = 0;
        bool valid = false;

        if (_reader is not null)
        {
            var entry = _reader.ReadGump(gumpId);
            if (entry is not null && entry.IsValid)
            {
                pixelData = entry.PixelData;
                width = entry.Width;
                height = entry.Height;
                valid = true;
            }
        }
        else if (_uopReader is not null)
        {
            var entry = _uopReader.ReadGump(gumpId);
            if (entry is not null && entry.IsValid)
            {
                pixelData = entry.PixelData;
                width = entry.Width;
                height = entry.Height;
                valid = true;
            }
        }

        if (!valid || pixelData is null)
        {
            _bitmapCache[gumpId] = null;
            return null;
        }

        var bitmap = BitmapHelper.CreateBitmap(pixelData, width, height);
        _bitmapCache[gumpId] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// Get a hue-tinted bitmap for a gump. Applies UO hue color remapping.
    /// </summary>
    public WriteableBitmap? GetHuedBitmap(int gumpId, int hueId)
    {
        if (hueId <= 0) return GetBitmap(gumpId);
        if (_huesReader is null) return GetBitmap(gumpId);
        if (_reader is null && _uopReader is null) return GetBitmap(gumpId);

        var key = (gumpId, hueId);
        if (_huedBitmapCache.TryGetValue(key, out var cached))
            return cached;

        // Read pixel data from MUL or UOP
        byte[]? pixelData = null;
        int width = 0, height = 0;
        bool valid = false;

        if (_reader is not null)
        {
            var entry = _reader.ReadGump(gumpId);
            if (entry is not null && entry.IsValid)
                (pixelData, width, height, valid) = (entry.PixelData, entry.Width, entry.Height, true);
        }
        else if (_uopReader is not null)
        {
            var entry = _uopReader.ReadGump(gumpId);
            if (entry is not null && entry.IsValid)
                (pixelData, width, height, valid) = (entry.PixelData, entry.Width, entry.Height, true);
        }

        if (!valid || pixelData is null)
        {
            _huedBitmapCache[key] = null;
            return null;
        }

        var hue = _huesReader.GetHue(hueId);
        if (hue is null) return GetBitmap(gumpId);

        var huedPixels = ApplyHue(pixelData, hue);
        var bitmap = BitmapHelper.CreateBitmap(huedPixels, width, height);
        _huedBitmapCache[key] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// Get the ARGB preview colors for a hue (for display in properties panel).
    /// Returns 32 ARGB colors.
    /// </summary>
    public uint[]? GetHueColors(int hueId)
    {
        var hue = _huesReader?.GetHue(hueId);
        if (hue is null) return null;

        var colors = new uint[32];
        for (int i = 0; i < 32; i++)
            colors[i] = Argb1555ToArgb8888(hue.Colors[i]);
        return colors;
    }

    /// <summary>
    /// Get a thumbnail-sized bitmap for the asset browser.
    /// </summary>
    public WriteableBitmap? GetThumbnail(int gumpId, int maxSize = 56)
    {
        if (_reader is null && _uopReader is null) return null;

        byte[]? pixelData = null;
        int width = 0, height = 0;

        if (_reader is not null)
        {
            var entry = _reader.ReadGump(gumpId);
            if (entry is not null && entry.IsValid)
                (pixelData, width, height) = (entry.PixelData, entry.Width, entry.Height);
        }
        else if (_uopReader is not null)
        {
            var entry = _uopReader.ReadGump(gumpId);
            if (entry is not null && entry.IsValid)
                (pixelData, width, height) = (entry.PixelData, entry.Width, entry.Height);
        }

        if (pixelData is null) return null;
        return BitmapHelper.CreateThumbnail(pixelData, width, height, maxSize);
    }

    /// <summary>
    /// Get the dimensions of a gump without decoding the full bitmap.
    /// </summary>
    public (int Width, int Height)? GetDimensions(int gumpId)
    {
        if (_reader is not null)
        {
            var entry = _reader.ReadGump(gumpId);
            if (entry is not null && entry.IsValid)
                return (entry.Width, entry.Height);
        }
        else if (_uopReader is not null)
        {
            var entry = _uopReader.ReadGump(gumpId);
            if (entry is not null && entry.IsValid)
                return (entry.Width, entry.Height);
        }
        return null;
    }

    /// <summary>Get the total number of entries in the index.</summary>
    public int EntryCount => _reader?.EntryCount ?? _uopReader?.EntryCount ?? 0;

    /// <summary>Enumerate all valid gump IDs.</summary>
    public IEnumerable<int> GetValidGumpIds() =>
        _reader?.GetValidGumpIds() ?? _uopReader?.GetValidGumpIds() ?? [];

    public void Dispose()
    {
        _reader?.Dispose();
        _uopReader?.Dispose();
        _writer?.Dispose();
        _bitmapCache.Clear();
        _huedBitmapCache.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Apply a hue to BGRA pixel data. UO hue tables remap grayscale (based on
    /// brightness) to the 32-color hue palette.
    /// </summary>
    private static byte[] ApplyHue(byte[] pixelData, HueEntry hue)
    {
        var result = new byte[pixelData.Length];
        Buffer.BlockCopy(pixelData, 0, result, 0, pixelData.Length);

        for (int i = 0; i < result.Length; i += 4)
        {
            byte b = result[i];
            byte g = result[i + 1];
            byte r = result[i + 2];
            byte a = result[i + 3];
            if (a == 0) continue;

            // Calculate luminance and map to hue index (0-31)
            int lum = (r * 77 + g * 150 + b * 29) >> 8; // 0-255
            int hueIndex = lum >> 3; // 0-31
            if (hueIndex > 31) hueIndex = 31;

            uint argb = Argb1555ToArgb8888(hue.Colors[hueIndex]);
            result[i + 2] = (byte)((argb >> 16) & 0xFF); // R
            result[i + 1] = (byte)((argb >> 8) & 0xFF);  // G
            result[i] = (byte)(argb & 0xFF);              // B
            // Keep original alpha
        }
        return result;
    }

    private static uint Argb1555ToArgb8888(ushort color)
    {
        uint r = (uint)((color >> 10) & 0x1F) * 255 / 31;
        uint g = (uint)((color >> 5) & 0x1F) * 255 / 31;
        uint b = (uint)(color & 0x1F) * 255 / 31;
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }

    private static string? FindFile(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path)) return path;
        try
        {
            return Directory.GetFiles(folder)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }
}
