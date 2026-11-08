using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Risultato strutturato dell'estrazione semantica di un singolo file sorgente Kotlin (.kt o .kts).
/// </summary>
public sealed class KotlinExtractionResult
{
    /// <summary>Percorso assoluto o relativo del file sorgente Kotlin analizzato.</summary>
    public required string FilePath { get; init; }

    /// <summary>Elenco dei simboli radice (classi, interfacce, object, enum o facciata di file per elementi top-level) dichiarati nel file.</summary>
    public required IReadOnlyList<Symbol> TopLevelSymbols { get; init; }

    /// <summary>Elenco dei call-site di chiamata rilevati nei corpi delle funzioni o proprietà.</summary>
    public required IReadOnlyList<KotlinCallSite> CallSites { get; init; }

    /// <summary>Elenco delle istanziazioni rilevate nei metodi o nelle inizializzazioni di proprietà.</summary>
    public required IReadOnlyList<KotlinInstantiation> Instantiations { get; init; }

    /// <summary>Elenco delle relazioni di ereditarietà e implementazione di interfacce estratte dalle dichiarazioni di tipo.</summary>
    public required IReadOnlyList<KotlinTypeInheritance> Inheritances { get; init; }

    /// <summary>Elenco degli errori diagnostici di parsing rilevati sul file.</summary>
    public required IReadOnlyList<string> ParseErrors { get; init; }

    /// <summary>Indica se il file è stato parsato ed estratto con successo senza errori bloccanti.</summary>
    public bool Success => ParseErrors.Count == 0;
}
