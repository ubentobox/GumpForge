using GumpForge.Core.Models;

namespace GumpForge.Parsers;

/// <summary>
/// Result of parsing source code into a GumpDocument.
/// </summary>
public class ParseResult
{
    public GumpDocument? Document { get; set; }
    public bool Success => Document is not null && Errors.Count == 0;
    public List<ParseDiagnostic> Errors { get; init; } = [];
    public List<ParseDiagnostic> Warnings { get; init; } = [];
}

public class ParseDiagnostic
{
    public int Line { get; init; }
    public int Column { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Severity { get; init; } = "Error";
}

/// <summary>
/// Interface for pluggable code parsers.
/// Implementations convert source code strings back into GumpDocument models.
/// </summary>
public interface IGumpCodeParser
{
    /// <summary>Display name (e.g. "ServUO").</summary>
    string TargetName { get; }

    /// <summary>Quick check: can this parser handle the given source?</summary>
    bool CanParse(string source);

    /// <summary>Parse source code into a GumpDocument, with error diagnostics.</summary>
    ParseResult Parse(string source);
}
