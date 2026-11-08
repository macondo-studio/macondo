using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

/// <summary>
/// Suite di test per costrutti idiomatici moderni ed edge cases del linguaggio Kotlin (.kt),
/// inclusi funzioni/costanti top-level, scope functions (apply, let, run, also, with), object expressions anonimi,
/// property getter/setter con backing field 'field', lateinit, Elvis chaining ed espressioni 'when' con smart casting.
/// </summary>
public class KotlinModernPatternsAndIdiomaticEdgeCasesTests
{
    #region Top-Level Functions and Properties

    /// <summary>
    /// In Kotlin non è obbligatorio incapsulare funzioni e variabili in una classe:
    /// verifica la corretta estrazione di funzioni e costanti definite a livello di package (top-level).
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractTopLevelFunctionsAndProperties()
    {
        // Arrange
        var code = """
            package com.acme.utilities

            val SYSTEM_VERSION: String = "2026.1"
            const val TIMEOUT_MILLIS: Long = 5000L

            fun formatTimestamp(epochMs: Long): String {
                return "Formatted: $epochMs"
            }

            fun executeDiagnostics() {
                val time = formatTimestamp(1000)
                recordLog(time)
            }

            private fun recordLog(text: String) { }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Utilities.kt", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var formatMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "formatTimestamp");
        formatMethod.Should().NotBeNull();

        var diagMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "executeDiagnostics");
        diagMethod.Should().NotBeNull();

        var logMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "recordLog");
        logMethod.Should().NotBeNull();

        var calls = graph.GetOutgoingRelationships(diagMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        calls.Should().Contain(r => r.TargetId == formatMethod!.Id);
        calls.Should().Contain(r => r.TargetId == logMethod!.Id);
    }

    #endregion

    #region Scope Functions (let, run, apply, also, with)

    /// <summary>
    /// Verifica che le invocazioni di metodi e funzioni all'interno dei blocchi delle scope functions
    /// standard di Kotlin (let, apply, also, run) vengano tracciate correttamente nel Call Graph.
    /// </summary>
    [Fact]
    public void KotlinCallGraph_ShouldResolveCallsInsideScopeFunctions()
    {
        // Arrange
        var code = """
            package com.acme.scope

            class DeviceConfig {
                var port: Int = 8080
                var host: String = "localhost"
                fun validate() { }
            }

            class NetworkSetup {
                fun configureDevice(): DeviceConfig {
                    val config = DeviceConfig().apply {
                        port = 9090
                        validate()
                    }
                    config.also {
                        notifyAdmin(it.port)
                    }
                    return config
                }

                fun notifyAdmin(port: Int) { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("ScopeTest.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var configMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "configureDevice");
        configMethod.Should().NotBeNull();

        var validateMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "validate");
        validateMethod.Should().NotBeNull();

        var notifyMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "notifyAdmin");
        notifyMethod.Should().NotBeNull();

        var outgoing = graph.GetOutgoingRelationships(configMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        outgoing.Should().Contain(r => r.TargetId == validateMethod!.Id);
        outgoing.Should().Contain(r => r.TargetId == notifyMethod!.Id);
    }

    #endregion

    #region Anonymous Object Expressions

    /// <summary>
    /// In Kotlin 'object : Interface { ... }' crea una classe anonima inline.
    /// Verifica che la classe e i suoi metodi interni siano estratti e le chiamate interne risolte.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractAnonymousObjectExpressions()
    {
        // Arrange
        var code = """
            package com.acme.events

            interface EventCallback {
                fun onCompleted(success: Boolean)
            }

            class EventDispatcher {
                fun dispatch() {
                    val callback = object : EventCallback {
                        override fun onCompleted(success: Boolean) {
                            auditEvent(success)
                        }
                    }
                    callback.onCompleted(true)
                }

                fun auditEvent(success: Boolean) { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Events.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var iface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "EventCallback");
        iface.Should().NotBeNull();

        var dispatcher = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "EventDispatcher");
        dispatcher.Should().NotBeNull();

        var dispatchMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "dispatch");
        dispatchMethod.Should().NotBeNull();

        var auditMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "auditEvent");
        auditMethod.Should().NotBeNull();
    }

    #endregion

    #region Custom Property Getters/Setters and Backing Field 'field'

    /// <summary>
    /// Verifica la gestione di proprietà Kotlin con getter/setter personalizzati e identificatore speciale 'field'.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractCustomPropertyGettersAndSettersWithField()
    {
        // Arrange
        var code = """
            package com.acme.properties

            class TemperatureSensor {
                var celsius: Double = 0.0
                    get() = field
                    set(value) {
                        if (value >= -273.15) {
                            field = value
                        }
                    }

                val fahrenheit: Double
                    get() = (celsius * 9.0 / 5.0) + 32.0

                fun reset() {
                    celsius = 0.0
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Temperature.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var sensorClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "TemperatureSensor");
        sensorClass.Should().NotBeNull();

        var celsiusProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "celsius");
        celsiusProp.Should().NotBeNull();

        var fahrProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "fahrenheit");
        fahrProp.Should().NotBeNull();

        var resetMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "reset");
        resetMethod.Should().NotBeNull();
    }

