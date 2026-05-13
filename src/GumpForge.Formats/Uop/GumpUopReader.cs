namespace GumpForge.Formats.Uop;

/// <summary>
/// Gump-specific UOP reader that wraps the generic UopReader to provide
/// gump art access from gumpartLegacyMUL.uop files.
///
/// In newer UO clients, gumps are stored inside a UOP archive with entries
/// named like "build/gumpartlegacymul/{index:D8}.tga". Each entry contains
/// the same RLE-compressed gump data as in gumpart.mul — just wrapped
/// in UOP container blocks.
///
/// This reader pre-computes hashes for gump IDs 0–65535 and builds an
/// index for fast lookup.
/// </summary>
public class GumpUopReader : IDisposable
{
    private readonly UopReader _uopReader;
    private readonly Dictionary<int, ulong> _gumpIdToHash = [];

    /// <summary>
    /// The filename pattern used inside UOP gump archives.
    /// </summary>
    private const string GumpPattern = "build/gumpartlegacymul/{0:D8}.tga";

    /// <summary>
    /// Maximum number of gump IDs to pre-hash for lookup.
    /// </summary>
    private const int MaxGumpId = 65536;

    public int EntryCount => _gumpIdToHash.Count;

    public GumpUopReader(string uopPath)
    {
        _uopReader = new UopReader(uopPath);

        // Pre-compute hashes and build gumpId → hash mapping
        for (int i = 0; i < MaxGumpId; i++)
        {
            string filename = string.Format(GumpPattern, i);
            ulong hash = UopReader.HashFilename(filename);

            if (_uopReader.Entries.ContainsKey(hash))
            {
                _gumpIdToHash[i] = hash;
            }
        }
    }

    /// <summary>
    /// Check if a gump ID exists in this UOP archive.
    /// </summary>
    public bool HasGump(int gumpId) => _gumpIdToHash.ContainsKey(gumpId);

    /// <summary>
    /// Get all valid gump IDs in this archive.
    /// </summary>
    public IEnumerable<int> GetValidGumpIds() => _gumpIdToHash.Keys.OrderBy(k => k);

    /// <summary>
    /// Read the raw (decompressed) gump data for a given ID.
    /// The returned bytes contain the same RLE-compressed gump art format
    /// as found in gumpart.mul — including the row lookup table and pixel runs.
    /// </summary>
    public byte[]? ReadRawGumpData(int gumpId)
    {
        if (!_gumpIdToHash.TryGetValue(gumpId, out ulong hash))
            return null;

        return _uopReader.ReadEntry(hash);
    }

    /// <summary>
    /// Read a gump and decode it to RGBA8888 pixel data, similar to GumpMulReader.
    /// Returns null if the gump doesn't exist or decoding fails.
    /// </summary>
    public GumpArtEntry? ReadGump(int gumpId)
    {
        byte[]? rawData = ReadRawGumpData(gumpId);
        if (rawData is null || rawData.Length < 8)
            return null;

        try
        {
            return DecodeGumpFromRaw(rawData, gumpId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decode raw gump data (same RLE format as MUL) into an RGBA8888 pixel entry.
    /// The UOP entry wraps the same binary format as gumpart.mul data:
    ///   - First 4 bytes: width (int32)
    ///   - Next 4 bytes: height (int32)
    ///   - Then: row lookup table (height × int32 offsets)
    ///   - Then: RLE-encoded pixel runs per row
    /// </summary>
    private static GumpArtEntry? DecodeGumpFromRaw(byte[] raw, int gumpId)
    {
        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);

        // UOP gump entries store width/height at the start of the data block
        int width = br.ReadInt32();
        int height = br.ReadInt32();

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            return null;

        // Row lookup table
        int[] rowLookup = new int[height];
        for (int i = 0; i < height; i++)
            rowLookup[i] = br.ReadInt32();

        byte[] pixels = new byte[width * height * 4];
        int dataStart = 8 + height * 4; // after header + lookup table

        for (int y = 0; y < height; y++)
        {
            ms.Seek(dataStart + rowLookup[y] * 4, SeekOrigin.Begin);
            int x = 0;

            while (x < width)
            {
                ushort val = br.ReadUInt16();
                ushort run = br.ReadUInt16();

                if (val == 0 && run == 0) break; // Row terminator

                if (val == 0)
                {
                    // Transparency run
                    x += run;
                }
                else
                {
                    // Color run: 'val' is actually the color, 'run' is repeat count
                    // Wait — standard UO gump RLE:
                    // Each pair is (color16, runLength16)
                    // val=color in ARGB1555, run=pixel count
                    for (int p = 0; p < run && x < width; p++, x++)
                    {
                        int idx = (y * width + x) * 4;
                        // Decode ARGB1555 to RGBA8888
                        int r = (val >> 10) & 0x1F;
                        int g = (val >> 5) & 0x1F;
                        int b = val & 0x1F;
                        pixels[idx] = (byte)((r << 3) | (r >> 2));
                        pixels[idx + 1] = (byte)((g << 3) | (g >> 2));
                        pixels[idx + 2] = (byte)((b << 3) | (b >> 2));
                        pixels[idx + 3] = (val != 0) ? (byte)255 : (byte)0;
                    }
                }
            }
        }

        return new GumpArtEntry
        {
            GumpId = gumpId,
            Width = width,
            Height = height,
            PixelData = pixels,
            IsValid = true
        };
    }

    public void Dispose()
    {
        _uopReader.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Decoded gump art entry with RGBA8888 pixel data.
/// </summary>
public class GumpArtEntry
{
    public int GumpId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] PixelData { get; init; } = [];
    public bool IsValid { get; init; }
}
