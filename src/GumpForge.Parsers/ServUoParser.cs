using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using GumpForge.Core.Models;

namespace GumpForge.Parsers;

/// <summary>
/// Parses ServUO/RunUO/ModernUO C# gump source code into a GumpDocument using Roslyn syntax analysis.
/// Handles literal arguments cleanly; dynamic/computed arguments become opaque expressions.
/// </summary>
public class ServUoParser : IGumpCodeParser
{
    public string TargetName => "ServUO";

    public bool CanParse(string source)
    {
        return source.Contains("Gump") && source.Contains("Add") &&
               (source.Contains(": Gump") || source.Contains(":Gump"));
    }

    public ParseResult Parse(string source)
    {
        var result = new ParseResult
        {
            Errors = [],
            Warnings = []
        };

        try
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();

            // Find the Gump-derived class
            var classDecl = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.BaseList?.Types
                    .Any(t => t.ToString().Contains("Gump")) == true);

            if (classDecl is null)
            {
                result.Errors.Add(new ParseDiagnostic
                {
                    Message = "No class inheriting from Gump found.",
                    Line = 1, Column = 1
                });
                return result;
            }

            var doc = new GumpDocument
            {
                GumpClassName = classDecl.Identifier.Text
            };

            // Extract namespace — handle both block and file-scoped namespaces
            var namespaceDecl = classDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            if (namespaceDecl is not null)
                doc.Namespace = namespaceDecl.Name.ToString();

            // Find the constructor (or first method that calls Add* methods)
            var constructor = classDecl.Members
                .OfType<ConstructorDeclarationSyntax>()
                .FirstOrDefault();

            if (constructor is not null)
            {
                // Parse base(x, y) initializer
                if (constructor.Initializer is not null)
                {
                    var args = constructor.Initializer.ArgumentList.Arguments;
                    if (args.Count >= 2)
                    {
                        doc.GumpX = TryParseInt(args[0].Expression) ?? 100;
                        doc.GumpY = TryParseInt(args[1].Expression) ?? 100;
                    }
                }

                // Parse all statements looking for Add* calls and property assignments
                ParseStatements(constructor.Body?.Statements ?? [], doc, result);
            }

            // Auto-fit canvas to content bounds
            AutoFitCanvas(doc);

