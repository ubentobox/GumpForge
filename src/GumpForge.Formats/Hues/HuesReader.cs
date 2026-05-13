namespace GumpForge.Formats.Hues;

/// <summary>
/// A single hue entry — 32 color remapping entries plus a name.
/// Hues remap the grayscale palette of gump art to colored equivalents.
/// </summary>
public class HueEntry
{
    public int HueId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>32 color entries as ARGB1555 values.</summary>
    public ushort[] Colors { get; init; } = new ushort[32];

    /// <summary>Starting color for table end.</summary>
    public ushort TableStart { get; init; }

    /// <summary>Ending color for table end.</summary>
    public ushort TableEnd { get; init; }
}

/// <summary>
/// Reads hues.mul — color remapping tables used for hued gump rendering.
///
/// Format: Groups of 8 hue entries.
/// Each group: 4 bytes header + 8 × (32 × uint16 colors + uint16 start + uint16 end + 20 bytes name)
/// Total per entry: 32*2 + 2 + 2 + 20 = 88 bytes
/// Total per group: 4 + 8*88 = 708 bytes
/// </summary>
public class HuesReader
{
    private readonly List<HueEntry> _hues = [];

    public IReadOnlyList<HueEntry> Hues => _hues;

    public HuesReader(string huesPath)
    {
        using var fs = File.OpenRead(huesPath);
        using var br = new BinaryReader(fs);

        int groupCount = (int)(fs.Length / 708);
        int hueId = 0;

        for (int g = 0; g < groupCount; g++)
        {
            br.ReadInt32(); // Group header (unused)

            for (int i = 0; i < 8; i++)
            {
                var colors = new ushort[32];
                for (int c = 0; c < 32; c++)
                    colors[c] = br.ReadUInt16();

                ushort tableStart = br.ReadUInt16();
                ushort tableEnd = br.ReadUInt16();

                byte[] nameBytes = br.ReadBytes(20);
                string name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                _hues.Add(new HueEntry
                {
                    HueId = hueId++,
                    Name = name,
                    Colors = colors,
                    TableStart = tableStart,
                    TableEnd = tableEnd
                });
            }
        }
    }

    /// <summary>Get a hue by its ID (1-based in game, 0-based here).</summary>
    public HueEntry? GetHue(int hueId)
    {
        // Game uses 1-based hue IDs; index 0 = hue 1
        int index = hueId - 1;
        if (index >= 0 && index < _hues.Count)
            return _hues[index];
        return null;
    }
}
