using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GumpForge.ScriptAnalysis;

/// <summary>
/// Uses Roslyn syntax analysis to extract gump construction details from C# scripts.
/// Discovers: class hierarchy, constructor parameters, Add* calls, conditional branches,
/// and cross-file references.
/// </summary>
public class RoslynGumpAnalyzer
{
    /// <summary>
    /// Analyzes a single script file and extracts all gump class definitions.
    /// </summary>
    public List<DiscoveredGump> AnalyzeFile(string filePath)
    {
        if (!File.Exists(filePath))
            return [];

        var source = File.ReadAllText(filePath);
        return AnalyzeSource(source, filePath);
    }

    /// <summary>
    /// Analyzes source code and extracts all gump class definitions.
    /// </summary>
    public List<DiscoveredGump> AnalyzeSource(string source, string filePath = "<inline>")
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var results = new List<DiscoveredGump>();

        // Find all classes that inherit from Gump or BaseGump
        var gumpClasses = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(c => c.BaseList?.Types.Any(t =>
            {
                var typeName = t.Type.ToString();
                return typeName.Contains("Gump") || typeName.Contains("BaseGump");
            }) == true)
            .ToList();

        foreach (var classDecl in gumpClasses)
        {
            var gump = new DiscoveredGump
            {
                FilePath = filePath,
                ClassName = classDecl.Identifier.Text,
                BaseClass = classDecl.BaseList?.Types.FirstOrDefault()?.Type.ToString() ?? "Gump"
            };

            // Extract namespace
            var ns = classDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            if (ns is not null)
                gump.Namespace = ns.Name.ToString();

            // Analyze constructors
            var constructors = classDecl.Members.OfType<ConstructorDeclarationSyntax>().ToList();
            foreach (var ctor in constructors)
            {
                // Extract parameters as variables
                foreach (var param in ctor.ParameterList.Parameters)
                {
                    gump.Variables.Add(new ScriptVariable
                    {
                        Name = param.Identifier.Text,
                        TypeName = param.Type?.ToString() ?? "object",
                        Kind = InferVariableKind(param.Type?.ToString() ?? "object"),
                        DefaultValue = GetDefaultForType(param.Type?.ToString() ?? "object")
                    });
                }

                // Extract gump calls from constructor body
                if (ctor.Body is not null)
                {
                    ExtractGumpCalls(ctor.Body.Statements, gump, null);
                }
            }

            // Also check for an override void Compile() or AddGumpLayout() method
            var layoutMethods = classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text is "Compile" or "AddGumpLayout" or "CompileLayout")
                .ToList();

            foreach (var method in layoutMethods)
            {
                if (method.Body is not null)
                    ExtractGumpCalls(method.Body.Statements, gump, null);
            }

            // Extract field references that might point to other files
            ExtractCrossReferences(classDecl, gump);

