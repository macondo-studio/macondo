using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

public class JavaSymbolExtractionTests
{
    /// <summary>
    /// Verify that JavaExtractor correctly recognizes and extracts all core Java-type constructs:
    /// Standard classes, Interfaces, Records and Enums (with their constant values).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractClassInterfaceRecordAndEnum()
    {
        // Arrange
        var code = """
            package com.acme.core;

            public class ServiceManager {
                public void run() { }
            }

            public interface Worker {
                void execute();
            }

            public record UserDto(String name, int age) { }

            public enum AppStatus {
                INACTIVE,
                ACTIVE,
                PAUSED;
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("CoreTypes.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ServiceManager");
        classSym.Should().NotBeNull();
        classSym!.QualifiedName.Should().Be("com.acme.core.ServiceManager");
        classSym.Accessibility.Should().Be(Accessibility.Public);
        classSym.Language.Should().Be(SourceLanguage.Java);

        var ifaceSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Worker");
        ifaceSym.Should().NotBeNull();
        ifaceSym!.QualifiedName.Should().Be("com.acme.core.Worker");
        ifaceSym.Accessibility.Should().Be(Accessibility.Public);

        var recordSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Record && s.Name == "UserDto");
        recordSym.Should().NotBeNull();
        recordSym!.QualifiedName.Should().Be("com.acme.core.UserDto");

        // Check 'name' and 'age' record fields
        var recordFields = graph.GetChildren(recordSym.Id).ToList();
        recordFields.Should().Contain(s => s.Name == "name" && s.DeclaredTypeName == "String");
        recordFields.Should().Contain(s => s.Name == "age" && s.DeclaredTypeName == "int");

        var enumSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "AppStatus");
        enumSym.Should().NotBeNull();
        enumSym!.QualifiedName.Should().Be("com.acme.core.AppStatus");

        var enumMembers = graph.GetChildren(enumSym.Id).ToList();
        enumMembers.Should().HaveCount(3);
        enumMembers.Select(m => m.Name).Should().Contain(new[] { "INACTIVE", "ACTIVE", "PAUSED" });
    }

    /// <summary>
    /// Tests the extraction of methods with multiple parameters, varargs, throws clause, and primitive and complex return types.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractMethodsWithParametersAndThrows()
    {
        // Arrange
        var code = """
            package com.acme.services;

            import java.io.IOException;
            import java.sql.SQLException;

            public class OrderService {
                public int processOrder(String orderId, double amount, String... tags) throws IOException, SQLException {
                    return 1;
                }

                private static void logAction(String message) {
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("OrderService.java", code);

        // Assert
        result.Success.Should().BeTrue();

        var methodSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "processOrder");
        methodSym.Should().NotBeNull();
        methodSym!.QualifiedName.Should().Be("com.acme.services.OrderService.processOrder");
        methodSym.ReturnType.Should().Be("int");
        methodSym.Accessibility.Should().Be(Accessibility.Public);
        methodSym.IsStatic.Should().BeFalse();

        methodSym.Parameters.Should().HaveCount(3);
        methodSym.Parameters[0].Name.Should().Be("orderId");
        methodSym.Parameters[0].DeclaredTypeName.Should().Be("String");
        methodSym.Parameters[1].Name.Should().Be("amount");
        methodSym.Parameters[1].DeclaredTypeName.Should().Be("double");
        methodSym.Parameters[2].Name.Should().Be("tags");
        methodSym.Parameters[2].DeclaredTypeName.Should().Be("String[]");

        var staticMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "logAction");
        staticMethod.Should().NotBeNull();
        staticMethod!.IsStatic.Should().BeTrue();
        staticMethod.Accessibility.Should().Be(Accessibility.Private);
        staticMethod.ReturnType.Should().Be("void");
    }

