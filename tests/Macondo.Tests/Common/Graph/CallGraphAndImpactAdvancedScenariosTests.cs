using FluentAssertions;
using Macondo.Semantic.Export;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Graph;

public class CallGraphAndImpactAdvancedScenariosTests
{
    #region CallGraph Multi-Path and Complex Queries

    /// <summary>
    /// Verifica che CallGraphQueries trovi tutti i molteplici percorsi di esecuzione indipendenti
    /// in una rete a maglie con biforcazioni e confluenze multiple (es. A -> B -> D, A -> C -> D, A -> B -> C -> D).
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldFindAllDistinctPathsInMeshTopology()
    {
        // Arrange
        var graph = new CodeGraph();

        var nodeA = new Symbol { Id = Guid.NewGuid(), Name = "EntryA", QualifiedName = "Mod.EntryA", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var nodeB = new Symbol { Id = Guid.NewGuid(), Name = "StepB", QualifiedName = "Mod.StepB", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var nodeC = new Symbol { Id = Guid.NewGuid(), Name = "StepC", QualifiedName = "Mod.StepC", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var nodeD = new Symbol { Id = Guid.NewGuid(), Name = "TargetD", QualifiedName = "Mod.TargetD", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { nodeA, nodeB, nodeC, nodeD });

        // Paths: A -> B -> D, A -> C -> D, A -> B -> C -> D
        graph.AddCalls(nodeA, nodeB);
        graph.AddCalls(nodeA, nodeC);
        graph.AddCalls(nodeB, nodeD);
        graph.AddCalls(nodeC, nodeD);
        graph.AddCalls(nodeB, nodeC);

        var queries = new CallGraphQueries(graph);

        // Act
        var paths = queries.FindExecutionPaths(nodeA, nodeD);

        // Assert
        paths.Should().NotBeNull();
        paths.Should().HaveCount(3, "Ci sono esattamente 3 percorsi distinti da A a D");
        paths.Should().OnlyContain(p => p.Source == nodeA && p.Target == nodeD);
    }

    /// <summary>
    /// Verifica il rilevamento di cicli annidati e sovrapposti (Loop 1: A <-> B, Loop 2: B <-> C).
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldDetectOverlappingCycles()
    {
        // Arrange
        var graph = new CodeGraph();

        var symA = new Symbol { Id = Guid.NewGuid(), Name = "RoutineA", QualifiedName = "Mod.RoutineA", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var symB = new Symbol { Id = Guid.NewGuid(), Name = "RoutineB", QualifiedName = "Mod.RoutineB", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var symC = new Symbol { Id = Guid.NewGuid(), Name = "RoutineC", QualifiedName = "Mod.RoutineC", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { symA, symB, symC });

        // Cycle 1: A -> B -> A
        graph.AddCalls(symA, symB);
        graph.AddCalls(symB, symA);

        // Cycle 2: B -> C -> B
        graph.AddCalls(symB, symC);
        graph.AddCalls(symC, symB);

        var queries = new CallGraphQueries(graph);

        // Act
        var cycles = queries.DetectCycles();

        // Assert
        cycles.Should().NotBeNull();
        cycles.Should().NotBeEmpty();
    }

    #endregion

    #region Advanced Impact Analysis Scenarios

    /// <summary>
    /// Verifica che ImpactAnalyzer su un nodo completamente isolato (nessun chiamante né dipendenza)
    /// restituisca una risposta valida con 0 chiamanti diretti e indiretti.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldHandleCompletelyIsolatedNode()
    {
        // Arrange
        var graph = new CodeGraph();
        var isolatedMethod = new Symbol { Id = Guid.NewGuid(), Name = "OrphanSub", QualifiedName = "Mod.OrphanSub", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        graph.Symbols.Add(isolatedMethod);

        var analyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = analyzer.AnalyzeSymbolImpact(isolatedMethod);

        // Assert
        impact.Should().NotBeNull();
        impact.DirectCallers.Should().BeEmpty();
        impact.IndirectCallers.Should().BeEmpty();
        impact.AffectedForms.Should().BeEmpty();
        impact.TotalImpactedSymbolsCount.Should().Be(0);
    }

    /// <summary>
    /// Verifica che ImpactAnalyzer gestisca una topologia a grafo completo (clique K4)
    /// terminando rapidamente senza duplicazioni o ricorsioni infinite.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldHandleFullyConnectedClique()
    {
        // Arrange: 4 nodi tutti collegati tra loro
        var graph = new CodeGraph();
        var nodes = new List<Symbol>();

        for (int i = 0; i < 4; i++)
        {
            var node = new Symbol { Id = Guid.NewGuid(), Name = $"Node_{i}", QualifiedName = $"Mod.Node_{i}", FilePath = "Mod.bas", Kind = SymbolKind.Method };
            nodes.Add(node);
            graph.Symbols.Add(node);
        }

        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                if (i != j)
                {
                    graph.AddCalls(nodes[i], nodes[j]);
                }
            }
        }

        var analyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = analyzer.AnalyzeSymbolImpact(nodes[0]);

        // Assert
        impact.Should().NotBeNull();
        impact.DirectCallers.Should().HaveCount(3);
        impact.TotalImpactedSymbolsCount.Should().Be(3);
    }

    #endregion

    #region Full Graph Roundtrip with All Relationship Kinds

    /// <summary>
    /// Verifica la persistenza e deserializzazione completa di un grafo che contiene tutti i tipi di relazione:
    /// Contains, Calls, UsesType, Implements, Inherits, HandlesEvent, Instantiates, Reads, Writes.
    /// </summary>
    [Fact]
    public void CodeGraphJsonExporter_ShouldPreserveAllNineRelationshipKinds()
    {
        // Arrange
        var graph = new CodeGraph();

        var mod = new Symbol { Id = Guid.NewGuid(), Name = "MainModule", QualifiedName = "MainModule", FilePath = "MainModule.bas", Kind = SymbolKind.Module };
        var iface = new Symbol { Id = Guid.NewGuid(), Name = "IService", QualifiedName = "IService", FilePath = "IService.cls", Kind = SymbolKind.Interface };
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "ServiceImpl", QualifiedName = "ServiceImpl", FilePath = "ServiceImpl.cls", Kind = SymbolKind.Class };
        var baseCls = new Symbol { Id = Guid.NewGuid(), Name = "BaseService", QualifiedName = "BaseService", FilePath = "BaseService.cls", Kind = SymbolKind.Class };
        var mth = new Symbol { Id = Guid.NewGuid(), Name = "Run", QualifiedName = "ServiceImpl.Run", FilePath = "ServiceImpl.cls", Kind = SymbolKind.Method };
        var callee = new Symbol { Id = Guid.NewGuid(), Name = "Log", QualifiedName = "MainModule.Log", FilePath = "MainModule.bas", Kind = SymbolKind.Method };
        var field = new Symbol { Id = Guid.NewGuid(), Name = "g_State", QualifiedName = "MainModule.g_State", FilePath = "MainModule.bas", Kind = SymbolKind.Field };
        var btn = new Symbol { Id = Guid.NewGuid(), Name = "cmdRun", QualifiedName = "Form1.cmdRun", FilePath = "Form1.frm", Kind = SymbolKind.Field };

        graph.Symbols.AddRange(new[] { mod, iface, cls, baseCls, mth, callee, field, btn });

        graph.AddContains(cls, mth);
        graph.AddCalls(mth, callee);
        graph.AddUsesType(mth, iface);
        graph.AddImplements(cls, iface);
        graph.AddInherits(cls, baseCls);
        graph.AddHandlesEvent(btn, mth);
        graph.AddInstantiates(callee, cls);
        graph.AddReads(mth, field);
        graph.AddWrites(callee, field);

        // Act
        var json = CodeGraphJsonExporter.ExportToJson(graph, indented: true);
        var restored = CodeGraphJsonExporter.ImportFromJson(json);

        // Assert
        restored.Should().NotBeNull();
        restored.Symbols.Should().HaveCount(8);
        restored.Relationships.Should().HaveCount(9);

        var kinds = restored.Relationships.Select(r => r.Kind).Distinct().ToList();
        kinds.Should().HaveCount(9);
        kinds.Should().Contain(new[]
        {
            RelationshipKind.Contains,
            RelationshipKind.Calls,
            RelationshipKind.UsesType,
            RelationshipKind.Implements,
            RelationshipKind.Inherits,
            RelationshipKind.HandlesEvent,
            RelationshipKind.Instantiates,
            RelationshipKind.Reads,
            RelationshipKind.Writes
        });
    }

    #endregion
}
