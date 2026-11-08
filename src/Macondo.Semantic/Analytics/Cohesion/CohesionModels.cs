using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Cohesion;

/// <summary>
/// Rappresenta un cluster coeso o componente connessa isolata all'interno di una classe o modulo.
/// Se una classe ha più di un cluster, ciascun cluster rappresenta un candidato a diventare una classe separata.
/// </summary>
public sealed class CohesionCluster
{
    /// <summary>Identificativo del cluster all'interno del modulo.</summary>
    public int ClusterIndex { get; init; }

    /// <summary>Elenco dei metodi/proprietà appartenenti a questo cluster di responsabilità.</summary>
    public List<Symbol> Methods { get; init; } = new();

    /// <summary>Elenco dei campi o proprietà di stato condivisi e acceduti dai metodi di questo cluster.</summary>
    public List<Symbol> AccessedFields { get; init; } = new();

    /// <summary>Nome suggerito per l'eventuale classe estratta dal refactoring.</summary>
    public string SuggestedRefactoringName { get; set; } = string.Empty;
}

/// <summary>
/// Risultato dettagliato dell'analisi di coesione per un singolo contenitore (Classe, Modulo, Form, Struct).
/// </summary>
public sealed class ContainerCohesionReport
{
    /// <summary>Simbolo del contenitore analizzato.</summary>
    public Symbol Container { get; init; } = null!;

    /// <summary>Identificativo univoco del contenitore.</summary>
    public Guid Id => Container.Id;

    /// <summary>Nome del contenitore.</summary>
    public string Name => Container.Name;

    /// <summary>Nome qualificato del contenitore.</summary>
    public string QualifiedName => Container.QualifiedName;

    /// <summary>File sorgente di appartenenza.</summary>
    public string FilePath => Container.FilePath;

    /// <summary>Tipologia di simbolo.</summary>
    public SymbolKind Kind => Container.Kind;

    /// <summary>Numero totale di metodi e proprietà presenti nel contenitore.</summary>
    public int MethodCount { get; set; }

    /// <summary>Numero totale di campi e variabili di stato del contenitore.</summary>
    public int FieldCount { get; set; }

    /// <summary>
    /// Metrica LCOM4 (Lack of Cohesion in Methods - Hitz & Montazeri).
    /// Indica il numero di componenti connesse disgiunte nel grafo metodi-campi.
    /// - LCOM4 = 1: Ottima coesione (Single Responsibility).
    /// - LCOM4 >= 2: Scarsa coesione; la classe svolge responsabilità multiple e dovrebbe essere suddivisa in LCOM4 classi.
    /// - LCOM4 = 0: Nessun metodo o classe vuota.
    /// </summary>
    public int Lcom4 { get; set; }

    /// <summary>
    /// Metrica TCC (Tight Class Cohesion - Bieman & Kang).
    /// Percentuale di coppie di metodi direttamente collegate tramite l'accesso ad almeno un campo comune (in [0.0 .. 1.0]).
    /// </summary>
    public double TightClassCohesion { get; set; }

    /// <summary>
    /// Metrica LCC (Loose Class Cohesion - Bieman & Kang).
    /// Percentuale di coppie di metodi direttamente o transitivamente collegate (in [0.0 .. 1.0]).
    /// </summary>
    public double LooseClassCohesion { get; set; }

    /// <summary>
    /// Metrica WMC (Weighted Methods per Class - Chidamber & Kemerer).
    /// Somma della complessità ciclomatica di tutti i metodi (o conteggio se non calcolata).
    /// </summary>
    public int WeightedMethodsCount { get; set; }

    /// <summary>
    /// Metrica CBO (Coupling Between Object classes - C&K).
    /// Numero di altri contenitori/tipi con cui questa classe è accoppiata.
    /// </summary>
    public int CouplingBetweenObjects { get; set; }

    /// <summary>
    /// Metrica RFC (Response for a Class - C&K).
    /// Numero di metodi della classe più numero di metodi esterni chiamati direttamente dai suoi metodi.
    /// </summary>
    public int ResponseForClass { get; set; }

    /// <summary>
    /// Indica se la classe è un candidato prioritario per il refactoring (es. LCOM4 >= 2 o TCC < 0.3 con più di 3 metodi).
    /// </summary>
    public bool IsRefactoringCandidate => Lcom4 >= 2 || (MethodCount >= 5 && TightClassCohesion < 0.33);

    /// <summary>
    /// Componenti connesse (cluster di metodi e campi) individuate per la suddivisione della classe.
    /// </summary>
    public List<CohesionCluster> Clusters { get; init; } = new();
}

/// <summary>
/// Dataset completo dell'analisi di coesione per un intero progetto o workspace poliglotto.
/// </summary>
public sealed class CohesionDataset
{
    /// <summary>Elenco dei report di coesione per ogni contenitore del progetto.</summary>
    public List<ContainerCohesionReport> Items { get; init; } = new();

    /// <summary>LCOM4 medio tra tutti i contenitori analizzati con almeno 2 metodi.</summary>
    public double AverageLcom4 => Items.Where(i => i.MethodCount >= 2).Select(i => (double)i.Lcom4).DefaultIfEmpty(0.0).Average();

    /// <summary>TCC medio tra tutti i contenitori con almeno 2 metodi.</summary>
    public double AverageTcc => Items.Where(i => i.MethodCount >= 2).Select(i => i.TightClassCohesion).DefaultIfEmpty(1.0).Average();

    /// <summary>Numero totale di classi/moduli identificati come candidati prioritari al refactoring.</summary>
    public int RefactoringCandidateCount => Items.Count(i => i.IsRefactoringCandidate);
}
