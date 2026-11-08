using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.SocialCode;

/// <summary>
/// Singolo record di commit o modifica registrato nella storia del VCS.
/// </summary>
public sealed class FileCommitRecord
{
    public string Author { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public int LinesAdded { get; init; }
    public int LinesDeleted { get; init; }
}

/// <summary>
/// Statistiche di contribuzione di un autore su un file o modulo.
/// </summary>
public sealed class AuthorContribution
{
    public string Author { get; init; } = string.Empty;
    public int CommitCount { get; set; }
    public int TotalLinesChanged { get; set; }
    public double OwnershipPercentage { get; set; }
    public DateTime LastCommitDate { get; set; }
}

/// <summary>
/// Livello di rischio legato al Bus Factor e turnover per un modulo.
/// </summary>
public enum BusFactorRisk
{
    /// <summary>Rischio Basso: molteplici sviluppatori contribuiscono attivamente al componente.</summary>
    Low,

    /// <summary>Rischio Medio: 2 autori principali.</summary>
    Medium,

    /// <summary>Rischio Alto / Critico: un solo autore detiene l'intero know-how del componente (Bus Factor = 1).</summary>
    High,

    /// <summary>Codice Orfano (Orphan Code): l'autore principale non contribuisce più al repository o è inattivo.</summary>
    Orphan
}

/// <summary>
/// Report sociale e di ownership per un singolo file/modulo del progetto.
/// </summary>
public sealed class FileSocialReport
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public Guid? SymbolId { get; init; }

    /// <summary>Autore principale che ha scritto/modificato la maggior parte del codice.</summary>
    public string PrimaryAuthor { get; set; } = string.Empty;

    /// <summary>Percentuale di ownership dell'autore principale (0-100%).</summary>
    public double PrimaryOwnership { get; set; }

    /// <summary>Numero totale di autori che hanno contribuito a questo file.</summary>
    public int TotalAuthorsCount => AuthorContributions.Count;

    /// <summary>Bus Factor calcolato per questo file (numero minimo di autori per coprire l'80% del codice).</summary>
    public int BusFactor { get; set; }

    /// <summary>Livello di rischio assegnato.</summary>
    public BusFactorRisk Risk { get; set; }

    /// <summary>Dettaglio delle contribuzioni per singolo autore.</summary>
    public List<AuthorContribution> AuthorContributions { get; init; } = new();

    /// <summary>Frammentazione della conoscenza (Shannon Entropy normalizzata [0..1]).</summary>
    public double KnowledgeEntropy { get; set; }
}

/// <summary>
/// Dataset aggregato dell'analisi sociale del codice e legge di Conway.
/// </summary>
public sealed class SocialCodeDataset
{
    public List<FileSocialReport> Files { get; init; } = new();

    /// <summary>Bus Factor medio dell'intera codebase.</summary>
    public double AverageBusFactor => Files.Count > 0 ? Files.Average(f => f.BusFactor) : 1.0;

    /// <summary>Numero di file/moduli a rischio critico di perdita di conoscenza (Bus Factor = 1).</summary>
    public int HighRiskCount => Files.Count(f => f.Risk == BusFactorRisk.High);

    /// <summary>Numero di file orfani (l'autore principale non è più attivo).</summary>
    public int OrphanFilesCount => Files.Count(f => f.Risk == BusFactorRisk.Orphan);

    /// <summary>Mappa globale degli autori con conteggio file posseduti.</summary>
    public Dictionary<string, int> PrimaryOwnershipCountByAuthor { get; init; } = new();
}
