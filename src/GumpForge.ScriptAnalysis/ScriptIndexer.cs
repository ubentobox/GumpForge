namespace GumpForge.ScriptAnalysis;

/// <summary>
/// Scans a server scripts directory to find all C# files containing gump definitions.
/// Builds a lightweight index without full Roslyn analysis for fast discovery.
/// </summary>
public class ScriptIndexer
{
    private static readonly string[] GumpMarkers =
    [
        ": Gump", ":Gump", ": BaseGump", ":BaseGump",
        "AddPage(", "AddBackground(", "AddImage(", "AddButton(",
        "AddLabel(", "AddHtml(", "AddAlphaRegion(", "AddTextEntry(",
        "AddCheck(", "AddRadio(", "AddTooltip(", "AddImageTiled(",
        "AddItem(", "AddSpriteImage("
    ];

    /// <summary>
    /// Scans a directory recursively for .cs files that contain gump-related code.
    /// Returns file paths sorted by relevance (files with class inheritance first).
    /// </summary>
    public async Task<List<IndexedScript>> ScanDirectoryAsync(string scriptsPath,
        IProgress<(int scanned, int found)>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(scriptsPath))
            return [];

        var results = new List<IndexedScript>();
        var files = Directory.GetFiles(scriptsPath, "*.cs", SearchOption.AllDirectories);
        int scanned = 0;

        await Task.Run(() =>
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var content = File.ReadAllText(file);
                    var markers = GumpMarkers.Where(m =>
                        content.Contains(m, StringComparison.Ordinal)).ToList();

                    if (markers.Count > 0)
                    {
                        bool hasClassDecl = markers.Any(m => m.Contains(':'));
                        int addCallCount = markers.Count(m => m.StartsWith("Add"));

                        results.Add(new IndexedScript
                        {
                            FilePath = file,
                            RelativePath = Path.GetRelativePath(scriptsPath, file),
                            FileName = Path.GetFileName(file),
                            HasGumpClass = hasClassDecl,
                            GumpCallCount = addCallCount,
                            FileSize = new FileInfo(file).Length,
                            MatchedMarkers = markers
                        });
                    }
                }
                catch
                {
                    // Skip unreadable files
                }

                scanned++;
                if (scanned % 50 == 0)
                    progress?.Report((scanned, results.Count));
            }
        }, ct);

        progress?.Report((files.Length, results.Count));

        // Sort: files with gump class declarations first, then by call count
        return results
            .OrderByDescending(s => s.HasGumpClass)
            .ThenByDescending(s => s.GumpCallCount)
            .ToList();
    }
}

/// <summary>
/// A script file discovered during indexing.
/// </summary>
public class IndexedScript
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool HasGumpClass { get; set; }
    public int GumpCallCount { get; set; }
    public long FileSize { get; set; }
    public List<string> MatchedMarkers { get; set; } = [];

    public string DisplayName => HasGumpClass
        ? $"📄 {FileName} (class)"
        : $"📎 {FileName} ({GumpCallCount} calls)";
}
