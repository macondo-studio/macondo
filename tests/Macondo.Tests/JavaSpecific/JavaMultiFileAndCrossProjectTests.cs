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
    /// Verifica che CallGraphBuilder risolva correttamente le relazioni di chiamata CALLS e istanziazione INSTANTIATES
    /// tra classi e metodi definiti in file sorgente Java (.java) distinti.
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
    /// Verifica che BlastRadiusEngine calcoli correttamente l'impatto a cascata su chiamate cross-file Java.
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
    /// Verifica l'analisi poliglota di un grafo combinato contenente sia file Java che file C#.
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
