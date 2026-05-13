using GumpForge.Core.Models;

namespace GumpForge.Generators;

/// <summary>
/// Options controlling code generation output.
/// </summary>
public class GenerationOptions
{
    public string Namespace { get; set; } = "Server.Gumps";
    public string ClassName { get; set; } = "MyGump";
    public bool UseHexIds { get; set; } = true;
    public bool GenerateOnResponse { get; set; } = true;
    public string AccessModifier { get; set; } = "public";
}

/// <summary>
/// Interface for pluggable code generators.
/// Each emulator target implements this to produce code from a GumpDocument.
/// </summary>
public interface IGumpCodeGenerator
{
    /// <summary>Display name (e.g. "ServUO").</summary>
    string TargetName { get; }

    /// <summary>File extension (e.g. ".cs").</summary>
    string FileExtension { get; }

    /// <summary>Generate source code from the document model.</summary>
    string Generate(GumpDocument doc, GenerationOptions opts);
}
