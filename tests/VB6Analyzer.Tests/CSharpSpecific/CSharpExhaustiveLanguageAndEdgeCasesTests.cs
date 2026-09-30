using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpExhaustiveLanguageAndEdgeCasesTests
{
    #region Complex Generic Type Constraints with Multiple Type Parameters

    /// <summary>
    /// Verifica l'estrazione di classi ed interfacce generiche con molteplici parametri di tipo
    /// e clausole di vincolo complesse (notnull, IComparable, class, new()).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractComplexGenericConstraints()
    {
        // Arrange
        var code = """
            using System;

            namespace Enterprise.Data.Structures
            {
                public interface IIdentifiable<TId>
                {
                    TId Id { get; }
                }

                public class CacheStore<TKey, TEntity> 
                    where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
                    where TEntity : class, IIdentifiable<TKey>, new()
                {
                    public void Store(TKey key, TEntity item)
                    {
                        var id = item.Id;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("CacheStore.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var interfaceSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "IIdentifiable");
        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "CacheStore");

        interfaceSym.Should().NotBeNull();
        classSym.Should().NotBeNull();

        var storeMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Store");
        storeMethod.Should().NotBeNull();
        storeMethod!.Parameters.Should().HaveCount(2);
    }

    #endregion

    #region Indexers and Custom Operator Overloads

    /// <summary>
    /// Verifica l'estrazione di indicizzatori personalizzati (this[x, y]), operatori aritmetici/logici (+, ==, !=)
    /// e operatori di conversione implicita ed esplicita.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractIndexersAndOperatorOverloads()
    {
        // Arrange
        var code = """
            namespace Enterprise.Maths
            {
                public readonly struct ComplexVector
                {
                    public readonly double X;
                    public readonly double Y;

                    public ComplexVector(double x, double y)
                    {
                        X = x;
                        Y = y;
                    }

                    public double this[int index] => index == 0 ? X : Y;

                    public static ComplexVector operator +(ComplexVector a, ComplexVector b) =>
                        new ComplexVector(a.X + b.X, a.Y + b.Y);

                    public static bool operator ==(ComplexVector a, ComplexVector b) =>
                        a.X == b.X && a.Y == b.Y;

                    public static bool operator !=(ComplexVector a, ComplexVector b) => !(a == b);

                    public static implicit operator ComplexVector((double X, double Y) tuple) =>
                        new ComplexVector(tuple.X, tuple.Y);

                    public static explicit operator double(ComplexVector v) =>
                        System.Math.Sqrt(v.X * v.X + v.Y * v.Y);
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("ComplexVector.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var vectorStruct = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Struct && s.Name == "ComplexVector");
        vectorStruct.Should().NotBeNull();
    }

    #endregion

    #region Nested Generic Types and Internal Call Graph Resolution

    /// <summary>
    /// Verifica l'estrazione e il call graph in presenza di classi generiche annidate (Nested Types: Outer&lt;T&gt;.Inner&lt;U&gt;).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldResolveNestedGenericTypeMethods()
    {
        // Arrange
        var code = """
            namespace Enterprise.Generics
            {
                public class Container<T>
                {
                    public class Node<U>
                    {
                        public void Process(T parentVal, U nodeVal)
                        {
                            Helper.LogValues(parentVal, nodeVal);
                        }
                    }
                }

                public static class Helper
                {
                    public static void LogValues<A, B>(A a, B b) { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Container.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var containerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Container");
        var nodeClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Node");
        var helperClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Helper");

        containerClass.Should().NotBeNull();
        nodeClass.Should().NotBeNull();
        helperClass.Should().NotBeNull();

        var procMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Process");
        var logMethod = graph.Symbols.FirstOrDefault(s => s.Name == "LogValues");

        procMethod.Should().NotBeNull();
        logMethod.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == procMethod!.Id).ToList();
        calls.Should().Contain(r => r.TargetId == logMethod!.Id);
    }

    #endregion

    #region File-Scoped Types and Access Modifiers

    /// <summary>
    /// Verifica l'estrazione di tipi file-scoped (file class) e membri con modificatori di accesso 'private protected' e 'protected internal'.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldHandleFileScopedTypesAndModernModifiers()
    {
        // Arrange
        var code = """
            namespace Enterprise.Internal
            {
                public class BaseService
                {
                    protected internal virtual void OnServiceStarting() { }
                    private protected virtual void OnServiceSecuring() { }
                }

                file class LocalSecretWorker
                {
                    public void DoSecretWork() { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("InternalServices.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var baseClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "BaseService");
        baseClass.Should().NotBeNull();

        var fileScopedClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "LocalSecretWorker");
        fileScopedClass.Should().NotBeNull();

        var startMethod = graph.Symbols.FirstOrDefault(s => s.Name == "OnServiceStarting");
        var secureMethod = graph.Symbols.FirstOrDefault(s => s.Name == "OnServiceSecuring");

        startMethod.Should().NotBeNull();
        secureMethod.Should().NotBeNull();
    }

    #endregion

    #region Pattern Matching, Switch Expressions and Tuples

    /// <summary>
    /// Verifica l'estrazione di metodi contenenti pattern matching complessi (posizionale, proprietà, relazionale e su tuple).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractMethodsUsingAdvancedPatternMatching()
    {
        // Arrange
        var code = """
            namespace Enterprise.Rules
            {
                public enum Tier { Bronze, Silver, Gold, Platinum }

                public class DiscountCalculator
                {
                    public decimal Calculate(Tier tier, decimal amount, bool isFirstPurchase) =>
                        (tier, amount, isFirstPurchase) switch
                        {
                            (Tier.Platinum, _, _) => amount * 0.30m,
                            (Tier.Gold, > 1000m, _) => amount * 0.25m,
                            (Tier.Gold, _, _) => amount * 0.15m,
                            (Tier.Silver, _, true) => amount * 0.10m,
                            (Tier.Silver, > 500m, false) => amount * 0.08m,
                            (_, _, true) => amount * 0.05m,
                            _ => 0m
                        };
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("DiscountCalculator.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var calcClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "DiscountCalculator");
        calcClass.Should().NotBeNull();

        var calcMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Calculate");
        calcMethod.Should().NotBeNull();
        calcMethod!.Parameters.Should().HaveCount(3);
    }

    #endregion

    #region Async Streams and Iterator Generators

    /// <summary>
    /// Verifica l'estrazione di metodi generatori con iteratori sincroni (IEnumerable yield return)
    /// e flussi asincroni (IAsyncEnumerable con CancellationToken).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractAsyncStreamsAndYieldIterators()
    {
        // Arrange
        var code = """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Enterprise.Streaming
            {
                public class DataStreamer
                {
                    public IEnumerable<int> GenerateNumbers(int count)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            yield return i;
                        }
                    }

                    public async IAsyncEnumerable<string> StreamRecordsAsync(int batchSize, [EnumeratorCancellation] CancellationToken ct = default)
                    {
                        for (int i = 0; i < batchSize; i++)
                        {
                            if (ct.IsCancellationRequested) yield break;
                            await Task.Delay(10, ct);
                            yield return $"Record_{i}";
                        }
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("DataStreamer.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var streamerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "DataStreamer");
        streamerClass.Should().NotBeNull();

        var genMethod = graph.Symbols.FirstOrDefault(s => s.Name == "GenerateNumbers");
        var streamMethod = graph.Symbols.FirstOrDefault(s => s.Name == "StreamRecordsAsync");

        genMethod.Should().NotBeNull();
        streamMethod.Should().NotBeNull();
    }

    #endregion
}