            gump.ElementCount = gump.GumpCalls.Count;
            results.Add(gump);
        }

        return results;
    }

    /// <summary>
    /// Analyzes multiple files and builds a call graph connecting related scripts.
    /// </summary>
    public GumpCallGraph BuildCallGraph(List<string> filePaths)
    {
        var graph = new GumpCallGraph();

        foreach (var path in filePaths)
        {
            var gumps = AnalyzeFile(path);
            foreach (var g in gumps)
            {
                graph.Gumps.Add(g);
            }
        }

        // Resolve cross-references between discovered gumps
        foreach (var gump in graph.Gumps)
        {
            foreach (var refFile in gump.ReferencedFiles.ToList())
            {
                var referenced = graph.Gumps.Where(g =>
                    Path.GetFileName(g.FilePath).Equals(refFile, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var r in referenced)
                {
                    if (!graph.Edges.Any(e => e.From == gump.ClassName && e.To == r.ClassName))
                    {
                        graph.Edges.Add(new CallGraphEdge
                        {
                            From = gump.ClassName,
                            To = r.ClassName,
                            Relationship = "references"
                        });
                    }
                }
            }
        }

        return graph;
    }

    // ── Private extraction methods ──────────────────────────────

    private void ExtractGumpCalls(SyntaxList<StatementSyntax> statements,
        DiscoveredGump gump, string? parentCondition)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case ExpressionStatementSyntax exprStmt:
                    if (exprStmt.Expression is InvocationExpressionSyntax invocation)
                    {
                        var methodName = GetMethodName(invocation);
                        if (IsGumpApiCall(methodName))
                        {
                            gump.GumpCalls.Add(new GumpCallInfo
                            {
                                MethodName = methodName,
                                Arguments = invocation.ArgumentList.Arguments
                                    .Select(a => a.Expression.ToString()).ToList(),
                                LineNumber = exprStmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                ConditionExpression = parentCondition
                            });
                        }
                    }
                    break;

                case IfStatementSyntax ifStmt:
                    var condition = ifStmt.Condition.ToString();
                    var branch = new ConditionalBranch
                    {
                        Condition = condition,
                        LineNumber = ifStmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                    };

                    // True branch
                    if (ifStmt.Statement is BlockSyntax trueBlock)
                    {
                        ExtractGumpCallsFromBlock(trueBlock.Statements, branch.TrueBranch, condition);
                        ExtractGumpCalls(trueBlock.Statements, gump, condition);
                    }
                    else if (ifStmt.Statement is ExpressionStatementSyntax singleStmt)
                    {
                        var tempStatements = new SyntaxList<StatementSyntax>(new[] { singleStmt });
                        ExtractGumpCalls(tempStatements, gump, condition);
                    }

                    // Else branch
                    if (ifStmt.Else?.Statement is BlockSyntax elseBlock)
                    {
                        ExtractGumpCallsFromBlock(elseBlock.Statements, branch.FalseBranch, $"!({condition})");
                        ExtractGumpCalls(elseBlock.Statements, gump, $"!({condition})");
                    }

                    if (branch.TrueBranch.Count > 0 || branch.FalseBranch.Count > 0)
                        gump.Conditionals.Add(branch);
                    break;

                case ForStatementSyntax forStmt:
                    if (forStmt.Statement is BlockSyntax forBlock)
                        ExtractGumpCalls(forBlock.Statements, gump, $"for: {forStmt.Condition}");
                    break;

                case ForEachStatementSyntax foreachStmt:
                    if (foreachStmt.Statement is BlockSyntax foreachBlock)
                        ExtractGumpCalls(foreachBlock.Statements, gump, $"foreach: {foreachStmt.Expression}");
                    break;

                case BlockSyntax block:
                    ExtractGumpCalls(block.Statements, gump, parentCondition);
                    break;
            }
        }
    }

    private void ExtractGumpCallsFromBlock(SyntaxList<StatementSyntax> statements,
        List<GumpCallInfo> target, string condition)
    {
        foreach (var stmt in statements)
        {
            if (stmt is ExpressionStatementSyntax exprStmt &&
                exprStmt.Expression is InvocationExpressionSyntax invocation)
            {
                var methodName = GetMethodName(invocation);
                if (IsGumpApiCall(methodName))
                {
                    target.Add(new GumpCallInfo
                    {
                        MethodName = methodName,
                        Arguments = invocation.ArgumentList.Arguments
                            .Select(a => a.Expression.ToString()).ToList(),
                        LineNumber = stmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        ConditionExpression = condition
                    });
                }
            }
        }
    }

    private void ExtractCrossReferences(ClassDeclarationSyntax classDecl, DiscoveredGump gump)
    {
        // Look for "new XyzGump(" patterns — these reference other gumps
        var objectCreations = classDecl.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(o => o.Type.ToString().Contains("Gump"))
            .ToList();

        foreach (var creation in objectCreations)
        {
            var typeName = creation.Type.ToString();
            if (typeName != gump.ClassName)
                gump.ReferencedFiles.Add(typeName + ".cs");
        }

        // Look for static method calls on other classes
        var memberAccess = classDecl.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(m => m.Expression is IdentifierNameSyntax id &&
                         char.IsUpper(id.Identifier.Text[0]) &&
                         id.Identifier.Text != gump.ClassName)
            .Select(m => ((IdentifierNameSyntax)m.Expression).Identifier.Text)
            .Distinct()
            .ToList();

        // Only add likely script references (not System types)
        foreach (var name in memberAccess)
        {
            if (!IsSystemType(name))
                gump.ReferencedFiles.Add(name + ".cs");
        }
    }

    private static string GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            _ => invocation.Expression.ToString()
        };
    }

    private static bool IsGumpApiCall(string methodName)
    {
        return methodName.StartsWith("Add") && methodName.Length > 3 &&
               char.IsUpper(methodName[3]);
    }

    private static bool IsSystemType(string name)
    {
        return name is "Console" or "Math" or "String" or "Convert" or "Int32"
            or "Boolean" or "List" or "Dictionary" or "Array" or "Enum"
            or "Object" or "Type" or "DateTime" or "TimeSpan" or "Guid"
            or "Path" or "File" or "Directory" or "Environment"
            or "Task" or "Thread" or "Timer" or "Debug" or "Trace";
    }

    private static VariableKind InferVariableKind(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "int" or "int32" or "uint" or "long" or "short" or "byte"
                or "double" or "float" or "decimal" => VariableKind.Integer,
            "bool" or "boolean" => VariableKind.Boolean,
            "string" => VariableKind.Text,
            _ => VariableKind.Object
        };
    }

    private static string GetDefaultForType(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "int" or "int32" or "uint" or "long" or "short" or "byte" => "0",
            "double" or "float" or "decimal" => "0.0",
            "bool" or "boolean" => "true",
            "string" => "",
            _ => "(mock)"
        };
    }
}

/// <summary>
/// Represents the call graph between multiple gump scripts.
/// </summary>
public class GumpCallGraph
{
    public List<DiscoveredGump> Gumps { get; set; } = [];
    public List<CallGraphEdge> Edges { get; set; } = [];
}

public class CallGraphEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
}
