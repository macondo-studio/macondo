using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Risultato strutturato dell'estrazione semantica di un singolo file sorgente VB.NET (.vb).
/// </summary>
public sealed class VisualBasicNetExtractionResult
{
    /// <summary>Percorso assoluto o relativo del file sorgente VB.NET analizzato.</summary>
    public required string FilePath { get; init; }

    /// <summary>Elenco dei simboli di primo livello (classi, interfacce, moduli, strutture, enum) dichiarati nel file.</summary>
    public required IReadOnlyList<Symbol> TopLevelSymbols { get; init; }

    /// <summary>Elenco dei call-site di invocazione rilevati nei corpi delle routine VB.NET.</summary>
    public required IReadOnlyList<VisualBasicNetCallSite> CallSites { get; init; }

    /// <summary>Elenco delle istanziazioni 'New' rilevate nei metodi o nelle inizializzazioni di campo.</summary>
    public required IReadOnlyList<VisualBasicNetInstantiation> Instantiations { get; init; }

    /// <summary>Elenco delle relazioni di ereditarietà ('Inherits') e interfacce ('Implements') estratte.</summary>
    public required IReadOnlyList<VisualBasicNetTypeInheritance> Inheritances { get; init; }

    /// <summary>Elenco degli errori diagnostici di compilazione o parsing rilevati da Roslyn sul file.</summary>
    public required IReadOnlyList<string> ParseErrors { get; init; }

    /// <summary>Indica se il file è stato parsato ed estratto con successo senza errori critici di sintassi.</summary>
    public bool Success => ParseErrors.Count == 0;
}
