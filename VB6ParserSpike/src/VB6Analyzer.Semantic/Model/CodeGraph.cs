namespace Macondo.Semantic.Model;

/// <summary>
/// Grafo del codice semantico in memoria (CodeGraph, sezioni 3 e 18 del charter).
/// 
/// Rappresenta l'intero modello del codice analizzato: nodi (<see cref="Symbols"/>) ed archi orientati (<see cref="Relationships"/>).
/// È progettato per aggregare molteplici moduli, classi e form di un intero progetto VB6 in un'unica struttura
/// in RAM altamente interrogabile, indipendente da qualsiasi database o interfaccia utente.
/// </summary>
public sealed class CodeGraph
{
    /// <summary>Elenco completo di tutti i simboli estratti nel progetto.</summary>
    public List<Symbol> Symbols { get; } = new();

    /// <summary>Elenco completo di tutte le relazioni semantiche tra i simboli.</summary>
    public List<Relationship> Relationships { get; } = new();

    /// <summary>
    /// Aggiunge una relazione strutturale gerarchica di contenimento (CONTAINS) tra un parent e un child.
    /// </summary>
    /// <param name="parent">Il simbolo contenitore (es. Modulo o Metodo).</param>
    /// <param name="child">Il simbolo contenuto (es. Metodo o Variabile locale).</param>
    /// <param name="line">Numero di riga nel sorgente.</param>
    public void AddContains(Symbol parent, Symbol child, int? line = null)
    {
        Relationships.Add(new Relationship
        {
            SourceId = parent.Id,
            TargetId = child.Id,
            Kind = RelationshipKind.Contains,
            Line = line,
            Confidence = ResolutionConfidence.Exact
        });
    }

    /// <summary>
    /// Aggiunge una relazione di chiamata (CALLS) da una routine chiamante a una routine chiamata.
    /// </summary>
    /// <param name="caller">La routine chiamante (Method o Property).</param>
    /// <param name="callee">La routine chiamata (Method o Property).</param>
    /// <param name="line">Numero di riga nel sorgente in cui si origina la chiamata.</param>
    /// <param name="confidence">Grado di certezza della risoluzione (Exact, Inferred, Unknown).</param>
    public void AddCalls(Symbol caller, Symbol callee, int? line = null, ResolutionConfidence confidence = ResolutionConfidence.Exact)
    {
        Relationships.Add(new Relationship
        {
            SourceId = caller.Id,
            TargetId = callee.Id,
            Kind = RelationshipKind.Calls,
            Line = line,
            Confidence = confidence
        });
    }

    /// <summary>
    /// Aggiunge una relazione di istanziazione (INSTANTIATES) da una routine/modulo verso la classe istanziata.
    /// </summary>
    /// <param name="source">La routine o modulo che istanzia l'oggetto.</param>
    /// <param name="targetClass">Il simbolo della classe istanziata.</param>
    /// <param name="line">Numero di riga nel sorgente.</param>
    /// <param name="confidence">Grado di certezza della risoluzione.</param>
    public void AddInstantiates(Symbol source, Symbol targetClass, int? line = null, ResolutionConfidence confidence = ResolutionConfidence.Exact)
    {
        Relationships.Add(new Relationship
        {
            SourceId = source.Id,
            TargetId = targetClass.Id,
            Kind = RelationshipKind.Instantiates,
            Line = line,
            Confidence = confidence
        });
    }

    /// <summary>
    /// Aggiunge una relazione di dipendenza di tipo (USES_TYPE) da un simbolo verso il tipo utilizzato.
    /// </summary>
    /// <param name="source">Il simbolo che dichiara o usa il tipo (variabile, routine, modulo).</param>
    /// <param name="targetType">Il simbolo del tipo utilizzato (Class, UDT, Enum, Form).</param>
    /// <param name="line">Numero di riga nel sorgente.</param>
    public void AddUsesType(Symbol source, Symbol targetType, int? line = null)
    {
        // Evita duplicati identici se già presente
        if (Relationships.Any(r => r.Kind == RelationshipKind.UsesType && r.SourceId == source.Id && r.TargetId == targetType.Id))
            return;

        Relationships.Add(new Relationship
        {
            SourceId = source.Id,
            TargetId = targetType.Id,
            Kind = RelationshipKind.UsesType,
            Line = line,
            Confidence = ResolutionConfidence.Exact
        });
    }

    /// <summary>
    /// Restituisce tutti i simboli figli direttamente contenuti all'interno del simbolo specificato.
    /// </summary>
    /// <param name="parentId">ID del simbolo padre.</param>
    public IEnumerable<Symbol> GetChildren(Guid parentId) =>
        Relationships
            .Where(r => r.Kind == RelationshipKind.Contains && r.SourceId == parentId)
            .Select(r => Symbols.First(s => s.Id == r.TargetId));

    /// <summary>
    /// Recupera un simbolo dal grafo tramite il suo ID univoco.
    /// </summary>
    public Symbol? FindSymbolById(Guid id) =>
        Symbols.FirstOrDefault(s => s.Id == id);
}
