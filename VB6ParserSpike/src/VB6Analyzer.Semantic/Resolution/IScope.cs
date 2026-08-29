using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Resolution;

/// <summary>
/// Interfaccia per uno scope di visibilità dei simboli in VB6 (sezione 13 del charter).
/// </summary>
public interface IScope
{
    IScope? ParentScope { get; }
    Symbol? ScopeSymbol { get; }
    string ScopeName { get; }

    /// <summary>
    /// Risolve un identificatore semplice (non qualificato) secondo l'ordine di lookup VB6:
    /// local -> parameter -> class/module member -> .bas public member -> project types.
    /// </summary>
    ResolutionResult Resolve(string name);
}
