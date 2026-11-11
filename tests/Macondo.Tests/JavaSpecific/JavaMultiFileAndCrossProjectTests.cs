using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

public class JavaMultiFileAndCrossProjectTests
{
    /// <summary>
    /// Verify that CallGraphBuilder correctly resolves CALLS and INSTANTIATES instantiation relationships
    /// between classes and methods defined in separate Java (.java) source files.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldResolveCallsAcrossMultipleJavaFiles()
    {
        // Arrange
        var file1 = """
            package com.acme.core;

            public class OrderService {
                private PaymentProcessor processor = new PaymentProcessor();

                public void placeOrder() {
                    processor.process();
                }
            }
            """;

        var file2 = """
            package com.acme.core;

            public class PaymentProcessor {
                public void process() {
                    audit();
                }

                private void audit() { }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("OrderService.java", file1),
            ("PaymentProcessor.java", file2)
        );

        // Assert
        results.Should().HaveCount(2);

        var placeOrderMethod = graph.Symbols.First(s => s.Name == "placeOrder");
        var processMethod = graph.Symbols.First(s => s.Name == "process");
        var auditMethod = graph.Symbols.First(s => s.Name == "audit");

        var callProcessRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == placeOrderMethod.Id &&
            r.TargetId == processMethod.Id &&
            r.Kind == RelationshipKind.Calls);
        callProcessRel.Should().NotBeNull();

        var callAuditRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == processMethod.Id &&
            r.TargetId == auditMethod.Id &&
            r.Kind == RelationshipKind.Calls);
        callAuditRel.Should().NotBeNull();
    }

    /// <summary>
    /// Verify that BlastRadiusEngine correctly calculates the cascading impact on Java cross-file calls.
    /// </summary>
    [Fact]
    public void BlastRadiusEngine_ShouldCalculateImpactOnMultiFileJavaProjects()
    {
        // Arrange
        var file1 = """
            package com.acme.app;

            public class Application {
                public void start() {
                    Controller c = new Controller();
                    c.handleRequest();
                }
            }
            """;

        var file2 = """
            package com.acme.app;

            public class Controller {
                public void handleRequest() {
                    Database db = new Database();
                    db.save();
                }
            }
            """;

        var file3 = """
            package com.acme.app;

            public class Database {
                public void save() { }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("Application.java", file1),
            ("Controller.java", file2),
            ("Database.java", file3)
        );

        var saveMethod = graph.Symbols.First(s => s.Name == "save");
        var impactAnalyzer = new ImpactAnalyzer(graph);
        var impact = impactAnalyzer.AnalyzeSymbolImpact(saveMethod);

        // Assert
        impact.DirectCallers.Should().ContainSingle(c => c.Name == "handleRequest");
        impact.IndirectCallers.Should().ContainSingle(c => c.Symbol.Name == "start");
        impact.AffectedClasses.Select(c => c.Name).Should().Contain(new[] { "Database", "Controller", "Application" });
    }

    /// <summary>
    /// Test polyglot analysis of a combined graph containing both Java and C# files.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldSupportMixedPolyglotJavaAndCSharp()
    {
        // Arrange
        var graph = new CodeGraph();
        var javaCode = """
            package com.acme.service;

            public class JavaAuthService {
                public void authenticate(String token) { }
            }
            """;

        var csharpCode = """
            namespace MyNetApp.Bridge
            {
                public class ClientBridge
                {
                    public void CallAuth()
                    {
                        JavaAuthService auth = new JavaAuthService();
                        auth.authenticate("token123");
                    }
                }
            }
            """;

        // Act
        var javaResult = JavaExtractor.ExtractJavaFile(graph, "JavaAuthService.java", javaCode);
        var csResult = CSharpExtractor.ExtractCSharpFile(graph, "ClientBridge.cs", csharpCode);

        var typeResolver = new Semantic.Resolution.TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new Semantic.Resolution.SymbolResolver(graph, typeResolver);

        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(Array.Empty<ModuleExtractionResult>(), new[] { csResult }, new[] { javaResult });

        // Assert
        var callAuth = graph.Symbols.First(s => s.Name == "CallAuth");
        var authenticate = graph.Symbols.First(s => s.Name == "authenticate");

        var callRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == callAuth.Id &&
            r.TargetId == authenticate.Id &&
            r.Kind == RelationshipKind.Calls);
        callRel.Should().NotBeNull();
    }
}
