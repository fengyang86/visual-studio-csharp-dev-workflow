using System;
using System.Collections.Generic;

namespace VisualStudio.CSharpNavigator.Protocol;

public sealed class WorkspaceStatusRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public bool SnapshotOnly { get; set; }
}

public sealed class WorkspaceStatus
{
    public string WorkspaceVersion { get; set; } = string.Empty;

    public string InstanceId { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public bool IsSolutionLoaded { get; set; }

    public string SolutionPath { get; set; } = string.Empty;

    public string SolutionName { get; set; } = string.Empty;

    public int ProjectCount { get; set; }

    public int DocumentCount { get; set; }

    public string ActiveConfigurationName { get; set; } = string.Empty;

    public string ActivePlatformName { get; set; } = string.Empty;

    public string[] StartupProjects { get; set; } = Array.Empty<string>();

    public WorkspaceProjectStatus[] Projects { get; set; } = Array.Empty<WorkspaceProjectStatus>();

    public bool IsProjectListPartial { get; set; }
}

public sealed class WorkspaceProjectStatus
{
    public string ProjectName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string[] TargetFrameworks { get; set; } = Array.Empty<string>();
}

public sealed class NavigatorHealthReport
{
    public string Status { get; set; } = string.Empty;

    public string ServerAssemblyVersion { get; set; } = string.Empty;

    public string ExpectedBridgeProtocolVersion { get; set; } = string.Empty;

    public VisualStudioBridgeInstanceDescriptor[] Instances { get; set; } = Array.Empty<VisualStudioBridgeInstanceDescriptor>();

    public WorkspaceStatus? WorkspaceStatus { get; set; }

    public CodeDiagnostic[] DiagnosticsPreview { get; set; } = Array.Empty<CodeDiagnostic>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class WorkspacePreparationReport
{
    public string Status { get; set; } = string.Empty;

    public string RootDirectory { get; set; } = string.Empty;

    public string PreferredName { get; set; } = string.Empty;

    public CSharpSolutionCandidate[] SolutionCandidates { get; set; } = Array.Empty<CSharpSolutionCandidate>();

    public CSharpSolutionCandidate? SelectedSolution { get; set; }

    public VisualStudioBridgeInstanceDescriptor[] MatchingInstances { get; set; } = Array.Empty<VisualStudioBridgeInstanceDescriptor>();

    public VisualStudioBridgeInstanceDescriptor? SelectedInstance { get; set; }

    public bool IsReady { get; set; }

    public bool RequiresVisualStudioLaunch { get; set; }

    public bool IsAmbiguous { get; set; }

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class VisualStudioSolutionOpenResult
{
    public string Status { get; set; } = string.Empty;

    public string SolutionPath { get; set; } = string.Empty;

    public string DevenvPath { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public bool Started { get; set; }

    public VisualStudioBridgeInstanceDescriptor[] MatchingInstances { get; set; } = Array.Empty<VisualStudioBridgeInstanceDescriptor>();

    public VisualStudioBridgeInstanceDescriptor? SelectedInstance { get; set; }

    public VisualStudioBridgeWaitReport? WaitReport { get; set; }

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class VisualStudioBridgeWaitReport
{
    public string Status { get; set; } = string.Empty;

    public string SolutionPath { get; set; } = string.Empty;

    public int TimeoutMilliseconds { get; set; }

    public int PollIntervalMilliseconds { get; set; }

    public int ElapsedMilliseconds { get; set; }

    public VisualStudioBridgeInstanceDescriptor[] MatchingInstances { get; set; } = Array.Empty<VisualStudioBridgeInstanceDescriptor>();

    public VisualStudioBridgeInstanceDescriptor? SelectedInstance { get; set; }

    public bool IsReady { get; set; }

    public bool IsAmbiguous { get; set; }

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class VisualStudioInstancesRequest
{
    public bool IncludeStale { get; set; } = true;
}

public sealed class CSharpSolutionCandidate
{
    public string FilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string SolutionName { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public int Depth { get; set; }

    public int Score { get; set; }

    public bool IsRootLevel { get; set; }

    public bool IsSlnx { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
}

public sealed class VisualStudioBridgeInstanceDescriptor
{
    public const string ExpectedBridgeProtocolVersion = "1";

    public string InstanceId { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public string SolutionPath { get; set; } = string.Empty;

    public string SolutionName { get; set; } = string.Empty;

    public string PipeName { get; set; } = string.Empty;

    public string BridgeProtocolVersion { get; set; } = string.Empty;

    public string ExtensionAssemblyVersion { get; set; } = string.Empty;

    public string ExtensionFileVersion { get; set; } = string.Empty;

    public DateTimeOffset LastSeenUtc { get; set; }

    public bool IsAlive { get; set; }

    public bool IsStale { get; set; }

    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
}

public sealed class VisualStudioBridgeInstance
{
    public string InstanceId { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public string SolutionPath { get; set; } = string.Empty;

    public string SolutionName { get; set; } = string.Empty;

    public string PipeName { get; set; } = string.Empty;

    public string BridgeProtocolVersion { get; set; } = string.Empty;

    public string ExtensionAssemblyVersion { get; set; } = string.Empty;

    public string ExtensionFileVersion { get; set; } = string.Empty;

    public DateTimeOffset LastSeenUtc { get; set; }
}

// VS-side build orchestration: start (fire-and-forget) + poll (read status).
// The 60s bridge timeout requires this split; heavy builds exceed it.
public sealed class VisualStudioBuildRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public bool Rebuild { get; set; }
}

public sealed class VisualStudioBuildResult
{
    public bool BuildSubmitted { get; set; }
}

public sealed class VisualStudioBuildStatusRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }
}

public sealed class VisualStudioBuildStatus
{
    public string BuildState { get; set; } = string.Empty;

    public bool IsBuildInProgress { get; set; }

    public int LastBuildErrorCount { get; set; }

    public int LastBuildWarningCount { get; set; }

    public string StartupProjectName { get; set; } = string.Empty;

    public string ActiveConfigurationName { get; set; } = string.Empty;
}

// VS activity journal: bounded ring buffer of recent Build/Debug/Solution/Document
// events observed inside Visual Studio, readable by AI agents.
public sealed class VisualStudioActivityRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public int MaxEvents { get; set; } = 50;

    public string? Category { get; set; }
}

public sealed class VisualStudioActivityEvent
{
    public DateTimeOffset TimestampUtc { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}

public sealed class VisualStudioActivityResult
{
    public System.Collections.Generic.IReadOnlyList<VisualStudioActivityEvent> Events { get; set; } = Array.Empty<VisualStudioActivityEvent>();
}
