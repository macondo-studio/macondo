using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Risultato strutturato dell'estrazione semantica di un singolo file sorgente Java (.java).
/// </summary>
public sealed class JavaExtractionResult
{
    /// <summary>Percorso assoluto o relativo del file sorgente Java analizzato.</summary>
    public required string FilePath { get; init; }

    /// <summary>Elenco dei simboli di primo livello (classi, interfacce, record, enum, annotazioni) dichiarati nel file.</summary>
    public required IReadOnlyList<Symbol> TopLevelSymbols { get; init; }

    /// <summary>Elenco dei call-site di invocazione rilevati nei corpi dei metodi Java.</summary>
    public required IReadOnlyList<JavaCallSite> CallSites { get; init; }

    /// <summary>Elenco delle istanziazioni 'new' rilevate nei metodi o nelle inizializzazioni di campo.</summary>
    public required IReadOnlyList<JavaInstantiation> Instantiations { get; init; }

    /// <summary>Elenco delle relazioni di ereditarietà e implementazione di interfacce estratte dalle dichiarazioni di tipo.</summary>
    public required IReadOnlyList<JavaTypeInheritance> Inheritances { get; init; }

    /// <summary>Elenco degli errori diagnostici di parsing rilevati sul file.</summary>
    public required IReadOnlyList<string> ParseErrors { get; init; }

    /// <summary>Indica se il file è stato parsato ed estratto con successo senza errori bloccanti.</summary>
    public bool Success => ParseErrors.Count == 0;
}
