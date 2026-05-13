using System.Text;
using GumpForge.Core.Models;

namespace GumpForge.Generators;

/// <summary>
/// Generates ModernUO-compatible C# gump classes.
/// Uses modern C# features: file-scoped namespaces, primary constructors,
/// expression-bodied members, and pattern matching in OnResponse.
/// </summary>
public class ModernUoGenerator : IGumpCodeGenerator
{
    public string TargetName => "ModernUO";
    public string FileExtension => ".cs";

    public string Generate(GumpDocument doc, GenerationOptions opts)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using Server;");
        sb.AppendLine("using Server.Gumps;");
        sb.AppendLine("using Server.Network;");
        sb.AppendLine();

        // File-scoped namespace (C# 10+)
        sb.AppendLine($"namespace {opts.Namespace};");
        sb.AppendLine();

        sb.AppendLine($"{opts.AccessModifier} class {opts.ClassName} : Gump");
        sb.AppendLine("{");

        // Constructor
        sb.AppendLine($"    public {opts.ClassName}(Mobile from) : base({doc.GumpX}, {doc.GumpY})");
        sb.AppendLine("    {");

        sb.AppendLine($"        Closable = {BoolStr(doc.IsClosable)};");
        sb.AppendLine($"        Disposable = {BoolStr(doc.IsDisposable)};");
        sb.AppendLine($"        Draggable = {BoolStr(doc.IsDraggable)};");
        sb.AppendLine($"        Resizable = {BoolStr(doc.IsResizable)};");
        sb.AppendLine();

