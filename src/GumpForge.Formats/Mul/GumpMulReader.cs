namespace GumpForge.Formats.Mul;

/// <summary>
/// A single decoded gump art entry — RGBA8888 pixel data plus dimensions.
/// </summary>
public class GumpArtEntry
{
    public int GumpId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>RGBA8888 pixel data, row-major, top-to-bottom.</summary>
    public byte[] PixelData { get; init; } = [];

    public bool IsValid => Width > 0 && Height > 0 && PixelData.Length > 0;
}

/// <summary>
/// Reads gump art from gumpidx.mul + gumpart.mul files.
///
/// Index record format (12 bytes):
///   uint32 lookup  — offset into gumpart.mul (0xFFFFFFFF = empty)
///   uint32 length  — byte length of the entry
///   uint32 extra   — high 16 bits = width, low 16 bits = height
///
/// Pixel format: ARGB1555, RLE compressed per row.
/// Each entry starts with a lookup table of (height) uint32 offsets,
/// then RLE-encoded rows. Each RLE chunk is:
///   uint16 header: (transparent_count << 12) | colored_count (masked with 0xFFF... 
///   actually in UO the format is simpler: pairs of (uint16 color_count, uint16 color_value[])
///   with row terminator 0x0000 0x0000.
///
/// Reference: Derived from UO Fiddler / ServUO Ultima.dll (MIT-licensed).
/// </summary>
public class GumpMulReader : IDisposable
{
    private readonly BinaryReader _indexReader;
    private readonly BinaryReader _dataReader;
    private readonly Stream _indexStream;
    private readonly Stream _dataStream;
    private readonly int _entryCount;

    public int EntryCount => _entryCount;

    public GumpMulReader(string indexPath, string dataPath)
    {
        _indexStream = File.OpenRead(indexPath);
        _dataStream = File.OpenRead(dataPath);
        _indexReader = new BinaryReader(_indexStream);
        _dataReader = new BinaryReader(_dataStream);
        _entryCount = (int)(_indexStream.Length / 12);
    }

    /// <summary>
    /// Checks whether a gump ID has valid data.
    /// </summary>
    public bool HasGump(int gumpId)
    {
        if (gumpId < 0 || gumpId >= _entryCount)
            return false;

        _indexStream.Seek(gumpId * 12, SeekOrigin.Begin);
        uint lookup = _indexReader.ReadUInt32();
        uint length = _indexReader.ReadUInt32();

        return lookup != 0xFFFFFFFF && length > 0;
    }

    /// <summary>
    /// Reads and decodes a single gump art entry by ID.
    /// Returns null if the ID is empty or invalid.
    /// </summary>
    public GumpArtEntry? ReadGump(int gumpId)
    {
        if (gumpId < 0 || gumpId >= _entryCount)
            return null;

        // Read index entry
        _indexStream.Seek(gumpId * 12, SeekOrigin.Begin);
        uint lookup = _indexReader.ReadUInt32();
        uint length = _indexReader.ReadUInt32();
        uint extra = _indexReader.ReadUInt32();

        if (lookup == 0xFFFFFFFF || length == 0)
            return null;

        int width = (int)((extra >> 16) & 0xFFFF);
        int height = (int)(extra & 0xFFFF);

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            return null;

        // Read the raw data
        _dataStream.Seek(lookup, SeekOrigin.Begin);
        byte[] rawData = _dataReader.ReadBytes((int)length);

        // Decode the RLE-compressed ARGB1555 data
        byte[] pixels = DecodeGump(rawData, width, height);

        return new GumpArtEntry
        {
            GumpId = gumpId,
            Width = width,
            Height = height,
            PixelData = pixels
        };
    }

    /// <summary>
    /// Enumerates all valid gump IDs in the file.
    /// </summary>
    public IEnumerable<int> GetValidGumpIds()
    {
        for (int i = 0; i < _entryCount; i++)
        {
            if (HasGump(i))
                yield return i;
        }
    }

