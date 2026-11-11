using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.App.Generation;

/// <summary>
/// Synthetic/dummy graph generator for load testing, UI benchmarks and demo modes.
/// Produces a realistic hierarchical structure (~5000 nodes) with Project, Form, Classes, Modules, Methods,
/// Properties and semantic relations of call (CALLS), containment (CONTAINS) and instantiation (INSTANTIATES),
/// with heterogeneous clusters and components of markedly different sizes to enhance the visual layouts.
/// </summary>
public static class FakeGraphGenerator
{
    private sealed record ClusterTemplate(
        string Domain,
        double Weight,
        string[] FormNames,
        string[] ClassNames,
        string[] ModuleNames);

    private static readonly ClusterTemplate[] Clusters =
    [
        new("Core", 22.0,
            ["frmMainDashboard", "frmSystemConsole", "frmDatabaseManager", "frmConfigEditor"],
            ["clsDbContext", "clsConnectionPool", "clsTransactionManager", "clsEntityCache", "clsSchemaValidator", "clsQueryBuilder", "clsDataRowMapper", "clsRepositoryBase", "clsAuditInterceptor", "clsRecordLock"],
            ["modDatabaseCore", "modSqlHelpers", "modGlobalState", "modSystemRegistry", "modAppInit", "modBufferUtils"]),

        new("Billing", 18.0,
            ["frmInvoiceEditor", "frmPaymentEntry", "frmTaxRates", "frmBillingSummary", "frmCreditNote"],
            ["clsInvoiceProcessor", "clsTaxCalculator", "clsPaymentGateway", "clsDiscountEngine", "clsRecurringBilling", "clsFiscalReceipt", "clsInvoiceRepository", "clsBillingJournal", "clsPaymentVoucher"],
            ["modBillingMath", "modVatRules", "modCurrencyConverter", "modRounding"]),

        new("Sales", 16.0,
            ["frmOrderEntry", "frmOrderBrowser", "frmQuotation", "frmPriceLookup", "frmDiscountSetup"],
            ["clsOrderManager", "clsPricingPolicy", "clsCommissionService", "clsDiscountPolicy", "clsOrderValidator", "clsShippingCalculator", "clsOrderHistory", "clsPromoRuleEngine"],
            ["modOrderState", "modBarcodeLookup", "modSalesTax"]),

        new("CRM", 14.0,
            ["frmCustomerCard", "frmCustomerSearch", "frmContactList", "frmLeadManager", "frmCustomerHistory"],
            ["clsCustomerManager", "clsContactRepository", "clsLeadPipeline", "clsCreditCheck", "clsCustomerActivity", "clsVipClassifier", "clsLoyaltyCardService"],
            ["modCustomerUtils", "modAddressFormatter", "modFiscalValidator"]),

        new("Inventory", 13.0,
            ["frmStockExplorer", "frmItemCard", "frmWarehouseTransfer", "frmInventoryAdjustment", "frmStockReorder"],
            ["clsInventoryEngine", "clsStockTracker", "clsWarehouseManager", "clsBarcodeProcessor", "clsReorderCalculator", "clsSerialValidator", "clsLotManager"],
            ["modStockAlgorithms", "modStorageUnits"]),

        new("Analytics", 11.0,
            ["frmKpiOverview", "frmTrendAnalysis", "frmDrilldownView", "frmForecastingBoard"],
            ["clsMetricsAggregator", "clsTimeSeriesAnalysis", "clsForecastEngine", "clsChurnPredictor", "clsKpiExporter", "clsCohortAnalyzer"],
            ["modStatisticalMath", "modAnalyticsFormatter"]),

        new("Reporting", 9.0,
            ["frmReportViewer", "frmReportDesigner", "frmPrintSetup", "frmBatchPrint"],
            ["clsReportEngine", "clsPdfRenderer", "clsExcelExporter", "clsCsvGenerator", "clsDocumentLayout", "clsWatermarkService"],
            ["modPrintSpooler", "modFontMetrics"]),

        new("Security", 8.0,
            ["frmLoginDialog", "frmUserManager", "frmRolePermissions", "frmAuditViewer"],
            ["clsAuthenticator", "clsPermissionManager", "clsSessionService", "clsPasswordPolicy", "clsTokenValidator", "clsAccessControlList"],
            ["modSecurityAudit", "modRoleCache"]),

        new("Workflow", 7.0,
            ["frmWorkflowBoard", "frmTaskQueue", "frmApprovalDialog"],
            ["clsWorkflowEngine", "clsStateMachine", "clsTaskDispatcher", "clsEscalationRules", "clsNotificationTrigger"],
            ["modWorkflowTimers"]),

        new("Integration", 6.0,
            ["frmIntegrationEndpoints", "frmSyncStatus", "frmWebhookLog"],
            ["clsEdiParser", "clsRestApiClient", "clsXmlPayloadHandler", "clsWebhookReceiver", "clsFtpSyncService"],
            ["modJsonUtils"]),

        new("Messaging", 5.0,
            ["frmNotificationCenter", "frmEmailTemplateEditor"],
            ["clsEmailService", "clsSmsGateway", "clsPushNotifier", "clsMessageQueue"],
            ["modMailFormatter"]),

        new("Diagnostics", 4.5,
            ["frmDiagnosticsViewer", "frmLogExplorer"],
            ["clsDiagnosticsMonitor", "clsMemoryTracker", "clsLogAggregator"],
            ["modTraceLogger", "modCrashHandler"]),

        new("Hardware", 4.0,
            ["frmDeviceManager", "frmPortConfig"],
            ["clsPosPrinterDriver", "clsSerialPortInterface", "clsScaleReader"],
            ["modIoPorts"]),

        new("Migration", 3.0,
            ["frmDataMigrationWizard"],
            ["clsLegacyDataParser", "clsSchemaConverter"],
            ["modCodepageConversion"]),

        new("Crypto", 2.0,
            [],
            ["clsAesEngine", "clsRsaHelper"],
            ["modSha256"]),

        new("Compression", 1.8,
            [],
            ["clsZipEngine"],
            ["modDeflate"]),

        new("MathUtils", 1.2,
            [],
            [],
            ["modMathHelpers"]),

        new("StringUtils", 1.0,
            [],
            [],
            ["modStringFormat"]),

        new("Constants", 0.8,
            [],
            [],
            ["modAppConstants"])
    ];

