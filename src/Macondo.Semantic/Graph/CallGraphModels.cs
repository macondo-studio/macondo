using Macondo.Semantic.Model;

namespace Macondo.Semantic.Graph;

/// <summary>
/// Represents an ordered execution path between a starting point
/// and a destination.
/// Esempio: Form1.cmdSave_Click -> Order.Save -> OrderRepository.Insert -> DatabaseHelper.Execute
/// </summary>
public sealed class CallPath
{
    /// <summary>Ordered sequence of symbols crossed along the path.</summary>
    public IReadOnlyList<Symbol> Steps { get; }

    /// <summary>Number of jumps / steps in the call chain.</summary>
    public int Length => Steps.Count > 0 ? Steps.Count - 1 : 0;

    /// <summary>Initial symbol of the path (entry point).</summary>
    public Symbol Source => Steps[0];

    /// <summary>Final symbol reached on the journey.</summary>
    public Symbol Target => Steps[^1];

    public CallPath(IEnumerable<Symbol> steps)
    {
        Steps = steps.ToList();
    }

    public override string ToString() =>
        string.Join(" -> ", Steps.Select(s => s.QualifiedName));
}

/// <summary>
/// Represents a call loop or mutual recursion identified in the Call Graph.
/// Esempio: ServiceA.Method1 -> ServiceB.Method2 -> ServiceA.Method1
/// </summary>
public sealed class CallCycle
{
    /// <summary>Cyclic sequence of symbols that forms the cycle (the first and last elements coincide).</summary>
    public IReadOnlyList<Symbol> CycleSymbols { get; }

    public CallCycle(IEnumerable<Symbol> symbols)
    {
        CycleSymbols = symbols.ToList();
    }

    public override string ToString() =>
        string.Join(" -> ", CycleSymbols.Select(s => s.QualifiedName));
}
