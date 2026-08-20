using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class VisualStudioDocumentTools
{
    private readonly CodeNavigationTools _inner;

    public VisualStudioDocumentTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "get_visual_studio_open_documents", ReadOnly = true, Idempotent = true)]
    [Description("Return open Visual Studio document snapshots, including dirty and selection state when available.")]
    public Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetVisualStudioOpenDocuments(
        [Description("Maximum open documents to return.")]
        int maxResults = 100,
        [Description("Whether current text selection should be included when available.")]
        bool includeSelection = true,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetVisualStudioOpenDocuments(maxResults, includeSelection, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "get_visual_studio_active_document_context", ReadOnly = true, Idempotent = true)]
    [Description("Return the active Visual Studio document snapshot, including current selection when available.")]
    public Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetVisualStudioActiveDocumentContext(
        [Description("Whether current text selection should be included when available.")]
        bool includeSelection = true,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetVisualStudioActiveDocumentContext(includeSelection, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "open_csharp_source_location", ReadOnly = false, Idempotent = false)]
    [Description("Open a C# source file in Visual Studio and navigate to a one-based line and column. This changes Visual Studio UI focus.")]
    public Task<WorkspaceQueryResult<SourceNavigationResult>> OpenCSharpSourceLocation(
        [Description("Absolute source file path to open in Visual Studio.")]
        string filePath,
        [Description("One-based source line.")]
        int line = 1,
        [Description("One-based source column.")]
        int column = 1,
        [Description("Whether Visual Studio should activate the source window.")]
        bool activate = true,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.OpenCSharpSourceLocation(filePath, line, column, activate, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }
}
