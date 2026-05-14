namespace GumpForge.ScriptAnalysis;

/// <summary>
/// Represents a discovered gump class in a server script.
/// </summary>
public class DiscoveredGump
{
    /// <summary>Full path to the .cs file containing this gump.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Class name (e.g. "StatusGump", "SpellbookGump").</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>Namespace (e.g. "Server.Gumps").</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Base class (e.g. "Gump", "BaseGump").</summary>
    public string BaseClass { get; set; } = string.Empty;

    /// <summary>Number of AddPage/AddBackground/AddImage etc. calls found.</summary>
    public int ElementCount { get; set; }

    /// <summary>Constructor parameters — used to discover test inputs.</summary>
    public List<ScriptVariable> Variables { get; set; } = [];

    /// <summary>Other script files referenced by this gump (via method calls, field access).</summary>
    public List<string> ReferencedFiles { get; set; } = [];

    /// <summary>Elements extracted from the gump construction.</summary>
    public List<GumpCallInfo> GumpCalls { get; set; } = [];

    /// <summary>Conditional branches that affect gump rendering.</summary>
    public List<ConditionalBranch> Conditionals { get; set; } = [];

    /// <summary>Friendly display name.</summary>
    public string DisplayName => $"{ClassName} ({Path.GetFileName(FilePath)})";
}

/// <summary>
/// A variable/parameter discovered in a gump constructor or method.
/// Users can set test values for these in the preview panel.
/// </summary>
public class ScriptVariable
{
    /// <summary>Parameter/field name (e.g. "m_From", "skill", "hasSpell").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>C# type name (e.g. "Mobile", "int", "bool", "string").</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Inferred UI control type for the test panel.</summary>
    public VariableKind Kind { get; set; } = VariableKind.Text;

    /// <summary>Default test value as a string.</summary>
    public string DefaultValue { get; set; } = string.Empty;

    /// <summary>Current test value set by the user.</summary>
    public string TestValue { get; set; } = string.Empty;
}

public enum VariableKind
{
    Text,       // Generic text input
    Integer,    // NumericUpDown
    Boolean,    // CheckBox
    Enum,       // ComboBox with known values
    Object      // Complex object — collapsed to a text representation
}

/// <summary>
/// Represents a single gump API call (e.g. AddBackground(10, 10, 5054, 400, 300)).
/// </summary>
public class GumpCallInfo
{
    /// <summary>Method name (e.g. "AddBackground", "AddImage", "AddButton").</summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>Raw argument expressions as strings.</summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>Source line number in the script file.</summary>
    public int LineNumber { get; set; }

    /// <summary>Whether this call is inside a conditional branch.</summary>
    public string? ConditionExpression { get; set; }

    /// <summary>Whether the arguments contain computed expressions (not just literals).</summary>
    public bool HasDynamicArgs => Arguments.Any(a =>
        !int.TryParse(a, out _) && !bool.TryParse(a, out _) && a != "true" && a != "false");
}

/// <summary>
/// A conditional branch that affects which gump elements are rendered.
/// </summary>
public class ConditionalBranch
{
    /// <summary>The condition expression (e.g. "player.Skills.Magery.Value > 50").</summary>
    public string Condition { get; set; } = string.Empty;

    /// <summary>Gump calls inside the true branch.</summary>
    public List<GumpCallInfo> TrueBranch { get; set; } = [];

    /// <summary>Gump calls inside the false/else branch.</summary>
    public List<GumpCallInfo> FalseBranch { get; set; } = [];

    /// <summary>Source line number.</summary>
    public int LineNumber { get; set; }
}
