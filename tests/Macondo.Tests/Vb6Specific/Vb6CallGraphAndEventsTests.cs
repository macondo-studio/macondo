using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6CallGraphAndEventsTests
{
    /// <summary>
    /// Verifica che CallGraphBuilder colleghi le relazioni HANDLES_EVENT sia dal controllo UI (CommandButton)
    /// alla rispettiva Sub di evento (_Click), sia dalla Form contenitore come generatore globale degli eventi.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireHandlesEventRelationships_ForUIControlsAndForm()
    {
        // Arrange
        var graph = new CodeGraph();
        var formCode = """
            VERSION 5.00
            Begin VB.Form OrderForm
               Caption = "Order Form"
               Begin VB.CommandButton cmdSave
                  Caption = "Save"
               End
            End
            Attribute VB_Name = "OrderForm"

            Private Sub cmdSave_Click()
                ' Save action
            End Sub

            Private Sub Form_Load()
                ' Load action
            End Sub
            """;

        var res = ModuleExtractor.ExtractModule(graph, "OrderForm.frm", formCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { res });

        // Assert
        var formSym = graph.Symbols.First(s => s.Kind == SymbolKind.Form && s.Name == "OrderForm");
        var cmdSaveSym = graph.Symbols.First(s => s.Kind == SymbolKind.Field && s.Name == "cmdSave");
        var cmdSaveClick = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "cmdSave_Click");
        var formLoad = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Form_Load");

        var eventRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.HandlesEvent).ToList();
        eventRels.Should().NotBeEmpty();

        // cmdSave should have HandlesEvent to cmdSave_Click
        eventRels.Should().Contain(r => r.SourceId == cmdSaveSym.Id && r.TargetId == cmdSaveClick.Id);
        // OrderForm should have HandlesEvent to Form_Load and cmdSave_Click
        eventRels.Should().Contain(r => r.SourceId == formSym.Id && r.TargetId == formLoad.Id);
    }

    /// <summary>
    /// Verifica che dichiarazioni di auto-istanziazione ('Dim m_Repo As New Repository') generino
    /// una relazione INSTANTIATES dal campo verso la classe target.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireInstantiatesRelationships_FromDimAsNewDeclarations()
    {
        // Arrange
        var graph = new CodeGraph();
        var repoCls = """
            Public Sub Fetch()
            End Sub
            """;
        var serviceCls = """
            Private m_Repo As New Repository

            Public Sub Execute()
                m_Repo.Fetch
            End Sub
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "Repository.cls", repoCls);
        var res2 = ModuleExtractor.ExtractModule(graph, "Service.cls", serviceCls);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { res1, res2 });

        // Assert
        var repoSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Repository");
        var mRepoField = graph.Symbols.First(s => s.Name == "m_Repo");

        var instantiatesRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Instantiates && r.SourceId == mRepoField.Id && r.TargetId == repoSym.Id);
        instantiatesRel.Should().NotBeNull("Auto-instantiating field 'Dim As New Repository' should create INSTANTIATES relationship to Repository class");
    }

    /// <summary>
    /// Verifica che i parametri formali e i tipi di ritorno definiti con tipi utente (es. 'Dto')
    /// generino relazioni USES_TYPE dalla routine verso la classe target.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireUsesTypeRelationships_ForParametersAndReturnTypes()
    {
        // Arrange
        var graph = new CodeGraph();
        var dtoCls = """
            Public Id As Long
            """;
        var factoryCls = """
            Public Function CreateDto(ByVal id As Long, ByRef existing As Dto) As Dto
                Set CreateDto = existing
            End Function
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "Dto.cls", dtoCls);
        var res2 = ModuleExtractor.ExtractModule(graph, "Factory.cls", factoryCls);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { res1, res2 });

        // Assert
        var dtoSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Dto");
        var factoryMethod = graph.Symbols.First(s => s.Name == "CreateDto");

        var usesTypes = graph.Relationships.Where(r => r.Kind == RelationshipKind.UsesType && r.SourceId == factoryMethod.Id && r.TargetId == dtoSym.Id).ToList();
        usesTypes.Should().NotBeEmpty("Function parameter and return type of type Dto should create USES_TYPE relationships");
    }
}
