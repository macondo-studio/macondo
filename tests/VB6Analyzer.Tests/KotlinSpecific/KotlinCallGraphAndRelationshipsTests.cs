using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

public class KotlinCallGraphAndRelationshipsTests
{
    [Fact]
    public void CallGraphBuilder_ShouldResolveKotlinMethodCallsAndSafeCalls()
    {
        // Arrange
        var code = """
            package com.acme.shop

            class Logger {
                fun log(message: String) { }
            }

            class OrderService {
                val logger: Logger = Logger()

                fun checkout(orderId: String) {
                    logger?.log("Checking out")
                    validate(orderId)
                }

                fun validate(orderId: String): Boolean = true
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Shop.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var checkoutMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "checkout");
        checkoutMethod.Should().NotBeNull();

        var logMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "log");
        logMethod.Should().NotBeNull();

        var validateMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "validate");
        validateMethod.Should().NotBeNull();

        var outgoingCalls = graph.GetOutgoingRelationships(checkoutMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        outgoingCalls.Should().Contain(r => r.TargetId == logMethod!.Id);
        outgoingCalls.Should().Contain(r => r.TargetId == validateMethod!.Id);
    }

    [Fact]
    public void CallGraphBuilder_ShouldResolveKotlinInstantiations()
    {
        // Arrange
        var code = """
            package com.acme.factory

            class Engine { }

            class Car {
                fun assemble(): Engine {
                    val e = Engine()
                    return e
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Car.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var assembleMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "assemble");
        assembleMethod.Should().NotBeNull();

        var engineClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Engine");
        engineClass.Should().NotBeNull();

        var instRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Instantiates && r.SourceId == assembleMethod!.Id);
        instRel.Should().NotBeNull();
        instRel!.TargetId.Should().Be(engineClass!.Id);
    }

    [Fact]
    public void CallGraphBuilder_ShouldResolveKotlinInheritanceAndInterfaceImplementation()
    {
        // Arrange
        var code = """
            package com.acme.shapes

            interface Drawable {
                fun draw()
            }

            open class Shape { }

            class Circle : Shape(), Drawable {
                override fun draw() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Shapes.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var circleClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Circle");
        var shapeClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Shape");
        var drawableInterface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Drawable");

        circleClass.Should().NotBeNull();
        shapeClass.Should().NotBeNull();
        drawableInterface.Should().NotBeNull();

        var inheritsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Inherits && r.SourceId == circleClass!.Id);
        inheritsRel.Should().NotBeNull();
        inheritsRel!.TargetId.Should().Be(shapeClass!.Id);

        var implementsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Implements && r.SourceId == circleClass!.Id);
        implementsRel.Should().NotBeNull();
        implementsRel!.TargetId.Should().Be(drawableInterface!.Id);
    }
}
