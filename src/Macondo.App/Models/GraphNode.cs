using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Visual node of the CodeGraph rendered by the GraphControl.
/// Encapsulates the real semantic <see cref="Symbol"/> extracted from the VB6 parsing pipeline
/// and maintains coordinates for layout and interaction state (zoom, select, expand).
/// </summary>
public sealed class GraphNode
{
    /// <summary>Unique ID of the node (corresponds to the Symbol.Id for real symbols).</summary>
    public required Guid Id { get; init; }

    /// <summary>Display name of the symbol.</summary>
    public required string Name { get; init; }

    /// <summary>Nome qualificato completo (es. "Customer.Save").</summary>
    public required string QualifiedName { get; init; }

    /// <summary>Category of the semantic symbol.</summary>
    public required SymbolKind Kind { get; init; }

    /// <summary>The underlying real semantic symbol (null for the synthetic project root node).</summary>
    public Symbol? SemanticSymbol { get; init; }

    /// <summary>Parent container node in the CONTAINS hierarchy (null for the Project root).</summary>
    public Guid? ParentId { get; set; }

    /// <summary>ID of the project to which this node belongs (if in a workspace).</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Name of the project to which this node belongs.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Position X in the graph (calculated by the layout engine).</summary>
    public double X { get; set; }

    /// <summary>Y position in the graph (calculated by the layout engine).</summary>
    public double Y { get; set; }

    /// <summary>Node drawing radius in pixels.</summary>
    public double Radius => Kind switch
    {
        SymbolKind.Project => 28,
        SymbolKind.Form or SymbolKind.Class or SymbolKind.Module or SymbolKind.UserControl => 20,
        SymbolKind.Method or SymbolKind.Property => 11,
        SymbolKind.Field or SymbolKind.Constant or SymbolKind.Enum or SymbolKind.Type => 9,
        _ => 10
    };

    /// <summary>
    /// Indicates whether the node is an expandable container (Class, Form, Module, Type, Enum).
    /// </summary>
    public bool IsContainer => Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                                    or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum;

    /// <summary>
    /// Expansion state: child members are visible only if the container is expanded (progressive disclosure).
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    private SourceLanguage? _language;

    /// <summary>Node source programming language (e.g. VB6, C#, C++, etc.).</summary>
    public SourceLanguage Language
    {
        get
        {
            if (_language.HasValue && _language.Value != SourceLanguage.Unknown)
                return _language.Value;

            // 1. Primary detection from source file path
            var detected = DetectLanguageFromPath();
            if (detected != SourceLanguage.Unknown)
                return detected;

            // 2. Secondary detection from the actual semantic symbol
            if (SemanticSymbol != null)
            {
                var symLang = SemanticSymbol.GetEffectiveLanguage();
                if (symLang != SourceLanguage.Unknown)
                    return symLang;
            }

            return SourceLanguage.Unknown;
        }
        set => _language = value;
    }

    private SourceLanguage DetectLanguageFromPath()
    {
        var path = SemanticSymbol?.FilePath;
        if (string.IsNullOrEmpty(path)) return SourceLanguage.Unknown;
        var ext = System.IO.Path.GetExtension(path);
        return ext.ToLowerInvariant() switch
        {
            ".vbp" or ".vbg" or ".frm" or ".bas" or ".cls" or ".ctl" or ".pag" or ".dsx" or ".dob" => SourceLanguage.VisualBasic6,
            ".csproj" or ".sln" or ".cs" => SourceLanguage.CSharp,
            ".cpp" or ".c" or ".h" or ".hpp" or ".vcxproj" => SourceLanguage.CPlusPlus,
            ".java" => SourceLanguage.Java,
            ".kt" or ".kts" => SourceLanguage.Kotlin,
            ".py" => SourceLanguage.Python,
            ".ts" => SourceLanguage.TypeScript,
            ".js" => SourceLanguage.JavaScript,
            _ => SourceLanguage.Unknown
        };
    }

    public override string ToString() => $"{Kind}: {QualifiedName}";
}