            result.Document = doc;
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ParseDiagnostic
            {
                Message = $"Parse error: {ex.Message}",
                Line = 1, Column = 1
            });
        }

        return result;
    }

    private void ParseStatements(SyntaxList<StatementSyntax> statements, GumpDocument doc, ParseResult result)
    {
        int currentPage = 0;

        foreach (var statement in statements)
        {
            if (statement is ExpressionStatementSyntax exprStmt)
            {
                if (exprStmt.Expression is InvocationExpressionSyntax invocation)
                {
                    var methodName = GetMethodName(invocation);
                    var args = invocation.ArgumentList.Arguments;
                    var lineSpan = invocation.GetLocation().GetLineSpan();
                    int line = lineSpan.StartLinePosition.Line + 1;

                    try
                    {
                        switch (methodName)
                        {
                            case "AddPage":
                                currentPage = TryParseInt(args[0].Expression) ?? 0;
                                doc.GetOrCreatePage(currentPage);
                                break;

                            case "AddBackground":
                            case "AddResizeGump": // Alias used by some emulators
                                if (args.Count >= 5)
                                {
                                    AddToPage(doc, currentPage, new GumpBackground
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        Width = Arg(args, 2), Height = Arg(args, 3),
                                        GumpId = Arg(args, 4),
                                        Name = $"Background_{Arg(args, 4):X}"
                                    });
                                }
                                break;

                            case "AddImage":
                            case "AddGumpPicture": // Alias
                                if (args.Count >= 3)
                                {
                                    var img = new GumpImage
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        GumpId = Arg(args, 2),
                                        Width = 44, Height = 44, // Default; will be updated from art
                                        Name = $"Image_{Arg(args, 2):X}"
                                    };
                                    if (args.Count >= 4) img.Hue = Arg(args, 3);
                                    AddToPage(doc, currentPage, img);
                                }
                                break;

                            case "AddImageTiled":
                                if (args.Count >= 5)
                                {
                                    AddToPage(doc, currentPage, new GumpImageTiled
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        Width = Arg(args, 2), Height = Arg(args, 3),
                                        GumpId = Arg(args, 4),
                                        Name = $"TiledImage_{Arg(args, 4):X}"
                                    });
                                }
                                break;

                            case "AddAlphaRegion":
                                if (args.Count >= 4)
                                {
                                    AddToPage(doc, currentPage, new GumpAlphaRegion
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        Width = Arg(args, 2), Height = Arg(args, 3),
                                        Name = "AlphaRegion"
                                    });
                                }
                                break;

                            case "AddButton":
                                if (args.Count >= 7)
                                {
                                    AddToPage(doc, currentPage, new GumpButton
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        NormalId = Arg(args, 2), PressedId = Arg(args, 3),
                                        ButtonId = Arg(args, 4),
                                        ButtonType = ParseButtonType(args, 5),
                                        Param = Arg(args, 6),
                                        Width = 40, Height = 40, // Default button size
                                        Name = $"Button_{Arg(args, 4)}"
                                    });
                                }
                                break;

                            case "AddCheck":
                                if (args.Count >= 6)
                                {
                                    AddToPage(doc, currentPage, new GumpCheck
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        InactiveId = Arg(args, 2), ActiveId = Arg(args, 3),
                                        InitialState = ArgBool(args, 4),
                                        SwitchId = Arg(args, 5),
                                        Width = 30, Height = 30,
                                        Name = $"Check_{Arg(args, 5)}"
                                    });
                                }
                                break;

                            case "AddRadio":
                                if (args.Count >= 6)
                                {
                                    AddToPage(doc, currentPage, new GumpRadio
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        InactiveId = Arg(args, 2), ActiveId = Arg(args, 3),
                                        InitialState = ArgBool(args, 4),
                                        SwitchId = Arg(args, 5),
                                        Width = 30, Height = 30,
                                        Name = $"Radio_{Arg(args, 5)}"
                                    });
                                }
                                break;

                            case "AddLabel":
                                if (args.Count >= 4)
                                {
                                    var text = ArgString(args, 3);
                                    AddToPage(doc, currentPage, new GumpLabel
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        Hue = Arg(args, 2),
                                        Text = text,
                                        Width = Math.Max(text.Length * 8, 60),
                                        Height = 20,
                                        Name = $"Label"
                                    });
                                }
                                break;

                            case "AddLabelCropped":
                                if (args.Count >= 6)
                                {
                                    AddToPage(doc, currentPage, new GumpLabelCropped
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        Width = Arg(args, 2), Height = Arg(args, 3),
                                        Hue = Arg(args, 4),
                                        Text = ArgString(args, 5),
                                        Name = $"LabelCropped"
                                    });
                                }
                                break;

                            case "AddHtml":
                                if (args.Count >= 7)
                                {
                                    AddToPage(doc, currentPage, new GumpHtml
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        Width = Arg(args, 2), Height = Arg(args, 3),
                                        Text = ArgString(args, 4),
                                        HasBackground = ArgBool(args, 5),
                                        HasScrollbar = ArgBool(args, 6),
                                        Name = "Html"
                                    });
                                }
                                break;

                            case "AddHtmlLocalized":
                                if (args.Count >= 7)
                                {
                                    var loc = new GumpHtmlLocalized
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        Width = Arg(args, 2), Height = Arg(args, 3),
                                        ClilocId = Arg(args, 4),
                                        Name = $"HtmlLoc_{Arg(args, 4)}"
                                    };
                                    if (args.Count == 7)
                                    {
                                        loc.HasBackground = ArgBool(args, 5);
                                        loc.HasScrollbar = ArgBool(args, 6);
                                    }
                                    else if (args.Count == 8)
                                    {
                                        loc.Color = Arg(args, 5);
                                        loc.HasBackground = ArgBool(args, 6);
                                        loc.HasScrollbar = ArgBool(args, 7);
                                    }
                                    else if (args.Count >= 9)
                                    {
                                        // AddHtmlLocalized(x, y, w, h, cliloc, args, color, bg, scroll)
                                        loc.Args = ArgString(args, 5);
                                        loc.Color = Arg(args, 6);
                                        loc.HasBackground = ArgBool(args, 7);
                                        loc.HasScrollbar = ArgBool(args, 8);
                                    }
                                    AddToPage(doc, currentPage, loc);
                                }
                                break;

                            case "AddTextEntry":
                                if (args.Count >= 7)
                                {
                                    var entry = new GumpTextEntry
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        Width = Arg(args, 2), Height = Arg(args, 3),
                                        Hue = Arg(args, 4),
                                        EntryId = Arg(args, 5),
                                        InitialText = ArgString(args, 6),
                                        Name = $"TextEntry_{Arg(args, 5)}"
                                    };
                                    if (args.Count >= 8)
                                        entry.MaxLength = Arg(args, 7);
                                    AddToPage(doc, currentPage, entry);
                                }
                                break;

                            case "AddItem":
                            case "AddItemProperty": // alias
                                if (args.Count >= 3)
                                {
                                    var item = new GumpItem
                                    {
                                        X = Arg(args, 0), Y = Arg(args, 1),
                                        ItemId = Arg(args, 2),
                                        Width = 44, Height = 44,
                                        Name = $"Item_{Arg(args, 2):X}"
                                    };
                                    if (args.Count >= 4) item.Hue = Arg(args, 3);
                                    AddToPage(doc, currentPage, item);
                                }
                                break;

                            case "AddTooltip":
                                if (args.Count >= 1)
                                {
                                    AddToPage(doc, currentPage, new GumpTooltip
                                    {
                                        ClilocId = Arg(args, 0),
                                        Name = $"Tooltip_{Arg(args, 0)}"
                                    });
                                }
                                break;

                            default:
                                if (methodName?.StartsWith("Add") == true)
                                {
                                    result.Warnings.Add(new ParseDiagnostic
                                    {
                                        Line = line, Column = 1,
                                        Message = $"Unrecognized Add method: {methodName}",
                                        Severity = "Warning"
                                    });
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add(new ParseDiagnostic
                        {
                            Line = line, Column = 1,
                            Message = $"Could not parse {methodName}: {ex.Message}",
                            Severity = "Warning"
                        });
                    }
                }
                else if (exprStmt.Expression is AssignmentExpressionSyntax assignment)
                {
                    // Handle property assignments like Closable = true
                    var propName = assignment.Left.ToString().Replace("this.", "").Trim();
                    var value = assignment.Right.ToString().Trim().ToLower();

                    switch (propName)
                    {
                        case "Closable": doc.IsClosable = value == "true"; break;
                        case "Disposable": doc.IsDisposable = value == "true"; break;
                        // Handle all spelling variants
                        case "Dragable":
                        case "Draggable":
                        case "Movable":
                        case "Moveable":
                            doc.IsDraggable = value == "true"; break;
                        case "Resizable":
                        case "Resizeable":
                            doc.IsResizable = value == "true"; break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Auto-fit the canvas size to encompass all parsed elements with padding.
    /// </summary>
    private static void AutoFitCanvas(GumpDocument doc)
    {
        int maxRight = 400;
        int maxBottom = 300;

        foreach (var page in doc.Pages)
        {
            foreach (var el in page.Elements)
            {
                int right = el.X + Math.Max(el.Width, 44);
                int bottom = el.Y + Math.Max(el.Height, 44);
                if (right > maxRight) maxRight = right;
                if (bottom > maxBottom) maxBottom = bottom;
            }
        }

        // Add padding and round up to nearest 50
        doc.CanvasWidth = ((maxRight + 80) / 50) * 50;
        doc.CanvasHeight = ((maxBottom + 80) / 50) * 50;

        // Minimum canvas size
        doc.CanvasWidth = Math.Max(doc.CanvasWidth, 400);
        doc.CanvasHeight = Math.Max(doc.CanvasHeight, 300);
    }

    private static void AddToPage(GumpDocument doc, int pageNumber, GumpElement element)
    {
        element.Page = pageNumber;
        var page = doc.GetOrCreatePage(pageNumber);
        page.Elements.Add(element);
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
    }

    private static int Arg(SeparatedSyntaxList<ArgumentSyntax> args, int index)
    {
        if (index >= args.Count) return 0;
        return TryParseInt(args[index].Expression) ?? 0;
    }

    private static bool ArgBool(SeparatedSyntaxList<ArgumentSyntax> args, int index)
    {
        if (index >= args.Count) return false;
        var text = args[index].Expression.ToString().Trim().ToLower();
        return text == "true" || text == "1";
    }

    private static string ArgString(SeparatedSyntaxList<ArgumentSyntax> args, int index)
    {
        if (index >= args.Count) return string.Empty;
        var expr = args[index].Expression;

        // Handle @"..." verbatim strings, "..." regular strings, and String.Format
        return expr switch
        {
            LiteralExpressionSyntax literal => literal.Token.ValueText,
            InterpolatedStringExpressionSyntax interpolated => interpolated.ToString().Trim('$', '"', '@'),
            _ => expr.ToString().Trim('"', '@', ' ')
        };
    }

    private static GumpButtonType ParseButtonType(SeparatedSyntaxList<ArgumentSyntax> args, int index)
    {
        if (index >= args.Count) return GumpButtonType.Reply;
        var text = args[index].Expression.ToString();
        if (text.Contains("Page")) return GumpButtonType.Page;
        if (text.Contains("Reply")) return GumpButtonType.Reply;
        // Try numeric
        if (int.TryParse(text, out int val))
            return (GumpButtonType)val;
        return GumpButtonType.Reply;
    }

    private static int? TryParseInt(ExpressionSyntax expr)
    {
        var text = expr.ToString().Trim();

        // Handle hex literals (0x1234)
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out int hexVal))
                return hexVal;
        }

        // Handle regular integers
        if (int.TryParse(text, out int val))
            return val;

        // Handle negation
        if (expr is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            var inner = TryParseInt(prefix.Operand);
            if (inner.HasValue) return -inner.Value;
        }

        // Handle casts like (int)Buttons.Confirm
        if (expr is CastExpressionSyntax cast)
            return TryParseInt(cast.Expression);

        // Handle parenthesized: (100)
        if (expr is ParenthesizedExpressionSyntax parens)
            return TryParseInt(parens.Expression);

        // Can't resolve — dynamic expression
        return null;
    }
}
