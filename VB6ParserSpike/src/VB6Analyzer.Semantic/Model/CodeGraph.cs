namespace VB6Analyzer.Semantic.Model;

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
    /// Restituisce tutti i simboli figli direttamente contenuti all'interno del simbolo specificato.
    /// </summary>
    /// <param name="parentId">ID del simbolo padre.</param>
    public IEnumerable<Symbol> GetChildren(Guid parentId) =>
        Relationships
            .Where(r => r.Kind == RelationshipKind.Contains && r.SourceId == parentId)
            .Select(r => Symbols.First(s => s.Id == r.TargetId));
}
