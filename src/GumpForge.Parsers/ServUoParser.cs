using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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

    public Dictionary<string, string> EvaluationContext { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, MethodDeclarationSyntax> m_ClassMethods = new(StringComparer.OrdinalIgnoreCase);

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

            // Index all class methods for inlining
            m_ClassMethods.Clear();
            foreach (var member in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                m_ClassMethods[member.Identifier.Text] = member;
            }

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
                        doc.GumpX = ResolveValueAsInt(args[0].Expression) ?? 100;
                        doc.GumpY = ResolveValueAsInt(args[1].Expression) ?? 100;
                    }
                }

                // Bind constructor formal parameters to context mock fields
                var paramList = constructor.ParameterList.Parameters;
                foreach (var p in paramList)
                {
                    var pName = p.Identifier.Text;
                    // If not already in context, default string parameter to mock / empty
                    if (!EvaluationContext.ContainsKey(pName))
                    {
                        EvaluationContext[pName] = p.Type.ToString().Equals("string", StringComparison.OrdinalIgnoreCase) ? "" : "null";
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
                                currentPage = ResolveValueAsInt(args[0].Expression) ?? 0;
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
                                        Name = string.Format("Background_{0:X}", Arg(args, 4))
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
                                        Name = string.Format("Image_{0:X}", Arg(args, 2))
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
                                        Name = string.Format("TiledImage_{0:X}", Arg(args, 4))
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
                                        Name = string.Format("Button_{0}", Arg(args, 4))
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
                                        Name = string.Format("Check_{0}", Arg(args, 5))
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
                                        Name = string.Format("Radio_{0}", Arg(args, 5))
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
                                        Name = "Label"
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
                                        Name = "LabelCropped"
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
                                        Name = string.Format("HtmlLoc_{0}", Arg(args, 4))
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
                                        Name = string.Format("TextEntry_{0}", Arg(args, 5))
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
                                        Name = string.Format("Item_{0:X}", Arg(args, 2))
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
                                        Name = string.Format("Tooltip_{0}", Arg(args, 0))
                                    });
                                }
                                break;

                            default:
                                // CHECK FOR CLASS-LEVEL METHOD INLINING
                                if (methodName != null && m_ClassMethods.TryGetValue(methodName, out var methodDecl))
                                {
                                    var scopedContext = new Dictionary<string, string>(EvaluationContext, StringComparer.OrdinalIgnoreCase);
                                    var paramList = methodDecl.ParameterList.Parameters;
                                    for (int i = 0; i < paramList.Count && i < args.Count; i++)
                                    {
                                        var pName = paramList[i].Identifier.Text;
                                        var argVal = ResolveValueAsString(args[i].Expression);
                                        if (argVal != null)
                                        {
                                            scopedContext[pName] = argVal;
                                        }
                                    }

                                    var previousContext = EvaluationContext;
                                    EvaluationContext = scopedContext;

                                    ParseStatements(methodDecl.Body?.Statements ?? [], doc, result);

                                    EvaluationContext = previousContext;
                                }
                                else if (methodName?.StartsWith("Add") == true)
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
                        case "Dragable":
                        case "Draggable":
                        case "Movable":
                        case "Moveable":
                            doc.IsDraggable = value == "true"; break;
                        case "Resizable":
                        case "Resizeable":
                            doc.IsResizable = value == "true"; break;
                        default:
                            var rightVal = ResolveValueAsString(assignment.Right);
                            if (rightVal != null)
                            {
                                EvaluationContext[propName] = rightVal;
                            }
                            break;
                    }
                }
            }
            else if (statement is LocalDeclarationStatementSyntax localDecl)
            {
                foreach (var variable in localDecl.Declaration.Variables)
                {
                    var varName = variable.Identifier.Text;
                    if (variable.Initializer != null)
                    {
                        var varValue = ResolveValueAsString(variable.Initializer.Value);
                        if (varValue != null)
                        {
                            EvaluationContext[varName] = varValue;
                        }
                    }
                }
            }
            else if (statement is IfStatementSyntax ifStmt)
            {
                bool condValue = EvaluateCondition(ifStmt.Condition);
                if (condValue)
                {
                    if (ifStmt.Statement is BlockSyntax block)
                    {
                        ParseStatements(block.Statements, doc, result);
                    }
                    else
                    {
                        ParseStatements(new SyntaxList<StatementSyntax>().Add(ifStmt.Statement), doc, result);
                    }
                }
                else if (ifStmt.Else != null)
                {
                    if (ifStmt.Else.Statement is BlockSyntax elseBlock)
                    {
                        ParseStatements(elseBlock.Statements, doc, result);
                    }
                    else
                    {
                        ParseStatements(new SyntaxList<StatementSyntax>().Add(ifStmt.Else.Statement), doc, result);
                    }
                }
            }
        }
    }

    private bool EvaluateCondition(ExpressionSyntax condition)
    {
        if (condition == null) return true;

        if (condition is LiteralExpressionSyntax literal)
        {
            if (condition.IsKind(SyntaxKind.TrueLiteralExpression)) return true;
            if (condition.IsKind(SyntaxKind.FalseLiteralExpression)) return false;
        }

        if (condition is ParenthesizedExpressionSyntax parens)
        {
            return EvaluateCondition(parens.Expression);
        }

        if (condition is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return !EvaluateCondition(prefix.Operand);
        }

        if (condition is BinaryExpressionSyntax binary)
        {
            if (binary.IsKind(SyntaxKind.LogicalAndExpression))
            {
                return EvaluateCondition(binary.Left) && EvaluateCondition(binary.Right);
            }
            if (binary.IsKind(SyntaxKind.LogicalOrExpression))
            {
                return EvaluateCondition(binary.Left) || EvaluateCondition(binary.Right);
            }

            var leftStr = ResolveValueAsString(binary.Left);
            var rightStr = ResolveValueAsString(binary.Right);

            var leftNum = ResolveValueAsInt(binary.Left);
            var rightNum = ResolveValueAsInt(binary.Right);

            if (leftNum.HasValue && rightNum.HasValue)
            {
                int l = leftNum.Value;
                int r = rightNum.Value;

                if (binary.IsKind(SyntaxKind.EqualsExpression)) return l == r;
                if (binary.IsKind(SyntaxKind.NotEqualsExpression)) return l != r;
                if (binary.IsKind(SyntaxKind.GreaterThanExpression)) return l > r;
                if (binary.IsKind(SyntaxKind.LessThanExpression)) return l < r;
                if (binary.IsKind(SyntaxKind.GreaterThanOrEqualExpression)) return l >= r;
                if (binary.IsKind(SyntaxKind.LessThanOrEqualExpression)) return l <= r;
            }
            else
            {
                string l = (leftStr == "null" ? "" : leftStr) ?? "";
                string r = (rightStr == "null" ? "" : rightStr) ?? "";

                if (binary.IsKind(SyntaxKind.EqualsExpression)) return l.Equals(r, StringComparison.OrdinalIgnoreCase);
                if (binary.IsKind(SyntaxKind.NotEqualsExpression)) return !l.Equals(r, StringComparison.OrdinalIgnoreCase);
            }
        }

        var identifier = condition.ToString().Trim();
        if (EvaluationContext.TryGetValue(identifier, out string? boolVal))
        {
            return boolVal.ToLower() == "true" || boolVal == "1";
        }

        if (!string.IsNullOrEmpty(identifier) && identifier != "null" && identifier != "false")
        {
            if (EvaluationContext.TryGetValue(identifier, out string? val))
            {
                return !string.IsNullOrEmpty(val) && val != "null" && val.ToLower() != "false";
            }
        }

        return false;
    }

    private string? ResolveValueAsString(ExpressionSyntax expr)
    {
        if (expr == null) return null;
        var text = expr.ToString().Trim();
        
        if (expr is LiteralExpressionSyntax literal && expr.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        if (expr is IdentifierNameSyntax id)
        {
            var varName = id.Identifier.Text;
            if (EvaluationContext.TryGetValue(varName, out string? val))
            {
                return val;
            }
        }

        // Handle ternary conditional expressions (cond ? trueVal : falseVal)
        if (expr is ConditionalExpressionSyntax condExpr)
        {
            bool cond = EvaluateCondition(condExpr.Condition);
            return cond ? ResolveValueAsString(condExpr.WhenTrue) : ResolveValueAsString(condExpr.WhenFalse);
        }

        if (expr is InterpolatedStringExpressionSyntax interpolated)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolatedStringTextSyntax textSyntax)
                {
                    sb.Append(textSyntax.TextToken.ValueText);
                }
                else if (content is InterpolationSyntax interpolation)
                {
                    var val = ResolveValueAsString(interpolation.Expression);
                    sb.Append(val ?? "");
                }
            }
            return sb.ToString();
        }

        if (expr is MemberAccessExpressionSyntax memberAccess)
        {
            var objName = memberAccess.Expression.ToString();
            var propName = memberAccess.Name.Identifier.Text;
            if (propName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                if (EvaluationContext.TryGetValue("Name", out string? nameVal)) return nameVal;
                if (EvaluationContext.TryGetValue(objName + "Name", out string? objNameVal)) return objNameVal;
            }
            if (EvaluationContext.TryGetValue(objName + "." + propName, out string? dottedVal)) return dottedVal;
            if (EvaluationContext.TryGetValue(propName, out string? propVal)) return propVal;
        }

        if (expr is InvocationExpressionSyntax invocation)
        {
            var mName = GetMethodName(invocation);
            if (mName != null && m_ClassMethods.TryGetValue(mName, out var method))
            {
                return EvaluateMethodReturnString(method, invocation.ArgumentList.Arguments);
            }
            else if (mName == "ToString")
            {
                if (invocation.Expression is MemberAccessExpressionSyntax ma)
                {
                    return ResolveValueAsString(ma.Expression);
                }
            }
            else if (mName == "Format" && invocation.Expression.ToString().Contains("String"))
            {
                var args = invocation.ArgumentList.Arguments;
                if (args.Count > 0)
                {
                    var formatStr = ResolveValueAsString(args[0].Expression) ?? "";
                    var formatArgs = new object[args.Count - 1];
                    for (int i = 1; i < args.Count; i++)
                    {
                        formatArgs[i - 1] = ResolveValueAsString(args[i].Expression) ?? "";
                    }
                    try
                    {
                        return string.Format(formatStr, formatArgs);
                    }
                    catch { return formatStr; }
                }
            }
        }

        if (EvaluationContext.TryGetValue(text, out string? value))
        {
            return value;
        }

        return text.Trim('"', '@', ' ');
    }

    private string? EvaluateMethodReturnString(MethodDeclarationSyntax method, SeparatedSyntaxList<ArgumentSyntax> args)
    {
        var scopedContext = new Dictionary<string, string>(EvaluationContext, StringComparer.OrdinalIgnoreCase);
        var paramList = method.ParameterList.Parameters;
        for (int i = 0; i < paramList.Count && i < args.Count; i++)
        {
            var val = ResolveValueAsString(args[i].Expression);
            if (val != null) scopedContext[paramList[i].Identifier.Text] = val;
        }

        var previousContext = EvaluationContext;
        EvaluationContext = scopedContext;

        var returnStmt = method.Body?.DescendantNodes().OfType<ReturnStatementSyntax>().FirstOrDefault();
        string? result = null;
        if (returnStmt != null)
        {
            result = ResolveValueAsString(returnStmt.Expression);
        }

        EvaluationContext = previousContext;
        return result;
    }

    private int? ResolveValueAsInt(ExpressionSyntax expr)
    {
        var resolvedStr = ResolveValueAsString(expr);
        if (resolvedStr == null) return null;

        if (int.TryParse(resolvedStr, out int val))
        {
            return val;
        }

        if (resolvedStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(resolvedStr[2..], System.Globalization.NumberStyles.HexNumber, null, out int hexVal))
                return hexVal;
        }

        if (expr is BinaryExpressionSyntax binary)
        {
            var left = ResolveValueAsInt(binary.Left);
            var right = ResolveValueAsInt(binary.Right);
            if (left.HasValue && right.HasValue)
            {
                if (binary.IsKind(SyntaxKind.AddExpression)) return left.Value + right.Value;
                if (binary.IsKind(SyntaxKind.SubtractExpression)) return left.Value - right.Value;
                if (binary.IsKind(SyntaxKind.MultiplyExpression)) return left.Value * right.Value;
                if (binary.IsKind(SyntaxKind.DivideExpression) && right.Value != 0) return left.Value / right.Value;
            }
        }

        return TryParseInt(expr);
    }

    private bool? ResolveValueAsBool(ExpressionSyntax expr)
    {
        var resolvedStr = ResolveValueAsString(expr)?.ToLower().Trim();
        if (resolvedStr == "true" || resolvedStr == "1") return true;
        if (resolvedStr == "false" || resolvedStr == "0") return false;

        return EvaluateCondition(expr);
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

    private int Arg(SeparatedSyntaxList<ArgumentSyntax> args, int index)
    {
        if (index >= args.Count) return 0;
        return ResolveValueAsInt(args[index].Expression) ?? 0;
    }

    private bool ArgBool(SeparatedSyntaxList<ArgumentSyntax> args, int index)
    {
        if (index >= args.Count) return false;
        return ResolveValueAsBool(args[index].Expression) ?? false;
    }

    private string ArgString(SeparatedSyntaxList<ArgumentSyntax> args, int index)
    {
        if (index >= args.Count) return string.Empty;
        return ResolveValueAsString(args[index].Expression) ?? string.Empty;
    }

    private static GumpButtonType ParseButtonType(SeparatedSyntaxList<ArgumentSyntax> args, int index)
    {
        if (index >= args.Count) return GumpButtonType.Reply;
        var text = args[index].Expression.ToString();
        if (text.Contains("Page")) return GumpButtonType.Page;
        if (text.Contains("Reply")) return GumpButtonType.Reply;
        if (int.TryParse(text, out int val))
            return (GumpButtonType)val;
        return GumpButtonType.Reply;
    }

    private static int? TryParseInt(ExpressionSyntax expr)
    {
        var text = expr.ToString().Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out int hexVal))
                return hexVal;
        }

        if (int.TryParse(text, out int val))
            return val;

        if (expr is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            var inner = TryParseInt(prefix.Operand);
            if (inner.HasValue) return -inner.Value;
        }

        if (expr is CastExpressionSyntax cast)
            return TryParseInt(cast.Expression);

        if (expr is ParenthesizedExpressionSyntax parens)
            return TryParseInt(parens.Expression);

        return null;
    }
}