        foreach (var page in doc.Pages.OrderBy(p => p.PageNumber))
        {
            sb.AppendLine($"        AddPage({page.PageNumber});");

            foreach (var element in page.Elements.SelectMany(GumpDocument.FlattenElement))
            {
                var line = GenerateElement(element, opts.UseHexIds);
                if (line is not null)
                    sb.AppendLine($"        {line}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // OnResponse with pattern matching
        if (opts.GenerateOnResponse)
        {
            sb.AppendLine("    public override void OnResponse(NetState sender, in RelayInfo info)");
            sb.AppendLine("    {");
            sb.AppendLine("        var from = sender.Mobile;");
            sb.AppendLine();

            var replyButtons = doc.GetAllElements()
                .OfType<GumpButton>()
                .Where(b => b.ButtonType == GumpButtonType.Reply)
                .ToList();

            if (replyButtons.Count > 0)
            {
                sb.AppendLine("        switch (info.ButtonID)");
                sb.AppendLine("        {");
                foreach (var btn in replyButtons)
                {
                    var comment = string.IsNullOrEmpty(btn.Name) ? $"Button {btn.ButtonId}" : btn.Name;
                    sb.AppendLine($"            case {btn.ButtonId}: // {comment}");
                    sb.AppendLine("                break;");
                }
                sb.AppendLine("        }");
            }

            // Collect text entries for info.GetTextEntry
            var textEntries = doc.GetAllElements().OfType<GumpTextEntry>().ToList();
            if (textEntries.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("        // Text entry values:");
                foreach (var te in textEntries)
                {
                    var name = string.IsNullOrEmpty(te.Name) ? $"entry{te.EntryId}" : te.Name.Replace(" ", "");
                    sb.AppendLine($"        // var {name} = info.GetTextEntry({te.EntryId});");
                }
            }

            // Collect switches for info.IsSwitched
            var switches = doc.GetAllElements().OfType<GumpCheck>().ToList();
            if (switches.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("        // Switch values:");
                foreach (var sw in switches)
                {
                    var name = string.IsNullOrEmpty(sw.Name) ? $"switch{sw.SwitchId}" : sw.Name.Replace(" ", "");
                    sb.AppendLine($"        // var {name} = info.IsSwitched({sw.SwitchId});");
                }
            }

            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string? GenerateElement(GumpElement element, bool hex)
    {
        return element switch
        {
            GumpBackground bg =>
                $"AddBackground({bg.X}, {bg.Y}, {bg.Width}, {bg.Height}, {Id(bg.GumpId, hex)});",
            GumpImage img when img.Hue != 0 =>
                $"AddImage({img.X}, {img.Y}, {Id(img.GumpId, hex)}, {img.Hue});",
            GumpImage img =>
                $"AddImage({img.X}, {img.Y}, {Id(img.GumpId, hex)});",
            GumpImageTiled tiled =>
                $"AddImageTiled({tiled.X}, {tiled.Y}, {tiled.Width}, {tiled.Height}, {Id(tiled.GumpId, hex)});",
            GumpAlphaRegion alpha =>
                $"AddAlphaRegion({alpha.X}, {alpha.Y}, {alpha.Width}, {alpha.Height});",
            GumpButton btn =>
                $"AddButton({btn.X}, {btn.Y}, {Id(btn.NormalId, hex)}, {Id(btn.PressedId, hex)}, {btn.ButtonId}, GumpButtonType.{btn.ButtonType}, {btn.Param});",
            GumpCheck chk =>
                $"AddCheck({chk.X}, {chk.Y}, {Id(chk.InactiveId, hex)}, {Id(chk.ActiveId, hex)}, {BoolStr(chk.InitialState)}, {chk.SwitchId});",
            GumpRadio radio =>
                $"AddRadio({radio.X}, {radio.Y}, {Id(radio.InactiveId, hex)}, {Id(radio.ActiveId, hex)}, {BoolStr(radio.InitialState)}, {radio.SwitchId});",
            GumpLabel label =>
                $"AddLabel({label.X}, {label.Y}, {label.Hue}, \"{EscapeString(label.Text)}\");",
            GumpLabelCropped cropped =>
                $"AddLabelCropped({cropped.X}, {cropped.Y}, {cropped.Width}, {cropped.Height}, {cropped.Hue}, \"{EscapeString(cropped.Text)}\");",
            GumpHtml html =>
                $"AddHtml({html.X}, {html.Y}, {html.Width}, {html.Height}, \"{EscapeString(html.Text)}\", {BoolStr(html.HasBackground)}, {BoolStr(html.HasScrollbar)});",
            GumpHtmlLocalized loc when loc.Color != 0 && !string.IsNullOrEmpty(loc.Args) =>
                $"AddHtmlLocalized({loc.X}, {loc.Y}, {loc.Width}, {loc.Height}, {loc.ClilocId}, \"{EscapeString(loc.Args)}\", {loc.Color}, {BoolStr(loc.HasBackground)}, {BoolStr(loc.HasScrollbar)});",
            GumpHtmlLocalized loc when loc.Color != 0 =>
                $"AddHtmlLocalized({loc.X}, {loc.Y}, {loc.Width}, {loc.Height}, {loc.ClilocId}, {loc.Color}, {BoolStr(loc.HasBackground)}, {BoolStr(loc.HasScrollbar)});",
            GumpHtmlLocalized loc =>
                $"AddHtmlLocalized({loc.X}, {loc.Y}, {loc.Width}, {loc.Height}, {loc.ClilocId}, {BoolStr(loc.HasBackground)}, {BoolStr(loc.HasScrollbar)});",
            GumpTextEntry entry when entry.MaxLength > 0 =>
                $"AddTextEntry({entry.X}, {entry.Y}, {entry.Width}, {entry.Height}, {entry.Hue}, {entry.EntryId}, \"{EscapeString(entry.InitialText)}\", {entry.MaxLength});",
            GumpTextEntry entry =>
                $"AddTextEntry({entry.X}, {entry.Y}, {entry.Width}, {entry.Height}, {entry.Hue}, {entry.EntryId}, \"{EscapeString(entry.InitialText)}\");",
            GumpItem item when item.Hue != 0 =>
                $"AddItem({item.X}, {item.Y}, {Id(item.ItemId, hex)}, {item.Hue});",
            GumpItem item =>
                $"AddItem({item.X}, {item.Y}, {Id(item.ItemId, hex)});",
            GumpTooltip tt =>
                $"AddTooltip({tt.ClilocId});",
            GumpGroup => null,
            _ => $"// Unknown: {element.ElementType}"
        };
    }

    private static string Id(int id, bool hex) => hex ? $"0x{id:X}" : id.ToString();
    private static string BoolStr(bool value) => value ? "true" : "false";
    private static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
