using GumpForge.Core.Services;
using GumpForge.Formats.Mul;
using GumpForge.App.ViewModels;

namespace GumpForge.App.Services;

/// <summary>
/// Service responsible for loading gump art from MUL files into the Asset Browser.
/// Runs on a background thread to avoid blocking the UI.
/// </summary>
public class AssetLoadingService
{
    private readonly AssetBrowserViewModel _browser;
    private readonly AssetCache _cache;
    private GumpMulReader? _reader;

    public AssetLoadingService(AssetBrowserViewModel browser, AssetCache cache)
    {
        _browser = browser;
        _cache = cache;
    }

    /// <summary>
    /// Loads gump art assets from the specified client data folder.
    /// Expects to find Gumpart.mul and Gumpidx.mul (case-insensitive).
    /// </summary>
    public async Task LoadFromClientFolderAsync(string dataFolderPath)
    {
        // Find the MUL files (case-insensitive)
        var indexFile = FindFile(dataFolderPath, "gumpidx.mul");
        var dataFile = FindFile(dataFolderPath, "gumpart.mul");

        if (indexFile is null || dataFile is null)
        {
            return; // Files not found
        }

        _browser.IsLoading = true;
        _browser.AllThumbnails.Clear();
        _browser.Thumbnails.Clear();

        try
        {
            _reader?.Dispose();
            _reader = new GumpMulReader(indexFile, dataFile);

            // Collect valid gump IDs first
            var validIds = new List<int>();
            await Task.Run(() =>
            {
                foreach (var id in _reader.GetValidGumpIds())
                {
                    validIds.Add(id);
                    // Cap at 50000 to avoid excessive memory use during scan
                    if (validIds.Count >= 50000) break;
                }
            });

            _browser.TotalAssets = validIds.Count;

            // Load thumbnails in batches to keep the UI responsive
            const int batchSize = 100;
            for (int i = 0; i < validIds.Count; i += batchSize)
            {
                var batch = validIds.Skip(i).Take(batchSize).ToList();
                var thumbnails = new List<AssetThumbnail>();

                await Task.Run(() =>
                {
                    foreach (var id in batch)
                    {
                        var entry = _reader.ReadGump(id);
                        if (entry is not null && entry.IsValid)
                        {
                            // Cache the full pixel data
                            _cache.Put("gumpart.mul", id, entry.PixelData, entry.Width, entry.Height);

                            thumbnails.Add(new AssetThumbnail
                            {
                                GumpId = id,
                                Width = entry.Width,
                                Height = entry.Height,
                                PixelData = entry.PixelData
                            });
                        }
                    }
                });

                // Add to master list on the main thread
                foreach (var thumb in thumbnails)
                    _browser.AllThumbnails.Add(thumb);

                // Apply current filter to show in UI
                _browser.ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading gump art: {ex.Message}");
        }
        finally
        {
            _browser.IsLoading = false;
        }
    }

    private static string? FindFile(string folder, string fileName)
    {
        // Try exact match, then case-insensitive
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path)) return path;

        // Case-insensitive search
        try
        {
            return Directory.GetFiles(folder)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
    }
}
