using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpComprehensiveLanguageEdgeCasesTests
{
    #region Primary Constructors with Base Invocations

    /// <summary>
    /// Verifica l'estrazione di classi con costruttore primario (Primary Constructor)
    /// che invoca il costruttore della classe base derivata.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractPrimaryConstructors_WithBaseClassInvocations()
    {
        // Arrange
        var code = """
            namespace Domain.Orders
            {
                public class BaseEntity(int id)
                {
                    public int Id => id;
                }

                public class Order(int id, string orderNumber, decimal totalAmount) : BaseEntity(id)
                {
                    public string OrderNumber => orderNumber;
                    public decimal TotalAmount => totalAmount;

                    public void Process()
                    {
                        var currentId = Id;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Orders.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var baseCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "BaseEntity");
        var orderCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Order");
        var processMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Process");

        baseCls.Should().NotBeNull();
        orderCls.Should().NotBeNull();
        processMethod.Should().NotBeNull();
    }

    #endregion

    #region Records, Record Structs and With-Expressions

    /// <summary>
    /// Verifica l'estrazione di record class, record struct, readonly record struct
    /// e costrutti di mutazione non distruttiva con espressione 'with'.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractRecordsAndRecordStructs()
    {
        // Arrange
        var code = """
            namespace Domain.Models
            {
                public record class CustomerRecord(string Name, string Email);

                public readonly record struct GeoLocation(double Latitude, double Longitude);

                public class RecordService
                {
                    public CustomerRecord UpdateEmail(CustomerRecord current, string newEmail)
                    {
                        return current with { Email = newEmail };
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Models.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var customerSym = graph.Symbols.FirstOrDefault(s => s.Name == "CustomerRecord");
        var geoSym = graph.Symbols.FirstOrDefault(s => s.Name == "GeoLocation");
        var serviceSym = graph.Symbols.FirstOrDefault(s => s.Name == "RecordService");

        customerSym.Should().NotBeNull();
        geoSym.Should().NotBeNull();
        serviceSym.Should().NotBeNull();
    }

    #endregion

    #region Collection Expressions and Spread Operator (C# 12+)

    /// <summary>
    /// Verifica il parsing di espressioni di collezione ('[1, 2, 3]') e spread operator ('[..first, ..second]').
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldHandleCollectionExpressionsAndSpreadOperators()
    {
        // Arrange
        var code = """
            using System.Collections.Generic;

            namespace Domain.Collections
            {
                public class ListBuilder
                {
                    public List<int> CombineLists(int[] first, int[] second)
                    {
                        int[] combined = [..first, ..second, 99];
                        return [..combined];
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("ListBuilder.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var builderClass = graph.Symbols.FirstOrDefault(s => s.Name == "ListBuilder");
        var combineMethod = graph.Symbols.FirstOrDefault(s => s.Name == "CombineLists");

        builderClass.Should().NotBeNull();
        combineMethod.Should().NotBeNull();
    }

    #endregion

    #region Async Streams and IAsyncEnumerable

    /// <summary>
    /// Verifica l'estrazione di metodi che restituiscono flussi asincroni (IAsyncEnumerable) con 'yield return' e 'await foreach'.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractAsyncStreamsAndYieldReturn()
    {
        // Arrange
        var code = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Domain.Streaming
            {
                public class DataStreamer
                {
                    public async IAsyncEnumerable<int> GenerateNumbersAsync(int count, CancellationToken ct = default)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            await Task.Delay(10, ct);
                            yield return i;
                        }
                    }

                    public async Task<int> ConsumeStreamAsync()
                    {
                        int sum = 0;
                        await foreach (var num in GenerateNumbersAsync(5))
                        {
                            sum += num;
                        }
                        return sum;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("DataStreamer.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var streamerClass = graph.Symbols.FirstOrDefault(s => s.Name == "DataStreamer");
        var genMethod = graph.Symbols.FirstOrDefault(s => s.Name == "GenerateNumbersAsync");
        var consumeMethod = graph.Symbols.FirstOrDefault(s => s.Name == "ConsumeStreamAsync");

        streamerClass.Should().NotBeNull();
        genMethod.Should().NotBeNull();
        consumeMethod.Should().NotBeNull();

        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == consumeMethod!.Id && r.TargetId == genMethod!.Id);
        callRel.Should().NotBeNull();
    }

    #endregion

    #region Attributes with Arguments and Custom Targets

    /// <summary>
    /// Verifica l'estrazione di classi e metodi decorati con attributi provvisti di argomenti posizionali e nominati.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldHandleAttributesWithNamedArguments()
    {
        // Arrange
        var code = """
            using System;

            namespace Domain.Governance
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public class AuditPolicyAttribute(string policyName) : Attribute
                {
                    public string PolicyName => policyName;
                    public int MaxRetries { get; set; } = 3;
                }

                [AuditPolicy("FinancialTransaction", MaxRetries = 5)]
                public class PaymentGateway
                {
                    [AuditPolicy("ExecuteCharge")]
                    public void ExecuteCharge(decimal amount)
                    {
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Governance.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var attrClass = graph.Symbols.FirstOrDefault(s => s.Name == "AuditPolicyAttribute");
        var gatewayClass = graph.Symbols.FirstOrDefault(s => s.Name == "PaymentGateway");
        var chargeMethod = graph.Symbols.FirstOrDefault(s => s.Name == "ExecuteCharge");

        attrClass.Should().NotBeNull();
        gatewayClass.Should().NotBeNull();
        chargeMethod.Should().NotBeNull();
    }

    #endregion
}
