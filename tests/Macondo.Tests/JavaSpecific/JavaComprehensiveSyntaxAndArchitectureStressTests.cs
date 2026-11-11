using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

/// <summary>
/// Syntactic and architectural stress test suite for Java sources:
/// deep inheritance hierarchies (5 levels), multiple inheritance between interfaces,
/// Builder pattern with chained calls (fluent API), custom annotations with meta-annotations
/// e file singoli contenenti classi multiple.
/// </summary>
public class JavaComprehensiveSyntaxAndArchitectureStressTests
{
    #region Deep Class Hierarchy (5 Levels)

    /// <summary>
    /// Test the reconstruction of a 5-level deep inheritance hierarchy
    /// (AbstractNode -> BaseNode -> StructuralNode -> LayoutNode -> FlexLayoutNode).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldResolveDeepClassHierarchy_5Levels()
    {
        // Arrange
        var code = """
            package com.example.hierarchy;

            public abstract class AbstractNode {
                public abstract void render();
            }

            public abstract class BaseNode extends AbstractNode {
                public void initialize() { }
            }

            public class StructuralNode extends BaseNode {
                @Override public void render() { }
            }

            public class LayoutNode extends StructuralNode {
                public void computeLayout() { }
            }

            public class FlexLayoutNode extends LayoutNode {
                public void applyFlex() {
                    computeLayout();
                    initialize();
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("DeepHierarchy.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var abstractNode = graph.Symbols.FirstOrDefault(s => s.Name == "AbstractNode");
        var baseNode = graph.Symbols.FirstOrDefault(s => s.Name == "BaseNode");
        var structuralNode = graph.Symbols.FirstOrDefault(s => s.Name == "StructuralNode");
        var layoutNode = graph.Symbols.FirstOrDefault(s => s.Name == "LayoutNode");
        var flexLayoutNode = graph.Symbols.FirstOrDefault(s => s.Name == "FlexLayoutNode");

        abstractNode.Should().NotBeNull();
        baseNode.Should().NotBeNull();
        structuralNode.Should().NotBeNull();
        layoutNode.Should().NotBeNull();
        flexLayoutNode.Should().NotBeNull();

        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Inherits && r.SourceId == baseNode!.Id && r.TargetId == abstractNode!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Inherits && r.SourceId == structuralNode!.Id && r.TargetId == baseNode!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Inherits && r.SourceId == layoutNode!.Id && r.TargetId == structuralNode!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Inherits && r.SourceId == flexLayoutNode!.Id && r.TargetId == layoutNode!.Id);
    }

    #endregion

    #region Multiple Interface Inheritance on Interfaces

    /// <summary>
    /// Verifies the correct extraction of interfaces that simultaneously extend multiple base interfaces
    /// ('interface ExtendedInterface extends A, B, C').
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleMultipleInterfaceInheritance()
    {
        // Arrange
        var code = """
            package com.example.contracts;

            public interface Identifiable {
                String getId();
            }

            public interface Versionable {
                long getVersion();
            }

            public interface Auditable {
                String getCreatedBy();
            }

            public interface EntityMetadata extends Identifiable, Versionable, Auditable {
                boolean isDeleted();
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Contracts.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var identifiable = graph.Symbols.FirstOrDefault(s => s.Name == "Identifiable");
        var versionable = graph.Symbols.FirstOrDefault(s => s.Name == "Versionable");
        var auditable = graph.Symbols.FirstOrDefault(s => s.Name == "Auditable");
        var entityMeta = graph.Symbols.FirstOrDefault(s => s.Name == "EntityMetadata");

        identifiable.Should().NotBeNull();
        versionable.Should().NotBeNull();
        auditable.Should().NotBeNull();
        entityMeta.Should().NotBeNull();

        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Inherits && r.SourceId == entityMeta!.Id && r.TargetId == identifiable!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Inherits && r.SourceId == entityMeta!.Id && r.TargetId == versionable!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Inherits && r.SourceId == entityMeta!.Id && r.TargetId == auditable!.Id);
    }

    #endregion

    #region Fluent API and Builder Pattern

    /// <summary>
    /// Test Fluent Builder pattern extraction with concatenation of method calls returning 'this'.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractFluentBuilderPattern()
    {
        // Arrange
        var code = """
            package com.example.builder;

            public class HttpRequest {
                private final String url;
                private final String method;
                private final int timeout;

                private HttpRequest(Builder builder) {
                    this.url = builder.url;
                    this.method = builder.method;
                    this.timeout = builder.timeout;
                }

                public static Builder newBuilder() {
                    return new Builder();
                }

                public static class Builder {
                    private String url;
                    private String method = "GET";
                    private int timeout = 5000;

                    public Builder url(String url) {
                        this.url = url;
                        return this;
                    }

                    public Builder method(String method) {
                        this.method = method;
                        return this;
                    }

                    public Builder timeout(int timeout) {
                        this.timeout = timeout;
                        return this;
                    }

                    public HttpRequest build() {
                        return new HttpRequest(this);
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("HttpRequest.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var requestClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "HttpRequest");
        var builderClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Builder");

        requestClass.Should().NotBeNull();
        builderClass.Should().NotBeNull();

        var buildMethod = graph.Symbols.FirstOrDefault(s => s.Name == "build");
        buildMethod.Should().NotBeNull();
    }

    #endregion

    #region Custom Annotations with Meta-Annotations

    /// <summary>
    /// Test extraction of custom annotation definitions decorated with standard meta-annotations (@Retention, @Target).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractCustomAnnotationDefinitionsWithMetaAnnotations()
    {
        // Arrange
        var code = """
            package com.example.annotations;

            import java.lang.annotation.ElementType;
            import java.lang.annotation.Retention;
            import java.lang.annotation.RetentionPolicy;
            import java.lang.annotation.Target;

            @Retention(RetentionPolicy.RUNTIME)
            @Target({ElementType.METHOD, ElementType.TYPE})
            public @interface Transactional {
                String readOnly() default "false";
                int timeoutSeconds() default 30;
                Class<? extends Throwable>[] rollbackFor() default {};
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Transactional.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var annot = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Transactional");
        annot.Should().NotBeNull();
    }

    #endregion
}
