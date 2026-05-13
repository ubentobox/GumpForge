using System.IO.Compression;

namespace GumpForge.Formats.Uop;

/// <summary>
/// A single entry within a UOP archive block table.
/// </summary>
public class UopEntry
{
    public long Offset { get; init; }
    public int HeaderLength { get; init; }
    public int CompressedLength { get; init; }
    public int DecompressedLength { get; init; }
    public ulong Hash { get; init; }
    public bool IsCompressed { get; init; }
}

/// <summary>
/// Reads UOP (Ultima Online Package) archives — the modern container format
/// used by newer UO clients to wrap legacy MUL data.
///
/// Header (28 bytes):
///   bytes[4]  magic    — "MYP\0"
///   uint32    version  — format version (typically 5)
///   uint32    signature
///   long      blockOffset — offset to the first block table
///   uint32    blockCapacity — max entries per block
///   uint32    fileCount — total files in the archive
///
/// Block Table (linked list of blocks):
///   Each block: uint32 count, long nextBlockOffset, then 'count' entries.
///   Each entry (34 bytes):
///     long   offset         — data offset in file
///     int    headerLength   — per-entry header before data
///     int    compressedLen  — compressed data size
///     int    decompressedLen — original data size
///     ulong  hash           — filename hash (Adler32-based)
///     uint   adler32        — data checksum
///     short  flags          — bit 0 = compressed with Deflate
///
/// Data is typically Zlib/Deflate compressed.
///
/// Reference: UOFiddler / Ultima SDK (MIT-licensed).
/// </summary>
public class UopReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly Dictionary<ulong, UopEntry> _entries = [];
    private readonly int _version;

    public int FileCount => _entries.Count;
    public int Version => _version;
    public IReadOnlyDictionary<ulong, UopEntry> Entries => _entries;

    public UopReader(string uopPath)
    {
        _stream = File.OpenRead(uopPath);
        _reader = new BinaryReader(_stream);

        // Validate magic bytes: "MYP\0"
        byte[] magic = _reader.ReadBytes(4);
        if (magic[0] != 0x4D || magic[1] != 0x59 || magic[2] != 0x50 || magic[3] != 0x00)
            throw new InvalidDataException("Not a valid UOP file (bad magic).");

        _version = _reader.ReadInt32();
        _reader.ReadUInt32(); // signature/timestamp

        long blockOffset = _reader.ReadInt64();
        int blockCapacity = _reader.ReadInt32();
        _reader.ReadInt32(); // total file count (we count ourselves)

        // Read block table chain
        ReadBlocks(blockOffset, blockCapacity);
    }

    private void ReadBlocks(long blockOffset, int blockCapacity)
    {
        while (blockOffset > 0 && blockOffset < _stream.Length)
        {
            _stream.Seek(blockOffset, SeekOrigin.Begin);

            int count = _reader.ReadInt32();
            long nextBlock = _reader.ReadInt64();

            for (int i = 0; i < count; i++)
            {
                long offset = _reader.ReadInt64();
                int headerLength = _reader.ReadInt32();
                int compressedLen = _reader.ReadInt32();
                int decompressedLen = _reader.ReadInt32();
                ulong hash = _reader.ReadUInt64();
                _reader.ReadUInt32(); // adler32
                short flags = _reader.ReadInt16();

                if (offset == 0) continue; // Empty slot

                var entry = new UopEntry
                {
                    Offset = offset,
                    HeaderLength = headerLength,
                    CompressedLength = compressedLen,
                    DecompressedLength = decompressedLen,
                    Hash = hash,
                    IsCompressed = (flags & 1) != 0
                };

                _entries[hash] = entry;
            }

            blockOffset = nextBlock;
        }
    }

    /// <summary>
    /// Read and decompress a UOP entry's raw data by its hash.
    /// </summary>
    public byte[]? ReadEntry(ulong hash)
    {
        if (!_entries.TryGetValue(hash, out var entry))
            return null;

        return ReadEntryData(entry);
    }

    /// <summary>
    /// Read and decompress a UOP entry by looking up a filename hash.
    /// </summary>
    public byte[]? ReadByFilename(string filename)
    {
        ulong hash = HashFilename(filename);
        return ReadEntry(hash);
    }

    /// <summary>
    /// Read all entries as (hash, data) pairs. Useful for extracting entire archives.
    /// </summary>
    public IEnumerable<(ulong Hash, byte[] Data)> ReadAllEntries()
    {
        foreach (var (hash, entry) in _entries)
        {
            var data = ReadEntryData(entry);
            if (data is not null)
                yield return (hash, data);
        }
    }

    private byte[]? ReadEntryData(UopEntry entry)
    {
        try
        {
            _stream.Seek(entry.Offset + entry.HeaderLength, SeekOrigin.Begin);

            if (entry.IsCompressed && entry.CompressedLength > 0)
            {
                byte[] compressed = _reader.ReadBytes(entry.CompressedLength);
                return Decompress(compressed, entry.DecompressedLength);
            }
            else
            {
                int readLen = entry.DecompressedLength > 0
                    ? entry.DecompressedLength
                    : entry.CompressedLength;
                return _reader.ReadBytes(readLen);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decompress Zlib/Deflate data. UOP uses raw Deflate (no zlib header in some versions)
    /// but some entries have the 2-byte zlib header. We try both.
    /// </summary>
    private static byte[] Decompress(byte[] compressed, int expectedLength)
    {
        // Try with zlib header first (skip 2-byte header)
        if (compressed.Length >= 2)
        {
            try
            {
                using var ms = new MemoryStream(compressed, 2, compressed.Length - 2);
                using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                byte[] result = new byte[expectedLength];
                int totalRead = 0;
                while (totalRead < expectedLength)
                {
                    int read = deflate.Read(result, totalRead, expectedLength - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
                if (totalRead == expectedLength)
                    return result;
            }
            catch { /* Fall through to raw deflate */ }
        }

        // Try raw Deflate (no header)
        using var ms2 = new MemoryStream(compressed);
        using var deflate2 = new DeflateStream(ms2, CompressionMode.Decompress);
        byte[] result2 = new byte[expectedLength];
        int totalRead2 = 0;
        while (totalRead2 < expectedLength)
        {
            int read = deflate2.Read(result2, totalRead2, expectedLength - totalRead2);
            if (read == 0) break;
            totalRead2 += read;
        }
        return result2;
    }

    /// <summary>
    /// Hash a filename using the UO Adler32-based algorithm.
    /// This is used to look up entries by their original filename
    /// (e.g., "build/gumpartlegacymul/00000.tga").
    /// </summary>
    public static ulong HashFilename(string filename)
    {
        uint eax, ecx, edx, ebx, esi, edi;
        eax = ecx = edx = ebx = esi = edi = 0;
        ebx = edi = esi = (uint)filename.Length + 0xDEADBEEF;

        int i = 0;

        for (i = 0; i + 12 < filename.Length; i += 12)
        {
            edi = (uint)((filename[i + 7] << 24) | (filename[i + 6] << 16) |
                         (filename[i + 5] << 8) | filename[i + 4]) + edi;
            esi = (uint)((filename[i + 11] << 24) | (filename[i + 10] << 16) |
                         (filename[i + 9] << 8) | filename[i + 8]) + esi;
            edx = (uint)((filename[i + 3] << 24) | (filename[i + 2] << 16) |
                         (filename[i + 1] << 8) | filename[i]) - esi;

            edx = (edx + ebx) ^ (esi >> 28) ^ (esi << 4);
            esi += edi;
            edi = (edi - edx) ^ (edx >> 26) ^ (edx << 6);
            edx += esi;
            esi = (esi - edi) ^ (edi >> 24) ^ (edi << 8);
            edi += edx;
            ebx = (edx - esi) ^ (esi >> 16) ^ (esi << 16);
            esi += edi;
            edi = (edi - ebx) ^ (ebx >> 13) ^ (ebx << 19);
            ebx += esi;
            esi = (esi - edi) ^ (edi >> 28) ^ (edi << 4);
            edi += ebx;
        }

        if (filename.Length - i > 0)
        {
            switch (filename.Length - i)
            {
                case 12: esi += (uint)filename[i + 11] << 24; goto case 11;
                case 11: esi += (uint)filename[i + 10] << 16; goto case 10;
                case 10: esi += (uint)filename[i + 9] << 8; goto case 9;
                case 9: esi += filename[i + 8]; goto case 8;
                case 8: edi += (uint)filename[i + 7] << 24; goto case 7;
                case 7: edi += (uint)filename[i + 6] << 16; goto case 6;
                case 6: edi += (uint)filename[i + 5] << 8; goto case 5;
                case 5: edi += filename[i + 4]; goto case 4;
                case 4: ebx += (uint)filename[i + 3] << 24; goto case 3;
                case 3: ebx += (uint)filename[i + 2] << 16; goto case 2;
                case 2: ebx += (uint)filename[i + 1] << 8; goto case 1;
                case 1: ebx += filename[i]; break;
            }

            esi = (esi ^ edi) - ((edi >> 18) ^ (edi << 14));
            ecx = (esi ^ ebx) - ((esi >> 21) ^ (esi << 11));
            edi = (edi ^ ecx) - ((ecx >> 7) ^ (ecx << 25));
            esi = (esi ^ edi) - ((edi >> 16) ^ (edi << 16));
            edx = (esi ^ ecx) - ((esi >> 28) ^ (esi << 4));
            edi = (edi ^ edx) - ((edx >> 18) ^ (edx << 14));
            eax = (esi ^ edi) - ((edi >> 8) ^ (edi << 24));
        }
        else
        {
            eax = esi;
            edx = edi;
        }

        return ((ulong)edx << 32) | eax;
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
        GC.SuppressFinalize(this);
    }
}
