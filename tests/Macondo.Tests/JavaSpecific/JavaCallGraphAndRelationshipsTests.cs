using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

public class JavaCallGraphAndRelationshipsTests
{
    /// <summary>
    /// Verify that CALLS relationships between Java methods are generated correctly
    /// for both direct calls and receiver-qualified invocations ('service.execute()').
    /// </summary>
    [Fact]
    public void JavaCallGraph_ShouldResolveDirectAndReceiverMethodCalls()
    {
        // Arrange
        var serviceCode = """
            package com.acme.services;

            public class PaymentGateway {
                public void charge(double amount) {
                }
            }
            """;

        var clientCode = """
            package com.acme.clients;

            import com.acme.services.PaymentGateway;

            public class CheckoutController {
                public void checkout() {
                    PaymentGateway gateway = new PaymentGateway();
                    gateway.charge(100.0);
                    sendNotification();
                }

                public void sendNotification() {
                }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("PaymentGateway.java", serviceCode),
            ("CheckoutController.java", clientCode));

        // Assert
        var checkoutMethod = graph.Symbols.FirstOrDefault(s => s.Name == "checkout");
        var chargeMethod = graph.Symbols.FirstOrDefault(s => s.Name == "charge");
        var notifyMethod = graph.Symbols.FirstOrDefault(s => s.Name == "sendNotification");

        checkoutMethod.Should().NotBeNull();
        chargeMethod.Should().NotBeNull();
        notifyMethod.Should().NotBeNull();

        // CALLS relationship from checkout -> charge (via PaymentGateway receiver)
        var callChargeRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == checkoutMethod!.Id &&
            r.TargetId == chargeMethod!.Id &&
            r.Kind == RelationshipKind.Calls);
        callChargeRel.Should().NotBeNull();

        // CALLS relationship from checkout -> sendNotification (internal direct call)
        var callNotifyRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == checkoutMethod!.Id &&
            r.TargetId == notifyMethod!.Id &&
            r.Kind == RelationshipKind.Calls);
        callNotifyRel.Should().NotBeNull();
    }

    /// <summary>
    /// Check that the 'new ClassName(...)' expressions generate the INSTANTIATES relationship
    /// between the calling routine and the target class.
    /// </summary>
    [Fact]
    public void JavaCallGraph_ShouldResolveObjectInstantiations()
    {
        // Arrange
        var customerCode = """
            package com.acme.domain;

            public class Customer {
                public Customer(String name) { }
            }
            """;

        var factoryCode = """
            package com.acme.factories;

            import com.acme.domain.Customer;

            public class CustomerFactory {
                public Customer createDefault() {
                    return new Customer("John Doe");
                }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("Customer.java", customerCode),
            ("CustomerFactory.java", factoryCode));

        // Assert
        var factoryMethod = graph.Symbols.FirstOrDefault(s => s.Name == "createDefault");
        var customerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Customer");

        factoryMethod.Should().NotBeNull();
        customerClass.Should().NotBeNull();

        var instRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == factoryMethod!.Id &&
            r.TargetId == customerClass!.Id &&
            r.Kind == RelationshipKind.Instantiates);
        instRel.Should().NotBeNull();
    }

    /// <summary>
    /// Tests the generation of INHERITS relations from 'extends' clauses and IMPLEMENTS from 'implements' clauses.
    /// </summary>
    [Fact]
    public void JavaCallGraph_ShouldResolveInheritanceAndInterfaceImplementation()
    {
        // Arrange
        var baseCode = """
            package com.acme.base;

            public abstract class BaseEntity {
                public long id;
            }

            public interface Auditable {
                void audit();
            }

            public interface Versionable extends Auditable {
                int getVersion();
            }
            """;

        var derivedCode = """
            package com.acme.models;

            import com.acme.base.BaseEntity;
            import com.acme.base.Versionable;

            public class Product extends BaseEntity implements Versionable {
                public int getVersion() { return 1; }
                public void audit() { }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("Base.java", baseCode),
            ("Product.java", derivedCode));

        // Assert
        var baseEntity = graph.Symbols.FirstOrDefault(s => s.Name == "BaseEntity");
        var auditable = graph.Symbols.FirstOrDefault(s => s.Name == "Auditable");
        var versionable = graph.Symbols.FirstOrDefault(s => s.Name == "Versionable");
        var product = graph.Symbols.FirstOrDefault(s => s.Name == "Product");

        baseEntity.Should().NotBeNull();
        auditable.Should().NotBeNull();
        versionable.Should().NotBeNull();
        product.Should().NotBeNull();

        // Product extends BaseEntity -> INHERITS
        var inheritsRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == product!.Id &&
            r.TargetId == baseEntity!.Id &&
            r.Kind == RelationshipKind.Inherits);
        inheritsRel.Should().NotBeNull();

        // Product implements Versionable -> IMPLEMENTS
        var implementsRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == product!.Id &&
            r.TargetId == versionable!.Id &&
            r.Kind == RelationshipKind.Implements);
        implementsRel.Should().NotBeNull();

        // Versionable extends Auditable -> INHERITS (between interfaces)
        var ifaceInheritsRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == versionable!.Id &&
            r.TargetId == auditable!.Id &&
            r.Kind == RelationshipKind.Inherits);
        ifaceInheritsRel.Should().NotBeNull();
    }

    /// <summary>
    /// Tests resolution of super(...) and this(...) constructor invocations.
    /// </summary>
    [Fact]
    public void JavaCallGraph_ShouldResolveSuperAndThisConstructorCalls()
    {
        // Arrange
        var code = """
            package com.acme.hierarchy;

            public class BaseWorker {
                public BaseWorker(String id) { }
            }

            public class SpecializedWorker extends BaseWorker {
                public SpecializedWorker() {
                    this("default-worker");
                }

                public SpecializedWorker(String id) {
                    super(id);
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Workers.java", code);

        // Assert
        result.CallSites.Should().Contain(c => c.InvokedMethodName == "this");
        result.CallSites.Should().Contain(c => c.InvokedMethodName == "super");
    }

    /// <summary>
    /// Check the resolution of Java Method References (e.g. 'User::getName').
    /// </summary>
    [Fact]
    public void JavaCallGraph_ShouldExtractMethodReferences()
    {
        // Arrange
        var code = """
            package com.acme.streams;

            import java.util.List;

            public class StreamService {
                public void printAll(List<String> items) {
                    items.forEach(System.out::println);
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("StreamService.java", code);

        // Assert
        result.CallSites.Should().Contain(c => c.InvokedMethodName == "println" && c.ReceiverExpressionText == "out");
    }
}