    private static readonly string[] Verbs =
    [
        "Process", "Validate", "Calculate", "Execute", "Save", "Load", "Delete",
        "Format", "Log", "Init", "Reset", "Export", "Import", "Update", "Check",
        "Sync", "Dispatch", "Resolve", "Fetch", "Commit", "Rollback", "Notify",
        "Authorize", "Audit", "Compile", "Build", "Render", "Query", "Transform"
    ];

    private static readonly string[] Nouns =
    [
        "Data", "Record", "Status", "Item", "Config", "Payload", "Summary",
        "Entry", "Transaction", "Batch", "Queue", "Profile", "Token", "Event",
        "Message", "Session", "Request", "Response", "Report", "Customer",
        "Invoice", "Order", "Result", "Metric", "Entity", "Context", "Audit"
    ];

    /// <summary>
    /// Generates a synthetic <see cref="VisualCodeGraph"/> with random and heterogeneous distribution of nodes for clusters and components.
    /// By default it generates ~5000 total nodes with strong volumetric differentiation between components.
    /// </summary>
    /// <param name="seed">Random number generator seed (for deterministic reproducibility).</param>
    /// <param name="containerCount">Optional number of classes/forms/modules (if null, automatically calculated to reach targetNodeCount).</param>
    /// <param name="methodsPerContainer">Average optional number of methods per container.</param>
    /// <param name="targetNodeCount">Total number of nodes desired in the graph (default 5000 nodes).</param>
    public static VisualCodeGraph Generate(int seed = 42, int? containerCount = null, int? methodsPerContainer = null, int targetNodeCount = 5000)
    {
        var random = new Random(seed);
        var semanticGraph = new Semantic.Model.CodeGraph();
        var visualGraph = new VisualCodeGraph
        {
            SemanticGraph = semanticGraph,
            Queries = new CallGraphQueries(semanticGraph)
        };

        // 1. Project Root Node
        var projectNode = new GraphNode
        {
            Id = Guid.NewGuid(),
            Name = "SyntheticProject",
            QualifiedName = "SyntheticProject",
            Kind = SymbolKind.Project,
            IsExpanded = true
        };
        visualGraph.Nodes.Add(projectNode);

        // Quota calculation: number of containers and total number of members
        int numContainers;
        int totalMembers;

        if (containerCount.HasValue && methodsPerContainer.HasValue)
        {
            numContainers = Math.Max(1, containerCount.Value);
            totalMembers = Math.Max(0, numContainers * methodsPerContainer.Value);
        }
        else if (containerCount.HasValue)
        {
            numContainers = Math.Max(1, containerCount.Value);
            totalMembers = Math.Max(0, targetNodeCount - 1 - numContainers);
        }
        else
        {
            // ~140 containers for ~5000 total nodes
            numContainers = Math.Max(10, (int)Math.Round(targetNodeCount * 0.028));
            totalMembers = Math.Max(0, targetNodeCount - 1 - numContainers);
        }

        // 2. Distribution of containers on functional clusters
        var activeClusterCount = Math.Min(Clusters.Length, Math.Max(1, numContainers));
        var clusterContainerQuotas = DistributeContainerQuotas(numContainers, activeClusterCount, random);

        // 3. Generation and assignment of containers for each cluster
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clusterContainers = new List<List<GraphNode>>();
        var allContainers = new List<GraphNode>();

        for (var cIdx = 0; cIdx < clusterContainerQuotas.Length; cIdx++)
        {
            var quota = clusterContainerQuotas[cIdx];
            if (quota <= 0)
                continue;

            var template = Clusters[cIdx % Clusters.Length];
            var currentClusterNodes = new List<GraphNode>();

            var namePool = new List<string>();
            namePool.AddRange(template.FormNames);
            namePool.AddRange(template.ClassNames);
            namePool.AddRange(template.ModuleNames);

            for (var k = 0; k < quota; k++)
            {
                var baseName = k < namePool.Count
                    ? namePool[k]
                    : $"{template.Domain}_{k + 1}";

                var uniqueName = baseName;
                var suffixCounter = 2;
                while (!usedNames.Add(uniqueName))
                {
                    uniqueName = $"{baseName}_{suffixCounter++}";
                }

                var kind = uniqueName.StartsWith("frm", StringComparison.OrdinalIgnoreCase)
                    ? SymbolKind.Form
                    : (uniqueName.StartsWith("cls", StringComparison.OrdinalIgnoreCase) ? SymbolKind.Class : SymbolKind.Module);

                var ext = kind == SymbolKind.Form ? "frm" : (kind == SymbolKind.Class ? "cls" : "bas");

                var containerSymbol = new Symbol
                {
                    Id = Guid.NewGuid(),
                    Name = uniqueName,
                    QualifiedName = uniqueName,
                    Kind = kind,
                    Accessibility = Accessibility.Public,
                    FilePath = $"{uniqueName}.{ext}",
                    LineStart = 1,
                    LineEnd = 100
                };
                semanticGraph.Symbols.Add(containerSymbol);

                var containerNode = new GraphNode
                {
                    Id = containerSymbol.Id,
                    Name = uniqueName,
                    QualifiedName = uniqueName,
                    Kind = kind,
                    SemanticSymbol = containerSymbol,
                    ParentId = projectNode.Id,
                    IsExpanded = true
                };

                currentClusterNodes.Add(containerNode);
                allContainers.Add(containerNode);
                visualGraph.Nodes.Add(containerNode);

                // Contains relationship: Project -> Container
                visualGraph.Edges.Add(new GraphEdge
                {
                    SourceId = projectNode.Id,
                    TargetId = containerNode.Id,
                    Kind = RelationshipKind.Contains,
                    Confidence = ResolutionConfidence.Exact
                });
            }

            clusterContainers.Add(currentClusterNodes);
        }

        // 4. Calculate heterogeneous shares of members for each container
        var memberQuotas = DistributeMemberQuotas(clusterContainers, totalMembers, random);

        // 5. Creating members for each container
        var clusterMethods = new List<List<GraphNode>>();
        var globalContainerIdx = 0;

        for (var cIdx = 0; cIdx < clusterContainers.Count; cIdx++)
        {
            var cList = clusterContainers[cIdx];
            var methodsInCluster = new List<GraphNode>();

            for (var k = 0; k < cList.Count; k++)
            {
                var containerNode = cList[k];
                var containerSymbol = containerNode.SemanticSymbol!;
                var quota = memberQuotas[globalContainerIdx++];

                containerSymbol.LineEnd = Math.Max(100, 20 + quota * 18);

                for (var m = 0; m < quota; m++)
                {
                    var verb = Verbs[random.Next(Verbs.Length)];
                    var noun = Nouns[random.Next(Nouns.Length)];
                    var methodName = $"{verb}{noun}_{m + 1}";
                    var memberKind = (m % 5 == 0) ? SymbolKind.Property : SymbolKind.Method;
                    var memberAccess = (m % 3 == 0) ? Accessibility.Private : Accessibility.Public;

                    var lineStart = 10 + m * 15;
                    var lineEnd = lineStart + random.Next(4, 12);

                    var memberSymbol = new Symbol
                    {
                        Id = Guid.NewGuid(),
                        Name = methodName,
                        QualifiedName = $"{containerNode.Name}.{methodName}",
                        Kind = memberKind,
                        Accessibility = memberAccess,
                        FilePath = containerSymbol.FilePath,
                        LineStart = lineStart,
                        LineEnd = lineEnd
                    };
                    semanticGraph.Symbols.Add(memberSymbol);
                    semanticGraph.AddContains(containerSymbol, memberSymbol, memberSymbol.LineStart);

                    var methodNode = new GraphNode
                    {
                        Id = memberSymbol.Id,
                        Name = methodName,
                        QualifiedName = $"{containerNode.Name}.{methodName}",
                        Kind = memberKind,
                        SemanticSymbol = memberSymbol,
                        ParentId = containerNode.Id,
                        IsExpanded = false
                    };

                    methodsInCluster.Add(methodNode);
                    visualGraph.Nodes.Add(methodNode);

                    // Contains relationship: Container -> Member
                    visualGraph.Edges.Add(new GraphEdge
                    {
                        SourceId = containerNode.Id,
                        TargetId = methodNode.Id,
                        Kind = RelationshipKind.Contains,
                        Confidence = ResolutionConfidence.Exact,
                        Line = memberSymbol.LineStart
                    });
                }
            }

            clusterMethods.Add(methodsInCluster);
        }

        // 6. Generation of intra-cluster CALLS and INSTANTIATES relationships
        for (var cIdx = 0; cIdx < clusterContainers.Count; cIdx++)
        {
            var containersInCluster = clusterContainers[cIdx];
            var methods = clusterMethods[cIdx];
            if (methods.Count < 2)
                continue;

            var existingCalls = new HashSet<(Guid, Guid)>();

            // Intra-cluster spanning tree to ensure cohesion of the connected component in the island
            if (containersInCluster.Count > 1)
            {
                for (var i = 1; i < containersInCluster.Count; i++)
                {
                    var cCurrent = containersInCluster[i];
                    var cPrev = containersInCluster[random.Next(i)];

                    var currMembers = methods.Where(m => m.ParentId == cCurrent.Id).ToList();
                    var prevMembers = methods.Where(m => m.ParentId == cPrev.Id).ToList();

                    if (currMembers.Count > 0 && prevMembers.Count > 0)
                    {
                        var caller = currMembers[random.Next(currMembers.Count)];
                        var callee = prevMembers[random.Next(prevMembers.Count)];

                        if (caller.Id != callee.Id && existingCalls.Add((caller.Id, callee.Id)))
                        {
                            var line = random.Next(1, 80);
                            semanticGraph.AddCalls(caller.SemanticSymbol!, callee.SemanticSymbol!, line, ResolutionConfidence.Exact);
                            visualGraph.Edges.Add(new GraphEdge
                            {
                                SourceId = caller.Id,
                                TargetId = callee.Id,
                                Kind = RelationshipKind.Calls,
                                Confidence = ResolutionConfidence.Exact,
                                Line = line
                            });
                        }
                    }
                }
            }

            // Additional intra-cluster calls with preference for hub methods
            var callCount = Math.Min(methods.Count * 2, (int)(methods.Count * (1.2 + random.NextDouble() * 0.8)));
            for (var c = 0; c < callCount; c++)
            {
                var caller = methods[random.Next(methods.Count)];
                GraphNode callee;

                if (random.NextDouble() < 0.4)
                {
                    // Internal call to the same module
                    var sameContainer = methods.Where(m => m.ParentId == caller.ParentId && m.Id != caller.Id).ToList();
                    if (sameContainer.Count > 0)
                        callee = sameContainer[random.Next(sameContainer.Count)];
                    else
                        callee = methods[random.Next(methods.Count)];
                }
                else
                {
                    // Call to another container in the cluster
                    callee = methods[random.Next(methods.Count)];
                }

                if (caller.Id == callee.Id)
                    continue;

                if (!existingCalls.Add((caller.Id, callee.Id)))
                    continue;

                var line = random.Next(1, 80);
                if (caller.SemanticSymbol != null && callee.SemanticSymbol != null)
                    semanticGraph.AddCalls(caller.SemanticSymbol, callee.SemanticSymbol, line, ResolutionConfidence.Exact);

                visualGraph.Edges.Add(new GraphEdge
                {
                    SourceId = caller.Id,
                    TargetId = callee.Id,
                    Kind = RelationshipKind.Calls,
                    Confidence = ResolutionConfidence.Exact,
                    Line = line
                });
            }

            // Instantiations (INSTANTIATES) towards the cluster classes
            var classContainersInCluster = containersInCluster.Where(n => n.Kind == SymbolKind.Class).ToList();
            if (classContainersInCluster.Count > 0)
            {
                var instCount = Math.Max(1, containersInCluster.Count);
                for (var inst = 0; inst < instCount; inst++)
                {
                    var sourceMethod = methods[random.Next(methods.Count)];
                    var targetClass = classContainersInCluster[random.Next(classContainersInCluster.Count)];

                    if (sourceMethod.ParentId != targetClass.Id && sourceMethod.SemanticSymbol != null && targetClass.SemanticSymbol != null)
                    {
                        semanticGraph.AddInstantiates(sourceMethod.SemanticSymbol, targetClass.SemanticSymbol, 15);
                        visualGraph.Edges.Add(new GraphEdge
                        {
                            SourceId = sourceMethod.Id,
                            TargetId = targetClass.Id,
                            Kind = RelationshipKind.Instantiates,
                            Confidence = ResolutionConfidence.Exact,
                            Line = 15
                        });
                    }
                }
            }
        }

        // 7. Calculate the initial radial layout and finalize the graph
        RadialLayoutEngine.Apply(visualGraph);
        visualGraph.Invalidate();

        return visualGraph;
    }

