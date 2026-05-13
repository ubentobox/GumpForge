namespace GumpForge.Formats.Mul;

/// <summary>
/// Writes gump art back to gumpidx.mul + gumpart.mul files.
///
/// Encodes RGBA8888 pixel data into UO's RLE-compressed ARGB1555 format
/// and updates both the index and data files.
///
/// The writer supports:
///   - Replacing existing gump entries
///   - Adding new entries at empty slots
///   - Appending data to the end of the data file
///
/// Reference: Inverse of GumpMulReader. Format matches UO Fiddler / ServUO (MIT).
/// </summary>
public class GumpMulWriter : IDisposable
{
    private readonly FileStream _indexStream;
    private readonly FileStream _dataStream;
    private readonly BinaryWriter _indexWriter;
    private readonly BinaryWriter _dataWriter;
    private readonly int _entryCount;

    public GumpMulWriter(string indexPath, string dataPath)
    {
        _indexStream = File.Open(indexPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        _dataStream = File.Open(dataPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        _indexWriter = new BinaryWriter(_indexStream);
        _dataWriter = new BinaryWriter(_dataStream);
        _entryCount = (int)(_indexStream.Length / 12);
    }

    /// <summary>
    /// Write a gump entry at the given ID. Appends the encoded data to the
    /// end of the data file and updates the index entry.
    /// </summary>
    /// <param name="gumpId">The gump ID slot to write to.</param>
    /// <param name="pixelData">RGBA8888 pixel data, row-major, top-to-bottom.</param>
    /// <param name="width">Gump width in pixels.</param>
    /// <param name="height">Gump height in pixels.</param>
    public void WriteGump(int gumpId, byte[] pixelData, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixelData.Length < width * height * 4)
            throw new ArgumentException("Invalid dimensions or pixel data length.");

        // Encode pixel data to RLE
        byte[] encodedData = EncodeGump(pixelData, width, height);

        // Append encoded data to end of data file
        long dataOffset = _dataStream.Length;
        _dataStream.Seek(0, SeekOrigin.End);
        _dataWriter.Write(encodedData);

        // Ensure index file is large enough
        long requiredIndexSize = (gumpId + 1) * 12L;
        if (_indexStream.Length < requiredIndexSize)
        {
            _indexStream.SetLength(requiredIndexSize);
        }

        // Write index entry
        _indexStream.Seek(gumpId * 12, SeekOrigin.Begin);
        _indexWriter.Write((uint)dataOffset);           // lookup
        _indexWriter.Write((uint)encodedData.Length);    // length
        uint extra = ((uint)width << 16) | ((uint)height & 0xFFFF);
        _indexWriter.Write(extra);                       // extra = (w << 16) | h

        _indexWriter.Flush();
        _dataWriter.Flush();
    }

    /// <summary>
    /// Remove a gump entry by marking it as empty in the index.
    /// </summary>
    public void RemoveGump(int gumpId)
    {
        if (gumpId < 0) return;

        long requiredIndexSize = (gumpId + 1) * 12L;
        if (_indexStream.Length < requiredIndexSize) return;

        _indexStream.Seek(gumpId * 12, SeekOrigin.Begin);
        _indexWriter.Write(0xFFFFFFFF); // lookup = invalid
        _indexWriter.Write((uint)0);    // length = 0
        _indexWriter.Write((uint)0);    // extra = 0
        _indexWriter.Flush();
    }

    /// <summary>
    /// Encodes RGBA8888 pixel data into UO's gump RLE format.
    ///
    /// Output layout:
    ///   uint32[height] rowLookup — offset of each row (in uint32 units from data start)
    ///   Then for each row: sequence of (uint16 color, uint16 run) pairs,
    ///   terminated by (0, 0).
    /// </summary>
    private static byte[] EncodeGump(byte[] pixelData, int width, int height)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Reserve space for the row lookup table
        long lookupStart = ms.Position;
        for (int y = 0; y < height; y++)
            bw.Write((uint)0); // placeholder

        // Encode each row
        uint[] rowOffsets = new uint[height];

        for (int y = 0; y < height; y++)
        {
            // Row offset in uint32 units from start of data
            rowOffsets[y] = (uint)(ms.Position / 4);

            int x = 0;
            while (x < width)
            {
                int idx = (y * width + x) * 4;
                byte a = pixelData[idx + 3];

                if (a == 0)
                {
                    // Transparent run — count consecutive transparent pixels
                    int run = 0;
                    while (x + run < width)
                    {
                        int ci = (y * width + x + run) * 4;
                        if (pixelData[ci + 3] != 0) break;
                        run++;
                    }
                    // Write transparent run as color=0, run=count
                    bw.Write((ushort)0);
                    bw.Write((ushort)run);
                    x += run;
                }
                else
                {
                    // Colored pixel — convert to ARGB1555
                    ushort color = ConvertRgba8888ToArgb1555(pixelData, idx);

                    // Count consecutive same-color pixels
                    int run = 1;
                    while (x + run < width)
                    {
                        int ci = (y * width + x + run) * 4;
                        if (pixelData[ci + 3] == 0) break;
                        ushort nextColor = ConvertRgba8888ToArgb1555(pixelData, ci);
                        if (nextColor != color) break;
                        run++;
                    }

                    bw.Write(color);
                    bw.Write((ushort)run);
                    x += run;
                }
            }

            // Row terminator
            bw.Write((ushort)0);
            bw.Write((ushort)0);
        }

        // Write the actual row offsets
        byte[] result = ms.ToArray();
        for (int y = 0; y < height; y++)
        {
            int off = y * 4;
            result[off + 0] = (byte)(rowOffsets[y] & 0xFF);
            result[off + 1] = (byte)((rowOffsets[y] >> 8) & 0xFF);
            result[off + 2] = (byte)((rowOffsets[y] >> 16) & 0xFF);
            result[off + 3] = (byte)((rowOffsets[y] >> 24) & 0xFF);
        }

        return result;
    }

    /// <summary>
    /// Convert RGBA8888 to ARGB1555 (UO gump pixel format).
    /// </summary>
    private static ushort ConvertRgba8888ToArgb1555(byte[] data, int offset)
    {
        int r = data[offset] >> 3;      // 8-bit to 5-bit
        int g = data[offset + 1] >> 3;
        int b = data[offset + 2] >> 3;
        int a = data[offset + 3] > 127 ? 1 : 0;

        return (ushort)((a << 15) | (r << 10) | (g << 5) | b);
    }

    public void Dispose()
    {
        _indexWriter.Dispose();
        _dataWriter.Dispose();
        _indexStream.Dispose();
        _dataStream.Dispose();
        GC.SuppressFinalize(this);
    }
}