    /// <summary>
    /// Verify extraction of constructors and correct assignment of parameters and scope.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractConstructors()
    {
        // Arrange
        var code = """
            package com.acme.models;

            public class Customer {
                private String name;

                public Customer() {
                }

                public Customer(String name, int id) {
                    this.name = name;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Customer.java", code);

        // Assert
        result.Success.Should().BeTrue();

        var constructors = graph.Symbols.Where(s => s.Kind == SymbolKind.Method && s.Name == "Customer").ToList();
        constructors.Should().HaveCount(2);

        var paramCtor = constructors.FirstOrDefault(c => c.Parameters.Count == 2);
        paramCtor.Should().NotBeNull();
        paramCtor!.Parameters[0].Name.Should().Be("name");
        paramCtor.Parameters[1].Name.Should().Be("id");
    }

    /// <summary>
    /// Check the extraction of instance fields, static fields and constants ('static final' and interface constants).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractFieldsAndConstants()
    {
        // Arrange
        var code = """
            package com.acme.config;

            public class AppConfig {
                public static final String APP_NAME = "Macondo";
                public static final int MAX_RETRIES = 5;
                private int currentRetries = 0;
                protected boolean isEnabled;
            }

            public interface Constants {
                int TIMEOUT_MS = 5000;
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("AppConfig.java", code);

        // Assert
        result.Success.Should().BeTrue();

        var appNameConst = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "APP_NAME");
        appNameConst.Should().NotBeNull();
        appNameConst!.Accessibility.Should().Be(Accessibility.Public);
        appNameConst.IsStatic.Should().BeTrue();
        appNameConst.DeclaredTypeName.Should().Be("String");

        var retriesField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "currentRetries");
        retriesField.Should().NotBeNull();
        retriesField!.Accessibility.Should().Be(Accessibility.Private);
        retriesField.IsStatic.Should().BeFalse();

        var ifaceConst = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "TIMEOUT_MS");
        ifaceConst.Should().NotBeNull();
        ifaceConst!.DeclaredTypeName.Should().Be("int");
    }

    /// <summary>
    /// Tests the extraction of inner classes, static nested classes, and recursively nested types.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractNestedAndInnerClasses()
    {
        // Arrange
        var code = """
            package com.acme.structures;

            public class OuterTree {
                public static class TreeNode {
                    private Object value;
                    public Object getValue() { return value; }
                }

                public class TreeIterator {
                    public boolean hasNext() { return false; }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("OuterTree.java", code);

        // Assert
        result.Success.Should().BeTrue();

        var outerSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "OuterTree");
        outerSym.Should().NotBeNull();

        var treeNodeSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "TreeNode");
        treeNodeSym.Should().NotBeNull();
        treeNodeSym!.QualifiedName.Should().Be("com.acme.structures.OuterTree.TreeNode");
        treeNodeSym.IsStatic.Should().BeTrue();

        var treeIterSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "TreeIterator");
        treeIterSym.Should().NotBeNull();
        treeIterSym!.QualifiedName.Should().Be("com.acme.structures.OuterTree.TreeIterator");

        // Verify that the inner classes are connected with CONTAINS to the Outer
        var children = graph.GetChildren(outerSym!.Id).ToList();
        children.Should().Contain(treeNodeSym);
        children.Should().Contain(treeIterSym);
    }

    /// <summary>
    /// Check the management of custom annotations (@interface) with default attributes.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractAnnotationTypes()
    {
        // Arrange
        var code = """
            package com.acme.annotations;

            public @interface Cacheable {
                String key() default "";
                int ttlSeconds() default 300;
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Cacheable.java", code);

        // Assert
        result.Success.Should().BeTrue();

        var annotSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Cacheable");
        annotSym.Should().NotBeNull();
        annotSym!.QualifiedName.Should().Be("com.acme.annotations.Cacheable");

        var methods = graph.GetChildren(annotSym.Id).ToList();
        methods.Should().Contain(s => s.Name == "key" && s.ReturnType == "String");
        methods.Should().Contain(s => s.Name == "ttlSeconds" && s.ReturnType == "int");
    }
}
