using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpComprehensiveEdgeCasesDeepHunterTests
{
    #region Generic Type Parameter Constraints and Multi-Type Generics

    /// <summary>
    /// Verifica l'estrazione di classi e interfacce generiche con vincoli multipli (where T : class, new(), where K : struct, IComparable).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractGenericClasses_WithComplexConstraints()
    {
        // Arrange
        var code = """
            using System;

            namespace MyApp.Generics
            {
                public interface IRepository<TKey, TEntity>
                    where TKey : struct, IComparable<TKey>, IEquatable<TKey>
                    where TEntity : class, new()
                {
                    TEntity? GetById(TKey id);
                    void Save(TKey id, TEntity entity);
                }

                public class SqlRepository<TKey, TEntity> : IRepository<TKey, TEntity>
                    where TKey : struct, IComparable<TKey>, IEquatable<TKey>
                    where TEntity : class, new()
                {
                    public TEntity? GetById(TKey id)
                    {
                        return new TEntity();
                    }

                    public void Save(TKey id, TEntity entity)
                    {
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("GenericsWithConstraints.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var repoInterface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "IRepository");
        var repoClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "SqlRepository");

        repoInterface.Should().NotBeNull();
        repoClass.Should().NotBeNull();

        var getByIdMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "GetById");
        getByIdMethod.Should().NotBeNull();
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Contains && r.SourceId == repoClass!.Id && r.TargetId == getByIdMethod!.Id);
    }

    #endregion

    #region Operator Overloading and User-Defined Conversions

    /// <summary>
    /// Verifica l'estrazione di operatori sovraccaricati (+, -, ==, !=) e operatori di conversione implicita ed esplicita.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractOverloadedOperatorsAndConversionOperators()
    {
        // Arrange
        var code = """
            using System;

            namespace MyApp.Domain
            {
                public readonly struct Money : IEquatable<Money>
                {
                    public decimal Amount { get; }
                    public string Currency { get; }

                    public Money(decimal amount, string currency)
                    {
                        Amount = amount;
                        Currency = currency;
                    }

                    public static Money operator +(Money a, Money b)
                    {
                        if (a.Currency != b.Currency) throw new InvalidOperationException("Different currencies");
                        return new Money(a.Amount + b.Amount, a.Currency);
                    }

                    public static Money operator -(Money a, Money b)
                    {
                        if (a.Currency != b.Currency) throw new InvalidOperationException("Different currencies");
                        return new Money(a.Amount - b.Amount, a.Currency);
                    }

                    public static bool operator ==(Money a, Money b) => a.Amount == b.Amount && a.Currency == b.Currency;
                    public static bool operator !=(Money a, Money b) => !(a == b);

                    public static implicit operator decimal(Money m) => m.Amount;
                    public static explicit operator Money(decimal amount) => new Money(amount, "EUR");

                    public bool Equals(Money other) => this == other;
                    public override bool Equals(object? obj) => obj is Money other && Equals(other);
                    public override int GetHashCode() => HashCode.Combine(Amount, Currency);
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("MoneyOperators.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var moneyStruct = graph.Symbols.FirstOrDefault(s => s.Name == "Money" && s.Kind == SymbolKind.Struct);
        moneyStruct.Should().NotBeNull();

        var containsChildIds = graph.Relationships.Where(r => r.Kind == RelationshipKind.Contains && r.SourceId == moneyStruct!.Id).Select(r => r.TargetId).ToHashSet();
        var methods = graph.Symbols.Where(s => containsChildIds.Contains(s.Id)).ToList();
        methods.Should().NotBeEmpty();
    }

    #endregion

    #region Default Interface Methods (DIM) and Static Abstract Interface Members

    /// <summary>
    /// Verifica l'estrazione di interfacce con Default Interface Methods (C# 8+) e static abstract members (C# 11+).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractDefaultInterfaceMethods_AndStaticAbstractMembers()
    {
        // Arrange
        var code = """
            using System;

            namespace MyApp.Contracts
            {
                public interface IParsableValue<TSelf> where TSelf : IParsableValue<TSelf>
                {
                    static abstract TSelf Parse(string input);
                    static abstract bool TryParse(string input, out TSelf result);

                    void Print()
                    {
                        Console.WriteLine("Default interface implementation for printing");
                    }
                }

                public class CustomNumber : IParsableValue<CustomNumber>
                {
                    public int Value { get; set; }

                    public static CustomNumber Parse(string input) => new CustomNumber { Value = int.Parse(input) };

                    public static bool TryParse(string input, out CustomNumber result)
                    {
                        if (int.TryParse(input, out var val))
                        {
                            result = new CustomNumber { Value = val };
                            return true;
                        }
                        result = new CustomNumber();
                        return false;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("ParsableValue.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var iface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "IParsableValue");
        var cls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "CustomNumber");

        iface.Should().NotBeNull();
        cls.Should().NotBeNull();

        var parseMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Parse");
        parseMethod.Should().NotBeNull();
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Contains && r.SourceId == cls!.Id && r.TargetId == parseMethod!.Id);
    }

    #endregion

    #region Explicit Interface Implementation on Multi-Interface Classes

    /// <summary>
    /// Verifica la corretta estrazione e assegnazione dei metodi implementati in modo esplicito per molteplici interfacce.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractExplicitInterfaceImplementations()
    {
        // Arrange
        var code = """
            namespace MyApp.Events
            {
                public interface IOrderPlacedHandler
                {
                    void Handle(string orderId);
                }

                public interface IOrderCancelledHandler
                {
                    void Handle(string orderId);
                }

                public class CompositeOrderHandler : IOrderPlacedHandler, IOrderCancelledHandler
                {
                    void IOrderPlacedHandler.Handle(string orderId)
                    {
                        ProcessPlaced(orderId);
                    }

                    void IOrderCancelledHandler.Handle(string orderId)
                    {
                        ProcessCancelled(orderId);
                    }

                    private void ProcessPlaced(string orderId) { }
                    private void ProcessCancelled(string orderId) { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("CompositeOrderHandler.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var cls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "CompositeOrderHandler");
        cls.Should().NotBeNull();

        var childIds = graph.Relationships.Where(r => r.Kind == RelationshipKind.Contains && r.SourceId == cls!.Id).Select(r => r.TargetId).ToHashSet();
        var methods = graph.Symbols.Where(s => childIds.Contains(s.Id) && s.Kind == SymbolKind.Method).ToList();
        methods.Should().HaveCountGreaterOrEqualTo(2);
    }

    #endregion

    #region Nested Generic Classes and Types

    /// <summary>
    /// Verifica l'estrazione di classi generiche annidate all'interno di classi generiche (Outer&lt;T&gt;.Inner&lt;U&gt;).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractNestedGenericClasses_WithCorrectNestingHierarchy()
    {
        // Arrange
        var code = """
            namespace MyApp.Containers
            {
                public class CacheContainer<TCacheKey>
                {
                    public class Entry<TValue>
                    {
                        public TCacheKey Key { get; set; }
                        public TValue Value { get; set; }

                        public Entry(TCacheKey key, TValue value)
                        {
                            Key = key;
                            Value = value;
                        }

                        public void Refresh()
                        {
                        }
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("NestedGenerics.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var outerClass = graph.Symbols.FirstOrDefault(s => s.Name == "CacheContainer" && s.Kind == SymbolKind.Class);
        var innerClass = graph.Symbols.FirstOrDefault(s => s.Name == "Entry" && s.Kind == SymbolKind.Class);

        outerClass.Should().NotBeNull();
        innerClass.Should().NotBeNull();
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Contains && r.SourceId == outerClass!.Id && r.TargetId == innerClass!.Id);

        var refreshMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Refresh");
        refreshMethod.Should().NotBeNull();
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Contains && r.SourceId == innerClass!.Id && r.TargetId == refreshMethod!.Id);
    }

    #endregion

    #region Local Functions with Recursion and Closures

    /// <summary>
    /// Verifica che metodi contenenti local function ricorsive e closure vengano analizzati senza errori di estrazione.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractMethodsContainingRecursiveLocalFunctions()
    {
        // Arrange
        var code = """
            using System;
            using System.Collections.Generic;

            namespace MyApp.Algorithms
            {
                public class TreeTraverser
                {
                    public class Node
                    {
                        public int Val;
                        public List<Node> Children = new();
                    }

                    public int SumTree(Node root)
                    {
                        int accumulator = 0;

                        void Dfs(Node current)
                        {
                            if (current == null) return;
                            accumulator += current.Val;
                            foreach (var child in current.Children)
                            {
                                Dfs(child);
                            }
                        }

                        Dfs(root);
                        return accumulator;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("TreeTraverser.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var traverserClass = graph.Symbols.FirstOrDefault(s => s.Name == "TreeTraverser");
        var sumMethod = graph.Symbols.FirstOrDefault(s => s.Name == "SumTree");

        traverserClass.Should().NotBeNull();
        sumMethod.Should().NotBeNull();
    }

    #endregion

    #region Init-Only Setters, Required Members, and Expression-Bodied Indexers

    /// <summary>
    /// Verifica l'estrazione di proprietà C# moderne con 'init', modificatore 'required' e indicizzatori expression-bodied.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractRequiredPropertiesAndExpressionBodiedIndexers()
    {
        // Arrange
        var code = """
            using System.Collections.Generic;

            namespace MyApp.Models
            {
                public class UserProfile
                {
                    public required string Username { get; init; }
                    public required string Email { get; init; }
                    public string? DisplayName { get; init; }

                    private readonly Dictionary<string, string> _attributes = new();

                    public string this[string key]
                    {
                        get => _attributes.TryGetValue(key, out var val) ? val : string.Empty;
                        set => _attributes[key] = value;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("UserProfile.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var profileClass = graph.Symbols.FirstOrDefault(s => s.Name == "UserProfile");
        profileClass.Should().NotBeNull();

        var childIds = graph.Relationships.Where(r => r.Kind == RelationshipKind.Contains && r.SourceId == profileClass!.Id).Select(r => r.TargetId).ToHashSet();
        var props = graph.Symbols.Where(s => childIds.Contains(s.Id) && s.Kind == SymbolKind.Property).ToList();
        props.Should().Contain(p => p.Name == "Username");
        props.Should().Contain(p => p.Name == "Email");
    }

    #endregion

    #region Primary Constructors Inheritance Chain

    /// <summary>
    /// Verifica la corretta estrazione e risoluzione di classi derivate che invocano il primary constructor della classe base.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractPrimaryConstructors_WithInheritanceChain()
    {
        // Arrange
        var code = """
            namespace MyApp.DomainService
            {
                public interface ILogger { void Log(string msg); }
                public interface IConfig { string GetConnectionString(); }

                public abstract class BaseService(ILogger logger)
                {
                    public void LogInfo(string msg) => logger.Log(msg);
                }

                public class SqlDataService(ILogger logger, IConfig config) : BaseService(logger)
                {
                    public void Run()
                    {
                        LogInfo("Running with connection: " + config.GetConnectionString());
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("PrimaryInheritance.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var baseClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "BaseService");
        var derivedClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "SqlDataService");

        baseClass.Should().NotBeNull();
        derivedClass.Should().NotBeNull();

        var inheritsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Inherits && r.SourceId == derivedClass!.Id && r.TargetId == baseClass!.Id);
        inheritsRel.Should().NotBeNull();
    }

    #endregion
}
