using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

public class KotlinSymbolExtractionTests
{
    [Fact]
    public void KotlinExtractor_ShouldExtractClassInterfaceDataClassAndEnum()
    {
        // Arrange
        var code = """
            package com.acme.domain

            class OrderService {
                fun process() { }
            }

            interface PaymentProcessor {
                fun charge(amount: Double): Boolean
            }

            data class User(val id: Long, var name: String)

            enum class OrderStatus {
                PENDING,
                CONFIRMED,
                CANCELLED;
                fun isFinal(): Boolean = false
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("DomainModels.kt", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "OrderService");
        classSym.Should().NotBeNull();
        classSym!.QualifiedName.Should().Be("com.acme.domain.OrderService");
        classSym.Language.Should().Be(SourceLanguage.Kotlin);

        var ifaceSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "PaymentProcessor");
        ifaceSym.Should().NotBeNull();
        ifaceSym!.QualifiedName.Should().Be("com.acme.domain.PaymentProcessor");

        var dataClassSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "User");
        dataClassSym.Should().NotBeNull();

        // Verify that the val/var properties of the primary constructor have been extracted
        var idProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "id" && s.QualifiedName == "com.acme.domain.User.id");
        idProp.Should().NotBeNull();

        var nameProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "name" && s.QualifiedName == "com.acme.domain.User.name");
        nameProp.Should().NotBeNull();

        var enumSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "OrderStatus");
        enumSym.Should().NotBeNull();

        var enumPending = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "PENDING");
        enumPending.Should().NotBeNull();
        enumPending!.QualifiedName.Should().Be("com.acme.domain.OrderStatus.PENDING");
    }

    [Fact]
    public void KotlinExtractor_ShouldExtractSingletonObjectAndCompanionObject()
    {
        // Arrange
        var code = """
            package com.acme.util

            object AppConfig {
                val environment: String = "production"
                fun reload() { }
            }

            class ConnectionPool {
                companion object Factory {
                    fun create(): ConnectionPool = ConnectionPool()
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Singletons.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var objectSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "AppConfig");
        objectSym.Should().NotBeNull();
        objectSym!.IsStatic.Should().BeTrue();

        var companionSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Factory");
        companionSym.Should().NotBeNull();
        companionSym!.QualifiedName.Should().Be("com.acme.util.ConnectionPool.Factory");
    }

    [Fact]
    public void KotlinExtractor_ShouldExtractTopLevelFunctionsAndPropertiesWithFacadeClass()
    {
        // Arrange
        var code = """
            package com.acme.math

            const val PI_APPROX = 3.14159

            fun add(a: Int, b: Int): Int = a + b

            fun multiply(a: Int, b: Int): Int {
                return a * b
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("MathOps.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var facade = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "MathOpsKt");
        facade.Should().NotBeNull();
        facade!.QualifiedName.Should().Be("com.acme.math.MathOpsKt");

        var constSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "PI_APPROX");
        constSym.Should().NotBeNull();
        constSym!.QualifiedName.Should().Be("com.acme.math.MathOpsKt.PI_APPROX");

        var addSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "add");
        addSym.Should().NotBeNull();
        addSym!.QualifiedName.Should().Be("com.acme.math.MathOpsKt.add");
        addSym.Parameters.Should().HaveCount(2);

        var mulSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "multiply");
        mulSym.Should().NotBeNull();
    }

    [Fact]
    public void KotlinExtractor_ShouldExtractExtensionFunctions()
    {
        // Arrange
        var code = """
            package com.acme.ext

            fun String.toCustomSlug(): String {
                return this.lowercase()
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("StringExtensions.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var extFunc = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "toCustomSlug");
        extFunc.Should().NotBeNull();
        extFunc!.ReturnType.Should().Be("String");
    }
}