    private static int[] DistributeContainerQuotas(int totalContainers, int clusterCount, Random random)
    {
        var quotas = new int[clusterCount];
        if (totalContainers <= clusterCount)
        {
            for (var i = 0; i < totalContainers; i++)
                quotas[i] = 1;
            return quotas;
        }

        var weights = new double[clusterCount];
        var totalWeight = 0.0;
        for (var i = 0; i < clusterCount; i++)
        {
            var baseW = Clusters[i % Clusters.Length].Weight;
            var w = Math.Max(0.5, baseW * (0.85 + random.NextDouble() * 0.3));
            weights[i] = w;
            totalWeight += w;
        }

        var allocated = 0;
        for (var i = 0; i < clusterCount; i++)
        {
            // Leaf/small clusters keep 1-2 containers, large ones get many more
            var q = Math.Max(1, (int)Math.Round((weights[i] / totalWeight) * totalContainers));
            quotas[i] = q;
            allocated += q;
        }

        while (allocated < totalContainers)
        {
            var idx = random.Next(Math.Min(4, clusterCount));
            quotas[idx]++;
            allocated++;
        }

        while (allocated > totalContainers)
        {
            var maxIdx = 0;
            for (var i = 1; i < clusterCount; i++)
            {
                if (quotas[i] > quotas[maxIdx])
                    maxIdx = i;
            }

            if (quotas[maxIdx] > 1)
            {
                quotas[maxIdx]--;
                allocated--;
            }
            else
            {
                break;
            }
        }

        return quotas;
    }

