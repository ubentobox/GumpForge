using System.Text;

namespace GumpForge.Formats.Fonts;

/// <summary>
/// A single character glyph from the UO font file.
/// </summary>
public class FontGlyph
{
    public int CharCode { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Pixel data as RGBA8888, row-major.</summary>
    public byte[] PixelData { get; init; } = [];

    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>
/// A complete UO font (one of fonts 0–9).
/// </summary>
public class UoFont
{
    public int FontId { get; init; }
    public int MaxHeight { get; set; }
    private readonly Dictionary<int, FontGlyph> _glyphs = [];

    public FontGlyph? GetGlyph(int charCode) =>
        _glyphs.TryGetValue(charCode, out var g) ? g : null;

    public int MeasureWidth(string text)
    {
        int w = 0;
        foreach (char c in text)
        {
            var glyph = GetGlyph(c);
            w += glyph?.Width ?? 4; // 4px default for missing chars
        }
        return w;
    }

    internal void AddGlyph(FontGlyph glyph) => _glyphs[glyph.CharCode] = glyph;
}

/// <summary>
/// Reads fonts.mul — UO's standard ASCII font file (fonts 0–9).
///
/// Format:
///   Font 0: 1-byte header (unused)
///   Fonts 1-9: no header
///   Each font:
///     - 224 characters (ASCII 32-255)
///     - Per character: byte width, byte height, byte unknown
///     - Then (width * height) uint16 pixels in ARGB1555 format
///     - If width==0 && height==0, character is empty/missing
///
/// Reference: UO SDK / UO Fiddler (MIT-licensed).
/// </summary>
public class FontsReader
{
    private readonly List<UoFont> _fonts = [];
    public IReadOnlyList<UoFont> Fonts => _fonts;
    public int FontCount => _fonts.Count;

    public FontsReader(string fontsPath)
    {
        using var fs = File.OpenRead(fontsPath);
        using var br = new BinaryReader(fs);

        for (int fontId = 0; fontId < 10; fontId++)
        {
            if (fs.Position >= fs.Length) break;

            // Font 0 has a 1-byte header
            if (fontId == 0)
                br.ReadByte();

            int maxHeight = 0;
            var font = new UoFont { FontId = fontId };

            // Read 224 characters (ASCII 32-255)
            for (int c = 0; c < 224; c++)
            {
                if (fs.Position + 3 > fs.Length) break;

                int width = br.ReadByte();
                int height = br.ReadByte();
                byte unk = br.ReadByte(); // spacing/baseline offset

                if (width == 0 || height == 0)
                {
                    font.AddGlyph(new FontGlyph { CharCode = c + 32, Width = 0, Height = 0 });
                    continue;
                }

                if (height > maxHeight) maxHeight = height;

                int pixelCount = width * height;
                long bytesNeeded = pixelCount * 2L;
                if (fs.Position + bytesNeeded > fs.Length) break;

                byte[] pixelData = new byte[pixelCount * 4]; // RGBA8888

                for (int p = 0; p < pixelCount; p++)
                {
                    ushort color = br.ReadUInt16();
                    int idx = p * 4;

                    if (color == 0)
                    {
                        // Transparent
                        pixelData[idx] = 0;
                        pixelData[idx + 1] = 0;
                        pixelData[idx + 2] = 0;
                        pixelData[idx + 3] = 0;
                    }
                    else
                    {
                        // ARGB1555 → RGBA8888
                        int r = (color >> 10) & 0x1F;
                        int g = (color >> 5) & 0x1F;
                        int b = color & 0x1F;
                        pixelData[idx] = (byte)((r << 3) | (r >> 2));
                        pixelData[idx + 1] = (byte)((g << 3) | (g >> 2));
                        pixelData[idx + 2] = (byte)((b << 3) | (b >> 2));
                        pixelData[idx + 3] = 255;
                    }
                }

                font.AddGlyph(new FontGlyph
                {
                    CharCode = c + 32,
                    Width = width,
                    Height = height,
                    PixelData = pixelData
                });
            }

            font.MaxHeight = maxHeight;
            _fonts.Add(font);
        }
    }

    /// <summary>Get a font by its ID (0-9).</summary>
    public UoFont? GetFont(int fontId) =>
        fontId >= 0 && fontId < _fonts.Count ? _fonts[fontId] : null;

    /// <summary>Measure the pixel width of a string in the given font.</summary>
    public int MeasureWidth(int fontId, string text) =>
        GetFont(fontId)?.MeasureWidth(text) ?? text.Length * 8;

    /// <summary>Get the max height of a font in pixels.</summary>
    public int GetFontHeight(int fontId) =>
        GetFont(fontId)?.MaxHeight ?? 14;
}
