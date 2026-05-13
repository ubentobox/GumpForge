using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GumpForge.Core.Models;

/// <summary>
/// Abstract base class for all gump elements. Maps to the union of
/// element types across ServUO, RunUO, ModernUO, Sphere, and ClassicAssist.
/// </summary>
public abstract partial class GumpElement : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;
    [ObservableProperty] private int _width;
    [ObservableProperty] private int _height;
    [ObservableProperty] private int _page;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private string _colorTag = string.Empty;

    /// <summary>Unique identifier within the document for serialization and reference.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The gump element type name used for display and code generation.</summary>
    public abstract string ElementType { get; }

    /// <summary>Creates a deep clone of this element with a new Id.</summary>
    public abstract GumpElement Clone();
}

/// <summary>9-slice resizable background. Maps to AddBackground / resizepic.</summary>
public partial class GumpBackground : GumpElement
{
    [ObservableProperty] private int _gumpId;
    public override string ElementType => "Background";

    public override GumpElement Clone() => new GumpBackground
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        GumpId = GumpId
    };
}

/// <summary>Static gump image. Maps to AddImage / gumppic.</summary>
public partial class GumpImage : GumpElement
{
    [ObservableProperty] private int _gumpId;
    [ObservableProperty] private int _hue;
    public override string ElementType => "Image";

    public override GumpElement Clone() => new GumpImage
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        GumpId = GumpId, Hue = Hue
    };
}

/// <summary>Tiled image in a rectangle. Maps to AddImageTiled / gumppictiled.</summary>
public partial class GumpImageTiled : GumpElement
{
    [ObservableProperty] private int _gumpId;
    public override string ElementType => "ImageTiled";

    public override GumpElement Clone() => new GumpImageTiled
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        GumpId = GumpId
    };
}

/// <summary>Translucent dim rectangle. Maps to AddAlphaRegion / checkertrans.</summary>
public partial class GumpAlphaRegion : GumpElement
{
    public override string ElementType => "AlphaRegion";

    public override GumpElement Clone() => new GumpAlphaRegion
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page
    };
}

/// <summary>Interactive button. Maps to AddButton / button.</summary>
public partial class GumpButton : GumpElement
{
    [ObservableProperty] private int _normalId;
    [ObservableProperty] private int _pressedId;
    [ObservableProperty] private int _buttonId;
    [ObservableProperty] private GumpButtonType _buttonType = GumpButtonType.Reply;
    [ObservableProperty] private int _param;
    public override string ElementType => "Button";

    public override GumpElement Clone() => new GumpButton
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        NormalId = NormalId, PressedId = PressedId, ButtonId = ButtonId,
        ButtonType = ButtonType, Param = Param
    };
}

public enum GumpButtonType { Page = 0, Reply = 1 }

/// <summary>Checkbox. Maps to AddCheck / checkbox.</summary>
public partial class GumpCheck : GumpElement
{
    [ObservableProperty] private int _inactiveId;
    [ObservableProperty] private int _activeId;
    [ObservableProperty] private int _switchId;
    [ObservableProperty] private bool _initialState;
    public override string ElementType => "Check";

    public override GumpElement Clone() => new GumpCheck
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        InactiveId = InactiveId, ActiveId = ActiveId, SwitchId = SwitchId,
        InitialState = InitialState
    };
}

/// <summary>Radio button. Maps to AddRadio / radio.</summary>
public partial class GumpRadio : GumpElement
{
    [ObservableProperty] private int _inactiveId;
    [ObservableProperty] private int _activeId;
    [ObservableProperty] private int _groupId;
    [ObservableProperty] private int _switchId;
    [ObservableProperty] private bool _initialState;
    public override string ElementType => "Radio";

    public override GumpElement Clone() => new GumpRadio
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        InactiveId = InactiveId, ActiveId = ActiveId, GroupId = GroupId,
        SwitchId = SwitchId, InitialState = InitialState
    };
}

/// <summary>Single-line text label. Maps to AddLabel / text.</summary>
public partial class GumpLabel : GumpElement
{
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private int _hue;
    [ObservableProperty] private int _font;
    public override string ElementType => "Label";

    public override GumpElement Clone() => new GumpLabel
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        Text = Text, Hue = Hue, Font = Font
    };
}

/// <summary>Clipped label. Maps to AddLabelCropped / croppedtext.</summary>
public partial class GumpLabelCropped : GumpElement
{
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private int _hue;
    public override string ElementType => "LabelCropped";

    public override GumpElement Clone() => new GumpLabelCropped
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        Text = Text, Hue = Hue
    };
}

/// <summary>HTML text region. Maps to AddHtml / htmlgump.</summary>
public partial class GumpHtml : GumpElement
{
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _hasBackground;
    [ObservableProperty] private bool _hasScrollbar;
    public override string ElementType => "Html";

    public override GumpElement Clone() => new GumpHtml
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        Text = Text, HasBackground = HasBackground, HasScrollbar = HasScrollbar
    };
}

/// <summary>Localized HTML text. Maps to AddHtmlLocalized / xmfhtmlgump.</summary>
public partial class GumpHtmlLocalized : GumpElement
{
    [ObservableProperty] private int _clilocId;
    [ObservableProperty] private string _args = string.Empty;
    [ObservableProperty] private int _color;
    [ObservableProperty] private bool _hasBackground;
    [ObservableProperty] private bool _hasScrollbar;
    public override string ElementType => "HtmlLocalized";

    public override GumpElement Clone() => new GumpHtmlLocalized
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        ClilocId = ClilocId, Args = Args, Color = Color,
        HasBackground = HasBackground, HasScrollbar = HasScrollbar
    };
}

/// <summary>Editable text field. Maps to AddTextEntry / textentry.</summary>
public partial class GumpTextEntry : GumpElement
{
    [ObservableProperty] private int _entryId;
    [ObservableProperty] private string _initialText = string.Empty;
    [ObservableProperty] private int _hue;
    [ObservableProperty] private int _maxLength;
    public override string ElementType => "TextEntry";

    public override GumpElement Clone() => new GumpTextEntry
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        EntryId = EntryId, InitialText = InitialText, Hue = Hue, MaxLength = MaxLength
    };
}

/// <summary>Item art (from art.mul, not gumpart). Maps to AddItem / tilepic.</summary>
public partial class GumpItem : GumpElement
{
    [ObservableProperty] private int _itemId;
    [ObservableProperty] private int _hue;
    public override string ElementType => "Item";

    public override GumpElement Clone() => new GumpItem
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page,
        ItemId = ItemId, Hue = Hue
    };
}

/// <summary>Tooltip on previous element. Maps to AddTooltip / tooltip.</summary>
public partial class GumpTooltip : GumpElement
{
    [ObservableProperty] private int _clilocId;
    public override string ElementType => "Tooltip";

    public override GumpElement Clone() => new GumpTooltip
    {
        Name = Name, X = X, Y = Y, Page = Page, ClilocId = ClilocId
    };
}

/// <summary>
/// Editor-only grouping container. Flattens to child elements in generated code.
/// </summary>
public partial class GumpGroup : GumpElement
{
    public ObservableCollection<GumpElement> Children { get; init; } = [];
    public override string ElementType => "Group";

    public override GumpElement Clone()
    {
        var clone = new GumpGroup
        {
            Name = Name, X = X, Y = Y, Width = Width, Height = Height, Page = Page
        };
        foreach (var child in Children)
            clone.Children.Add(child.Clone());
        return clone;
    }
}
