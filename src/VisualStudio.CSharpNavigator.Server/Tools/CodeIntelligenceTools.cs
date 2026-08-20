using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class CodeIntelligenceTools
{
    private readonly CodeNavigationTools _inner;

    public CodeIntelligenceTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "search_csharp_symbols", ReadOnly = true, Idempotent = true)]
    [Description("Search C# symbols in the active Visual Studio solution and return file and line evidence.")]
    public Task<WorkspaceQueryResult<SymbolDescriptor>> SearchCSharpSymbols(string queryText, int maxResults = 50, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.SearchCSharpSymbols(queryText, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_references", ReadOnly = true, Idempotent = true)]
    [Description("Find references for a C# symbol identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpReferences(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, int maxResults = 1000, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpReferences(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_references_by_symbol_search", ReadOnly = true, Idempotent = true)]
    [Description("Find references by first searching symbols and then using the unique matched symbol key. Returns candidate diagnostics when the search is ambiguous.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpReferencesBySymbolSearch(string queryText, string? containingType = null, string? projectName = null, CodeSymbolKind? kind = null, int maxSymbolCandidates = 50, int maxResults = 1000, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpReferencesBySymbolSearch(queryText, containingType, projectName, kind, maxSymbolCandidates, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_definitions", ReadOnly = true, Idempotent = true)]
    [Description("Find definitions for a C# symbol identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolDescriptor>> FindCSharpDefinitions(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, int maxResults = 1000, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpDefinitions(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_implementations", ReadOnly = true, Idempotent = true)]
    [Description("Find C# symbols that implement an interface or interface member identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpImplementations(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, int maxResults = 1000, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpImplementations(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_overrides", ReadOnly = true, Idempotent = true)]
    [Description("Find C# members that override the specified member identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpOverrides(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, int maxResults = 1000, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpOverrides(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "describe_csharp_symbol", ReadOnly = true, Idempotent = true)]
    [Description("Describe a C# symbol with signature, accessibility, modifiers, containing type, base type, interfaces, attributes, and source evidence.")]
    public Task<WorkspaceQueryResult<SymbolDescription>> DescribeCSharpSymbol(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.DescribeCSharpSymbol(symbolKey, filePath, line, column, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_symbol_source", ReadOnly = true, Idempotent = true)]
    [Description("Return compact source snippets for a C# symbol declaration, including optional surrounding context and truncation metadata.")]
    public Task<WorkspaceQueryResult<SourceContextSnippet>> GetCSharpSymbolSource(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, int contextLines = 3, int maxChars = 12000, int maxSnippets = 3, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.GetCSharpSymbolSource(symbolKey, filePath, line, column, contextLines, maxChars, maxSnippets, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "batch_get_csharp_symbol_sources", ReadOnly = true, Idempotent = true)]
    [Description("Return compact C# symbol declaration source snippets for multiple symbol keys or source positions using one MCP call.")]
    public Task<WorkspaceQueryResult<SourceContextSnippet>> BatchGetCSharpSymbolSources(SourcePositionRequest[] symbols, int contextLines = 3, int maxCharsPerSymbol = 12000, int maxSnippetsPerSymbol = 3, int maxSymbols = 20, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.BatchGetCSharpSymbolSources(symbols, contextLines, maxCharsPerSymbol, maxSnippetsPerSymbol, maxSymbols, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_source_context", ReadOnly = true, Idempotent = true)]
    [Description("Return the enclosing C# member or type source around a file position, with compact context and truncation metadata.")]
    public Task<WorkspaceQueryResult<SourceContextSnippet>> GetCSharpSourceContext(string filePath, int line, int column, int contextLines = 3, int maxChars = 12000, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.GetCSharpSourceContext(filePath, line, column, contextLines, maxChars, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "batch_get_csharp_source_contexts", ReadOnly = true, Idempotent = true)]
    [Description("Return compact enclosing C# source snippets for multiple file positions using one MCP call.")]
    public Task<WorkspaceQueryResult<SourceContextSnippet>> BatchGetCSharpSourceContexts(SourcePositionRequest[] positions, int contextLines = 3, int maxCharsPerPosition = 12000, int maxPositions = 20, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.BatchGetCSharpSourceContexts(positions, contextLines, maxCharsPerPosition, maxPositions, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "list_csharp_document_symbols", ReadOnly = true, Idempotent = true)]
    [Description("List declared C# symbols in one document as a flat hierarchy with parent ids and source spans.")]
    public Task<WorkspaceQueryResult<DocumentSymbolNode>> ListCSharpDocumentSymbols(string filePath, int maxResults = 1000, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.ListCSharpDocumentSymbols(filePath, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_callers", ReadOnly = true, Idempotent = true)]
    [Description("Find C# callers of a symbol using Roslyn references and return call graph edges with source spans.")]
    public Task<WorkspaceQueryResult<CallGraphEdge>> FindCSharpCallers(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, int maxDepth = 1, int maxResults = 100, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpCallers(symbolKey, filePath, line, column, maxDepth, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_callees", ReadOnly = true, Idempotent = true)]
    [Description("Find C# callees used by symbol declaration bodies using Roslyn semantic analysis and return call graph edges with source spans.")]
    public Task<WorkspaceQueryResult<CallGraphEdge>> FindCSharpCallees(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, int maxDepth = 1, int maxResults = 100, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpCallees(symbolKey, filePath, line, column, maxDepth, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "analyze_csharp_symbol_impact", ReadOnly = true, Idempotent = true)]
    [Description("Aggregate real Roslyn references for one C# symbol and summarize affected projects, files, roles, and containing types.")]
    public Task<WorkspaceQueryResult<SymbolImpactSummary>> AnalyzeCSharpSymbolImpact(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, int maxDepth = 1, int maxResults = 1000, int maxProjects = 20, int maxFiles = 20, int maxContainingTypes = 20, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.AnalyzeCSharpSymbolImpact(symbolKey, filePath, line, column, maxDepth, maxResults, maxProjects, maxFiles, maxContainingTypes, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_related_tests", ReadOnly = true, Idempotent = true)]
    [Description("Find C# tests that directly reference or heuristically match a production symbol or source file.")]
    public Task<WorkspaceQueryResult<RelatedTestDescriptor>> FindCSharpRelatedTests(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, int maxResults = 100, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpRelatedTests(symbolKey, filePath, line, column, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_derived_types", ReadOnly = true, Idempotent = true)]
    [Description("Find C# types that derive from or implement the specified type using Roslyn symbol analysis.")]
    public Task<WorkspaceQueryResult<DerivedTypeDescriptor>> FindCSharpDerivedTypes(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, bool transitive = true, int maxResults = 1000, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpDerivedTypes(symbolKey, filePath, line, column, transitive, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_inheritance_chain", ReadOnly = true, Idempotent = true)]
    [Description("Return base type chain, direct interfaces, and all interfaces for a C# type.")]
    public Task<WorkspaceQueryResult<InheritanceChain>> GetCSharpInheritanceChain(string? symbolKey = null, string? filePath = null, int? line = null, int? column = null, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.GetCSharpInheritanceChain(symbolKey, filePath, line, column, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_project_graph", ReadOnly = true, Idempotent = true)]
    [Description("Return C# project references, document counts, target frameworks, and metadata reference summaries from the active solution.")]
    public Task<WorkspaceQueryResult<ProjectGraph>> GetCSharpProjectGraph(string? projectName = null, int maxProjects = 500, int maxMetadataReferencesPerProject = 50, bool includeMetadataReferences = true, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.GetCSharpProjectGraph(projectName, maxProjects, maxMetadataReferencesPerProject, includeMetadataReferences, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "list_csharp_generated_documents", ReadOnly = true, Idempotent = true)]
    [Description("List generated C# documents visible in the active Visual Studio workspace, including path-based generated files and Roslyn source-generated documents when available.")]
    public Task<WorkspaceQueryResult<GeneratedDocumentDescriptor>> ListCSharpGeneratedDocuments(string? projectName = null, int maxResults = 1000, bool includeSourceGeneratedDocuments = true, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.ListCSharpGeneratedDocuments(projectName, maxResults, includeSourceGeneratedDocuments, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "find_csharp_temporary_markers", ReadOnly = true, Idempotent = true)]
    [Description("Scan C# syntax trivia for temporary or simplified implementation markers such as TODO, FIXME, HACK, TEMP, WORKAROUND, 临时, and 简化.")]
    public Task<WorkspaceQueryResult<TemporaryMarker>> FindCSharpTemporaryMarkers(string? filePath = null, string? projectName = null, int maxResults = 500, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.FindCSharpTemporaryMarkers(filePath, projectName, maxResults, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);

    [McpServerTool(Name = "get_csharp_enclosing_context", ReadOnly = true, Idempotent = true)]
    [Description("Return namespace/type/member/local-function/lambda context for a C# source position.")]
    public Task<WorkspaceQueryResult<EnclosingContext>> GetCSharpEnclosingContext(string filePath, int line, int column, bool includeGeneratedCode = false, string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, CancellationToken cancellationToken = default)
        => _inner.GetCSharpEnclosingContext(filePath, line, column, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
}
