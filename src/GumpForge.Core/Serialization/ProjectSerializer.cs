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

    /// <summary>
    /// Deserialize a JSON string representing a gump directly into a GumpDocument.
    /// </summary>
    public static GumpDocument DeserializeGump(string json)
    {
        var dto = JsonSerializer.Deserialize<ProjectDto>(json, Options)
            ?? throw new InvalidOperationException("Failed to deserialize gump.");
        return FromDto(dto);
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
            foreach (var elDto in pageDto.Elements)
            {
                try
                {
                    page.Elements.Add(ElementFromDto(elDto));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to deserialize element: {ex.Message}");
                }
            }
            doc.Pages.Add(page);
        }

        if (doc.Pages.Count == 0)
            doc.Pages.Add(new GumpPage(0));

        // Reconstruct custom assets
        doc.CustomAssets.Clear();
        foreach (var assetDto in dto.CustomAssets)
        {
            doc.CustomAssets.Add(new CustomAssetEntry
            {
                GumpId = assetDto.GumpId,
                SourcePath = assetDto.SourcePath,
                FileHash = assetDto.FileHash,
                Tag = assetDto.Tag
            });
        }

        return doc;
    }

    private static GumpElement ElementFromDto(ElementDto dto)
    {
        GumpElement element = dto.Type switch
        {
            "Background" => new GumpBackground { GumpId = GetInt(dto.Properties, "gumpId") },
            "Image" => new GumpImage { GumpId = GetInt(dto.Properties, "gumpId"), Hue = GetInt(dto.Properties, "hue") },
            "ImageTiled" => new GumpImageTiled { GumpId = GetInt(dto.Properties, "gumpId") },
            "AlphaRegion" => new GumpAlphaRegion(),
            "Button" => new GumpButton
            {
                NormalId = GetInt(dto.Properties, "normalId"),
                PressedId = GetInt(dto.Properties, "pressedId"),
                ButtonId = GetInt(dto.Properties, "buttonId"),
                ButtonType = GetEnum<GumpButtonType>(dto.Properties, "buttonType"),
                Param = GetInt(dto.Properties, "param")
            },
            "Check" => new GumpCheck
            {
                InactiveId = GetInt(dto.Properties, "inactiveId"),
                ActiveId = GetInt(dto.Properties, "activeId"),
                SwitchId = GetInt(dto.Properties, "switchId"),
                InitialState = GetBool(dto.Properties, "initialState")
            },
            "Radio" => new GumpRadio
            {
                InactiveId = GetInt(dto.Properties, "inactiveId"),
                ActiveId = GetInt(dto.Properties, "activeId"),
                GroupId = GetInt(dto.Properties, "groupId"),
                SwitchId = GetInt(dto.Properties, "switchId"),
                InitialState = GetBool(dto.Properties, "initialState")
            },
            "Label" => new GumpLabel
            {
                Text = GetString(dto.Properties, "text"),
                Hue = GetInt(dto.Properties, "hue"),
                Font = GetInt(dto.Properties, "font")
            },
            "LabelCropped" => new GumpLabelCropped
            {
                Text = GetString(dto.Properties, "text"),
                Hue = GetInt(dto.Properties, "hue")
            },
            "Html" => new GumpHtml
            {
                Text = GetString(dto.Properties, "text"),
                HasBackground = GetBool(dto.Properties, "hasBackground"),
                HasScrollbar = GetBool(dto.Properties, "hasScrollbar")
            },
            "HtmlLocalized" => new GumpHtmlLocalized
            {
                ClilocId = GetInt(dto.Properties, "clilocId"),
                Args = GetString(dto.Properties, "args"),
                Color = GetInt(dto.Properties, "color"),
                HasBackground = GetBool(dto.Properties, "hasBackground"),
                HasScrollbar = GetBool(dto.Properties, "hasScrollbar")
            },
            "TextEntry" => new GumpTextEntry
            {
                EntryId = GetInt(dto.Properties, "entryId"),
                InitialText = GetString(dto.Properties, "initialText"),
                Hue = GetInt(dto.Properties, "hue"),
                MaxLength = GetInt(dto.Properties, "maxLength")
            },
            "Item" => new GumpItem
            {
                ItemId = GetInt(dto.Properties, "itemId"),
                Hue = GetInt(dto.Properties, "hue")
            },
            "Tooltip" => new GumpTooltip
            {
                ClilocId = GetInt(dto.Properties, "clilocId")
            },
            "Group" => DeserializeGroup(dto),
            _ => throw new NotSupportedException($"Unknown element type: {dto.Type}")
        };

        if (Guid.TryParse(dto.Id, out var id))
        {
            var backingField = typeof(GumpElement).GetField("<Id>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            backingField?.SetValue(element, id);
        }

        element.Name = dto.Name;
        element.X = dto.X;
        element.Y = dto.Y;
        element.Width = dto.Width;
        element.Height = dto.Height;
        element.Page = dto.Page;
        element.IsLocked = dto.IsLocked;
        element.IsVisible = dto.IsVisible;

        return element;
    }

    private static GumpGroup DeserializeGroup(ElementDto dto)
    {
        var group = new GumpGroup();
        if (dto.Properties.TryGetValue("children", out var childrenVal) && childrenVal.ValueKind == JsonValueKind.Array)
        {
            foreach (var childEl in childrenVal.EnumerateArray())
            {
                var childDto = JsonSerializer.Deserialize<ElementDto>(childEl.GetRawText(), Options);
                if (childDto != null)
                    group.Children.Add(ElementFromDto(childDto));
            }
        }
        return group;
    }

    private static int GetInt(Dictionary<string, JsonElement> props, string key) =>
        props.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.Number ? val.GetInt32() : 0;

    private static bool GetBool(Dictionary<string, JsonElement> props, string key) =>
        props.TryGetValue(key, out var val) && (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False) && val.GetBoolean();

    private static string GetString(Dictionary<string, JsonElement> props, string key) =>
        props.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : "";

    private static T GetEnum<T>(Dictionary<string, JsonElement> props, string key) where T : struct =>
        props.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String && Enum.TryParse<T>(val.GetString(), out var res) ? res : default;
}
