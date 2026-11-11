using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Cohesion;

/// <summary>
/// Represents a cohesive cluster or isolated connected component within a class or module.
/// If a class has more than one cluster, each cluster represents a candidate to become a separate class.
/// </summary>
public sealed class CohesionCluster
{
    /// <summary>Cluster identifier within the module.</summary>
    public int ClusterIndex { get; init; }

    /// <summary>List of methods/properties belonging to this responsibility cluster.</summary>
    public List<Symbol> Methods { get; init; } = new();

    /// <summary>List of fields or state properties shared and accessed by methods in this cluster.</summary>
    public List<Symbol> AccessedFields { get; init; } = new();

    /// <summary>Suggested name for any class extracted from the refactoring.</summary>
    public string SuggestedRefactoringName { get; set; } = string.Empty;
}

/// <summary>
/// Detailed cohesion analysis result for a single container (Class, Module, Form, Struct).
/// </summary>
public sealed class ContainerCohesionReport
{
    /// <summary>Symbol of the analyzed container.</summary>
    public Symbol Container { get; init; } = null!;

    /// <summary>Unique identifier of the container.</summary>
    public Guid Id => Container.Id;

    /// <summary>Container name.</summary>
    public string Name => Container.Name;

    /// <summary>Qualified name of the container.</summary>
    public string QualifiedName => Container.QualifiedName;

    /// <summary>Owner source file.</summary>
    public string FilePath => Container.FilePath;

    /// <summary>Type of symbol.</summary>
    public SymbolKind Kind => Container.Kind;

    /// <summary>Total number of methods and properties present in the container.</summary>
    public int MethodCount { get; set; }

    /// <summary>Total number of fields and state variables in the container.</summary>
    public int FieldCount { get; set; }

    /// <summary>
    /// LCOM4 metric (Lack of Cohesion in Methods - Hitz & Montazeri).
    /// Indicates the number of disjoint connected components in the methods-fields graph.
    /// - LCOM4 = 1: Excellent cohesion (Single Responsibility).
    /// - LCOM4 >= 2: Poor cohesion; the class carries out multiple responsibilities and should be divided into LCOM4 classes.
    /// - LCOM4 = 0: No empty methods or classes.
    /// </summary>
    public int Lcom4 { get; set; }

    /// <summary>
    /// Metrica TCC (Tight Class Cohesion - Bieman & Kang).
    /// Percentage of method pairs directly linked by accessing at least one common field (in [0.0 .. 1.0]).
    /// </summary>
    public double TightClassCohesion { get; set; }

    /// <summary>
    /// Metrica LCC (Loose Class Cohesion - Bieman & Kang).
    /// Percentage of directly or transitively connected method pairs (in [0.0 .. 1.0]).
    /// </summary>
    public double LooseClassCohesion { get; set; }

    /// <summary>
    /// WMC metric (Weighted Methods per Class - Chidamber & Kemerer).
    /// Sum of cyclomatic complexity of all methods (or count if not calculated).
    /// </summary>
    public int WeightedMethodsCount { get; set; }

    /// <summary>
    /// Metrica CBO (Coupling Between Object classes - C&K).
    /// Number of other containers/types this class is paired with.
    /// </summary>
    public int CouplingBetweenObjects { get; set; }

    /// <summary>
    /// RFC (Response for a Class - C&K) metric.
    /// Number of methods of the class plus number of external methods called directly by its methods.
    /// </summary>
    public int ResponseForClass { get; set; }

    /// <summary>
    /// Indicates whether the class is a priority candidate for refactoring (e.g. LCOM4 >= 2 or TCC < 0.3 with more than 3 methods).
    /// </summary>
    public bool IsRefactoringCandidate => Lcom4 >= 2 || (MethodCount >= 5 && TightClassCohesion < 0.33);

    /// <summary>
    /// Connected components (clusters of methods and fields) identified for class subdivision.
    /// </summary>
    public List<CohesionCluster> Clusters { get; init; } = new();
}

/// <summary>
/// Complete cohesion analysis dataset for an entire polyglot project or workspace.
/// </summary>
public sealed class CohesionDataset
{
    /// <summary>List of cohesion reports for each project container.</summary>
    public List<ContainerCohesionReport> Items { get; init; } = new();

    /// <summary>LCOM4 average across all containers analyzed with at least 2 methods.</summary>
    public double AverageLcom4 => Items.Where(i => i.MethodCount >= 2).Select(i => (double)i.Lcom4).DefaultIfEmpty(0.0).Average();

    /// <summary>Average TCC across all bins with at least 2 methods.</summary>
    public double AverageTcc => Items.Where(i => i.MethodCount >= 2).Select(i => i.TightClassCohesion).DefaultIfEmpty(1.0).Average();

    /// <summary>Total number of classes/modules identified as priority candidates for refactoring.</summary>
    public int RefactoringCandidateCount => Items.Count(i => i.IsRefactoringCandidate);
}
