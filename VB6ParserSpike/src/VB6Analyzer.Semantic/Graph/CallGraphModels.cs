using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Graph;

/// <summary>
/// Rappresenta un cammino di esecuzione (execution path) ordinato tra un punto di partenza
/// e una destinazione (sezione 16 del charter).
/// Esempio: Form1.cmdSave_Click -> Order.Save -> OrderRepository.Insert -> DatabaseHelper.Execute
/// </summary>
public sealed class CallPath
{
    /// <summary>Sequenza ordinata di simboli attraversati lungo il cammino.</summary>
    public IReadOnlyList<Symbol> Steps { get; }

    /// <summary>Numero di salti / passaggi nella catena di chiamate.</summary>
    public int Length => Steps.Count > 0 ? Steps.Count - 1 : 0;

    /// <summary>Simbolo iniziale del cammino (entry point).</summary>
    public Symbol Source => Steps[0];

    /// <summary>Simbolo finale raggiunto dal cammino.</summary>
    public Symbol Target => Steps[^1];

    public CallPath(IEnumerable<Symbol> steps)
    {
        Steps = steps.ToList();
    }

    public override string ToString() =>
        string.Join(" -> ", Steps.Select(s => s.QualifiedName));
}

/// <summary>
/// Rappresenta un ciclo di chiamate o ricorsione mutua individuato nel Call Graph (sezione 16 e 29 del charter).
/// Esempio: ServiceA.Method1 -> ServiceB.Method2 -> ServiceA.Method1
/// </summary>
public sealed class CallCycle
{
    /// <summary>Sequenza ciclica di simboli che forma il ciclo (il primo e l'ultimo elemento coincidono).</summary>
    public IReadOnlyList<Symbol> CycleSymbols { get; }

    public CallCycle(IEnumerable<Symbol> symbols)
    {
        CycleSymbols = symbols.ToList();
    }

    public override string ToString() =>
        string.Join(" -> ", CycleSymbols.Select(s => s.QualifiedName));
}

/// <summary>
/// Nodo gerarchico per la navigazione e visualizzazione ad albero dei chiamanti o dei chiamati.
/// </summary>
public sealed class CallTreeNode
{
    /// <summary>Simbolo rappresentato da questo nodo dell'albero.</summary>
    public required Symbol Symbol { get; init; }

    /// <summary>Numero di riga della chiamata verso il nodo figlio.</summary>
    public int? CallLine { get; init; }

    /// <summary>Grado di confidenza della chiamata.</summary>
    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Exact;

    /// <summary>Nodi figli (chiamati o chiamanti successivi nella gerarchia).</summary>
    public List<CallTreeNode> Children { get; } = new();

    public override string ToString() =>
        $"{Symbol.QualifiedName} (Children: {Children.Count})";
}
