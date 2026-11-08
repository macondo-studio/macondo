using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Maintainability;

/// <summary>
/// Livello di manutenibilità secondo lo standard Microsoft / SEI Code Metrics.
/// </summary>
public enum MaintainabilityRating
{
    /// <summary>Indice 80 - 100: Codice altamente manutenibile, chiaro e ben modulato.</summary>
    High,

    /// <summary>Indice 20 - 79: Manutenibilità moderata con aree di debito tecnico o complessità.</summary>
    Moderate,

    /// <summary>Indice 0 - 19: Codice ad altissimo rischio, monolitico o scarsamente manutenibile.</summary>
    Low
}

/// <summary>
/// Metriche di Halstead dettagliate per un elemento di codice (Maurice Halstead, 1977).
/// </summary>
public sealed class HalsteadMetrics
{
    /// <summary>Numero di operatori unici / distinti (n1).</summary>
    public int DistinctOperators { get; set; }

    /// <summary>Numero di operandi unici / distinti (n2).</summary>
    public int DistinctOperands { get; set; }

    /// <summary>Numero totale di occorrenze di operatori (N1).</summary>
    public int TotalOperators { get; set; }

    /// <summary>Numero totale di occorrenze di operandi (N2).</summary>
    public int TotalOperands { get; set; }

    /// <summary>Vocabolario del programma: eta = n1 + n2.</summary>
    public int Vocabulary => DistinctOperators + DistinctOperands;

    /// <summary>Lunghezza totale del programma: N = N1 + N2.</summary>
    public int Length => TotalOperators + TotalOperands;

    /// <summary>Lunghezza stimata calcolata: N_hat = n1 * log2(n1) + n2 * log2(n2).</summary>
    public double EstimatedLength
    {
        get
        {
            if (DistinctOperators <= 0 || DistinctOperands <= 0) return 0.0;
            return (DistinctOperators * Math.Log2(DistinctOperators)) + (DistinctOperands * Math.Log2(DistinctOperands));
        }
    }

    /// <summary>Volume di Halstead: V = N * log2(eta). Rappresenta lo spazio informativo in bit.</summary>
    public double Volume
    {
        get
        {
            int voc = Vocabulary;
            if (voc <= 1 || Length <= 0) return 1.0;
            return Length * Math.Log2(voc);
        }
    }

    /// <summary>Difficoltà di comprensione o implementazione: D = (n1 / 2) * (N2 / n2).</summary>
    public double Difficulty
    {
        get
        {
            if (DistinctOperands <= 0) return 1.0;
            return ((double)DistinctOperators / 2.0) * ((double)TotalOperands / DistinctOperands);
        }
    }

    /// <summary>Sforzo intellettuale richiesto per comprendere/scrivere il codice: E = D * V.</summary>
    public double Effort => Difficulty * Volume;

    /// <summary>Tempo stimato di comprensione in secondi (legge di Stroud): T = E / 18.</summary>
    public double TimeToUnderstandSeconds => Effort / 18.0;

    /// <summary>Numero stimato di bug / difetti latenti: B = V / 3000.</summary>
    public double EstimatedBugs => Volume / 3000.0;
}

/// <summary>
/// Singolo elemento (File, Classe, Modulo, Routine) valutato per la manutenibilità.
/// </summary>
public sealed class MaintainabilityItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }
    public SourceLanguage Language { get; init; }

    /// <summary>Linee di codice fisiche totali (LOC / LoC).</summary>
    public int LinesOfCode { get; set; }

    /// <summary>Complessità Ciclomatica di McCabe stimata o calcolata (CC / V(G)).</summary>
    public int CyclomaticComplexity { get; set; }

    /// <summary>Dettaglio delle metriche di Halstead.</summary>
    public HalsteadMetrics Halstead { get; init; } = new();

    /// <summary>
    /// Maintainability Index (MI) normalizzato secondo lo standard Microsoft / SEI:
    /// MI = MAX(0, (171 - 5.2 * ln(V) - 0.23 * CC - 16.2 * ln(LOC)) * 100 / 171)
    /// </summary>
    public double MaintainabilityIndex { get; set; }

    /// <summary>Livello qualitativo (High, Moderate, Low).</summary>
    public MaintainabilityRating Rating
    {
        get
        {
            if (MaintainabilityIndex >= 80.0) return MaintainabilityRating.High;
            if (MaintainabilityIndex >= 20.0) return MaintainabilityRating.Moderate;
            return MaintainabilityRating.Low;
        }
    }
}

/// <summary>
/// Dataset aggregato per l'analisi di manutenibilità del progetto.
/// </summary>
public sealed class MaintainabilityDataset
{
    public List<MaintainabilityItem> Items { get; init; } = new();

    /// <summary>Maintainability Index medio ponderato del progetto.</summary>
    public double AverageMaintainabilityIndex => Items.Count > 0 ? Items.Average(i => i.MaintainabilityIndex) : 100.0;

    /// <summary>Numero di elementi ad alta manutenibilità (MI >= 80).</summary>
    public int HighCount => Items.Count(i => i.Rating == MaintainabilityRating.High);

    /// <summary>Numero di elementi a moderata manutenibilità (20 <= MI < 80).</summary>
    public int ModerateCount => Items.Count(i => i.Rating == MaintainabilityRating.Moderate);

    /// <summary>Numero di elementi a bassa/critica manutenibilità (MI < 20).</summary>
    public int LowCount => Items.Count(i => i.Rating == MaintainabilityRating.Low);

    /// <summary>Stima totale dei bug latenti calcolata tramite la formula di Halstead.</summary>
    public double TotalEstimatedBugs => Items.Sum(i => i.Halstead.EstimatedBugs);
}