    private static int[] DistributeMemberQuotas(List<List<GraphNode>> clusterContainers, int totalMembers, Random random)
    {
        var totalContainers = clusterContainers.Sum(c => c.Count);
        var quotas = new int[totalContainers];
        if (totalMembers <= 0 || totalContainers == 0)
            return quotas;

        var weights = new double[totalContainers];
        var totalWeight = 0.0;
        var globalIdx = 0;

        for (var cIdx = 0; cIdx < clusterContainers.Count; cIdx++)
        {
            var list = clusterContainers[cIdx];
            var clusterBaseWeight = Clusters[cIdx % Clusters.Length].Weight;

            for (var k = 0; k < list.Count; k++)
            {
                double roleFactor;
                if (k == 0 && list.Count > 2)
                {
                    // "God Class" or main form of the cluster: very high volume (80-140 nodes)
                    roleFactor = 9.0 + random.NextDouble() * 4.5;
                }
                else if (k <= 2 && list.Count > 4)
                {
                    // Major secondary components (40-70 nodes)
                    roleFactor = 4.0 + random.NextDouble() * 2.0;
                }
                else if (list.Count <= 2)
                {
                    // Utility/leaf modules in small clusters (1-6 nodes)
                    roleFactor = 0.25 + random.NextDouble() * 0.4;
                }
                else
                {
                    // Medium and standard components (10-35 nodes)
                    roleFactor = 1.2 + random.NextDouble() * 1.0;
                }

                var w = Math.Max(0.1, clusterBaseWeight * roleFactor);
                weights[globalIdx++] = w;
                totalWeight += w;
            }
        }

        var allocated = 0;
        for (var i = 0; i < totalContainers; i++)
        {
            var q = Math.Max(1, (int)Math.Round((weights[i] / totalWeight) * totalMembers));
            quotas[i] = q;
            allocated += q;
        }

        // Exact tie number of members
        while (allocated < totalMembers)
        {
            // Assign the delta preferentially to the heaviest leading containers
            var topIdx = 0;
            for (var i = 1; i < totalContainers; i++)
            {
                if (quotas[i] > quotas[topIdx])
                    topIdx = i;
            }
            quotas[topIdx]++;
            allocated++;
        }

        while (allocated > totalMembers)
        {
            var topIdx = 0;
            for (var i = 1; i < totalContainers; i++)
            {
                if (quotas[i] > quotas[topIdx])
                    topIdx = i;
            }

            if (quotas[topIdx] > 1)
            {
                quotas[topIdx]--;
                allocated--;
            }
            else
            {
                break;
            }
        }

        return quotas;
    }
}
