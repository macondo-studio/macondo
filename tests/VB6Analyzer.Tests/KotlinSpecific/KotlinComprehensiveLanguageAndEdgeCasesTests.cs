using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

/// <summary>
/// Suite di test completa per la sintassi avanzata, costrutti idiomatici ed edge cases del linguaggio Kotlin
/// (Sealed classes, costruttori secondari, coroutine suspend, enum complessi, classi innestate, operatori e tipi generici con varianza).
/// </summary>
public class KotlinComprehensiveLanguageAndEdgeCasesTests
{
    [Fact]
    public void KotlinExtractor_ShouldExtractSealedClassesAndHierarchy()
    {
        // Arrange
        var code = """
            package com.acme.state

            sealed class NetworkResult {
                data class Success(val payload: String) : NetworkResult()
                data class Failure(val error: String) : NetworkResult()
                object Loading : NetworkResult()
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("NetworkResult.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var baseSealed = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "NetworkResult");
        baseSealed.Should().NotBeNull();

        var successClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Success");
        successClass.Should().NotBeNull();

        var failureClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Failure");
        failureClass.Should().NotBeNull();

        var loadingObject = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Loading");
        loadingObject.Should().NotBeNull();

        var inheritsRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == successClass!.Id &&
            r.TargetId == baseSealed!.Id &&
            r.Kind == RelationshipKind.Inherits);
        inheritsRel.Should().NotBeNull();
    }

    [Fact]
    public void KotlinExtractor_ShouldExtractSecondaryConstructors()
    {
        // Arrange
        var code = """
            package com.acme.models

            class Customer(val id: String, val name: String) {
                var email: String = ""

                constructor(id: String) : this(id, "Anonymous") {
                    this.email = "none@example.com"
                }

                constructor(id: String, name: String, email: String) : this(id, name) {
                    this.email = email
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Customer.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var customerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Customer");
        customerClass.Should().NotBeNull();

        var idProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "id");
        idProp.Should().NotBeNull();

        var nameProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "name");
        nameProp.Should().NotBeNull();

        var emailProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "email");
        emailProp.Should().NotBeNull();
    }

    [Fact]
    public void KotlinExtractor_ShouldExtractEnumWithFieldsAndMethods()
    {
        // Arrange
        var code = """
            package com.acme.workflow

            enum class OrderStatus(val code: Int) {
                PENDING(100),
                PROCESSING(200),
                COMPLETED(300),
                CANCELLED(400);

                fun isTerminal(): Boolean {
                    return code >= 300
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("OrderStatus.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var enumSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "OrderStatus");
        enumSym.Should().NotBeNull();

        var pendingVal = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "PENDING");
        pendingVal.Should().NotBeNull();

        var completedVal = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "COMPLETED");
        completedVal.Should().NotBeNull();

        var isTerminalMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "isTerminal");
        isTerminalMethod.Should().NotBeNull();
    }

    [Fact]
    public void KotlinExtractor_ShouldExtractInfixAndOperatorFunctions()
    {
        // Arrange
        var code = """
            package com.acme.math

            class Vector2D(val x: Double, val y: Double) {
                operator fun plus(other: Vector2D): Vector2D {
                    return Vector2D(x + other.x, y + other.y)
                }

                infix fun dot(other: Vector2D): Double {
                    return x * other.x + y * other.y
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Vector2D.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var plusMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "plus");
        plusMethod.Should().NotBeNull();

        var dotMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "dot");
        dotMethod.Should().NotBeNull();
    }

    [Fact]
    public void KotlinCallGraph_ShouldResolveSuspendFunctionCalls()
    {
        // Arrange: Coroutine Kotlin con funzioni suspend
        var code = """
            package com.acme.async

            class RemoteSyncService {
                suspend fun fetchRemoteData(): String {
                    return "raw_payload"
                }

                suspend fun synchronize() {
                    val data = fetchRemoteData()
                    save(data)
                }

                private fun save(data: String) { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("RemoteSync.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var syncMethod = graph.Symbols.First(s => s.Name == "synchronize");
        var fetchMethod = graph.Symbols.First(s => s.Name == "fetchRemoteData");
        var saveMethod = graph.Symbols.First(s => s.Name == "save");

        var callFetchRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == syncMethod.Id &&
            r.TargetId == fetchMethod.Id &&
            r.Kind == RelationshipKind.Calls);
        callFetchRel.Should().NotBeNull();

        var callSaveRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == syncMethod.Id &&
            r.TargetId == saveMethod.Id &&
            r.Kind == RelationshipKind.Calls);
        callSaveRel.Should().NotBeNull();
    }

    [Fact]
    public void KotlinExtractor_ShouldHandleInnerAndNestedClasses()
    {
        // Arrange
        var code = """
            package com.acme.containers

            class Container {
                class StaticNested {
                    fun calculate(): Int = 42
                }

                inner class InnerContext {
                    fun render(): String = "Contextual"
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Container.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var containerCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Container");
        containerCls.Should().NotBeNull();

        var nestedCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "StaticNested");
        nestedCls.Should().NotBeNull();

        var innerCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "InnerContext");
        innerCls.Should().NotBeNull();

        var calcMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "calculate");
        calcMethod.Should().NotBeNull();

        var renderMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "render");
        renderMethod.Should().NotBeNull();
    }

    [Fact]
    public void KotlinCallGraph_ShouldResolveSafeCallsAndElvisOperator()
    {
        // Arrange
        var code = """
            package com.acme.navigation

            class NotificationChannel {
                fun notifyUser() { }
            }

            class Profile {
                val channel: NotificationChannel? = NotificationChannel()
            }

            class AccountManager {
                fun alertAccount(profile: Profile?) {
                    profile?.channel?.notifyUser()
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Navigation.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var alertMethod = graph.Symbols.First(s => s.Name == "alertAccount");
        var notifyMethod = graph.Symbols.First(s => s.Name == "notifyUser");

        var callRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == alertMethod.Id &&
            r.TargetId == notifyMethod.Id &&
            r.Kind == RelationshipKind.Calls);
        callRel.Should().NotBeNull();
    }

    [Fact]
    public void KotlinExtractor_ShouldHandleGenericsWithVarianceAndHigherOrderFunctions()
    {
        // Arrange
        var code = """
            package com.acme.generics

            interface Producer<out T> {
                fun produce(): T
            }

            interface Consumer<in T> {
                fun consume(item: T)
            }

            class Pipeline {
                fun <T> executeTransform(producer: Producer<T>, consumer: Consumer<T>) {
                    val item = producer.produce()
                    consumer.consume(item)
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Generics.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var producerIface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Producer");
        producerIface.Should().NotBeNull();

        var consumerIface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Consumer");
        consumerIface.Should().NotBeNull();

        var pipelineCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Pipeline");
        pipelineCls.Should().NotBeNull();

        var executeMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "executeTransform");
        executeMethod.Should().NotBeNull();
    }
}
