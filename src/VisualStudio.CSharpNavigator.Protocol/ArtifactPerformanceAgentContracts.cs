using System;
using System.Collections.Generic;

namespace VisualStudio.CSharpNavigator.Protocol;

public enum ArtifactEvidenceKind
{
    Unknown = 0,
    Log = 1,
    Trace = 2,
    Report = 3,
    Json = 4,
    Text = 5,
}

public sealed class ArtifactEvidenceReport
{
    public string Status { get; set; } = string.Empty;

    public ArtifactEvidenceItem[] Artifacts { get; set; } = Array.Empty<ArtifactEvidenceItem>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class ArtifactEvidenceItem
{
    public string FilePath { get; set; } = string.Empty;

    public ArtifactEvidenceKind Kind { get; set; }

    public long Length { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string[] MatchedPatterns { get; set; } = Array.Empty<string>();

    public string[] WarningLines { get; set; } = Array.Empty<string>();

    public string[] ErrorLines { get; set; } = Array.Empty<string>();

    public bool IsTruncated { get; set; }
}

public sealed class CSharpWorkflowPerformanceSnapshot
{
    public string Status { get; set; } = string.Empty;

    public string BridgeStatus { get; set; } = string.Empty;

    public string TargetSolutionPath { get; set; } = string.Empty;

    public int ActiveInstanceCount { get; set; }

    public int StaleInstanceCount { get; set; }

    public int ToolCount { get; set; }

    public int ExpectedToolCount { get; set; }

    public string[] ActiveInstanceIds { get; set; } = Array.Empty<string>();

    public string[] StaleInstanceIds { get; set; } = Array.Empty<string>();

    public string[] RecommendedProfiles { get; set; } = Array.Empty<string>();

    public string[] SuggestedEnvironmentVariables { get; set; } = Array.Empty<string>();

    public CSharpWorkflowBudgetHint[] BudgetHints { get; set; } = Array.Empty<CSharpWorkflowBudgetHint>();

    public CSharpWorkflowTelemetrySignal[] TelemetrySignals { get; set; } = Array.Empty<CSharpWorkflowTelemetrySignal>();

    public long CacheHitCount { get; set; }

    public long CacheMissCount { get; set; }

    public long CacheSingleFlightJoinCount { get; set; }

    public int CacheEntryCount { get; set; }

    public int CacheInFlightCount { get; set; }

    public long BridgeCallCount { get; set; }

    public long BridgeFailedCallCount { get; set; }

    public long BridgeRecentAverageElapsedMilliseconds { get; set; }

    public long BridgeRecentMaxElapsedMilliseconds { get; set; }

    public long BridgeRecentAverageExecutionMilliseconds { get; set; }

    public VerificationCommand[] SuggestedCommands { get; set; } = Array.Empty<VerificationCommand>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpWorkflowBudgetHint
{
    public string Name { get; set; } = string.Empty;

    public int WarningThreshold { get; set; }

    public int RecommendedLimit { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class CSharpWorkflowTelemetrySignal
{
    public string Name { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Interpretation { get; set; } = string.Empty;

    public string RecommendedAction { get; set; } = string.Empty;
}

public sealed class CSharpRepoWorkflowAnalysis
{
    public string Status { get; set; } = string.Empty;

    public string RootDirectory { get; set; } = string.Empty;

    public CSharpSolutionCandidate[] SolutionCandidates { get; set; } = Array.Empty<CSharpSolutionCandidate>();

    public string SelectedSolutionPath { get; set; } = string.Empty;

    public string[] ProjectFiles { get; set; } = Array.Empty<string>();

    public string[] TestProjectFiles { get; set; } = Array.Empty<string>();

    public VerificationCommand[] BuildCommands { get; set; } = Array.Empty<VerificationCommand>();

    public VerificationCommand[] TestCommands { get; set; } = Array.Empty<VerificationCommand>();

    public string[] NoisyPathPatterns { get; set; } = Array.Empty<string>();

    public string[] ToolRoutingRules { get; set; } = Array.Empty<string>();

    public string[] ArtifactHints { get; set; } = Array.Empty<string>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpAgentInstructionsDraft
{
    public string Status { get; set; } = string.Empty;

    public string SuggestedFileName { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public CSharpRepoWorkflowAnalysis RepoWorkflow { get; set; } = new();

    public string[] Sections { get; set; } = Array.Empty<string>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpAgentWorkSplitPlan
{
    public string Status { get; set; } = string.Empty;

    public string Objective { get; set; } = string.Empty;

    public CSharpAgentWorkPacket[] Packets { get; set; } = Array.Empty<CSharpAgentWorkPacket>();

    public string ExpectedFindingSchema { get; set; } = string.Empty;

    public string[] MergeInstructions { get; set; } = Array.Empty<string>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpAgentWorkPacket
{
    public string PacketId { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Objective { get; set; } = string.Empty;

    public string[] ScopeFiles { get; set; } = Array.Empty<string>();

    public string[] ScopeSymbols { get; set; } = Array.Empty<string>();

    public string[] AllowedTools { get; set; } = Array.Empty<string>();

    public string[] EvidenceResources { get; set; } = Array.Empty<string>();

    public WorkflowBudget Budget { get; set; } = new();

    public VerificationCommand[] SuggestedCommands { get; set; } = Array.Empty<VerificationCommand>();

    public string ExpectedOutputSchema { get; set; } = string.Empty;

    public string[] StopConditions { get; set; } = Array.Empty<string>();
}

public sealed class CSharpAgentFinding
{
    public string PacketId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string[] Files { get; set; } = Array.Empty<string>();

    public string[] Symbols { get; set; } = Array.Empty<string>();

    public string[] EvidenceResources { get; set; } = Array.Empty<string>();

    public string[] Findings { get; set; } = Array.Empty<string>();

    public string[] Risks { get; set; } = Array.Empty<string>();

    public string[] RecommendedNextActions { get; set; } = Array.Empty<string>();
}

public sealed class CSharpAgentFindingMergeResult
{
    public string Status { get; set; } = string.Empty;

    public string Objective { get; set; } = string.Empty;

    public int ExpectedPacketCount { get; set; }

    public int CompletedPacketCount { get; set; }

    public string[] MissingPacketIds { get; set; } = Array.Empty<string>();

    public string MergedSummary { get; set; } = string.Empty;

    public string[] Findings { get; set; } = Array.Empty<string>();

    public string[] Risks { get; set; } = Array.Empty<string>();

    public string[] FollowUpWork { get; set; } = Array.Empty<string>();

    public string ExpectedOutputSchema { get; set; } = string.Empty;
}
