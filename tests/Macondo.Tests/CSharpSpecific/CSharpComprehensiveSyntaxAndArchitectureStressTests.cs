using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpComprehensiveSyntaxAndArchitectureStressTests
{
    #region Switch Expressions and Advanced Pattern Matching

    /// <summary>
    /// Tests extraction and parsing of modern C# methods containing switch expressions,
    /// relational patterns (>, <=) and property patterns on domain types.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldHandleSwitchExpressionsAndPropertyPatterns()
    {
        // Arrange
        var code = """
            namespace MyApp.Billing
            {
                public record Order(decimal Subtotal, bool IsVip, int ItemCount);

                public class DiscountCalculator
                {
                    public decimal CalculateDiscount(Order order) => order switch
                    {
                        { IsVip: true, Subtotal: > 500m } => 0.20m,
                        { IsVip: true } => 0.10m,
                        { ItemCount: > 10 } => 0.05m,
                        _ => 0.0m
                    };
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("DiscountCalculator.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var orderRecord = graph.Symbols.FirstOrDefault(s => s.Name == "Order");
        var calcClass = graph.Symbols.FirstOrDefault(s => s.Name == "DiscountCalculator");
        var calcMethod = graph.Symbols.FirstOrDefault(s => s.Name == "CalculateDiscount");

        orderRecord.Should().NotBeNull();
        calcClass.Should().NotBeNull();
        calcMethod.Should().NotBeNull();
    }

    #endregion

    #region Custom Indexers and Overloaded Operators

    /// <summary>
    /// Test extraction of custom indexers (Indexer this[int index]) and overloaded operators (operator +, operator ==).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractIndexersAndOverloadedOperators()
    {
        // Arrange
        var code = """
            using System;
            using System.Collections.Generic;

            namespace MyApp.Collections
            {
                public class CustomMatrix
                {
                    private readonly double[,] _data = new double[4, 4];

                    public double this[int row, int col]
                    {
                        get => _data[row, col];
                        set => _data[row, col] = value;
                    }

                    public static CustomMatrix operator +(CustomMatrix a, CustomMatrix b)
                    {
                        return new CustomMatrix();
                    }

                    public static bool operator ==(CustomMatrix a, CustomMatrix b) => true;
                    public static bool operator !=(CustomMatrix a, CustomMatrix b) => !(a == b);

                    public override bool Equals(object? obj) => true;
                    public override int GetHashCode() => 0;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("CustomMatrix.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var matrixClass = graph.Symbols.FirstOrDefault(s => s.Name == "CustomMatrix");
        matrixClass.Should().NotBeNull();

        var members = graph.GetChildren(matrixClass!.Id).ToList();
        members.Should().NotBeEmpty();
    }

    #endregion

    #region Generic Type Definitions with Complex Constraints

    /// <summary>
    /// Test extraction of generic classes and interfaces with multiple constraints (where T : class, IComparable<T>, new()).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractGenericTypesWithMultipleConstraints()
    {
        // Arrange
        var code = """
            using System;

            namespace MyApp.Generics
            {
                public interface IIdentifiable<TKey> where TKey : struct
                {
                    TKey Id { get; }
                }

                public class Repository<TEntity, TKey> 
                    where TEntity : class, IIdentifiable<TKey>, new()
                    where TKey : struct, IComparable<TKey>
                {
                    public TEntity CreateDefault()
                    {
                        return new TEntity();
                    }

                    public bool IsGreaterThan(TKey a, TKey b)
                    {
                        return a.CompareTo(b) > 0;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("GenericRepo.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var iface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name.StartsWith("IIdentifiable"));
        var repo = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name.StartsWith("Repository"));

        iface.Should().NotBeNull();
        repo.Should().NotBeNull();

        var createMethod = graph.Symbols.FirstOrDefault(s => s.Name == "CreateDefault");
        createMethod.Should().NotBeNull();
    }

    #endregion

    #region Nested Classes and Cross References

    /// <summary>
    /// Tests the extraction of Nested Classes and the tracing of calls between the outer class and the inner class.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractNestedClassesAndInternalCalls()
    {
        // Arrange
        var code = """
            namespace MyApp.Encapsulation
            {
                public class CacheManager
                {
                    private readonly CacheNode _head = new CacheNode("root", 0);

                    public void Insert(string key, int value)
                    {
                        var node = new CacheNode(key, value);
                        node.Attach();
                    }

                    private class CacheNode
                    {
                        public string Key { get; }
                        public int Value { get; }

                        public CacheNode(string key, int value)
                        {
                            Key = key;
                            Value = value;
                        }

                        public void Attach()
                        {
                            // Internal link logic
                        }
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("CacheManager.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var outerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "CacheManager");
        var innerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "CacheNode");

        outerClass.Should().NotBeNull();
        innerClass.Should().NotBeNull();

        var insertMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Insert");
        var attachMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Attach");

        insertMethod.Should().NotBeNull();
        attachMethod.Should().NotBeNull();

        var callsAttach = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == insertMethod!.Id && r.TargetId == attachMethod!.Id);
        callsAttach.Should().NotBeNull();
    }

    #endregion

    #region Custom Event Accessors (add / remove) and Event Subscriptions

    /// <summary>
    /// Test C# event extraction with custom 'add' and 'remove' accessories.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractCustomEventAccessors()
    {
        // Arrange
        var code = """
            using System;

            namespace MyApp.Events
            {
                public class EventBroker
                {
                    private EventHandler? _internalHandler;

                    public event EventHandler StatusChanged
                    {
                        add
                        {
                            _internalHandler += value;
                        }
                        remove
                        {
                            _internalHandler -= value;
                        }
                    }

                    public void Notify()
                    {
                        _internalHandler?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("EventBroker.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var brokerClass = graph.Symbols.FirstOrDefault(s => s.Name == "EventBroker");
        var notifyMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Notify");

        brokerClass.Should().NotBeNull();
        notifyMethod.Should().NotBeNull();
    }

    #endregion

    #region Local Functions and Inner Routine Invocations

    /// <summary>
    /// Tests the parsing of methods containing Local Functions and nested calls.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldHandleLocalFunctionsInsideMethodBodies()
    {
        // Arrange
        var code = """
            using System;
            using System.Collections.Generic;

            namespace MyApp.Algorithms
            {
                public class Sorter
                {
                    public void QuickSort(int[] array)
                    {
                        if (array == null || array.Length <= 1) return;

                        Sort(0, array.Length - 1);

                        void Sort(int low, int high)
                        {
                            if (low < high)
                            {
                                int p = Partition(low, high);
                                Sort(low, p - 1);
                                Sort(p + 1, high);
                            }
                        }

                        int Partition(int low, int high)
                        {
                            int pivot = array[high];
                            int i = low - 1;
                            for (int j = low; j < high; j++)
                            {
                                if (array[j] < pivot)
                                {
                                    i++;
                                    Swap(i, j);
                                }
                            }
                            Swap(i + 1, high);
                            return i + 1;
                        }

                        void Swap(int a, int b)
                        {
                            (array[a], array[b]) = (array[b], array[a]);
                        }
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Sorter.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var sorterClass = graph.Symbols.FirstOrDefault(s => s.Name == "Sorter");
        var qsortMethod = graph.Symbols.FirstOrDefault(s => s.Name == "QuickSort");

        sorterClass.Should().NotBeNull();
        qsortMethod.Should().NotBeNull();
    }

    #endregion
}
