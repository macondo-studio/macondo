using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Resolution;

/// <summary>
/// Interfaccia per uno scope di visibilità e lookup dei simboli in VB6 (sezione 13 del charter).
/// 
/// Gli scope sono organizzati in una catena gerarchica genitore-figlio (parent chain):
/// - LocalScope (variabili locali, parametri)
///   -> ModuleScope (membri della classe o modulo, Me)
///     -> ProjectScope (tipi di progetto, membri Public di moduli .bas)
/// 
/// Blocchi contestuali come 'With' inseriscono un <see cref="WithScope"/> nella catena.
/// </summary>
public interface IScope
{
    /// <summary>Scope genitore a cui delegare la ricerca se il simbolo non è trovato localmente.</summary>
    IScope? ParentScope { get; }

    /// <summary>Simbolo associato a questo scope (es. il Method per LocalScope o la Class per ModuleScope).</summary>
    Symbol? ScopeSymbol { get; }

    /// <summary>Nome identificativo dello scope a fini di diagnostica e tracciamento.</summary>
    string ScopeName { get; }

    /// <summary>
    /// Risolve un identificatore non qualificato all'interno dello scope corrente,
    /// risalendo la gerarchia se necessario.
    /// </summary>
    /// <param name="name">Nome del simbolo da risolvere (es. "localCust", "Save", "LogMessage").</param>
    /// <returns>Il risultato della risoluzione con simbolo target e livello di confidenza.</returns>
    ResolutionResult Resolve(string name);
}
