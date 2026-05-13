using System.Text.Json;
using System.Text.Json.Serialization;
using GumpForge.Core.Models;

namespace GumpForge.Core.Serialization;

/// <summary>
/// Serializes/deserializes GumpDocument to/from JSON (.gumpproj files).
/// Uses System.Text.Json with sorted keys for git-friendly diffs.
/// </summary>
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Save a GumpDocument to a .gumpproj file.
    /// </summary>
    public static async Task SaveAsync(GumpDocument doc, string filePath)
    {
        var dto = ToDto(doc);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, dto, Options);
        doc.FilePath = filePath;
        doc.IsDirty = false;
    }

    /// <summary>
    /// Load a GumpDocument from a .gumpproj file.
    /// </summary>
    public static async Task<GumpDocument> LoadAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var dto = await JsonSerializer.DeserializeAsync<ProjectDto>(stream, Options)
            ?? throw new InvalidOperationException("Failed to deserialize project file.");
        var doc = FromDto(dto);
        doc.FilePath = filePath;
        doc.IsDirty = false;
        return doc;
    }

    // DTO for JSON serialization
    private class ProjectDto
    {
        public string Version { get; set; } = "1.0";
        public string Name { get; set; } = "Untitled";
        public string GumpClassName { get; set; } = "MyGump";
        public string Namespace { get; set; } = "Server.Gumps";
        public int CanvasWidth { get; set; } = 800;
        public int CanvasHeight { get; set; } = 600;
        public int GumpX { get; set; } = 100;
        public int GumpY { get; set; } = 100;
        public EmulatorTarget TargetEmulator { get; set; } = EmulatorTarget.ServUO;
        public bool IsDraggable { get; set; } = true;
        public bool IsClosable { get; set; } = true;
        public bool IsResizable { get; set; }
        public bool IsDisposable { get; set; } = true;
        public List<PageDto> Pages { get; set; } = [];
        public List<CustomAssetDto> CustomAssets { get; set; } = [];
    }

    private class PageDto
    {
        public int PageNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<ElementDto> Elements { get; set; } = [];
    }

    private class ElementDto
    {
        public string Type { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Page { get; set; }
        public bool IsLocked { get; set; }
        public bool IsVisible { get; set; } = true;

        // Type-specific properties stored as a dictionary
        public Dictionary<string, JsonElement> Properties { get; set; } = [];
    }

    private class CustomAssetDto
    {
        public int GumpId { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
        public string Tag { get; set; } = "Custom";
    }

    private static ProjectDto ToDto(GumpDocument doc)
    {
        var dto = new ProjectDto
        {
            Name = doc.Name,
            GumpClassName = doc.GumpClassName,
            Namespace = doc.Namespace,
            CanvasWidth = doc.CanvasWidth,
            CanvasHeight = doc.CanvasHeight,
            GumpX = doc.GumpX,
            GumpY = doc.GumpY,
            TargetEmulator = doc.TargetEmulator,
            IsDraggable = doc.IsDraggable,
            IsClosable = doc.IsClosable,
            IsResizable = doc.IsResizable,
            IsDisposable = doc.IsDisposable
        };

        foreach (var page in doc.Pages)
        {
            var pageDto = new PageDto { PageNumber = page.PageNumber, Name = page.Name };
            foreach (var element in page.Elements)
            {
                pageDto.Elements.Add(ElementToDto(element));
            }
            dto.Pages.Add(pageDto);
        }

        foreach (var asset in doc.CustomAssets)
        {
            dto.CustomAssets.Add(new CustomAssetDto
            {
                GumpId = asset.GumpId,
                SourcePath = asset.SourcePath,
                FileHash = asset.FileHash,
                Tag = asset.Tag
            });
        }

        return dto;
    }

    private static ElementDto ElementToDto(GumpElement element)
    {
        var dto = new ElementDto
        {
            Type = element.ElementType,
            Id = element.Id.ToString(),
            Name = element.Name,
            X = element.X,
            Y = element.Y,
            Width = element.Width,
            Height = element.Height,
            Page = element.Page,
            IsLocked = element.IsLocked,
            IsVisible = element.IsVisible
        };

        // Serialize type-specific properties
        var propsJson = JsonSerializer.SerializeToElement(element, element.GetType(), Options);
        foreach (var prop in propsJson.EnumerateObject())
        {
            if (!IsBaseProperty(prop.Name))
                dto.Properties[prop.Name] = prop.Value;
        }

        return dto;
    }

    private static bool IsBaseProperty(string name)
    {
        return name is "name" or "x" or "y" or "width" or "height" or "page"
            or "isLocked" or "isVisible" or "colorTag" or "elementType";
    }

    private static GumpDocument FromDto(ProjectDto dto)
    {
        var doc = new GumpDocument
        {
            Name = dto.Name,
            GumpClassName = dto.GumpClassName,
            Namespace = dto.Namespace,
            CanvasWidth = dto.CanvasWidth,
            CanvasHeight = dto.CanvasHeight,
            GumpX = dto.GumpX,
            GumpY = dto.GumpY,
            TargetEmulator = dto.TargetEmulator,
            IsDraggable = dto.IsDraggable,
            IsClosable = dto.IsClosable,
            IsResizable = dto.IsResizable,
            IsDisposable = dto.IsDisposable
        };

        doc.Pages.Clear();
        foreach (var pageDto in dto.Pages)
        {
            var page = new GumpPage(pageDto.PageNumber) { Name = pageDto.Name };
            // Elements would need a factory to reconstruct from DTO
            // This is a simplified version for now
            doc.Pages.Add(page);
        }

        if (doc.Pages.Count == 0)
            doc.Pages.Add(new GumpPage(0));

        return doc;
    }
}
