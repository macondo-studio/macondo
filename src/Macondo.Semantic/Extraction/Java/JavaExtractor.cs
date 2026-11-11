using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Semantic extractor for Java source files (.java) with fault-tolerant recursive resilient AST parser.
/// 
/// CARATTERISTICHE E CAPACITÀ SUPPORTATE:
/// 1. Package & Imports: Hierarchical package tracking to generate precise QualifiedNames (e.g. 'com.acme.service.OrderService').
/// 2. Annotations: support for annotations on classes, methods and fields (@Override, @Entity, @Autowired, @RestController, etc.).
/// 3. Type Declarations: Classes (standard, abstract, final, static, inner), Interfaces, Enums (with constants), Records (Java 16+) and @interface.
/// 4. Inheritance and Interfaces: 'extends', 'implements' and 'permits' clauses with support for generic parameters (<T, K>).
/// 5. Routines & Constructors: static/instance methods, constructors, typed parameters, return types, throws clauses.
/// 6. Fields & Constants: instance variables, static fields, constants ('static final' and enum values).
/// 7. Invocation Analysis (CALLS): detection of method invocations with explicit (obj.method()), implicit or super/this receiver.
/// 8. Object Creation Analysis (INSTANTIATES): 'new TypeName(...)' expressions.
/// </summary>
public static class JavaExtractor
{
    /// <summary>
    /// Performs parsing and semantic extraction of a Java file by directly inserting symbols into the <see cref="CodeGraph"/>.
    /// </summary>
    /// <param name="graph">The shared CodeGraph in which to record symbols and relationships CONTAINS.</param>
    /// <param name="filePath">Path or name of the analyzed Java source file.</param>
    /// <param name="sourceText">The complete Java source code text of the file.</param>
    /// <returns>The structured result containing symbols, call-sites, instantiations and inheritance.</returns>
    public static JavaExtractionResult ExtractJavaFile(CodeGraph graph, string filePath, string sourceText)
    {
        var parser = new JavaFileParser(graph, filePath, sourceText);
        return parser.Parse();
    }
}
