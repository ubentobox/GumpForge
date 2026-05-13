using System.Text;

namespace GumpForge.Formats.Cliloc;

/// <summary>
/// A single cliloc entry — a localized text string keyed by a numeric ID.
/// Used for HtmlLocalized gump elements that reference text by cliloc number.
/// </summary>
public record ClilocEntry(int Id, string Text);

/// <summary>
/// Reads cliloc files (cliloc.enu, cliloc.deu, etc.) — Ultima Online's
/// localized string tables.
///
/// Format:
///   Header: 6 bytes (3 bytes magic + 2 bytes padding + 1 byte unknown)
///   Entries: repeating { int32 id, byte unused, ushort length, byte[length] text }
///   Text is UTF-8 encoded in newer clients, ASCII in older ones.
/// </summary>
public class ClilocReader
{
    private readonly Dictionary<int, string> _entries = [];

    public IReadOnlyDictionary<int, string> Entries => _entries;
    public int Count => _entries.Count;

    public ClilocReader(string clilocPath)
    {
        using var fs = File.OpenRead(clilocPath);
        using var br = new BinaryReader(fs);

        // Skip 6-byte header
        if (fs.Length < 6) return;
        br.ReadBytes(6);

        while (fs.Position < fs.Length)
        {
            try
            {
                int id = br.ReadInt32();
                br.ReadByte(); // unused flag
                ushort length = br.ReadUInt16();

                if (length > 0 && fs.Position + length <= fs.Length)
                {
                    byte[] textBytes = br.ReadBytes(length);
                    // Try UTF-8 first, fall back to ASCII
                    string text = Encoding.UTF8.GetString(textBytes);
                    _entries[id] = text;
                }
                else if (length == 0)
                {
                    _entries[id] = string.Empty;
                }
                else
                {
                    break; // Corrupt data
                }
            }
            catch
            {
                break; // End of valid data
            }
        }
    }

    /// <summary>Get the text for a cliloc ID. Returns null if not found.</summary>
    public string? GetText(int clilocId)
    {
        return _entries.TryGetValue(clilocId, out var text) ? text : null;
    }

    /// <summary>
    /// Search cliloc entries by text content (case-insensitive substring match).
    /// </summary>
    public IEnumerable<ClilocEntry> Search(string query, int maxResults = 50)
    {
        int count = 0;
        foreach (var (id, text) in _entries)
        {
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                yield return new ClilocEntry(id, text);
                if (++count >= maxResults) yield break;
            }
        }
    }
}
