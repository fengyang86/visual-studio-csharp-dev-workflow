using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class WorkspacePreparationTools
{
    private readonly CodeNavigationTools _inner;

    public WorkspacePreparationTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "list_visual_studio_instances", ReadOnly = true, Idempotent = true)]
    [Description("List Visual Studio VSIX bridge instances discovered by the local MCP server, including stale candidates when requested.")]
    public Task<WorkspaceQueryResult<VisualStudioBridgeInstanceDescriptor>> ListVisualStudioInstances(
        [Description("Whether stale discovery records should be included for diagnostics.")]
        bool includeStale = true,
        CancellationToken cancellationToken = default)
    {
        return _inner.ListVisualStudioInstances(includeStale, cancellationToken);
    }

    [McpServerTool(Name = "find_csharp_solutions", ReadOnly = true, Idempotent = true)]
    [Description("Find Visual Studio C# solution files (.sln and .slnx) under a directory and rank candidates for automatic workspace preparation.")]
    public Task<WorkspaceQueryResult<CSharpSolutionCandidate>> FindCSharpSolutions(
        [Description("Directory to search. Defaults to the MCP server working directory.")]
        string? rootDirectory = null,
        [Description("Optional preferred solution name or path fragment used to rank candidates.")]
        string? preferredName = null,
        [Description("Maximum directory depth to scan from the root directory.")]
        int maxDepth = 4,
        [Description("Maximum solution candidates to return.")]
        int maxResults = 20,
        [Description("Whether to include .slnx solution files.")]
        bool includeSlnx = true,
        CancellationToken cancellationToken = default)
    {
        return _inner.FindCSharpSolutions(
            rootDirectory,
            preferredName,
            maxDepth,
            maxResults,
            includeSlnx,
            cancellationToken);
    }

    [McpServerTool(Name = "prepare_csharp_workspace", ReadOnly = true, Idempotent = true)]
    [Description("Create a read-only preparation plan for opening or selecting a Visual Studio C# solution and bridge target.")]
    public Task<WorkspaceQueryResult<WorkspacePreparationReport>> PrepareCSharpWorkspace(
        [Description("Directory to search for .sln and .slnx files. Defaults to the MCP server working directory.")]
        string? rootDirectory = null,
        [Description("Optional preferred solution name or path fragment used to rank candidates.")]
        string? preferredName = null,
        [Description("Maximum directory depth to scan from the root directory.")]
        int maxDepth = 4,
        [Description("Maximum solution candidates to consider.")]
        int maxSolutions = 20,
        [Description("Whether to include .slnx solution files.")]
        bool includeSlnx = true,
        CancellationToken cancellationToken = default)
    {
        return _inner.PrepareCSharpWorkspace(
            rootDirectory,
            preferredName,
            maxDepth,
            maxSolutions,
            includeSlnx,
            cancellationToken);
    }

    [McpServerTool(Name = "open_csharp_solution_in_visual_studio", ReadOnly = false, Idempotent = false)]
    [Description("Launch Visual Studio with a .sln or .slnx file, optionally waiting for the VSIX bridge discovery record. Does not close existing Visual Studio instances.")]
    public Task<WorkspaceQueryResult<VisualStudioSolutionOpenResult>> OpenCSharpSolutionInVisualStudio(
        [Description("Absolute path to the .sln or .slnx file to open in Visual Studio.")]
        string solutionPath,
        [Description("Optional explicit devenv.exe path. When omitted, the server locates Visual Studio with vswhere, PATH, or common install paths.")]
        string? devenvPath = null,
        [Description("Whether to wait for a matching VSIX bridge after launching Visual Studio.")]
        bool waitForBridge = true,
        [Description("Maximum time to wait for bridge discovery when waitForBridge is true.")]
        int timeoutMilliseconds = 60000,
        [Description("Polling interval while waiting for bridge discovery.")]
        int pollIntervalMilliseconds = 1000,
        CancellationToken cancellationToken = default)
    {
        return _inner.OpenCSharpSolutionInVisualStudio(
            solutionPath,
            devenvPath,
            waitForBridge,
            timeoutMilliseconds,
            pollIntervalMilliseconds,
            cancellationToken);
    }

    [McpServerTool(Name = "wait_for_visual_studio_bridge", ReadOnly = true, Idempotent = true)]
    [Description("Wait for a Visual Studio VSIX bridge discovery record that matches a .sln or .slnx path.")]
    public Task<WorkspaceQueryResult<VisualStudioBridgeWaitReport>> WaitForVisualStudioBridge(
        [Description("Absolute path to the .sln or .slnx file expected to be open in Visual Studio.")]
        string solutionPath,
        [Description("Maximum time to wait for bridge discovery.")]
        int timeoutMilliseconds = 60000,
        [Description("Polling interval while waiting for bridge discovery.")]
        int pollIntervalMilliseconds = 1000,
        CancellationToken cancellationToken = default)
    {
        return _inner.WaitForVisualStudioBridge(
            solutionPath,
            timeoutMilliseconds,
            pollIntervalMilliseconds,
            cancellationToken);
    }
}
