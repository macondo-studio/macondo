using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Martin;

/// <summary>
/// Zona concettuale del diagramma di Martin (Robert C. Martin / Uncle Bob).
/// </summary>
public enum MartinZone
{
    /// <summary>
    /// Componenti bilanciati, posizionati lungo la Sequenza Principale (A + I ≈ 1).
    /// </summary>
    MainSequence,

    /// <summary>
    /// Zona di Sofferenza (Zone of Pain): Bassa astrattezza (A basso) e alta stabilità (I basso, molti dipendenti).
    /// Tipico dei God Object / Moduli monolitici concreti difficilissimi da modificare.
    /// </summary>
    ZoneOfPain,

    /// <summary>
    /// Zona di Inutilità (Zone of Uselessness): Alta astrattezza (A alto) e alta instabilità (I alto, nessuno ne dipende).
    /// Astrazioni sovra-ingegnerizzate o interfacce orfane.
    /// </summary>
    ZoneOfUselessness,

    /// <summary>
    /// Zona intermedia bilanciata non critica.
    /// </summary>
    Balanced
}

/// <summary>
/// Singolo componente analizzato nel Diagramma di Martin (Modulo, Classe, Form, Interfaccia, Controllo).
/// </summary>
public sealed class MartinMetricItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }

    /// <summary>Accoppiamento Afferente (Ca): Numero di componenti esterni che dipendono da questo modulo.</summary>
    public int AfferentCoupling { get; set; }

    /// <summary>Accoppiamento Efferente (Ce): Numero di componenti esterni da cui questo modulo dipende.</summary>
    public int EfferentCoupling { get; set; }

    /// <summary>Instabilità (I = Ce / (Ca + Ce)): Varia da 0.0 (massima stabilità) a 1.0 (massima instabilità).</summary>
    public double Instability { get; set; }

    /// <summary>Numero di elementi astratti (interfacce, metodi astratti, firme vuote/pure).</summary>
    public int AbstractSymbolsCount { get; set; }

    /// <summary>Numero di elementi concreti (metodi implementati, campi, proprietà concrete).</summary>
    public int ConcreteSymbolsCount { get; set; }

    /// <summary>Astrattezza (A = Na / (Na + Nc)): Varia da 0.0 (puramente concreto) a 1.0 (puramente astratto).</summary>
    public double Abstractness { get; set; }

    /// <summary>Distanza normalizzata dalla Sequenza Principale D = |A + I - 1| / sqrt(2) in [0..1].</summary>
    public double NormalizedDistance { get; set; }

    /// <summary>Distanza assoluta D = |A + I - 1|.</summary>
    public double Distance => Math.Abs(Abstractness + Instability - 1.0);

    /// <summary>Zona architettonica assegnata.</summary>
    public MartinZone Zone { get; set; }

    /// <summary>Linee di codice stimate o numero membri totali.</summary>
    public int LocOrSize { get; set; }
}

/// <summary>
/// Dataset completo delle Metriche di Martin per un progetto o workspace.
/// </summary>
public sealed class MartinMetricsDataset
{
    public List<MartinMetricItem> Items { get; init; } = new();

    public double AverageDistance => Items.Count > 0 ? Items.Average(i => i.NormalizedDistance) : 0.0;
    public int PainZoneCount => Items.Count(i => i.Zone == MartinZone.ZoneOfPain);
    public int UselessnessZoneCount => Items.Count(i => i.Zone == MartinZone.ZoneOfUselessness);
    public int MainSequenceCount => Items.Count(i => i.Zone == MartinZone.MainSequence);
}