    #endregion

    #region Lateinit and Chained Elvis Operator

    /// <summary>
    /// Verifica la gestione della keyword 'lateinit var' e del fallback con l'operatore Elvis '?:' su catene complesse.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldHandleLateinitAndChainedElvisOperator()
    {
        // Arrange
        var code = """
            package com.acme.services

            class ConfigProvider {
                fun loadSetting(): String? = null
            }

            class AppContext {
                lateinit var config: ConfigProvider

                fun getActiveSetting(): String {
                    val value = config?.loadSetting() ?: getDefaultSetting()
                    return value
                }

                fun getDefaultSetting(): String = "default"
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("AppContext.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var getActiveMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "getActiveSetting");
        getActiveMethod.Should().NotBeNull();

        var loadMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "loadSetting");
        loadMethod.Should().NotBeNull();

        var getDefaultMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "getDefaultSetting");
        getDefaultMethod.Should().NotBeNull();

        var calls = graph.GetOutgoingRelationships(getActiveMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        calls.Should().Contain(r => r.TargetId == loadMethod!.Id);
        calls.Should().Contain(r => r.TargetId == getDefaultMethod!.Id);
    }

    #endregion

    #region Smart Casts and Exhaustive When Pattern

    /// <summary>
    /// Verifica che espressioni 'when' con smart casting su tipi (is Type) risolvano correttamente
    /// le chiamate ai metodi specifici del tipo castato.
    /// </summary>
    [Fact]
    public void KotlinCallGraph_ShouldResolveCallsInExhaustiveWhenWithSmartCasting()
    {
        // Arrange
        var code = """
            package com.acme.commands

            sealed class Command
            class StartCommand(val speed: Int) : Command() {
                fun executeStart() { }
            }
            class StopCommand(val force: Boolean) : Command() {
                fun executeStop() { }
            }

            class CommandHandler {
                fun handle(cmd: Command) {
                    when (cmd) {
                        is StartCommand -> cmd.executeStart()
                        is StopCommand -> cmd.executeStop()
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Commands.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var handleMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "handle");
        handleMethod.Should().NotBeNull();

        var startMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "executeStart");
        startMethod.Should().NotBeNull();

        var stopMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "executeStop");
        stopMethod.Should().NotBeNull();

        var calls = graph.GetOutgoingRelationships(handleMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        calls.Should().Contain(r => r.TargetId == startMethod!.Id);
        calls.Should().Contain(r => r.TargetId == stopMethod!.Id);
    }

    #endregion

    #region Vararg, Default Arguments and Named Parameters

    /// <summary>
    /// Verifica l'estrazione e il tracciamento di metodi con parametri vararg, valori di default
    /// e invocazioni con argomenti con nome (named arguments).
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldHandleVarargsDefaultArgumentsAndNamedParameters()
    {
        // Arrange
        var code = """
            package com.acme.messaging

            class MessageBroadcaster {
                fun broadcast(channel: String, priority: Int = 1, vararg recipients: String) {
                    deliver(channel, priority)
                }

                private fun deliver(channel: String, priority: Int) { }
            }

            class NotificationManager {
                val broadcaster: MessageBroadcaster = MessageBroadcaster()

                fun notifySubscribers() {
                    broadcaster.broadcast(
                        priority = 5,
                        channel = "alerts",
                        recipients = arrayOf("user1", "user2")
                    )
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Broadcaster.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var broadcastMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "broadcast");
        broadcastMethod.Should().NotBeNull();

        var deliverMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "deliver");
        deliverMethod.Should().NotBeNull();

        var notifyMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "notifySubscribers");
        notifyMethod.Should().NotBeNull();

        var broadcastCalls = graph.GetOutgoingRelationships(broadcastMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        broadcastCalls.Should().Contain(r => r.TargetId == deliverMethod!.Id);

        var notifyCalls = graph.GetOutgoingRelationships(notifyMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        notifyCalls.Should().Contain(r => r.TargetId == broadcastMethod!.Id);
    }

    #endregion
}
