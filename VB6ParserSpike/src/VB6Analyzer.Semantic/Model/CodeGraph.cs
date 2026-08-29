namespace VB6Analyzer.Semantic.Model;

/// <summary>
/// CodeGraph reale (sezione 18): Symbols + Relationships, in RAM. Pensato per
/// aggregare più moduli/file (in futuro un intero progetto .vbp), non solo uno.
///
/// Non ancora collegato al GraphControl del POC Avalonia (M0): quello resta
/// alimentato dal generatore fittizio (FakeGraphGenerator) finché non affrontiamo
/// esplicitamente l'integrazione UI — è un passo successivo, non implicito in M2.
/// </summary>
public sealed class CodeGraph
{
    public List<Symbol> Symbols { get; } = new();
    public List<Relationship> Relationships { get; } = new();

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

    public IEnumerable<Symbol> GetChildren(Guid parentId) =>
        Relationships
            .Where(r => r.Kind == RelationshipKind.Contains && r.SourceId == parentId)
            .Select(r => Symbols.First(s => s.Id == r.TargetId));
}
