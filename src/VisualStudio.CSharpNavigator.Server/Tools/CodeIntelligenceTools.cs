using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class CodeIntelligenceTools
{
    private const string SymbolKeyDescription = "Symbol key returned by search_csharp_symbols or another navigation tool. Preferred input; omit it to use a source position instead.";
    private const string FilePathDescription = "Absolute path of a source file, combined with line and column when no symbol key is available.";
    private const string LineDescription = "1-based line number of the source position.";
    private const string ColumnDescription = "1-based column (character offset) of the source position.";
    private const string MaxResultsDescription = "Maximum items to return. Truncation is reported through diagnostics and isPartial.";
    private const string IncludeGeneratedDescription = "Also include generated documents such as .Designer.cs and source-generated files.";
    private const string TargetPipeNameDescription = "Optional target Visual Studio bridge pipe name. Highest priority when provided.";
    private const string TargetInstanceIdDescription = "Optional target Visual Studio bridge instance id. Preferred for repeated calls in one session.";
    private const string TargetSolutionPathDescription = "Optional target Visual Studio solution path. Fails with candidates when ambiguous.";

    private readonly CodeNavigationTools _inner;

    public CodeIntelligenceTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "search_csharp_symbols", ReadOnly = true, Idempotent = true)]
    [Description("Search C# symbols in the active Visual Studio solution and return file and line evidence. The first choice for turning a symbol NAME into a symbolKey for the other navigation tools. Result shape: items[] (typed results), diagnostics[] (string notes), isPartial (bool, true when truncated), succeeded (bool, false only on rejection), errorCode (optional string, from the first diagnostic code prefix).")]
    public Task<WorkspaceQueryResult<SymbolDescriptor>> SearchCSharpSymbols(
        [Description("Case-insensitive substring of the symbol name, optionally containing type or namespace parts.")] string queryText,
        [Description(MaxResultsDescription)] int maxResults = 50,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.SearchCSharpSymbols(queryText, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_references", ReadOnly = true, Idempotent = true)]
    [Description("Find references for a C# symbol identified by symbol key or source position. Result shape: items[] (typed results), diagnostics[] (string notes), isPartial (bool, true when truncated), succeeded (bool, false only on rejection), errorCode (optional string, from the first diagnostic code prefix).")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpReferences(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description(MaxResultsDescription + " High-frequency symbols can return large lists, so keep this bounded.")] int maxResults = 300,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpReferences(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_references_by_symbol_search", ReadOnly = true, Idempotent = true)]
    [Description("Find references by first searching symbols and then using the unique matched symbol key. Use this when you only have a symbol NAME; once any tool returned a symbolKey, call find_csharp_references with that key instead. Returns candidate diagnostics when the search is ambiguous.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpReferencesBySymbolSearch(
        [Description("Case-insensitive substring of the symbol name to resolve first.")] string queryText,
        [Description("Optional containing type name to disambiguate the symbol search.")] string? containingType = null,
        [Description("Optional project name to disambiguate the symbol search.")] string? projectName = null,
        [Description("Optional symbol kind filter: Namespace, Type, Method, Property, Field, Event, Parameter, or Local.")] CodeSymbolKind? kind = null,
        [Description("Maximum symbol candidates to consider before failing as ambiguous.")] int maxSymbolCandidates = 50,
        [Description(MaxResultsDescription)] int maxResults = 300,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpReferencesBySymbolSearch(queryText, containingType, projectName, kind, maxSymbolCandidates, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_definitions", ReadOnly = true, Idempotent = true)]
    [Description("Find definitions for a C# symbol identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolDescriptor>> FindCSharpDefinitions(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description(MaxResultsDescription)] int maxResults = 300,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpDefinitions(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_implementations", ReadOnly = true, Idempotent = true)]
    [Description("Find C# symbols that implement an interface or interface member identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpImplementations(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description(MaxResultsDescription)] int maxResults = 300,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpImplementations(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_overrides", ReadOnly = true, Idempotent = true)]
    [Description("Find C# members that override the specified member identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpOverrides(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description(MaxResultsDescription)] int maxResults = 300,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpOverrides(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "describe_csharp_symbol", ReadOnly = true, Idempotent = true)]
    [Description("Describe a C# symbol with signature, accessibility, modifiers, containing type, base type, interfaces, attributes, and source evidence.")]
    public Task<WorkspaceQueryResult<SymbolDescription>> DescribeCSharpSymbol(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.DescribeCSharpSymbol(symbolKey, filePath, line, column, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_symbol_source", ReadOnly = true, Idempotent = true)]
    [Description("Return compact source snippets for a C# symbol declaration, including optional surrounding context and truncation metadata. Prefer this over reading whole files before editing a known symbol.")]
    public Task<WorkspaceQueryResult<SourceContextSnippet>> GetCSharpSymbolSource(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description("Lines of context to include around the declaration.")] int contextLines = 3,
        [Description("Maximum total characters of returned snippet text.")] int maxChars = 12000,
        [Description("Maximum snippets to return for overloaded or partial symbols.")] int maxSnippets = 3,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.GetCSharpSymbolSource(symbolKey, filePath, line, column, contextLines, maxChars, maxSnippets, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "batch_get_csharp_symbol_sources", ReadOnly = true, Idempotent = true)]
    [Description("Return compact C# symbol declaration source snippets for multiple symbol keys or source positions using one MCP call.")]
    public Task<WorkspaceQueryResult<SourceContextSnippet>> BatchGetCSharpSymbolSources(
        [Description("Symbol keys or source positions to fetch; each entry is a symbolKey or a filePath/line/column triple.")] SourcePositionRequest[] symbols,
        [Description("Lines of context to include around each declaration.")] int contextLines = 3,
        [Description("Maximum characters of snippet text per symbol.")] int maxCharsPerSymbol = 12000,
        [Description("Maximum snippets per symbol.")] int maxSnippetsPerSymbol = 3,
        [Description("Maximum symbols to process in one call.")] int maxSymbols = 20,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.BatchGetCSharpSymbolSources(symbols, contextLines, maxCharsPerSymbol, maxSnippetsPerSymbol, maxSymbols, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_source_context", ReadOnly = true, Idempotent = true)]
    [Description("Return the enclosing C# member or type source around a file position, with compact context and truncation metadata. Use it for build errors, diagnostics, and debugger frames instead of reading whole files.")]
    public Task<WorkspaceQueryResult<SourceContextSnippet>> GetCSharpSourceContext(
        [Description("Absolute path of the source file.")] string filePath,
        [Description(LineDescription)] int line,
        [Description(ColumnDescription)] int column,
        [Description("Lines of context to include around the enclosing member.")] int contextLines = 3,
        [Description("Maximum total characters of returned snippet text.")] int maxChars = 12000,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.GetCSharpSourceContext(filePath, line, column, contextLines, maxChars, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "batch_get_csharp_source_contexts", ReadOnly = true, Idempotent = true)]
    [Description("Return compact enclosing C# source snippets for multiple file positions using one MCP call.")]
    public Task<WorkspaceQueryResult<SourceContextSnippet>> BatchGetCSharpSourceContexts(
        [Description("Source positions (filePath/line/column) to fetch, typically build diagnostics or debugger frames.")] SourcePositionRequest[] positions,
        [Description("Lines of context to include around each enclosing member.")] int contextLines = 3,
        [Description("Maximum characters of snippet text per position.")] int maxCharsPerPosition = 12000,
        [Description("Maximum positions to process in one call.")] int maxPositions = 20,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.BatchGetCSharpSourceContexts(positions, contextLines, maxCharsPerPosition, maxPositions, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "list_csharp_document_symbols", ReadOnly = true, Idempotent = true)]
    [Description("List declared C# symbols in one document as a flat hierarchy with parent ids and source spans.")]
    public Task<WorkspaceQueryResult<DocumentSymbolNode>> ListCSharpDocumentSymbols(
        [Description("Absolute path of the document.")] string filePath,
        [Description(MaxResultsDescription)] int maxResults = 1000,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.ListCSharpDocumentSymbols(filePath, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_callers", ReadOnly = true, Idempotent = true)]
    [Description("Find C# callers of a symbol using Roslyn references and return call graph edges with source spans. maxDepth > 1 expands transitively with BFS and deduplication.")]
    public Task<WorkspaceQueryResult<CallGraphEdge>> FindCSharpCallers(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description("Recursion depth 1..10; 1 returns direct callers only.")] int maxDepth = 1,
        [Description(MaxResultsDescription)] int maxResults = 100,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpCallers(symbolKey, filePath, line, column, maxDepth, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_callees", ReadOnly = true, Idempotent = true)]
    [Description("Find C# callees used by symbol declaration bodies using Roslyn semantic analysis and return call graph edges with source spans. maxDepth > 1 expands transitively with BFS and deduplication.")]
    public Task<WorkspaceQueryResult<CallGraphEdge>> FindCSharpCallees(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description("Recursion depth 1..10; 1 returns direct callees only.")] int maxDepth = 1,
        [Description(MaxResultsDescription)] int maxResults = 100,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpCallees(symbolKey, filePath, line, column, maxDepth, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "analyze_csharp_symbol_impact", ReadOnly = true, Idempotent = true)]
    [Description("Aggregate real Roslyn references for one C# symbol and summarize affected projects, files, roles, and containing types. maxDepth > 1 propagates through callers transitively. Result shape: items[] (typed results), diagnostics[] (string notes), isPartial (bool, true when truncated), succeeded (bool, false only on rejection), errorCode (optional string, from the first diagnostic code prefix).")]
    public Task<WorkspaceQueryResult<SymbolImpactSummary>> AnalyzeCSharpSymbolImpact(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description("Recursion depth 1..10 for transitive caller propagation; 1 aggregates direct references only.")] int maxDepth = 1,
        [Description("Maximum reference sites to process before marking the summary partial.")] int maxResults = 1000,
        [Description("Maximum distinct projects in the summary groups.")] int maxProjects = 20,
        [Description("Maximum distinct files in the summary groups.")] int maxFiles = 20,
        [Description("Maximum distinct containing types in the summary groups.")] int maxContainingTypes = 20,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.AnalyzeCSharpSymbolImpact(symbolKey, filePath, line, column, maxDepth, maxResults, maxProjects, maxFiles, maxContainingTypes, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_related_tests", ReadOnly = true, Idempotent = true)]
    [Description("Find C# tests that directly reference or heuristically match a production symbol or source file. Strong evidence comes from Roslyn references; naming and namespace matches are returned as heuristic reasons only.")]
    public Task<WorkspaceQueryResult<RelatedTestDescriptor>> FindCSharpRelatedTests(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description(MaxResultsDescription)] int maxResults = 100,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpRelatedTests(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_derived_types", ReadOnly = true, Idempotent = true)]
    [Description("Find C# types that derive from or implement the specified type using Roslyn symbol analysis.")]
    public Task<WorkspaceQueryResult<DerivedTypeDescriptor>> FindCSharpDerivedTypes(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description("true walks the whole hierarchy below the type; false returns direct descendants only.")] bool transitive = true,
        [Description(MaxResultsDescription)] int maxResults = 1000,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpDerivedTypes(symbolKey, filePath, line, column, transitive, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_inheritance_chain", ReadOnly = true, Idempotent = true)]
    [Description("Return base type chain, direct interfaces, and all interfaces for a C# type.")]
    public Task<WorkspaceQueryResult<InheritanceChain>> GetCSharpInheritanceChain(
        [Description(SymbolKeyDescription)] string? symbolKey = null,
        [Description(FilePathDescription)] string? filePath = null,
        [Description(LineDescription)] int? line = null,
        [Description(ColumnDescription)] int? column = null,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.GetCSharpInheritanceChain(symbolKey, filePath, line, column, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_project_graph", ReadOnly = true, Idempotent = true)]
    [Description("Return C# project references, document counts, target frameworks, and metadata reference summaries from the active solution.")]
    public Task<WorkspaceQueryResult<ProjectGraph>> GetCSharpProjectGraph(
        [Description("Optional project name or path fragment to scope the graph to one project (its outgoing edges are preserved).")] string? projectName = null,
        [Description("Maximum projects to return.")] int maxProjects = 500,
        [Description("Maximum metadata references per project; 0 omits metadata references entirely.")] int maxMetadataReferencesPerProject = 50,
        [Description("Whether to include metadata (assembly) references in each node.")] bool includeMetadataReferences = true,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.GetCSharpProjectGraph(projectName, maxProjects, maxMetadataReferencesPerProject, includeMetadataReferences, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "list_csharp_generated_documents", ReadOnly = true, Idempotent = true)]
    [Description("List generated C# documents visible in the active Visual Studio workspace, including path-based generated files and Roslyn source-generated documents when available.")]
    public Task<WorkspaceQueryResult<GeneratedDocumentDescriptor>> ListCSharpGeneratedDocuments(
        [Description("Optional project name to scope the listing.")] string? projectName = null,
        [Description(MaxResultsDescription)] int maxResults = 1000,
        [Description("Whether to include Roslyn source-generated in-memory documents.")] bool includeSourceGeneratedDocuments = true,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.ListCSharpGeneratedDocuments(projectName, maxResults, includeSourceGeneratedDocuments, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_temporary_markers", ReadOnly = true, Idempotent = true)]
    [Description("Scan C# syntax trivia for temporary or simplified implementation markers such as TODO, FIXME, HACK, TEMP, WORKAROUND, 临时, and 简化. Review helper only; empty results prove nothing.")]
    public Task<WorkspaceQueryResult<TemporaryMarker>> FindCSharpTemporaryMarkers(
        [Description("Optional absolute file path to scope the scan to one document.")] string? filePath = null,
        [Description("Optional project name to scope the scan.")] string? projectName = null,
        [Description(MaxResultsDescription)] int maxResults = 500,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.FindCSharpTemporaryMarkers(filePath, projectName, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_enclosing_context", ReadOnly = true, Idempotent = true)]
    [Description("Return namespace/type/member/local-function/lambda context for a C# source position.")]
    public Task<WorkspaceQueryResult<EnclosingContext>> GetCSharpEnclosingContext(
        [Description("Absolute path of the source file.")] string filePath,
        [Description(LineDescription)] int line,
        [Description(ColumnDescription)] int column,
        [Description(IncludeGeneratedDescription)] bool includeGeneratedCode = false,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.GetCSharpEnclosingContext(filePath, line, column, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
}