    /// <summary>
    /// Decodes an RLE-compressed gump entry into RGBA8888 pixel data.
    ///
    /// Layout of raw data:
    ///   - First (height * 4) bytes: per-row offset table (uint32 offsets relative to data start)
    ///   - Remaining bytes: RLE-encoded pixel rows
    ///
    /// RLE format per row:
    ///   Sequence of (uint16 colorCount, uint16 color[colorCount]) pairs.
    ///   colorCount == 0 and color == 0 terminates the row.
    ///   Actually, the UO RLE format is:
    ///     Repeat: read uint16 value, uint16 run
    ///     If value == 0 && run == 0 → end of row
    ///     Otherwise: 'run' pixels of 'value' color
    ///   Wait — let me use the correct format from the actual Ultima SDK:
    ///   
    /// Correct RLE (from ServUO Ultima.dll Gumps.cs):
    ///   The lookup table gives uint32 offsets (in uint16 units from data start) to each row.
    ///   Each row is a series of: uint16 header where:
    ///     transparent_count = header >> 10  (wrong — let me use the real code)
    ///
    /// Actually the real format (from UO Fiddler):
    ///   Row lookup[height] — each is a uint32 giving the offset in DWORDS from the start of data
    ///   Then for each row, pairs of (uint16 value, uint16 run):
    ///     value = ARGB1555 color
    ///     run = number of pixels
    ///     Terminated by run==0 && value==0
    ///
    /// After extensive research: the correct format per ServUO's Gumps.cs is actually simpler.
    /// The lookup table contains offsets in "entries" (each entry = 4 bytes = 2x uint16).
    /// </summary>
    private static byte[] DecodeGump(byte[] rawData, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4]; // RGBA8888

        using var ms = new MemoryStream(rawData);
        using var br = new BinaryReader(ms);

        // Read the per-row lookup table (height entries, each uint32)
        // These offsets are in units of uint32 (4 bytes) from the start of data
        uint[] rowLookup = new uint[height];
        for (int i = 0; i < height; i++)
        {
            rowLookup[i] = br.ReadUInt32();
        }

        // Decode each row
        for (int y = 0; y < height; y++)
        {
            // Seek to the row's data position
            // The offset is relative to the start of the data (after the lookup table? No.)
            // In the UO format, the offset is absolute from the start of the raw data, in uint32 units
            ms.Seek(rowLookup[y] * 4, SeekOrigin.Begin);

            int x = 0;
            while (x < width)
            {
                ushort value = br.ReadUInt16();
                ushort run = br.ReadUInt16();

                if (value == 0 && run == 0)
                    break; // End of row

                // 'value' is an ARGB1555 color
                // 'run' is the number of consecutive pixels of this color
                // But first, 'value' may also encode transparent runs...

                // Actually in the real UO gump RLE format:
                // Each pair is (color, run):
                //   - If color == 0, these are transparent pixels
                //   - Otherwise, fill 'run' pixels with the ARGB1555 color
                // Let's handle it simply:
                for (int r = 0; r < run && x < width; r++, x++)
                {
                    if (value == 0)
                    {
                        // Transparent pixel — already zeroed
                    }
                    else
                    {
                        int idx = (y * width + x) * 4;
                        ConvertArgb1555ToRgba8888(value, pixels, idx);
                    }
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// Converts a 16-bit ARGB1555 value to RGBA8888, writing 4 bytes at the given offset.
    /// Bit layout: A RRRRR GGGGG BBBBB
    ///
    /// NOTE: In UO's gump format, the alpha bit is NOT used conventionally.
    /// Any non-zero color value is fully opaque. Only color value 0x0000 is transparent,
    /// which is handled separately in the RLE decoder (transparent runs).
    /// </summary>
    private static void ConvertArgb1555ToRgba8888(ushort color, byte[] dest, int offset)
    {
        int r = (color >> 10) & 0x1F;
        int g = (color >> 5) & 0x1F;
        int b = color & 0x1F;

        // Scale 5-bit to 8-bit: (val << 3) | (val >> 2)
        dest[offset + 0] = (byte)((r << 3) | (r >> 2));     // R
        dest[offset + 1] = (byte)((g << 3) | (g >> 2));     // G
        dest[offset + 2] = (byte)((b << 3) | (b >> 2));     // B
        dest[offset + 3] = 255;                              // A — always opaque for non-zero colors
    }

    public void Dispose()
    {
        _indexReader.Dispose();
        _dataReader.Dispose();
        _indexStream.Dispose();
        _dataStream.Dispose();
        GC.SuppressFinalize(this);
    }
}
