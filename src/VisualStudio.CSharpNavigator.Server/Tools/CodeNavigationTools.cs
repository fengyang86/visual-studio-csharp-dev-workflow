using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Agentic;
using VisualStudio.CSharpNavigator.Server.Bridge;
using ModelContextProtocol.Server;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class CodeNavigationTools
{
    private const int MaxOpenSolutionBridgeWaitMilliseconds = 90000;

    private static readonly JsonSerializerOptions CacheKeyJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IVisualStudioWorkspaceBridge _workspaceBridge;
    private readonly ShortLivedQueryCache _queryCache;
    private readonly IVisualStudioSolutionLauncher _solutionLauncher;
    private readonly IVisualStudioActivityLogReader _activityLogReader;
    private readonly EvidencePacketBuilder _evidencePacketBuilder;
    private readonly BridgeCallTelemetryRecorder? _bridgeTelemetryRecorder;
    private const int MaxBuildFailureBindingIssues = 6;

    public CodeNavigationTools(
        IVisualStudioWorkspaceBridge workspaceBridge,
        ShortLivedQueryCache? queryCache = null,
        IVisualStudioSolutionLauncher? solutionLauncher = null,
        IVisualStudioActivityLogReader? activityLogReader = null,
        EvidencePacketBuilder? evidencePacketBuilder = null,
        BridgeCallTelemetryRecorder? bridgeTelemetryRecorder = null)
    {
        _workspaceBridge = workspaceBridge;
        _queryCache = queryCache ?? new ShortLivedQueryCache();
        _solutionLauncher = solutionLauncher ?? new VisualStudioSolutionLauncher();
        _activityLogReader = activityLogReader ?? new VisualStudioActivityLogReader();
        _evidencePacketBuilder = evidencePacketBuilder ?? new EvidencePacketBuilder(new EvidenceStore());
        _bridgeTelemetryRecorder = bridgeTelemetryRecorder;
    }

    public Task<WorkspaceQueryResult<VisualStudioBridgeInstanceDescriptor>> ListVisualStudioInstances(
        bool includeStale = true,
        CancellationToken cancellationToken = default)
    {
        return _workspaceBridge.ListVisualStudioInstancesAsync(
            new VisualStudioInstancesRequest { IncludeStale = includeStale },
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CSharpSolutionCandidate>> FindCSharpSolutions(
        string? rootDirectory = null,
        string? preferredName = null,
        int maxDepth = 4,
        int maxResults = 20,
        bool includeSlnx = true,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateFindSolutionsLimits(maxDepth, maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpSolutionCandidate>(validation));
        }

        var diagnostics = new List<string>();
        var resolvedRoot = ResolveSolutionSearchRoot(rootDirectory, diagnostics);
        if (resolvedRoot is null)
        {
            return Task.FromResult(Failure<CSharpSolutionCandidate>(
                $"Solution search root does not exist or is not a directory: {rootDirectory}"));
        }

        var candidates = FindSolutionCandidates(
                resolvedRoot,
                preferredName,
                maxDepth,
                includeSlnx,
                maxResults,
                diagnostics,
                cancellationToken)
            .ToArray();

        return Task.FromResult(new WorkspaceQueryResult<CSharpSolutionCandidate>
        {
            Items = candidates,
            Diagnostics = diagnostics.ToArray(),
            IsPartial = diagnostics.Any(diagnostic => diagnostic.StartsWith("SolutionCandidatesTruncated:", StringComparison.OrdinalIgnoreCase)),
        });
    }

    [Description("Return the active Visual Studio C# workspace status through the local VSIX bridge.")]
    public async Task<WorkspaceQueryResult<WorkspaceStatus>> GetCSharpWorkspaceStatus(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var request = new WorkspaceStatusRequest
        {
            Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
        };
        var cached = await _queryCache.GetOrAddAsync(
                CreateCacheKey("GetWorkspaceStatus", request),
                () => _workspaceBridge.GetWorkspaceStatusAsync(request, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        return cached.IsCacheHit
            ? WithCacheDiagnostic(cached.Value, "get_csharp_workspace_status")
            : cached.Value;
    }

    [Description("Parse C# build output and return ranked build issues, suppressing unrelated path noise and likely cascade errors.")]
    public Task<WorkspaceQueryResult<BuildTriageReport>> AnalyzeCSharpBuildErrors(
        [Description("Raw dotnet/MSBuild/Visual Studio build output text.")]
        string? buildOutput = null,
        [Description("Optional path to a compact build log file. Used together with buildOutput when both are provided.")]
        string? buildLogFilePath = null,
        [Description("Optional file, directory, or wildcard patterns to include. Use this to focus on touched folders.")]
        string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude. Use this to suppress known noisy legacy/generated folders.")]
        string[]? excludePathPatterns = null,
        [Description("Optional changed files from git diff or the current task. Issues in these files are ranked higher.")]
        string[]? changedFiles = null,
        [Description("Maximum ranked build issues to return.")]
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        var combinedBuildOutput = ReadBuildOutputInput(buildOutput, buildLogFilePath, diagnostics, out var readFailure);
        if (readFailure is not null)
        {
            return Task.FromResult(Failure<BuildTriageReport>(readFailure));
        }

        if (string.IsNullOrWhiteSpace(combinedBuildOutput))
        {
            return Task.FromResult(Failure<BuildTriageReport>(
                "Provide buildOutput, buildLogFilePath, or both."));
        }

        var result = BuildLogTriage.Analyze(
            combinedBuildOutput,
            NormalizePatterns(includePathPatterns),
            NormalizePatterns(excludePathPatterns),
            NormalizePatterns(changedFiles),
            maxResults);
        diagnostics.AddRange(result.Diagnostics);
        result.Diagnostics = diagnostics.ToArray();
        return Task.FromResult(result);
    }
    [Description("Start a read-only C# investigation by combining bridge health, optional build-log triage, scoped diagnostics, symbol lookup, and related context.")]
    public async Task<WorkspaceQueryResult<CSharpInvestigationReport>> StartCSharpInvestigation(
        [Description("Short problem statement or task context for the investigation.")]
        string? problemText = null,
        [Description("Optional raw dotnet/MSBuild/Visual Studio build output text to triage.")]
        string? buildOutput = null,
        [Description("Optional path to a compact build log file. Used together with buildOutput when both are provided.")]
        string? buildLogFilePath = null,
        [Description("Optional symbol name or partial name to search after workspace health succeeds.")]
        string? symbolQuery = null,
        [Description("Optional absolute source file path used to scope diagnostics and related tests.")]
        string? filePath = null,
        [Description("Optional file, directory, or wildcard patterns to include for build triage and diagnostics.")]
        string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude for build triage and diagnostics.")]
        string[]? excludePathPatterns = null,
        [Description("Optional changed files from git diff or the current task. These focus build triage and diagnostics.")]
        string[]? changedFiles = null,
        [Description("Optional Visual Studio project name for scoped diagnostics.")]
        string? projectName = null,
        [Description("Optional minimum severity filter for diagnostics: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off. Auto filters known noise only when no focused diagnostics scope is provided.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Whether to query whole-solution diagnostics when no file/project/path scope is available.")]
        bool includeWholeSolutionDiagnostics = false,
        [Description("Whether to read the Visual Studio Output Window Build pane for build triage when buildOutput and buildLogFilePath are omitted.")]
        bool includeVisualStudioBuildOutput = true,
        [Description("Maximum trailing characters to read from the Visual Studio Output Window Build pane.")]
        int maxVisualStudioBuildOutputCharacters = 20000,
        [Description("Maximum build issues to return from build-log triage.")]
        int maxBuildIssues = 20,
        [Description("Maximum Roslyn diagnostics to return.")]
        int maxDiagnostics = 30,
        [Description("Maximum symbol candidates to return.")]
        int maxSymbols = 20,
        [Description("Maximum references, callers, callees, and related tests to return for a unique symbol.")]
        int maxRelatedItems = 20,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInvestigationLimits(
            maxBuildIssues,
            maxDiagnostics,
            maxSymbols,
            maxRelatedItems,
            maxVisualStudioBuildOutputCharacters);
        if (validation is not null)
        {
            return Failure<CSharpInvestigationReport>(validation);
        }

        var diagnostics = new List<string>();
        var nextSteps = new List<string>();
        var normalizedChangedFiles = NormalizePatterns(changedFiles);
        var hasExplicitBuildInput = HasExplicitBuildInput(buildOutput, buildLogFilePath);
        var buildTriage = TryAnalyzeBuildOutput(
            buildOutput,
            buildLogFilePath,
            includePathPatterns,
            excludePathPatterns,
            normalizedChangedFiles,
            maxBuildIssues,
            diagnostics);

        var instancesResult = await _workspaceBridge.ListVisualStudioInstancesAsync(
                new VisualStudioInstancesRequest { IncludeStale = true },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(instancesResult.Diagnostics);
        var instances = instancesResult.Items.ToArray();
        var activeInstances = instances
            .Where(instance => instance.IsAlive && !instance.IsStale)
            .ToArray();
        var target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);

        if (activeInstances.Length == 0)
        {
            nextSteps.Add("Open Visual Studio with the target C# solution, or let the skill setup flow launch it.");
            nextSteps.AddRange(buildTriage?.SuggestedNextSteps ?? Array.Empty<string>());
            var noBridgeTaskContext = CreateTaskContextSummary(
                problemText,
                symbolQuery,
                filePath,
                projectName,
                normalizedChangedFiles,
                includePathPatterns,
                excludePathPatterns,
                hasExplicitBuildInput,
                usedVisualStudioBuildOutput: false,
                workspaceStatus: null,
                target);
            return Success(
                CreateInvestigationReport(
                    "NoActiveBridge",
                    new NavigatorHealthReport
                    {
                        Status = "NoActiveBridge",
                        Instances = instances,
                        SuggestedNextSteps = nextSteps.ToArray(),
                    },
                    buildTriage,
                    nextSteps,
                    noBridgeTaskContext,
                    CreateEvidenceLevel(buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<SymbolDescriptor>(), noBridgeTaskContext),
                    CreateInvestigationRecommendedNextActions("NoActiveBridge", buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<PrimaryFile>(), Array.Empty<PrimarySymbol>(), Array.Empty<PrimaryDiagnostic>(), Array.Empty<SymbolDescriptor>(), null, noBridgeTaskContext)),
                diagnostics,
                true);
        }

        if (target is null && activeInstances.Length > 1)
        {
            nextSteps.Add("Pass targetInstanceId, targetPipeName, or targetSolutionPath before running a C# investigation.");
            nextSteps.AddRange(buildTriage?.SuggestedNextSteps ?? Array.Empty<string>());
            var ambiguousTaskContext = CreateTaskContextSummary(
                problemText,
                symbolQuery,
                filePath,
                projectName,
                normalizedChangedFiles,
                includePathPatterns,
                excludePathPatterns,
                hasExplicitBuildInput,
                usedVisualStudioBuildOutput: false,
                workspaceStatus: null,
                target);
            return Success(
                CreateInvestigationReport(
                    "AmbiguousTarget",
                    new NavigatorHealthReport
                    {
                        Status = "AmbiguousTarget",
                        Instances = instances,
                        SuggestedNextSteps = nextSteps.ToArray(),
                    },
                    buildTriage,
                    nextSteps,
                    ambiguousTaskContext,
                    CreateEvidenceLevel(buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<SymbolDescriptor>(), ambiguousTaskContext),
                    CreateInvestigationRecommendedNextActions("AmbiguousTarget", buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<PrimaryFile>(), Array.Empty<PrimarySymbol>(), Array.Empty<PrimaryDiagnostic>(), Array.Empty<SymbolDescriptor>(), null, ambiguousTaskContext)),
                diagnostics,
                true);
        }

        target ??= new VisualStudioBridgeTarget
        {
            InstanceId = activeInstances[0].InstanceId,
            PipeName = activeInstances[0].PipeName,
            SolutionPath = activeInstances[0].SolutionPath,
        };

        var workspaceResult = await _workspaceBridge.GetWorkspaceStatusAsync(
                new WorkspaceStatusRequest { Target = target },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(workspaceResult.Diagnostics);
        var workspaceStatus = workspaceResult.Items.FirstOrDefault();
        var healthStatus = workspaceStatus is null
            ? "WorkspaceStatusUnavailable"
            : workspaceStatus.IsSolutionLoaded
                ? "Ready"
                : "SolutionNotLoaded";

        var health = new NavigatorHealthReport
        {
            Status = healthStatus,
            Instances = instances,
            WorkspaceStatus = workspaceStatus,
        };

        if (workspaceStatus is null || !workspaceStatus.IsSolutionLoaded)
        {
            nextSteps.Add("Load a C# solution in Visual Studio before using semantic investigation context.");
            nextSteps.AddRange(buildTriage?.SuggestedNextSteps ?? Array.Empty<string>());
            health.SuggestedNextSteps = nextSteps.ToArray();
            var unloadedTaskContext = CreateTaskContextSummary(
                problemText,
                symbolQuery,
                filePath,
                projectName,
                normalizedChangedFiles,
                includePathPatterns,
                excludePathPatterns,
                hasExplicitBuildInput,
                usedVisualStudioBuildOutput: false,
                workspaceStatus,
                target);
            return Success(
                CreateInvestigationReport(
                    healthStatus,
                    health,
                    buildTriage,
                    nextSteps,
                    unloadedTaskContext,
                    CreateEvidenceLevel(buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<SymbolDescriptor>(), unloadedTaskContext),
                    CreateInvestigationRecommendedNextActions(healthStatus, buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<PrimaryFile>(), Array.Empty<PrimarySymbol>(), Array.Empty<PrimaryDiagnostic>(), Array.Empty<SymbolDescriptor>(), null, unloadedTaskContext)),
                diagnostics,
                true);
        }

        var visualStudioBuildOutputIsPartial = false;
        if (buildTriage is null && !hasExplicitBuildInput && includeVisualStudioBuildOutput)
        {
            buildTriage = await TryAnalyzeVisualStudioBuildOutputAsync(
                    target,
                    includePathPatterns,
                    excludePathPatterns,
                    normalizedChangedFiles,
                    maxBuildIssues,
                    maxVisualStudioBuildOutputCharacters,
                    diagnostics,
                    () => visualStudioBuildOutputIsPartial = true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var scopedPatterns = CreateInvestigationIncludePatterns(
            includePathPatterns,
            normalizedChangedFiles,
            filePath,
            buildTriage);
        var codeDiagnostics = await GetInvestigationDiagnosticsAsync(
                target,
                filePath,
                scopedPatterns,
                excludePathPatterns,
                normalizedChangedFiles,
                projectName,
                minimumSeverity,
                noiseProfile,
                includeWholeSolutionDiagnostics,
                maxDiagnostics,
                includeGeneratedCode,
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(codeDiagnostics.Diagnostics);

        var symbols = Array.Empty<SymbolDescriptor>();
        var definitions = Array.Empty<SymbolDescriptor>();
        var references = Array.Empty<SymbolReference>();
        var callers = Array.Empty<CallGraphEdge>();
        var callees = Array.Empty<CallGraphEdge>();
        var relatedTests = Array.Empty<RelatedTestDescriptor>();
        SymbolImpactSummary? impactSummary = null;
        var symbolContextIsPartial = false;
        void MarkSymbolContextPartial()
        {
            symbolContextIsPartial = true;
        }

        if (!string.IsNullOrWhiteSpace(symbolQuery))
        {
            var searchResult = await _workspaceBridge.SearchSymbolsAsync(
                    new SymbolSearchRequest
                    {
                        Target = target,
                        QueryText = symbolQuery,
                        MaxResults = maxSymbols,
                        IncludeGeneratedCode = includeGeneratedCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(searchResult.Diagnostics);
            symbolContextIsPartial |= searchResult.IsPartial;
            symbols = searchResult.Items.ToArray();

            if (symbols.Length == 1 && symbols[0].Key is not null)
            {
                var symbolKey = symbols[0].Key!;
                definitions = await QueryDefinitionsAsync(target, symbolKey, includeGeneratedCode, diagnostics, MarkSymbolContextPartial, cancellationToken)
                    .ConfigureAwait(false);
                references = await QueryReferencesAsync(target, symbolKey, maxRelatedItems, includeGeneratedCode, diagnostics, MarkSymbolContextPartial, cancellationToken)
                    .ConfigureAwait(false);
                callers = await QueryCallGraphAsync(target, symbolKey, callers: true, maxRelatedItems, includeGeneratedCode, diagnostics, MarkSymbolContextPartial, cancellationToken)
                    .ConfigureAwait(false);
                callees = await QueryCallGraphAsync(target, symbolKey, callers: false, maxRelatedItems, includeGeneratedCode, diagnostics, MarkSymbolContextPartial, cancellationToken)
                    .ConfigureAwait(false);
                relatedTests = await QueryRelatedTestsAsync(target, symbolKey, filePath, maxRelatedItems, includeGeneratedCode, diagnostics, MarkSymbolContextPartial, cancellationToken)
                    .ConfigureAwait(false);
                impactSummary = await QuerySymbolImpactAsync(target, symbolKey, maxRelatedItems, includeGeneratedCode, diagnostics, MarkSymbolContextPartial, cancellationToken)
                    .ConfigureAwait(false);
                nextSteps.Add("A unique symbol was found; inspect returned definitions, references, callers/callees, and related tests before editing.");
            }
            else if (symbols.Length > 1)
            {
                nextSteps.Add("Symbol search is ambiguous; narrow symbolQuery with containing type, project, or source file context before deep navigation.");
            }
            else
            {
                nextSteps.Add("No symbol candidates were found for symbolQuery; fall back to scoped diagnostics and file context.");
            }
        }

        nextSteps.AddRange(buildTriage?.SuggestedNextSteps ?? Array.Empty<string>());
        if (codeDiagnostics.Items.Count > 0)
        {
            nextSteps.Add("Inspect scoped diagnostics before whole-solution diagnostics; they are the primary signal for this investigation.");
        }

        health.DiagnosticsPreview = codeDiagnostics.Items.ToArray();
        health.SuggestedNextSteps = nextSteps.ToArray();
        var usedVisualStudioBuildOutput = diagnostics.Any(diagnostic => diagnostic.StartsWith("VisualStudioBuildOutputUsed:", StringComparison.Ordinal));
        var taskContext = CreateTaskContextSummary(
            problemText,
            symbolQuery,
            filePath,
            projectName,
            normalizedChangedFiles,
            includePathPatterns,
            excludePathPatterns,
            hasExplicitBuildInput,
            usedVisualStudioBuildOutput,
            workspaceStatus,
            target);
        var primaryDiagnostics = CreatePrimaryDiagnostics(buildTriage, codeDiagnostics.Items);
        var primarySymbols = CreatePrimarySymbols(symbols, definitions);
        var primaryFiles = CreatePrimaryFiles(
            workspaceStatus,
            filePath,
            normalizedChangedFiles,
            buildTriage,
            codeDiagnostics.Items,
            symbols,
            definitions,
            references,
            relatedTests);
        var evidenceLevel = CreateEvidenceLevel(buildTriage, codeDiagnostics.Items, symbols, taskContext);

        return Success(
            new CSharpInvestigationReport
            {
                Status = "Ready",
                TaskContext = taskContext,
                EvidenceLevel = evidenceLevel,
                Health = health,
                BuildTriage = buildTriage,
                PrimaryFiles = primaryFiles,
                PrimarySymbols = primarySymbols,
                PrimaryDiagnostics = primaryDiagnostics,
                ImpactSummary = impactSummary,
                RecommendedNextActions = CreateInvestigationRecommendedNextActions(
                    "Ready",
                    buildTriage,
                    codeDiagnostics.Items,
                    primaryFiles,
                    primarySymbols,
                    primaryDiagnostics,
                    symbols,
                    impactSummary,
                    taskContext),
                Diagnostics = codeDiagnostics.Items.ToArray(),
                Symbols = symbols,
                Definitions = definitions,
                References = references,
                Callers = callers,
                Callees = callees,
                RelatedTests = relatedTests,
                SuggestedNextSteps = nextSteps.ToArray(),
            },
            diagnostics,
            instancesResult.IsPartial || workspaceResult.IsPartial || codeDiagnostics.IsPartial || symbolContextIsPartial || visualStudioBuildOutputIsPartial);
    }
    [Description("Create a compact task context package for C# work by combining investigation evidence with bounded source snippets for likely edit locations.")]
    public async Task<WorkspaceQueryResult<CSharpTaskContextPackage>> GetCSharpTaskContext(
        [Description("Short problem statement or task context for the investigation.")]
        string? problemText = null,
        [Description("Optional raw dotnet/MSBuild/Visual Studio build output text to triage.")]
        string? buildOutput = null,
        [Description("Optional path to a compact build log file. Used together with buildOutput when both are provided.")]
        string? buildLogFilePath = null,
        [Description("Optional symbol name or partial name to search after workspace health succeeds.")]
        string? symbolQuery = null,
        [Description("Optional absolute source file path used to scope diagnostics and source snippets.")]
        string? filePath = null,
        [Description("Optional file, directory, or wildcard patterns to include for build triage and diagnostics.")]
        string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude for build triage and diagnostics.")]
        string[]? excludePathPatterns = null,
        [Description("Optional changed files from git diff or the current task. These focus build triage, diagnostics, and source snippets.")]
        string[]? changedFiles = null,
        [Description("Optional Visual Studio project name for scoped diagnostics.")]
        string? projectName = null,
        [Description("Optional minimum severity filter for diagnostics: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Whether to query whole-solution diagnostics when no file/project/path scope is available.")]
        bool includeWholeSolutionDiagnostics = false,
        [Description("Whether to read the Visual Studio Output Window Build pane for build triage when buildOutput and buildLogFilePath are omitted.")]
        bool includeVisualStudioBuildOutput = true,
        [Description("Maximum trailing characters to read from the Visual Studio Output Window Build pane.")]
        int maxVisualStudioBuildOutputCharacters = 20000,
        [Description("Maximum build issues to return from build-log triage.")]
        int maxBuildIssues = 20,
        [Description("Maximum Roslyn diagnostics to return.")]
        int maxDiagnostics = 30,
        [Description("Maximum symbol candidates to return.")]
        int maxSymbols = 20,
        [Description("Maximum references, callers, callees, and related tests to return for a unique symbol.")]
        int maxRelatedItems = 20,
        [Description("Maximum source snippets to include in the task context package.")]
        int maxSourceSnippets = 8,
        [Description("Number of source lines to include around each snippet.")]
        int contextLines = 3,
        [Description("Maximum characters returned per source snippet request.")]
        int maxCharsPerSnippet = 8000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateTaskContextLimits(
            maxBuildIssues,
            maxDiagnostics,
            maxSymbols,
            maxRelatedItems,
            maxVisualStudioBuildOutputCharacters,
            maxSourceSnippets,
            contextLines,
            maxCharsPerSnippet);
        if (validation is not null)
        {
            return Failure<CSharpTaskContextPackage>(validation);
        }

        var investigationResult = await StartCSharpInvestigation(
                problemText,
                buildOutput,
                buildLogFilePath,
                symbolQuery,
                filePath,
                includePathPatterns,
                excludePathPatterns,
                changedFiles,
                projectName,
                minimumSeverity,
                noiseProfile,
                includeWholeSolutionDiagnostics,
                includeVisualStudioBuildOutput,
                maxVisualStudioBuildOutputCharacters,
                maxBuildIssues,
                maxDiagnostics,
                maxSymbols,
                maxRelatedItems,
                includeGeneratedCode,
                targetPipeName,
                targetInstanceId,
                targetSolutionPath,
                cancellationToken)
            .ConfigureAwait(false);

        var diagnostics = new List<string>(investigationResult.Diagnostics);
        var investigation = investigationResult.Items.FirstOrDefault();
        if (investigation is null)
        {
            return Success(
                new CSharpTaskContextPackage
                {
                    Status = "InvestigationUnavailable",
                    SuggestedNextSteps = new[] { "Retry start_csharp_investigation with a narrower target or explicit build output." },
                },
                diagnostics,
                true);
        }

        var target = CreateTaskContextTarget(investigation, targetPipeName, targetInstanceId, targetSolutionPath);
        var sourceResult = await CollectTaskContextSourceSnippetsAsync(
                investigation,
                target,
                maxSourceSnippets,
                contextLines,
                maxCharsPerSnippet,
                includeGeneratedCode,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        return Success(
            new CSharpTaskContextPackage
            {
                Status = investigation.Status,
                TaskContext = investigation.TaskContext,
                EvidenceLevel = investigation.EvidenceLevel,
                Health = investigation.Health,
                BuildTriage = investigation.BuildTriage,
                PrimaryFiles = investigation.PrimaryFiles,
                PrimarySymbols = investigation.PrimarySymbols,
                PrimaryDiagnostics = investigation.PrimaryDiagnostics,
                SourceSnippets = sourceResult.Snippets,
                ImpactSummary = investigation.ImpactSummary,
                RelatedTests = investigation.RelatedTests,
                RecommendedNextActions = investigation.RecommendedNextActions,
                SuggestedNextSteps = investigation.SuggestedNextSteps
                    .Concat(CreateTaskContextSnippetNextSteps(sourceResult.Snippets, maxSourceSnippets))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            },
            diagnostics,
            investigationResult.IsPartial || sourceResult.IsPartial);
    }
    [Description("Create a read-only verification plan for changed C# files by combining build triage, scoped diagnostics, affected projects, related tests, and dotnet command suggestions.")]
    public async Task<WorkspaceQueryResult<CSharpVerificationPlan>> PlanCSharpVerification(
        [Description("Optional raw dotnet/MSBuild/Visual Studio build output text to triage.")]
        string? buildOutput = null,
        [Description("Optional path to a compact build log file. Used together with buildOutput when both are provided.")]
        string? buildLogFilePath = null,
        [Description("Optional changed files from git diff or the current task. These drive affected-project and diagnostics ranking.")]
        string[]? changedFiles = null,
        [Description("Optional symbol name or partial name to find related tests for a specific symbol.")]
        string? symbolQuery = null,
        [Description("Optional absolute source file path used to scope diagnostics and related tests.")]
        string? filePath = null,
        [Description("Optional file, directory, or wildcard patterns to include for build triage and diagnostics.")]
        string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude for build triage and diagnostics.")]
        string[]? excludePathPatterns = null,
        [Description("Optional Visual Studio project name for scoped diagnostics and project build suggestions.")]
        string? projectName = null,
        [Description("Optional minimum severity filter for diagnostics: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off. Auto filters known noise only when no focused diagnostics scope is provided.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Whether to query whole-solution diagnostics when no file/project/path scope is available.")]
        bool includeWholeSolutionDiagnostics = false,
        [Description("Whether to read the Visual Studio Output Window Build pane for build triage when buildOutput and buildLogFilePath are omitted.")]
        bool includeVisualStudioBuildOutput = true,
        [Description("Maximum trailing characters to read from the Visual Studio Output Window Build pane.")]
        int maxVisualStudioBuildOutputCharacters = 20000,
        [Description("Maximum build issues to return from build-log triage.")]
        int maxBuildIssues = 20,
        [Description("Maximum Roslyn diagnostics to return.")]
        int maxDiagnostics = 30,
        [Description("Maximum related tests to return.")]
        int maxRelatedTests = 20,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateVerificationPlanLimits(
            maxBuildIssues,
            maxDiagnostics,
            maxRelatedTests,
            maxVisualStudioBuildOutputCharacters);
        if (validation is not null)
        {
            return Failure<CSharpVerificationPlan>(validation);
        }

        var diagnostics = new List<string>();
        var nextSteps = new List<string>();
        var normalizedChangedFiles = NormalizePatterns(changedFiles);
        var hasExplicitBuildInput = HasExplicitBuildInput(buildOutput, buildLogFilePath);
        var buildTriage = TryAnalyzeBuildOutput(
            buildOutput,
            buildLogFilePath,
            includePathPatterns,
            excludePathPatterns,
            normalizedChangedFiles,
            maxBuildIssues,
            diagnostics);

        var instancesResult = await _workspaceBridge.ListVisualStudioInstancesAsync(
                new VisualStudioInstancesRequest { IncludeStale = true },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(instancesResult.Diagnostics);
        var instances = instancesResult.Items.ToArray();
        var activeInstances = instances
            .Where(instance => instance.IsAlive && !instance.IsStale)
            .ToArray();
        var target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);

        if (activeInstances.Length == 0)
        {
            nextSteps.Add("Open Visual Studio with the target C# solution before asking for affected tests or project graph based verification commands.");
            nextSteps.AddRange(buildTriage?.SuggestedNextSteps ?? Array.Empty<string>());
            var noBridgeTaskContext = CreateTaskContextSummary(null, symbolQuery, filePath, projectName, normalizedChangedFiles, includePathPatterns, excludePathPatterns, hasExplicitBuildInput, false, null, target);
            return Success(
                CreateVerificationPlan(
                    "NoActiveBridge",
                    null,
                    normalizedChangedFiles,
                    buildTriage,
                    nextSteps,
                    noBridgeTaskContext,
                    CreateEvidenceLevel(buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<SymbolDescriptor>(), noBridgeTaskContext),
                    CreateVerificationRecommendedNextActions("NoActiveBridge", buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<VerificationProject>(), Array.Empty<VerificationCommand>(), noBridgeTaskContext, false)),
                diagnostics,
                true);
        }

        if (target is null && activeInstances.Length > 1)
        {
            nextSteps.Add("Pass targetInstanceId, targetPipeName, or targetSolutionPath before planning verification in a multi-Visual Studio session.");
            nextSteps.AddRange(buildTriage?.SuggestedNextSteps ?? Array.Empty<string>());
            var ambiguousTaskContext = CreateTaskContextSummary(null, symbolQuery, filePath, projectName, normalizedChangedFiles, includePathPatterns, excludePathPatterns, hasExplicitBuildInput, false, null, null);
            return Success(
                CreateVerificationPlan(
                    "AmbiguousTarget",
                    null,
                    normalizedChangedFiles,
                    buildTriage,
                    nextSteps,
                    ambiguousTaskContext,
                    CreateEvidenceLevel(buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<SymbolDescriptor>(), ambiguousTaskContext),
                    CreateVerificationRecommendedNextActions("AmbiguousTarget", buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<VerificationProject>(), Array.Empty<VerificationCommand>(), ambiguousTaskContext, false)),
                diagnostics,
                true);
        }

        target ??= new VisualStudioBridgeTarget
        {
            InstanceId = activeInstances[0].InstanceId,
            PipeName = activeInstances[0].PipeName,
            SolutionPath = activeInstances[0].SolutionPath,
        };

        var workspaceResult = await _workspaceBridge.GetWorkspaceStatusAsync(
                new WorkspaceStatusRequest { Target = target },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(workspaceResult.Diagnostics);
        var workspaceStatus = workspaceResult.Items.FirstOrDefault();
        if (workspaceStatus is null || !workspaceStatus.IsSolutionLoaded)
        {
            nextSteps.Add("Load a C# solution in Visual Studio before asking for project/test verification planning.");
            nextSteps.AddRange(buildTriage?.SuggestedNextSteps ?? Array.Empty<string>());
            var unloadedTaskContext = CreateTaskContextSummary(null, symbolQuery, filePath, projectName, normalizedChangedFiles, includePathPatterns, excludePathPatterns, hasExplicitBuildInput, false, workspaceStatus, target);
            return Success(
                CreateVerificationPlan(
                    "SolutionNotLoaded",
                    workspaceStatus,
                    normalizedChangedFiles,
                    buildTriage,
                    nextSteps,
                    unloadedTaskContext,
                    CreateEvidenceLevel(buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<SymbolDescriptor>(), unloadedTaskContext),
                    CreateVerificationRecommendedNextActions("SolutionNotLoaded", buildTriage, Array.Empty<CodeDiagnostic>(), Array.Empty<VerificationProject>(), Array.Empty<VerificationCommand>(), unloadedTaskContext, false)),
                diagnostics,
                true);
        }

        var visualStudioBuildOutputIsPartial = false;
        if (buildTriage is null && !hasExplicitBuildInput && includeVisualStudioBuildOutput)
        {
            buildTriage = await TryAnalyzeVisualStudioBuildOutputAsync(
                    target,
                    includePathPatterns,
                    excludePathPatterns,
                    normalizedChangedFiles,
                    maxBuildIssues,
                    maxVisualStudioBuildOutputCharacters,
                    diagnostics,
                    () => visualStudioBuildOutputIsPartial = true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var projectGraphResult = await _workspaceBridge.GetProjectGraphAsync(
                new ProjectGraphRequest
                {
                    Target = target,
                    IncludeMetadataReferences = false,
                    MaxProjects = 500,
                    MaxMetadataReferencesPerProject = 0,
                },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(projectGraphResult.Diagnostics);
        var projectGraph = projectGraphResult.Items.FirstOrDefault() ?? new ProjectGraph();

        var scopedPatterns = CreateInvestigationIncludePatterns(
            includePathPatterns,
            normalizedChangedFiles,
            filePath,
            buildTriage);
        var codeDiagnostics = await GetInvestigationDiagnosticsAsync(
                target,
                filePath,
                scopedPatterns,
                excludePathPatterns,
                normalizedChangedFiles,
                projectName,
                minimumSeverity,
                noiseProfile,
                includeWholeSolutionDiagnostics,
                maxDiagnostics,
                includeGeneratedCode,
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(codeDiagnostics.Diagnostics);

        var relatedTestsArePartial = false;
        void MarkRelatedTestsPartial()
        {
            relatedTestsArePartial = true;
        }

        var relatedTests = await QueryVerificationRelatedTestsAsync(
                target,
                workspaceStatus,
                symbolQuery,
                filePath,
                normalizedChangedFiles,
                maxRelatedTests,
                includeGeneratedCode,
                diagnostics,
                MarkRelatedTestsPartial,
                cancellationToken)
            .ConfigureAwait(false);

        var affectedProjects = CreateAffectedProjects(
            projectGraph,
            workspaceStatus,
            normalizedChangedFiles,
            buildTriage,
            codeDiagnostics.Items,
            relatedTests,
            projectName);
        var commands = CreateVerificationCommands(
            workspaceStatus,
            projectGraph,
            affectedProjects,
            relatedTests);
        if (commands.Length == 0)
        {
            nextSteps.Add("No reliable verification command could be derived. Run a solution build, then feed the build output into analyze_csharp_build_errors.");
        }

        nextSteps.AddRange(buildTriage?.SuggestedNextSteps ?? Array.Empty<string>());
        var usedVisualStudioBuildOutput = diagnostics.Any(diagnostic => diagnostic.StartsWith("VisualStudioBuildOutputUsed:", StringComparison.Ordinal));
        var taskContext = CreateTaskContextSummary(
            null,
            symbolQuery,
            filePath,
            projectName,
            normalizedChangedFiles,
            includePathPatterns,
            excludePathPatterns,
            hasExplicitBuildInput,
            usedVisualStudioBuildOutput,
            workspaceStatus,
            target);
        var evidenceLevel = CreateEvidenceLevel(buildTriage, codeDiagnostics.Items, Array.Empty<SymbolDescriptor>(), taskContext);
        var recommendedNextActions = CreateVerificationRecommendedNextActions(
            "Ready",
            buildTriage,
            codeDiagnostics.Items.ToArray(),
            affectedProjects,
            commands,
            taskContext,
            codeDiagnostics.IsPartial);
        return Success(
            new CSharpVerificationPlan
            {
                Status = "Ready",
                TaskContext = taskContext,
                EvidenceLevel = evidenceLevel,
                WorkspaceStatus = workspaceStatus,
                ChangedFiles = normalizedChangedFiles,
                BuildTriage = buildTriage,
                Diagnostics = codeDiagnostics.Items.ToArray(),
                RelatedTests = relatedTests,
                AffectedProjects = affectedProjects,
                RecommendedCommands = commands,
                RecommendedNextActions = recommendedNextActions,
                SuggestedNextSteps = nextSteps.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            },
            diagnostics,
            instancesResult.IsPartial || workspaceResult.IsPartial || projectGraphResult.IsPartial || codeDiagnostics.IsPartial || relatedTestsArePartial || visualStudioBuildOutputIsPartial);
    }
    [Description("Create a compact read-only audit package for a C# area by combining area paths, query terms, scoped diagnostics, symbol candidates, related tests, temporary markers, and next actions.")]
    public async Task<WorkspaceQueryResult<CSharpAreaAuditReport>> AuditCSharpArea(
        [Description("Short problem statement or audit question.")]
        string? problemText = null,
        [Description("Area path filters such as source folders or files to audit.")]
        string[]? areaPaths = null,
        [Description("Symbol or keyword terms to search semantically before falling back to text search.")]
        string[]? queryTerms = null,
        [Description("Optional test class, method, or path patterns used to filter related-test evidence.")]
        string[]? testPatterns = null,
        [Description("Optional changed files from git diff or the current task.")]
        string[]? changedFiles = null,
        [Description("Optional additional include path patterns for diagnostics.")]
        string[]? includePathPatterns = null,
        [Description("Optional exclude path patterns for diagnostics.")]
        string[]? excludePathPatterns = null,
        [Description("Optional Visual Studio project name for scoped diagnostics and marker scanning.")]
        string? projectName = null,
        [Description("Optional minimum severity filter for diagnostics: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Maximum Roslyn diagnostics to return.")]
        int maxDiagnostics = 30,
        [Description("Maximum symbols to return across all query terms.")]
        int maxSymbols = 30,
        [Description("Maximum related tests to return.")]
        int maxRelatedTests = 20,
        [Description("Maximum temporary implementation markers to return.")]
        int maxMarkers = 20,
        [Description("Maximum primary files and findings to return.")]
        int maxFiles = 20,
        [Description("Maximum number of C# projects to process for diagnostics. Use 0 for no project-count cap.")]
        int maxDiagnosticProjects = 0,
        [Description("Maximum elapsed milliseconds for diagnostics collection before returning partial results.")]
        int maxDiagnosticElapsedMilliseconds = 30000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateAreaAuditLimits(maxDiagnostics, maxSymbols, maxRelatedTests, maxMarkers, maxFiles, maxDiagnosticProjects, maxDiagnosticElapsedMilliseconds);
        if (validation is not null)
        {
            return Failure<CSharpAreaAuditReport>(validation);
        }

        var diagnostics = new List<string>();
        var nextSteps = new List<string>();
        var normalizedAreaPaths = NormalizePatterns(areaPaths);
        var normalizedQueryTerms = NormalizePatterns(queryTerms);
        var normalizedTestPatterns = NormalizePatterns(testPatterns);
        var normalizedChangedFiles = NormalizePatterns(changedFiles);
        var normalizedIncludePatterns = NormalizePatterns(includePathPatterns);
        var normalizedExcludePatterns = NormalizePatterns(excludePathPatterns);
        var auditScopes = CreateAuditScopePatterns(normalizedAreaPaths, normalizedIncludePatterns, normalizedChangedFiles);
        if (auditScopes.Length == 0 && normalizedQueryTerms.Length == 0 && string.IsNullOrWhiteSpace(projectName))
        {
            nextSteps.Add("Provide areaPaths, queryTerms, changedFiles, includePathPatterns, or projectName before running an area audit.");
            return Success(
                CreateAreaAuditReport(
                    "NoScope",
                    null,
                    CreateTaskContextSummary(problemText, string.Empty, null, projectName, normalizedChangedFiles, normalizedIncludePatterns, normalizedExcludePatterns, false, false, null, null),
                    Array.Empty<PrimaryFile>(),
                    Array.Empty<PrimarySymbol>(),
                    Array.Empty<PrimaryDiagnostic>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<CodeDiagnostic>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<RelatedTestDescriptor>(),
                    Array.Empty<TemporaryMarker>(),
                    CreateAreaAuditRecommendedNextActions("NoScope", Array.Empty<PrimaryDiagnostic>(), Array.Empty<PrimarySymbol>(), Array.Empty<RelatedTestDescriptor>(), false, null),
                    nextSteps),
                diagnostics,
                false);
        }

        var instancesResult = await _workspaceBridge.ListVisualStudioInstancesAsync(
                new VisualStudioInstancesRequest { IncludeStale = true },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(instancesResult.Diagnostics);
        var activeInstances = instancesResult.Items
            .Where(instance => instance.IsAlive && !instance.IsStale)
            .ToArray();
        var target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);
        if (activeInstances.Length == 0)
        {
            nextSteps.Add("Open Visual Studio with the target C# solution before running a semantic area audit.");
            return Success(
                CreateAreaAuditReport(
                    "NoActiveBridge",
                    null,
                    CreateTaskContextSummary(problemText, string.Join(", ", normalizedQueryTerms), null, projectName, normalizedChangedFiles, normalizedIncludePatterns, normalizedExcludePatterns, false, false, null, target),
                    Array.Empty<PrimaryFile>(),
                    Array.Empty<PrimarySymbol>(),
                    Array.Empty<PrimaryDiagnostic>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<CodeDiagnostic>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<RelatedTestDescriptor>(),
                    Array.Empty<TemporaryMarker>(),
                    CreateAreaAuditRecommendedNextActions("NoActiveBridge", Array.Empty<PrimaryDiagnostic>(), Array.Empty<PrimarySymbol>(), Array.Empty<RelatedTestDescriptor>(), false, null),
                    nextSteps),
                diagnostics,
                instancesResult.IsPartial);
        }

        if (target is null && activeInstances.Length > 1)
        {
            nextSteps.Add("Pass targetInstanceId, targetPipeName, or targetSolutionPath before auditing in a multi-Visual Studio session.");
            return Success(
                CreateAreaAuditReport(
                    "AmbiguousTarget",
                    null,
                    CreateTaskContextSummary(problemText, string.Join(", ", normalizedQueryTerms), null, projectName, normalizedChangedFiles, normalizedIncludePatterns, normalizedExcludePatterns, false, false, null, null),
                    Array.Empty<PrimaryFile>(),
                    Array.Empty<PrimarySymbol>(),
                    Array.Empty<PrimaryDiagnostic>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<CodeDiagnostic>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<RelatedTestDescriptor>(),
                    Array.Empty<TemporaryMarker>(),
                    CreateAreaAuditRecommendedNextActions("AmbiguousTarget", Array.Empty<PrimaryDiagnostic>(), Array.Empty<PrimarySymbol>(), Array.Empty<RelatedTestDescriptor>(), false, null),
                    nextSteps),
                diagnostics,
                true);
        }

        target ??= new VisualStudioBridgeTarget
        {
            InstanceId = activeInstances[0].InstanceId,
            PipeName = activeInstances[0].PipeName,
            SolutionPath = activeInstances[0].SolutionPath,
        };

        var workspaceResult = await _workspaceBridge.GetWorkspaceStatusAsync(
                new WorkspaceStatusRequest { Target = target },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(workspaceResult.Diagnostics);
        var workspaceStatus = workspaceResult.Items.FirstOrDefault();
        if (workspaceStatus is null || !workspaceStatus.IsSolutionLoaded)
        {
            nextSteps.Add("Load a C# solution in Visual Studio before running a semantic area audit.");
            return Success(
                CreateAreaAuditReport(
                    "SolutionNotLoaded",
                    workspaceStatus,
                    CreateTaskContextSummary(problemText, string.Join(", ", normalizedQueryTerms), null, projectName, normalizedChangedFiles, normalizedIncludePatterns, normalizedExcludePatterns, false, false, workspaceStatus, target),
                    Array.Empty<PrimaryFile>(),
                    Array.Empty<PrimarySymbol>(),
                    Array.Empty<PrimaryDiagnostic>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<CodeDiagnostic>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<RelatedTestDescriptor>(),
                    Array.Empty<TemporaryMarker>(),
                    CreateAreaAuditRecommendedNextActions("SolutionNotLoaded", Array.Empty<PrimaryDiagnostic>(), Array.Empty<PrimarySymbol>(), Array.Empty<RelatedTestDescriptor>(), false, null),
                    nextSteps),
                diagnostics,
                true);
        }

        var codeDiagnostics = await _workspaceBridge.GetDiagnosticsAsync(
                new DiagnosticsRequest
                {
                    Target = target,
                    IncludePathPatterns = auditScopes,
                    ExcludePathPatterns = normalizedExcludePatterns,
                    ChangedFiles = normalizedChangedFiles,
                    ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
                    MinimumSeverity = minimumSeverity,
                    NoiseProfile = noiseProfile,
                    MaxResults = maxDiagnostics,
                    MaxProjects = maxDiagnosticProjects,
                    MaxElapsedMilliseconds = maxDiagnosticElapsedMilliseconds,
                    IncludeGeneratedCode = includeGeneratedCode,
                },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(codeDiagnostics.Diagnostics);

        var symbolContextIsPartial = false;
        var symbols = await QueryAreaAuditSymbolsAsync(
                target,
                workspaceStatus,
                normalizedQueryTerms,
                auditScopes,
                projectName,
                maxSymbols,
                includeGeneratedCode,
                diagnostics,
                () => symbolContextIsPartial = true,
                cancellationToken)
            .ConfigureAwait(false);

        var relatedTestsArePartial = false;
        var relatedTests = await QueryAreaAuditRelatedTestsAsync(
                target,
                workspaceStatus,
                normalizedAreaPaths,
                normalizedChangedFiles,
                normalizedTestPatterns,
                maxRelatedTests,
                includeGeneratedCode,
                diagnostics,
                () => relatedTestsArePartial = true,
                cancellationToken)
            .ConfigureAwait(false);

        var markersArePartial = false;
        var markers = await QueryAreaAuditTemporaryMarkersAsync(
                target,
                workspaceStatus,
                normalizedAreaPaths,
                normalizedChangedFiles,
                projectName,
                maxMarkers,
                includeGeneratedCode,
                diagnostics,
                () => markersArePartial = true,
                cancellationToken)
            .ConfigureAwait(false);

        var taskContext = CreateTaskContextSummary(
            problemText,
            string.Join(", ", normalizedQueryTerms),
            null,
            projectName,
            normalizedChangedFiles,
            auditScopes,
            normalizedExcludePatterns,
            false,
            false,
            workspaceStatus,
            target);
        var primaryDiagnostics = CreatePrimaryDiagnostics(null, codeDiagnostics.Items);
        var primarySymbols = CreatePrimarySymbols(symbols, Array.Empty<SymbolDescriptor>());
        var primaryFiles = CreatePrimaryFiles(
                workspaceStatus,
                null,
                normalizedChangedFiles.Concat(normalizedAreaPaths).ToArray(),
                null,
                codeDiagnostics.Items,
                symbols,
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolReference>(),
                relatedTests)
            .Take(maxFiles)
            .ToArray();
        var findings = CreateAreaAuditFindings(primaryDiagnostics, primarySymbols, relatedTests, markers, maxFiles);
        var coveredBehaviors = CreateAreaAuditCoveredBehaviors(relatedTests, maxFiles);
        var coverageGaps = CreateAreaAuditCoverageGaps(
            primaryDiagnostics,
            primarySymbols,
            relatedTests,
            markers,
            normalizedQueryTerms,
            codeDiagnostics.IsPartial,
            maxFiles);
        var suggestedTests = CreateAreaAuditSuggestedTests(
            problemText,
            normalizedQueryTerms,
            primarySymbols,
            primaryDiagnostics,
            relatedTests,
            maxFiles);
        var candidateEditLocations = CreateAreaAuditCandidateEditLocations(
            primaryDiagnostics,
            primarySymbols,
            markers,
            maxFiles);
        if (findings.Length == 0)
        {
            nextSteps.Add("No focused audit evidence was found. Add narrower queryTerms, changedFiles, areaPaths, or build output before drawing conclusions.");
        }

        var recommendedNextActions = CreateAreaAuditRecommendedNextActions(
            "Ready",
            primaryDiagnostics,
            primarySymbols,
            relatedTests,
            codeDiagnostics.IsPartial,
            findings.FirstOrDefault());

        return Success(
            CreateAreaAuditReport(
                "Ready",
                workspaceStatus,
                taskContext,
                primaryFiles,
                primarySymbols,
                primaryDiagnostics,
                findings,
                coveredBehaviors,
                coverageGaps,
                suggestedTests,
                candidateEditLocations,
                codeDiagnostics.Items.ToArray(),
                symbols,
                relatedTests,
                markers,
                recommendedNextActions,
                nextSteps),
            diagnostics,
            instancesResult.IsPartial || workspaceResult.IsPartial || codeDiagnostics.IsPartial || symbolContextIsPartial || relatedTestsArePartial || markersArePartial);
    }
    [Description("Create a read-only semantic review package for changed C# files or a focused symbol, including impact, risks, test gaps, temporary markers, and verification next actions.")]
    public async Task<WorkspaceQueryResult<CSharpChangeReviewReport>> ReviewCSharpChange(
        [Description("Short problem statement or change summary for the review.")]
        string? problemText = null,
        [Description("Optional raw dotnet/MSBuild/Visual Studio build output text to triage.")]
        string? buildOutput = null,
        [Description("Optional path to a compact build log file. Used together with buildOutput when both are provided.")]
        string? buildLogFilePath = null,
        [Description("Changed C# files for current-task review focus.")]
        string[]? changedFiles = null,
        [Description("Optional area path patterns, directories, or files to review.")]
        string[]? areaPaths = null,
        [Description("Optional symbol name or partial name to review.")]
        string? symbolQuery = null,
        [Description("Optional Visual Studio project name for scoped diagnostics and marker scanning.")]
        string? projectName = null,
        [Description("Optional diagnostics include patterns.")]
        string[]? includePathPatterns = null,
        [Description("Optional diagnostics exclude patterns.")]
        string[]? excludePathPatterns = null,
        [Description("Optional minimum severity filter for diagnostics: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Maximum build issues to return from build-log triage.")]
        int maxBuildIssues = 20,
        [Description("Maximum Roslyn diagnostics to return.")]
        int maxDiagnostics = 30,
        [Description("Maximum symbol candidates to return.")]
        int maxSymbols = 20,
        [Description("Maximum references, impact groups, related tests, and markers to return.")]
        int maxRelatedItems = 20,
        [Description("Maximum primary files and findings to return.")]
        int maxFiles = 20,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInvestigationLimits(maxBuildIssues, maxDiagnostics, maxSymbols, maxRelatedItems, 20000);
        if (validation is not null)
        {
            return Failure<CSharpChangeReviewReport>(validation);
        }

        if (maxFiles is < 1 or > 200)
        {
            return Failure<CSharpChangeReviewReport>("MaxFiles must be between 1 and 200.");
        }

        var diagnostics = new List<string>();
        var nextSteps = new List<string>();
        var normalizedChangedFiles = NormalizePatterns(changedFiles);
        var normalizedAreaPaths = NormalizePatterns(areaPaths);
        var normalizedIncludePatterns = NormalizePatterns(includePathPatterns);
        var normalizedExcludePatterns = NormalizePatterns(excludePathPatterns);
        var hasExplicitBuildInput = HasExplicitBuildInput(buildOutput, buildLogFilePath);
        var buildTriage = TryAnalyzeBuildOutput(
            buildOutput,
            buildLogFilePath,
            normalizedAreaPaths.Concat(normalizedIncludePatterns).ToArray(),
            normalizedExcludePatterns,
            normalizedChangedFiles,
            maxBuildIssues,
            diagnostics);
        var reviewScopes = CreateAuditScopePatterns(normalizedAreaPaths, normalizedIncludePatterns, normalizedChangedFiles);
        if (reviewScopes.Length == 0 && string.IsNullOrWhiteSpace(symbolQuery) && buildTriage is null && string.IsNullOrWhiteSpace(projectName))
        {
            nextSteps.Add("Provide changedFiles, areaPaths, includePathPatterns, symbolQuery, projectName, or build output before reviewing a C# change.");
            return Success(
                CreateChangeReviewReport(
                    "NoScope",
                    null,
                    buildTriage,
                    CreateTaskContextSummary(problemText, symbolQuery, null, projectName, normalizedChangedFiles, reviewScopes, normalizedExcludePatterns, hasExplicitBuildInput, false, null, null),
                    Array.Empty<PrimaryFile>(),
                    Array.Empty<PrimarySymbol>(),
                    Array.Empty<PrimaryDiagnostic>(),
                    null,
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<CodeDiagnostic>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<RelatedTestDescriptor>(),
                    Array.Empty<TemporaryMarker>(),
                    CreateChangeReviewRecommendedNextActions("NoScope", null, Array.Empty<PrimaryDiagnostic>(), Array.Empty<PrimarySymbol>(), Array.Empty<RelatedTestDescriptor>(), null, Array.Empty<AreaAuditFinding>(), null),
                    nextSteps),
                diagnostics,
                false);
        }

        var instancesResult = await _workspaceBridge.ListVisualStudioInstancesAsync(
                new VisualStudioInstancesRequest { IncludeStale = true },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(instancesResult.Diagnostics);
        var activeInstances = instancesResult.Items.Where(instance => instance.IsAlive && !instance.IsStale).ToArray();
        var target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);
        if (activeInstances.Length == 0)
        {
            nextSteps.Add("Open Visual Studio with the target C# solution before running semantic change review.");
            var noBridgeReviewContext = CreateTaskContextSummary(problemText, symbolQuery, null, projectName, normalizedChangedFiles, reviewScopes, normalizedExcludePatterns, hasExplicitBuildInput, false, null, target);
            return Success(
                CreateChangeReviewReport(
                    "NoActiveBridge",
                    null,
                    buildTriage,
                    noBridgeReviewContext,
                    Array.Empty<PrimaryFile>(),
                    Array.Empty<PrimarySymbol>(),
                    Array.Empty<PrimaryDiagnostic>(),
                    null,
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<CodeDiagnostic>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<RelatedTestDescriptor>(),
                    Array.Empty<TemporaryMarker>(),
                    CreateChangeReviewRecommendedNextActions("NoActiveBridge", buildTriage, Array.Empty<PrimaryDiagnostic>(), Array.Empty<PrimarySymbol>(), Array.Empty<RelatedTestDescriptor>(), null, Array.Empty<AreaAuditFinding>(), noBridgeReviewContext),
                    nextSteps),
                diagnostics,
                true);
        }

        if (target is null && activeInstances.Length > 1)
        {
            nextSteps.Add("Pass targetInstanceId, targetPipeName, or targetSolutionPath before reviewing changes in a multi-Visual Studio session.");
            var ambiguousReviewContext = CreateTaskContextSummary(problemText, symbolQuery, null, projectName, normalizedChangedFiles, reviewScopes, normalizedExcludePatterns, hasExplicitBuildInput, false, null, null);
            return Success(
                CreateChangeReviewReport(
                    "AmbiguousTarget",
                    null,
                    buildTriage,
                    ambiguousReviewContext,
                    Array.Empty<PrimaryFile>(),
                    Array.Empty<PrimarySymbol>(),
                    Array.Empty<PrimaryDiagnostic>(),
                    null,
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<CodeDiagnostic>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<RelatedTestDescriptor>(),
                    Array.Empty<TemporaryMarker>(),
                    CreateChangeReviewRecommendedNextActions("AmbiguousTarget", buildTriage, Array.Empty<PrimaryDiagnostic>(), Array.Empty<PrimarySymbol>(), Array.Empty<RelatedTestDescriptor>(), null, Array.Empty<AreaAuditFinding>(), ambiguousReviewContext),
                    nextSteps),
                diagnostics,
                true);
        }

        target ??= new VisualStudioBridgeTarget
        {
            InstanceId = activeInstances[0].InstanceId,
            PipeName = activeInstances[0].PipeName,
            SolutionPath = activeInstances[0].SolutionPath,
        };

        var workspaceResult = await _workspaceBridge.GetWorkspaceStatusAsync(
                new WorkspaceStatusRequest { Target = target },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(workspaceResult.Diagnostics);
        var workspaceStatus = workspaceResult.Items.FirstOrDefault();
        if (workspaceStatus is null || !workspaceStatus.IsSolutionLoaded)
        {
            nextSteps.Add("Load a C# solution in Visual Studio before running semantic change review.");
            var unloadedReviewContext = CreateTaskContextSummary(problemText, symbolQuery, null, projectName, normalizedChangedFiles, reviewScopes, normalizedExcludePatterns, hasExplicitBuildInput, false, workspaceStatus, target);
            return Success(
                CreateChangeReviewReport(
                    "SolutionNotLoaded",
                    workspaceStatus,
                    buildTriage,
                    unloadedReviewContext,
                    Array.Empty<PrimaryFile>(),
                    Array.Empty<PrimarySymbol>(),
                    Array.Empty<PrimaryDiagnostic>(),
                    null,
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<AreaAuditFinding>(),
                    Array.Empty<CodeDiagnostic>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<RelatedTestDescriptor>(),
                    Array.Empty<TemporaryMarker>(),
                    CreateChangeReviewRecommendedNextActions("SolutionNotLoaded", buildTriage, Array.Empty<PrimaryDiagnostic>(), Array.Empty<PrimarySymbol>(), Array.Empty<RelatedTestDescriptor>(), null, Array.Empty<AreaAuditFinding>(), unloadedReviewContext),
                    nextSteps),
                diagnostics,
                true);
        }

        var codeDiagnostics = await GetInvestigationDiagnosticsAsync(
                target,
                null,
                reviewScopes,
                normalizedExcludePatterns,
                normalizedChangedFiles,
                projectName,
                minimumSeverity,
                noiseProfile,
                includeWholeSolutionDiagnostics: false,
                maxDiagnostics,
                includeGeneratedCode,
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(codeDiagnostics.Diagnostics);

        var symbols = Array.Empty<SymbolDescriptor>();
        SymbolImpactSummary? impactSummary = null;
        var reviewIsPartial = false;
        void MarkReviewPartial()
        {
            reviewIsPartial = true;
        }

        if (!string.IsNullOrWhiteSpace(symbolQuery))
        {
            var symbolResult = await _workspaceBridge.SearchSymbolsAsync(
                    new SymbolSearchRequest
                    {
                        Target = target,
                        QueryText = symbolQuery,
                        MaxResults = maxSymbols,
                        IncludeGeneratedCode = includeGeneratedCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(symbolResult.Diagnostics);
            reviewIsPartial |= symbolResult.IsPartial;
            symbols = symbolResult.Items.ToArray();
            if (symbols.Length == 1 && symbols[0].Key is not null)
            {
                impactSummary = await QuerySymbolImpactAsync(target, symbols[0].Key!, maxRelatedItems, includeGeneratedCode, diagnostics, MarkReviewPartial, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (symbols.Length > 1)
            {
                nextSteps.Add("Symbol query is ambiguous; narrow symbolQuery before trusting public API or impact conclusions.");
            }
        }

        var relatedTests = await QueryAreaAuditRelatedTestsAsync(
                target,
                workspaceStatus,
                normalizedAreaPaths,
                normalizedChangedFiles,
                Array.Empty<string>(),
                maxRelatedItems,
                includeGeneratedCode,
                diagnostics,
                MarkReviewPartial,
                cancellationToken)
            .ConfigureAwait(false);
        var markers = await QueryAreaAuditTemporaryMarkersAsync(
                target,
                workspaceStatus,
                normalizedAreaPaths,
                normalizedChangedFiles,
                projectName,
                maxRelatedItems,
                includeGeneratedCode,
                diagnostics,
                MarkReviewPartial,
                cancellationToken)
            .ConfigureAwait(false);

        var taskContext = CreateTaskContextSummary(problemText, symbolQuery, null, projectName, normalizedChangedFiles, reviewScopes, normalizedExcludePatterns, hasExplicitBuildInput, false, workspaceStatus, target);
        var primaryDiagnostics = CreatePrimaryDiagnostics(buildTriage, codeDiagnostics.Items);
        var primarySymbols = CreatePrimarySymbols(symbols, Array.Empty<SymbolDescriptor>());
        var primaryFiles = CreatePrimaryFiles(
                workspaceStatus,
                null,
                normalizedChangedFiles.Concat(normalizedAreaPaths).ToArray(),
                buildTriage,
                codeDiagnostics.Items,
                symbols,
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolReference>(),
                relatedTests)
            .Take(maxFiles)
            .ToArray();
        var findings = CreateAreaAuditFindings(primaryDiagnostics, primarySymbols, relatedTests, markers, maxFiles);
        var publicApiRisks = CreateChangeReviewPublicApiRisks(impactSummary, symbols, maxFiles);
        var testGaps = CreateAreaAuditCoverageGaps(primaryDiagnostics, primarySymbols, relatedTests, markers, string.IsNullOrWhiteSpace(symbolQuery) ? Array.Empty<string>() : new[] { symbolQuery! }, codeDiagnostics.IsPartial, maxFiles);
        var candidateEditLocations = CreateAreaAuditCandidateEditLocations(primaryDiagnostics, primarySymbols, markers, maxFiles);
        var recommendedNextActions = CreateChangeReviewRecommendedNextActions("Ready", buildTriage, primaryDiagnostics, primarySymbols, relatedTests, impactSummary, publicApiRisks, taskContext);

        return Success(
            CreateChangeReviewReport(
                "Ready",
                workspaceStatus,
                buildTriage,
                taskContext,
                primaryFiles,
                primarySymbols,
                primaryDiagnostics,
                impactSummary,
                findings,
                publicApiRisks,
                testGaps,
                candidateEditLocations,
                codeDiagnostics.Items.ToArray(),
                symbols,
                relatedTests,
                markers,
                recommendedNextActions,
                nextSteps),
            diagnostics,
            instancesResult.IsPartial || workspaceResult.IsPartial || codeDiagnostics.IsPartial || reviewIsPartial);
    }

    [Description("Run a read-only health check for the Visual Studio C# Navigator bridge, workspace target selection, and optional scoped diagnostics preview.")]
    public async Task<WorkspaceQueryResult<NavigatorHealthReport>> CheckVisualStudioCSharpNavigatorHealth(
        [Description("Whether to include a small Roslyn diagnostics preview after workspace selection succeeds.")]
        bool includeDiagnosticsPreview = false,
        [Description("Optional diagnostics include patterns used only when includeDiagnosticsPreview is true.")]
        string[]? includePathPatterns = null,
        [Description("Optional diagnostics exclude patterns used only when includeDiagnosticsPreview is true.")]
        string[]? excludePathPatterns = null,
        [Description("Optional changed files used only when includeDiagnosticsPreview is true. Matching diagnostics are ranked higher.")]
        string[]? changedFiles = null,
        [Description("Optional Visual Studio project name for diagnostics preview.")]
        string? projectName = null,
        [Description("Optional minimum severity filter for diagnostics preview: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled in the diagnostics preview: Auto, Penalize, Filter, or Off. Auto filters known noise only when no focused diagnostics scope is provided.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Maximum diagnostics preview items to return.")]
        int maxDiagnosticsPreview = 50,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxDiagnosticsPreview is < 1 or > 500)
        {
            return Failure<NavigatorHealthReport>("MaxDiagnosticsPreview must be between 1 and 500.");
        }

        var diagnostics = new List<string>();
        var nextSteps = new List<string>();
        var instancesResult = await _workspaceBridge.ListVisualStudioInstancesAsync(
                new VisualStudioInstancesRequest { IncludeStale = true },
                cancellationToken)
            .ConfigureAwait(false);

        diagnostics.AddRange(instancesResult.Diagnostics);
        var instances = instancesResult.Items.ToArray();
        var activeInstances = instances
            .Where(instance => instance.IsAlive && !instance.IsStale)
            .ToArray();
        var target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);

        if (activeInstances.Length == 0)
        {
            nextSteps.Add("Open Visual Studio with the target C# solution, or launch it from the skill setup flow.");
            nextSteps.Add("If Visual Studio is already open, check whether the VSIX is installed and discovery is not stale.");
            return Success(
                new NavigatorHealthReport
                {
                    Status = "NoActiveBridge",
                    ServerAssemblyVersion = GetServerAssemblyVersion(),
                    ExpectedBridgeProtocolVersion = VisualStudioBridgeInstanceDescriptor.ExpectedBridgeProtocolVersion,
                    Instances = instances,
                    SuggestedNextSteps = nextSteps.ToArray(),
                },
                diagnostics,
                instancesResult.IsPartial);
        }

        if (target is null && activeInstances.Length > 1)
        {
            nextSteps.Add("Pass targetInstanceId, targetPipeName, or targetSolutionPath before running workspace, diagnostics, or debug tools.");
            return Success(
                new NavigatorHealthReport
                {
                    Status = "AmbiguousTarget",
                    ServerAssemblyVersion = GetServerAssemblyVersion(),
                    ExpectedBridgeProtocolVersion = VisualStudioBridgeInstanceDescriptor.ExpectedBridgeProtocolVersion,
                    Instances = instances,
                    SuggestedNextSteps = nextSteps.ToArray(),
                },
                diagnostics,
                true);
        }

        target ??= new VisualStudioBridgeTarget
        {
            InstanceId = activeInstances[0].InstanceId,
            PipeName = activeInstances[0].PipeName,
            SolutionPath = activeInstances[0].SolutionPath,
        };

        var workspaceResult = await _workspaceBridge.GetWorkspaceStatusAsync(
                new WorkspaceStatusRequest { Target = target },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(workspaceResult.Diagnostics);
        var workspaceStatus = workspaceResult.Items.FirstOrDefault();

        var selectedInstance = FindSelectedHealthInstance(activeInstances, target);
        var hasBridgeVersionMismatch = AddBridgeVersionHealth(
            selectedInstance,
            diagnostics,
            nextSteps);

        CodeDiagnostic[] diagnosticsPreview = Array.Empty<CodeDiagnostic>();
        if (includeDiagnosticsPreview && workspaceStatus is not null)
        {
            var previewResult = await _workspaceBridge.GetDiagnosticsAsync(
                    new DiagnosticsRequest
                    {
                        Target = target,
                        IncludePathPatterns = NormalizePatterns(includePathPatterns),
                        ExcludePathPatterns = NormalizePatterns(excludePathPatterns),
                        ChangedFiles = NormalizePatterns(changedFiles),
                        ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
                        MinimumSeverity = minimumSeverity,
                        NoiseProfile = noiseProfile,
                        MaxResults = maxDiagnosticsPreview,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(previewResult.Diagnostics);
            diagnosticsPreview = previewResult.Items.ToArray();
        }

        if (workspaceStatus is null)
        {
            nextSteps.Add("The bridge was discovered, but workspace status could not be read. Check target selection and VSIX loading diagnostics.");
        }
        else if (!workspaceStatus.IsSolutionLoaded)
        {
            nextSteps.Add("Load a C# solution in Visual Studio before using semantic navigation tools.");
        }
        else
        {
            nextSteps.Add("Use targetInstanceId for subsequent C# Navigator calls in this session.");
            nextSteps.Add("For noisy large solutions, use includePathPatterns/excludePathPatterns or projectName for diagnostics.");
        }

        return Success(
            new NavigatorHealthReport
            {
                Status = hasBridgeVersionMismatch
                    ? "VersionMismatch"
                    : workspaceStatus is null
                    ? "WorkspaceStatusUnavailable"
                    : workspaceStatus.IsSolutionLoaded
                        ? "Ready"
                        : "SolutionNotLoaded",
                ServerAssemblyVersion = GetServerAssemblyVersion(),
                ExpectedBridgeProtocolVersion = VisualStudioBridgeInstanceDescriptor.ExpectedBridgeProtocolVersion,
                Instances = instances,
                WorkspaceStatus = workspaceStatus,
                DiagnosticsPreview = diagnosticsPreview,
                SuggestedNextSteps = nextSteps.ToArray(),
            },
            diagnostics,
            instancesResult.IsPartial || workspaceResult.IsPartial || hasBridgeVersionMismatch);
    }

    private static VisualStudioBridgeInstanceDescriptor? FindSelectedHealthInstance(
        IEnumerable<VisualStudioBridgeInstanceDescriptor> activeInstances,
        VisualStudioBridgeTarget target)
    {
        var instances = activeInstances.ToArray();
        if (!string.IsNullOrWhiteSpace(target.InstanceId))
        {
            var byInstance = instances.FirstOrDefault(instance => string.Equals(
                instance.InstanceId,
                target.InstanceId,
                StringComparison.OrdinalIgnoreCase));
            if (byInstance is not null)
            {
                return byInstance;
            }
        }

        if (!string.IsNullOrWhiteSpace(target.PipeName))
        {
            var byPipe = instances.FirstOrDefault(instance => string.Equals(
                instance.PipeName,
                target.PipeName,
                StringComparison.OrdinalIgnoreCase));
            if (byPipe is not null)
            {
                return byPipe;
            }
        }

        if (!string.IsNullOrWhiteSpace(target.SolutionPath))
        {
            var expectedPath = NormalizeHealthPath(target.SolutionPath);
            var bySolution = instances.FirstOrDefault(instance => string.Equals(
                NormalizeHealthPath(instance.SolutionPath),
                expectedPath,
                StringComparison.OrdinalIgnoreCase));
            if (bySolution is not null)
            {
                return bySolution;
            }
        }

        return instances.Length == 1 ? instances[0] : null;
    }

    private static bool AddBridgeVersionHealth(
        VisualStudioBridgeInstanceDescriptor? selectedInstance,
        ICollection<string> diagnostics,
        ICollection<string> nextSteps)
    {
        if (selectedInstance is null)
        {
            return false;
        }

        var expectedProtocol = VisualStudioBridgeInstanceDescriptor.ExpectedBridgeProtocolVersion;
        if (string.IsNullOrWhiteSpace(selectedInstance.BridgeProtocolVersion))
        {
            diagnostics.Add(
                $"BridgeVersionMetadataMissing: Visual Studio bridge '{selectedInstance.InstanceId}' did not advertise bridge protocol/version metadata. The VSIX may be older than the MCP server.");
            nextSteps.Add("If newer workspace/debug behavior is missing, install the packaged VSIX and restart Visual Studio.");
            return false;
        }

        if (!string.Equals(selectedInstance.BridgeProtocolVersion, expectedProtocol, StringComparison.Ordinal))
        {
            diagnostics.Add(
                $"BridgeProtocolMismatch: MCP server expects bridge protocol '{expectedProtocol}', but Visual Studio bridge '{selectedInstance.InstanceId}' advertises '{selectedInstance.BridgeProtocolVersion}'.");
            nextSteps.Add("Install the matching packaged VSIX, restart Visual Studio, and retry the health check before trusting semantic results.");
            return true;
        }

        return false;
    }

    private static string GetServerAssemblyVersion()
    {
        return typeof(CodeNavigationTools).Assembly.GetName().Version?.ToString() ?? string.Empty;
    }

    private static string NormalizeHealthPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (ArgumentException)
        {
            return path.Trim();
        }
        catch (NotSupportedException)
        {
            return path.Trim();
        }
        catch (PathTooLongException)
        {
            return path.Trim();
        }
    }

    public async Task<WorkspaceQueryResult<WorkspacePreparationReport>> PrepareCSharpWorkspace(
        string? rootDirectory = null,
        string? preferredName = null,
        int maxDepth = 4,
        int maxSolutions = 20,
        bool includeSlnx = true,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateFindSolutionsLimits(maxDepth, maxSolutions);
        if (validation is not null)
        {
            return Failure<WorkspacePreparationReport>(validation);
        }

        var diagnostics = new List<string>();
        var resolvedRoot = ResolveSolutionSearchRoot(rootDirectory, diagnostics);
        if (resolvedRoot is null)
        {
            return Failure<WorkspacePreparationReport>(
                $"Solution search root does not exist or is not a directory: {rootDirectory}");
        }

        var cacheKey = $"PrepareWorkspace|{resolvedRoot}|{preferredName}|{maxDepth}|{maxSolutions}|{includeSlnx}";
        var cached = await _queryCache.GetOrAddAsync(
                cacheKey,
                async () =>
                {
                    var localDiagnostics = new List<string>(diagnostics);
                    var solutionCandidates = FindSolutionCandidates(
                            resolvedRoot,
                            preferredName,
                            maxDepth,
                            includeSlnx,
                            maxSolutions,
                            localDiagnostics,
                            cancellationToken)
                        .ToArray();
                    var instancesResult = await _workspaceBridge.ListVisualStudioInstancesAsync(
                            new VisualStudioInstancesRequest { IncludeStale = false },
                            cancellationToken)
                        .ConfigureAwait(false);
                    localDiagnostics.AddRange(instancesResult.Diagnostics);
                    var activeInstances = instancesResult.Items.Where(instance => instance.IsAlive && !instance.IsStale).ToArray();
                    var report = CreateWorkspacePreparationReport(resolvedRoot, preferredName, solutionCandidates, activeInstances);
                    return Success(
                        report,
                        localDiagnostics,
                        instancesResult.IsPartial || localDiagnostics.Any(diagnostic => diagnostic.StartsWith("SolutionCandidatesTruncated:", StringComparison.OrdinalIgnoreCase)) || report.IsAmbiguous);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return cached.IsCacheHit
            ? WithCacheDiagnostic(cached.Value, "prepare_csharp_workspace")
            : cached.Value;
    }

    public async Task<WorkspaceQueryResult<VisualStudioSolutionOpenResult>> OpenCSharpSolutionInVisualStudio(
        string solutionPath,
        string? devenvPath = null,
        bool waitForBridge = true,
        int timeoutMilliseconds = 60000,
        int pollIntervalMilliseconds = 1000,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSolutionOpenRequest(
            solutionPath,
            timeoutMilliseconds,
            pollIntervalMilliseconds,
            out var normalizedSolutionPath);
        if (validation is not null)
        {
            return Failure<VisualStudioSolutionOpenResult>(validation);
        }

        var diagnostics = new List<string>();
        var existingInstances = await GetActiveInstancesForSolution(
                normalizedSolutionPath,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingInstances.Length == 1)
        {
            return Success(
                new VisualStudioSolutionOpenResult
                {
                    Status = "AlreadyOpen",
                    SolutionPath = normalizedSolutionPath,
                    MatchingInstances = existingInstances,
                    SelectedInstance = existingInstances[0],
                    SuggestedNextSteps = new[]
                    {
                        "Use selectedInstance.instanceId as targetInstanceId for subsequent C# workflow tools.",
                    },
                },
                diagnostics,
                isPartial: false);
        }

        if (existingInstances.Length > 1)
        {
            return Success(
                new VisualStudioSolutionOpenResult
                {
                    Status = "AmbiguousBridge",
                    SolutionPath = normalizedSolutionPath,
                    MatchingInstances = existingInstances,
                    SuggestedNextSteps = new[]
                    {
                        "Multiple Visual Studio instances already have this solution open. Use targetInstanceId or targetPipeName to disambiguate.",
                    },
                },
                diagnostics,
                isPartial: true);
        }

        var resolvedDevenvPath = ResolveDevenvPath(devenvPath);
        if (resolvedDevenvPath is null)
        {
            return Failure<VisualStudioSolutionOpenResult>(
                "Could not locate devenv.exe. Pass devenvPath explicitly or install Visual Studio with the core editor workload.");
        }

        VisualStudioLaunchResult launchResult;
        try
        {
            launchResult = _solutionLauncher.LaunchSolution(resolvedDevenvPath, normalizedSolutionPath);
        }
        catch (InvalidOperationException ex)
        {
            return Failure<VisualStudioSolutionOpenResult>($"Failed to launch Visual Studio: {ex.Message}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return Failure<VisualStudioSolutionOpenResult>($"Failed to launch Visual Studio: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(launchResult.Diagnostic))
        {
            diagnostics.Add(launchResult.Diagnostic);
        }

        diagnostics.AddRange(launchResult.Diagnostics);

        VisualStudioBridgeWaitReport? waitReport = null;
        if (waitForBridge)
        {
            var effectiveTimeoutMilliseconds = Math.Min(timeoutMilliseconds, MaxOpenSolutionBridgeWaitMilliseconds);
            if (effectiveTimeoutMilliseconds < timeoutMilliseconds)
            {
                diagnostics.Add($"OpenSolutionBridgeWaitCapped: requested {timeoutMilliseconds}ms; using {effectiveTimeoutMilliseconds}ms so the MCP client receives a diagnostic response before its outer timeout.");
            }

            waitReport = await WaitForVisualStudioBridgeCore(
                    normalizedSolutionPath,
                    effectiveTimeoutMilliseconds,
                    pollIntervalMilliseconds,
                    diagnostics,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var selectedInstance = waitReport?.SelectedInstance;
        var status = selectedInstance is not null
            ? "Ready"
            : string.Equals(waitReport?.Status, "TimedOut", StringComparison.OrdinalIgnoreCase)
                ? "LaunchedBridgeTimedOut"
                : "Launched";
        if (string.Equals(status, "LaunchedBridgeTimedOut", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.AddRange(_activityLogReader.ReadRecentIssues());
        }

        return Success(
            new VisualStudioSolutionOpenResult
            {
                Status = status,
                SolutionPath = normalizedSolutionPath,
                DevenvPath = resolvedDevenvPath,
                ProcessId = launchResult.ProcessId,
                Started = launchResult.ProcessId > 0,
                MatchingInstances = waitReport?.MatchingInstances ?? Array.Empty<VisualStudioBridgeInstanceDescriptor>(),
                SelectedInstance = selectedInstance,
                WaitReport = waitReport,
                SuggestedNextSteps = selectedInstance is not null
                    ? new[] { "Use selectedInstance.instanceId as targetInstanceId for subsequent C# workflow tools." }
                    : new[]
                    {
                        "If Visual Studio is still loading, call wait_for_visual_studio_bridge with this solutionPath.",
                        "If Visual Studio is open but no bridge appears, inspect Visual Studio ActivityLog.xml for VSIX package load errors.",
                    },
            },
            diagnostics,
            waitReport?.IsReady == false);
    }

    public async Task<WorkspaceQueryResult<VisualStudioBridgeWaitReport>> WaitForVisualStudioBridge(
        string solutionPath,
        int timeoutMilliseconds = 60000,
        int pollIntervalMilliseconds = 1000,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSolutionWaitRequest(
            solutionPath,
            timeoutMilliseconds,
            pollIntervalMilliseconds,
            out var normalizedSolutionPath);
        if (validation is not null)
        {
            return Failure<VisualStudioBridgeWaitReport>(validation);
        }

        var diagnostics = new List<string>();
        var report = await WaitForVisualStudioBridgeCore(
                normalizedSolutionPath,
                timeoutMilliseconds,
                pollIntervalMilliseconds,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);
        return Success(report, diagnostics, !report.IsReady);
    }
    [Description("Search C# symbols in the active Visual Studio solution and return file and line evidence.")]
    public async Task<WorkspaceQueryResult<SymbolDescriptor>> SearchCSharpSymbols(
        [Description("Symbol name or partial name to search for.")]
        string queryText,
        [Description("Maximum number of symbols to return.")]
        int maxResults = 50,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Failure<SymbolDescriptor>("Query text is required.");
        }

        if (maxResults is < 1 or > 500)
        {
            return Failure<SymbolDescriptor>("MaxResults must be between 1 and 500.");
        }

        var request = new SymbolSearchRequest
        {
            Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
            QueryText = queryText,
            MaxResults = maxResults,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        var cached = await _queryCache.GetOrAddAsync(
                CreateCacheKey("SearchSymbols", request),
                () => _workspaceBridge.SearchSymbolsAsync(request, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        return cached.IsCacheHit
            ? WithCacheDiagnostic(cached.Value, "search_csharp_symbols")
            : cached.Value;
    }
    [Description("Find references for a C# symbol identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpReferences(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Maximum number of references to return.")]
        int maxResults = 1000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSymbolReferenceLookup(symbolKey, filePath, line, column, maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<SymbolReference>(validation));
        }

        var request = CreateSymbolReferenceRequest(
            symbolKey,
            filePath,
            line,
            column,
            maxResults,
            includeGeneratedCode,
            CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath));

        return _workspaceBridge.FindReferencesAsync(request, cancellationToken);
    }
    [Description("Find references by first searching symbols and then using the unique matched symbol key. Returns candidate diagnostics when the search is ambiguous.")]
    public async Task<WorkspaceQueryResult<SymbolReference>> FindCSharpReferencesBySymbolSearch(
        [Description("Symbol name or partial name to search for before resolving references.")]
        string queryText,
        [Description("Optional containing type filter used to disambiguate symbol search results.")]
        string? containingType = null,
        [Description("Optional Visual Studio project name filter used to disambiguate symbol search results.")]
        string? projectName = null,
        [Description("Optional symbol kind filter used to disambiguate symbol search results.")]
        CodeSymbolKind? kind = null,
        [Description("Maximum symbol candidates to inspect before resolving references.")]
        int maxSymbolCandidates = 50,
        [Description("Maximum number of references to return after the selected symbol is resolved.")]
        int maxResults = 1000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Failure<SymbolReference>("Query text is required.");
        }

        if (maxSymbolCandidates is < 1 or > 500)
        {
            return Failure<SymbolReference>("MaxSymbolCandidates must be between 1 and 500.");
        }

        if (maxResults is < 1 or > 10000)
        {
            return Failure<SymbolReference>("MaxResults must be between 1 and 10000.");
        }

        var target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);
        var searchResult = await _workspaceBridge.SearchSymbolsAsync(
                new SymbolSearchRequest
                {
                    Target = target,
                    QueryText = queryText,
                    MaxResults = maxSymbolCandidates,
                    IncludeGeneratedCode = includeGeneratedCode,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var diagnostics = searchResult.Diagnostics.ToList();
        var candidates = FilterSymbolCandidates(
                searchResult.Items,
                queryText,
                containingType,
                projectName,
                kind)
            .ToArray();

        if (candidates.Length == 0)
        {
            diagnostics.Add("SymbolSearchNotFound: no symbol candidate matched the query and filters.");
            return new WorkspaceQueryResult<SymbolReference>
            {
                Items = Array.Empty<SymbolReference>(),
                Diagnostics = diagnostics.ToArray(),
                IsPartial = true,
            };
        }

        if (candidates.Length > 1)
        {
            diagnostics.Add("AmbiguousSymbolSearch: refine queryText, containingType, projectName, or kind.");
            diagnostics.AddRange(candidates.Take(10).Select(FormatSymbolCandidate));
            return new WorkspaceQueryResult<SymbolReference>
            {
                Items = Array.Empty<SymbolReference>(),
                Diagnostics = diagnostics.ToArray(),
                IsPartial = true,
            };
        }

        var selected = candidates[0];
        if (selected.Key is null || string.IsNullOrWhiteSpace(selected.Key.Value))
        {
            diagnostics.Add("SymbolSearchResultMissingKey: the selected symbol cannot be used for reference lookup.");
            return new WorkspaceQueryResult<SymbolReference>
            {
                Items = Array.Empty<SymbolReference>(),
                Diagnostics = diagnostics.ToArray(),
                IsPartial = true,
            };
        }

        var referencesResult = await _workspaceBridge.FindReferencesAsync(
                new SymbolReferenceRequest
                {
                    Target = target,
                    SymbolKey = selected.Key,
                    MaxResults = maxResults,
                    IncludeGeneratedCode = includeGeneratedCode,
                },
                cancellationToken)
            .ConfigureAwait(false);

        diagnostics.Add($"SelectedSymbol: {FormatSymbolCandidate(selected)}");
        diagnostics.AddRange(referencesResult.Diagnostics);
        return new WorkspaceQueryResult<SymbolReference>
        {
            Items = referencesResult.Items,
            Diagnostics = diagnostics.ToArray(),
            IsPartial = searchResult.IsPartial || referencesResult.IsPartial,
        };
    }
    [Description("Find definitions for a C# symbol identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolDescriptor>> FindCSharpDefinitions(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Maximum number of implementation results to return.")]
        int maxResults = 1000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasSymbolKeyOrPosition(symbolKey, filePath, line, column))
        {
            return Task.FromResult(Failure<SymbolDescriptor>(
                "Provide either a symbol key or a complete source position: filePath, line, and column."));
        }

        if (HasPartialPosition(filePath, line, column))
        {
            return Task.FromResult(Failure<SymbolDescriptor>(
                "Source position must include all three values: filePath, line, and column."));
        }

        if (HasInvalidPosition(line, column))
        {
            return Task.FromResult(Failure<SymbolDescriptor>("Line and column must be one-based positive integers."));
        }

        var request = CreateSymbolReferenceRequest(
            symbolKey,
            filePath,
            line,
            column,
            maxResults: 1000,
            includeGeneratedCode,
            CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath));

        return _workspaceBridge.FindDefinitionsAsync(request, cancellationToken);
    }
    [Description("Find C# symbols that implement an interface or interface member identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpImplementations(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Maximum number of implementation results to return.")]
        int maxResults = 1000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSymbolReferenceLookup(symbolKey, filePath, line, column, maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<SymbolReference>(validation));
        }

        var request = CreateSymbolReferenceRequest(
            symbolKey,
            filePath,
            line,
            column,
            maxResults,
            includeGeneratedCode,
            CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath));

        return _workspaceBridge.FindImplementationsAsync(request, cancellationToken);
    }
    [Description("Find C# members that override the specified member identified by symbol key or source position.")]
    public Task<WorkspaceQueryResult<SymbolReference>> FindCSharpOverrides(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Maximum number of override results to return.")]
        int maxResults = 1000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSymbolReferenceLookup(symbolKey, filePath, line, column, maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<SymbolReference>(validation));
        }

        var request = CreateSymbolReferenceRequest(
            symbolKey,
            filePath,
            line,
            column,
            maxResults,
            includeGeneratedCode,
            CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath));

        return _workspaceBridge.FindOverridesAsync(request, cancellationToken);
    }
    [Description("Describe a C# symbol with signature, accessibility, modifiers, containing type, base type, interfaces, attributes, and source evidence.")]
    public Task<WorkspaceQueryResult<SymbolDescription>> DescribeCSharpSymbol(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSymbolLookup(symbolKey, filePath, line, column);
        if (validation is not null)
        {
            return Task.FromResult(Failure<SymbolDescription>(validation));
        }

        var request = CreateSymbolDescriptionRequest(
            symbolKey,
            filePath,
            line,
            column,
            includeGeneratedCode,
            CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath));

        return _workspaceBridge.DescribeSymbolAsync(request, cancellationToken);
    }
    [Description("Return compact source snippets for a C# symbol declaration, including optional surrounding context and truncation metadata.")]
    public Task<WorkspaceQueryResult<SourceContextSnippet>> GetCSharpSymbolSource(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Number of source lines to include before and after each declaration.")]
        int contextLines = 3,
        [Description("Maximum characters returned per snippet.")]
        int maxChars = 12000,
        [Description("Maximum declaration snippets to return for partial symbols.")]
        int maxSnippets = 3,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSourceContextLookup(symbolKey, filePath, line, column, contextLines, maxChars, maxSnippets, requireSymbol: true);
        if (validation is not null)
        {
            return Task.FromResult(Failure<SourceContextSnippet>(validation));
        }

        return _workspaceBridge.GetSymbolSourceAsync(
            CreateSourceContextRequest(
                symbolKey,
                filePath,
                line,
                column,
                contextLines,
                maxChars,
                maxSnippets,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath)),
            cancellationToken);
    }
    [Description("Return compact C# symbol declaration source snippets for multiple symbol keys or source positions using one MCP call.")]
    public async Task<WorkspaceQueryResult<SourceContextSnippet>> BatchGetCSharpSymbolSources(
        [Description("Symbol or source-position requests. Each item should provide symbolKey or filePath/line/column.")]
        SourcePositionRequest[] symbols,
        [Description("Number of source lines to include before and after each declaration.")]
        int contextLines = 3,
        [Description("Maximum characters returned per symbol.")]
        int maxCharsPerSymbol = 12000,
        [Description("Maximum declaration snippets to return per symbol.")]
        int maxSnippetsPerSymbol = 3,
        [Description("Maximum number of symbol requests to inspect.")]
        int maxSymbols = 20,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateBatchSymbolSourceLookup(symbols, contextLines, maxCharsPerSymbol, maxSnippetsPerSymbol, maxSymbols);
        if (validation is not null)
        {
            return Failure<SourceContextSnippet>(validation);
        }

        var snippets = new List<SourceContextSnippet>();
        var diagnostics = new List<string>();
        var isPartial = false;
        var target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);

        foreach (var symbol in symbols.Take(maxSymbols))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _workspaceBridge.GetSymbolSourceAsync(
                    CreateSourceContextRequest(
                        symbol.SymbolKey,
                        symbol.FilePath,
                        symbol.Line,
                        symbol.Column,
                        contextLines,
                        maxCharsPerSymbol,
                        maxSnippetsPerSymbol,
                        includeGeneratedCode,
                        target),
                    cancellationToken)
                .ConfigureAwait(false);

            snippets.AddRange(result.Items);
            diagnostics.AddRange(result.Diagnostics.Select(diagnostic =>
                $"SymbolSource[{DescribeSourcePositionRequest(symbol)}]: {diagnostic}"));
            isPartial |= result.IsPartial;
        }

        if (symbols.Length > maxSymbols)
        {
            diagnostics.Add($"BatchSymbolSourcesTruncated: returning sources for {maxSymbols} of {symbols.Length} requested symbol(s).");
            isPartial = true;
        }

        return new WorkspaceQueryResult<SourceContextSnippet>
        {
            Items = snippets.ToArray(),
            Diagnostics = diagnostics.ToArray(),
            IsPartial = isPartial,
        };
    }
    [Description("Return the enclosing C# member or type source around a file position, with compact context and truncation metadata.")]
    public async Task<WorkspaceQueryResult<SourceContextSnippet>> GetCSharpSourceContext(
        [Description("Absolute source file path visible in the active Visual Studio solution.")]
        string filePath,
        [Description("One-based source line.")]
        int line,
        [Description("One-based source column.")]
        int column,
        [Description("Number of source lines to include before and after the enclosing source context.")]
        int contextLines = 3,
        [Description("Maximum characters returned.")]
        int maxChars = 12000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSourceContextLookup(
            symbolKey: null,
            filePath,
            line,
            column,
            contextLines,
            maxChars,
            maxSnippets: 1,
            requireSymbol: false);
        if (validation is not null)
        {
            return Failure<SourceContextSnippet>(validation);
        }

        var request = CreateSourceContextRequest(
                symbolKey: null,
                filePath,
                line,
                column,
                contextLines,
                maxChars,
                maxSnippets: 1,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath));
        var cached = await _queryCache.GetOrAddAsync(
                CreateCacheKey("GetSourceContext", request),
                () => _workspaceBridge.GetSourceContextAsync(request, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        return cached.IsCacheHit
            ? WithCacheDiagnostic(cached.Value, "get_csharp_source_context")
            : cached.Value;
    }
    [Description("Return compact enclosing C# source snippets for multiple file positions using one MCP call.")]
    public async Task<WorkspaceQueryResult<SourceContextSnippet>> BatchGetCSharpSourceContexts(
        [Description("Source positions to inspect. Each item needs absolute filePath plus one-based line and column.")]
        SourcePositionRequest[] positions,
        [Description("Number of source lines to include before and after each enclosing source context.")]
        int contextLines = 3,
        [Description("Maximum characters returned per position.")]
        int maxCharsPerPosition = 12000,
        [Description("Maximum number of positions to inspect.")]
        int maxPositions = 20,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateBatchSourceContextLookup(positions, contextLines, maxCharsPerPosition, maxPositions);
        if (validation is not null)
        {
            return Failure<SourceContextSnippet>(validation);
        }

        var snippets = new List<SourceContextSnippet>();
        var diagnostics = new List<string>();
        var isPartial = false;
        var target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);

        foreach (var position in positions.Take(maxPositions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _workspaceBridge.GetSourceContextAsync(
                    CreateSourceContextRequest(
                        symbolKey: null,
                        position.FilePath,
                        position.Line,
                        position.Column,
                        contextLines,
                        maxCharsPerPosition,
                        maxSnippets: 1,
                        includeGeneratedCode,
                        target),
                    cancellationToken)
                .ConfigureAwait(false);

            snippets.AddRange(result.Items);
            diagnostics.AddRange(result.Diagnostics.Select(diagnostic =>
                $"SourceContext[{position.FilePath}:{position.Line}:{position.Column}]: {diagnostic}"));
            isPartial |= result.IsPartial;
        }

        if (positions.Length > maxPositions)
        {
            diagnostics.Add($"BatchSourceContextsTruncated: returning contexts for {maxPositions} of {positions.Length} requested position(s).");
            isPartial = true;
        }

        return new WorkspaceQueryResult<SourceContextSnippet>
        {
            Items = snippets.ToArray(),
            Diagnostics = diagnostics.ToArray(),
            IsPartial = isPartial,
        };
    }
    [Description("List declared C# symbols in one document as a flat hierarchy with parent ids and source spans.")]
    public Task<WorkspaceQueryResult<DocumentSymbolNode>> ListCSharpDocumentSymbols(
        [Description("Absolute source file path visible in the active Visual Studio solution.")]
        string filePath,
        [Description("Maximum number of document symbols to return.")]
        int maxResults = 1000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult(Failure<DocumentSymbolNode>("FilePath is required."));
        }

        if (maxResults is < 1 or > 5000)
        {
            return Task.FromResult(Failure<DocumentSymbolNode>("MaxResults must be between 1 and 5000."));
        }

        return _workspaceBridge.ListDocumentSymbolsAsync(
            new DocumentSymbolsRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                FilePath = filePath,
                MaxResults = maxResults,
                IncludeGeneratedCode = includeGeneratedCode,
            },
            cancellationToken);
    }

    [Description("Return compiler and analyzer diagnostics from the active Visual Studio C# workspace, optionally scoped to a project or document.")]
    public async Task<WorkspaceQueryResult<CodeDiagnostic>> GetCSharpDiagnostics(
        [Description("Optional absolute source file path. When omitted, diagnostics are collected from matching projects or the whole solution.")]
        string? filePath = null,
        [Description("Optional file, directory, or wildcard patterns to include. Use this to focus diagnostics on changed files or touched folders.")]
        string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude. Use this to suppress known noisy generated or legacy folders.")]
        string[]? excludePathPatterns = null,
        [Description("Optional changed files from git diff or the current task. Matching diagnostics are ranked higher.")]
        string[]? changedFiles = null,
        [Description("Optional Visual Studio project name.")]
        string? projectName = null,
        [Description("Optional minimum severity filter: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = null,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off. Auto filters known noise only when no focused diagnostics scope is provided.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Maximum number of diagnostics to return.")]
        int maxResults = 500,
        [Description("Maximum number of C# projects to process before returning a partial result. Use 0 for no project-count cap.")]
        int maxProjects = 0,
        [Description("Maximum elapsed milliseconds for diagnostics collection before returning a partial result. Defaults below the bridge timeout so partial results are preserved.")]
        int maxElapsedMilliseconds = 45000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxResults is < 1 or > 5000)
        {
            return Failure<CodeDiagnostic>("MaxResults must be between 1 and 5000.");
        }

        if (maxProjects is < 0 or > 1000)
        {
            return Failure<CodeDiagnostic>("MaxProjects must be between 0 and 1000.");
        }

        if (maxElapsedMilliseconds is < 1000 or > 55000)
        {
            return Failure<CodeDiagnostic>("MaxElapsedMilliseconds must be between 1000 and 55000.");
        }

        var request = new DiagnosticsRequest
        {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath,
                IncludePathPatterns = NormalizePatterns(includePathPatterns),
                ExcludePathPatterns = NormalizePatterns(excludePathPatterns),
                ChangedFiles = NormalizePatterns(changedFiles),
                ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
                MinimumSeverity = minimumSeverity,
                NoiseProfile = noiseProfile,
                MaxResults = maxResults,
                MaxProjects = maxProjects,
                MaxElapsedMilliseconds = maxElapsedMilliseconds,
            IncludeGeneratedCode = includeGeneratedCode,
        };
        var cached = await _queryCache.GetOrAddAsync(
                CreateCacheKey("GetDiagnostics", request),
                () => _workspaceBridge.GetDiagnosticsAsync(request, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        return cached.IsCacheHit
            ? WithCacheDiagnostic(cached.Value, "get_csharp_diagnostics")
            : cached.Value;
    }

    [Description("Return the current Visual Studio Error List items through EnvDTE. This is VS UI context, not a replacement for build output or Roslyn diagnostics.")]
    public Task<WorkspaceQueryResult<VisualStudioErrorListItem>> GetVisualStudioErrorList(
        [Description("Maximum number of Error List items to return.")]
        int maxResults = 100,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxResults is < 1 or > 500)
        {
            return Task.FromResult(Failure<VisualStudioErrorListItem>("MaxResults must be between 1 and 500."));
        }

        return _workspaceBridge.GetErrorListAsync(
            new ErrorListRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                MaxResults = maxResults,
            },
            cancellationToken);
    }

    [Description("Return the tail text from a Visual Studio Output Window pane, usually Build. This is VS UI context and does not run a build.")]
    public Task<WorkspaceQueryResult<VisualStudioOutputWindowSnapshot>> GetVisualStudioOutputWindow(
        [Description("Output Window pane name. Defaults to Build.")]
        string paneName = "Build",
        [Description("Maximum number of trailing characters to return.")]
        int maxCharacters = 20000,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paneName))
        {
            return Task.FromResult(Failure<VisualStudioOutputWindowSnapshot>("PaneName is required."));
        }

        if (maxCharacters is < 1 or > 200000)
        {
            return Task.FromResult(Failure<VisualStudioOutputWindowSnapshot>("MaxCharacters must be between 1 and 200000."));
        }

        return _workspaceBridge.GetOutputWindowAsync(
            new OutputWindowRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                PaneName = paneName,
                MaxCharacters = maxCharacters,
            },
            cancellationToken);
    }

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
        var validation = ValidateVisualStudioDocumentsLookup(maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<VisualStudioDocumentSnapshot>(validation));
        }

        return _workspaceBridge.GetOpenDocumentsAsync(
            new VisualStudioDocumentsRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                MaxResults = maxResults,
                IncludeSelection = includeSelection,
            },
            cancellationToken);
    }

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
        return _workspaceBridge.GetActiveDocumentContextAsync(
            new VisualStudioDocumentsRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                MaxResults = 1,
                IncludeSelection = includeSelection,
            },
            cancellationToken);
    }

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
        var targetValidation = ValidateMutationTarget(targetPipeName, targetInstanceId, targetSolutionPath, "open_csharp_source_location");
        if (targetValidation is not null)
        {
            return Task.FromResult(Failure<SourceNavigationResult>(targetValidation));
        }

        var validation = ValidateSourceNavigation(filePath, line, column);
        if (validation is not null)
        {
            return Task.FromResult(Failure<SourceNavigationResult>(validation));
        }

        return _workspaceBridge.OpenSourceLocationAsync(
            new SourceNavigationRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                FilePath = filePath,
                Line = line,
                Column = column,
                Activate = activate,
            },
            cancellationToken);
    }
    [Description("Find C# callers of a symbol using Roslyn references and return call graph edges with source spans.")]
    public Task<WorkspaceQueryResult<CallGraphEdge>> FindCSharpCallers(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Maximum call graph depth. Use 1 for direct edges; larger values recursively expand callers.")]
        int maxDepth = 1,
        [Description("Maximum number of call graph edges to return.")]
        int maxResults = 100,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCallGraphLookup(symbolKey, filePath, line, column, maxDepth, maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CallGraphEdge>(validation));
        }

        return _workspaceBridge.FindCallersAsync(
            CreateCallGraphRequest(
                symbolKey,
                filePath,
                line,
                column,
                maxDepth,
                maxResults,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath)),
            cancellationToken);
    }
    [Description("Find C# callees used by symbol declaration bodies using Roslyn semantic analysis and return call graph edges with source spans.")]
    public Task<WorkspaceQueryResult<CallGraphEdge>> FindCSharpCallees(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Maximum call graph depth. Use 1 for direct edges; larger values recursively expand callees.")]
        int maxDepth = 1,
        [Description("Maximum number of call graph edges to return.")]
        int maxResults = 100,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCallGraphLookup(symbolKey, filePath, line, column, maxDepth, maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CallGraphEdge>(validation));
        }

        return _workspaceBridge.FindCalleesAsync(
            CreateCallGraphRequest(
                symbolKey,
                filePath,
                line,
                column,
                maxDepth,
                maxResults,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath)),
            cancellationToken);
    }
    [Description("Aggregate real Roslyn references for one C# symbol and summarize affected projects, files, roles, and containing types.")]
    public Task<WorkspaceQueryResult<SymbolImpactSummary>> AnalyzeCSharpSymbolImpact(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Maximum recursive caller depth to include in the impact aggregation. Use 1 for direct references only.")]
        int maxDepth = 1,
        [Description("Maximum number of references to aggregate before truncation.")]
        int maxResults = 1000,
        [Description("Maximum number of project groups to return.")]
        int maxProjects = 20,
        [Description("Maximum number of file groups to return.")]
        int maxFiles = 20,
        [Description("Maximum number of containing type groups to return.")]
        int maxContainingTypes = 20,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSymbolImpactLookup(
            symbolKey,
            filePath,
            line,
            column,
            maxDepth,
            maxResults,
            maxProjects,
            maxFiles,
            maxContainingTypes);
        if (validation is not null)
        {
            return Task.FromResult(Failure<SymbolImpactSummary>(validation));
        }

        return _workspaceBridge.AnalyzeSymbolImpactAsync(
            CreateSymbolImpactRequest(
                symbolKey,
                filePath,
                line,
                column,
                maxDepth,
                maxResults,
                maxProjects,
                maxFiles,
                maxContainingTypes,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath)),
            cancellationToken);
    }
    [Description("Find C# tests that directly reference or heuristically match a production symbol or source file.")]
    public Task<WorkspaceQueryResult<RelatedTestDescriptor>> FindCSharpRelatedTests(
        [Description("Stable symbol key returned from a previous symbol query. Optional when file path is provided.")]
        string? symbolKey = null,
        [Description("Source file path for file-level or position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Maximum number of related tests to return.")]
        int maxResults = 100,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateRelatedTestsLookup(symbolKey, filePath, line, column, maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<RelatedTestDescriptor>(validation));
        }

        return _workspaceBridge.FindRelatedTestsAsync(
            CreateRelatedTestsRequest(
                symbolKey,
                filePath,
                line,
                column,
                maxResults,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath)),
            cancellationToken);
    }

    [Description("Preview Roslyn C# symbol rename changes without applying edits to the workspace or files.")]
    public Task<WorkspaceQueryResult<RenamePreview>> PreviewCSharpRename(
        [Description("New C# identifier name for the target symbol.")]
        string newName,
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Whether Roslyn should rename overloads when supported for the symbol.")]
        bool renameOverloads = false,
        [Description("Whether Roslyn should rename matching text inside string literals.")]
        bool renameInStrings = false,
        [Description("Whether Roslyn should rename matching text inside comments.")]
        bool renameInComments = false,
        [Description("Whether Roslyn should include file rename metadata when supported. The MCP tool still only previews and never applies changes.")]
        bool renameFile = false,
        [Description("Maximum number of text changes to include in the preview.")]
        int maxTextChanges = 1000,
        [Description("Maximum old/new snippet length per text change.")]
        int maxSnippetLength = 200,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateRenamePreviewLookup(
            newName,
            symbolKey,
            filePath,
            line,
            column,
            maxTextChanges,
            maxSnippetLength);
        if (validation is not null)
        {
            return Task.FromResult(Failure<RenamePreview>(validation));
        }

        return _workspaceBridge.PreviewRenameAsync(
            CreateRenamePreviewRequest(
                newName,
                symbolKey,
                filePath,
                line,
                column,
                renameOverloads,
                renameInStrings,
                renameInComments,
                renameFile,
                maxTextChanges,
                maxSnippetLength,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath)),
            cancellationToken);
    }

    [Description("Apply a Roslyn C# symbol rename to the active Visual Studio workspace after safety checks. Requires an explicit target.")]
    public Task<WorkspaceQueryResult<RenameApplyResult>> ApplyCSharpRename(
        [Description("New C# identifier name for the target symbol.")]
        string newName,
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Whether Roslyn should rename overloads when supported for the symbol.")]
        bool renameOverloads = false,
        [Description("Whether Roslyn should rename matching text inside string literals.")]
        bool renameInStrings = false,
        [Description("Whether Roslyn should rename matching text inside comments.")]
        bool renameInComments = false,
        [Description("Whether Roslyn should include file rename metadata when supported.")]
        bool renameFile = false,
        [Description("Maximum number of text changes to include in the returned preview.")]
        int maxTextChanges = 1000,
        [Description("Maximum old/new snippet length per text change.")]
        int maxSnippetLength = 200,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Allow applying even when Roslyn reports rename conflicts.")]
        bool allowConflicts = false,
        [Description("Allow applying when generated document changes are included or omitted intentionally.")]
        bool allowGeneratedDocumentChanges = false,
        [Description("Allow applying when Roslyn produced added/removed document changes not represented as text diffs.")]
        bool allowUnsupportedDocumentChanges = false,
        [Description("Allow applying even when the returned preview was truncated by maxTextChanges.")]
        bool allowTruncatedPreview = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var targetValidation = ValidateMutationTarget(targetPipeName, targetInstanceId, targetSolutionPath, "apply_csharp_rename");
        if (targetValidation is not null)
        {
            return Task.FromResult(Failure<RenameApplyResult>(targetValidation));
        }

        var validation = ValidateRenamePreviewLookup(
            newName,
            symbolKey,
            filePath,
            line,
            column,
            maxTextChanges,
            maxSnippetLength);
        if (validation is not null)
        {
            return Task.FromResult(Failure<RenameApplyResult>(validation));
        }

        return _workspaceBridge.ApplyRenameAsync(
            CreateRenameApplyRequest(
                newName,
                symbolKey,
                filePath,
                line,
                column,
                renameOverloads,
                renameInStrings,
                renameInComments,
                renameFile,
                maxTextChanges,
                maxSnippetLength,
                includeGeneratedCode,
                allowConflicts,
                allowGeneratedDocumentChanges,
                allowUnsupportedDocumentChanges,
                allowTruncatedPreview,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath)),
            cancellationToken);
    }
    [Description("Find C# types that derive from or implement the specified type using Roslyn symbol analysis.")]
    public Task<WorkspaceQueryResult<DerivedTypeDescriptor>> FindCSharpDerivedTypes(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Whether to include transitive derived types. False returns only direct derivations when Roslyn can distinguish them.")]
        bool transitive = true,
        [Description("Maximum number of derived types to return.")]
        int maxResults = 1000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateDerivedTypesLookup(symbolKey, filePath, line, column, maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<DerivedTypeDescriptor>(validation));
        }

        return _workspaceBridge.FindDerivedTypesAsync(
            CreateDerivedTypesRequest(
                symbolKey,
                filePath,
                line,
                column,
                transitive,
                maxResults,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath)),
            cancellationToken);
    }
    [Description("Return base type chain, direct interfaces, and all interfaces for a C# type.")]
    public Task<WorkspaceQueryResult<InheritanceChain>> GetCSharpInheritanceChain(
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateSymbolLookup(symbolKey, filePath, line, column);
        if (validation is not null)
        {
            return Task.FromResult(Failure<InheritanceChain>(validation));
        }

        return _workspaceBridge.GetInheritanceChainAsync(
            CreateInheritanceChainRequest(
                symbolKey,
                filePath,
                line,
                column,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath)),
            cancellationToken);
    }
    [Description("Return C# project references, document counts, target frameworks, and metadata reference summaries from the active solution.")]
    public async Task<WorkspaceQueryResult<ProjectGraph>> GetCSharpProjectGraph(
        [Description("Optional Visual Studio project name. When omitted, all C# projects are returned.")]
        string? projectName = null,
        [Description("Maximum number of projects to return.")]
        int maxProjects = 500,
        [Description("Maximum number of metadata references to include per project.")]
        int maxMetadataReferencesPerProject = 50,
        [Description("Whether metadata reference summaries should be included.")]
        bool includeMetadataReferences = true,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxProjects is < 1 or > 1000)
        {
            return Failure<ProjectGraph>("MaxProjects must be between 1 and 1000.");
        }

        if (maxMetadataReferencesPerProject is < 0 or > 500)
        {
            return Failure<ProjectGraph>("MaxMetadataReferencesPerProject must be between 0 and 500.");
        }

        var request = new ProjectGraphRequest
        {
            Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
            MaxProjects = maxProjects,
            MaxMetadataReferencesPerProject = maxMetadataReferencesPerProject,
            IncludeMetadataReferences = includeMetadataReferences,
        };

        var cached = await _queryCache.GetOrAddAsync(
                CreateCacheKey("GetProjectGraph", request),
                () => _workspaceBridge.GetProjectGraphAsync(request, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        return cached.IsCacheHit
            ? WithCacheDiagnostic(cached.Value, "get_csharp_project_graph")
            : cached.Value;
    }

    [Description("Return the current Visual Studio debugger state, current process/thread, and current stack frame when available.")]
    public Task<WorkspaceQueryResult<DebugSessionStatus>> GetDebuggerStatus(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _workspaceBridge.GetDebuggerStatusAsync(
            new DebuggerStatusRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
            },
            cancellationToken);
    }

    [Description("Return the Visual Studio debugger call stack for the current or specified thread. Requires break mode.")]
    public Task<WorkspaceQueryResult<DebugStackFrameInfo>> GetDebugCallStack(
        [Description("Optional debugger thread id. When omitted, the current thread is used.")]
        int? threadId = null,
        [Description("Maximum stack frames to return.")]
        int maxFrames = 100,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (threadId is <= 0)
        {
            return Task.FromResult(Failure<DebugStackFrameInfo>("ThreadId must be a positive integer."));
        }

        if (maxFrames is < 1 or > 500)
        {
            return Task.FromResult(Failure<DebugStackFrameInfo>("MaxFrames must be between 1 and 500."));
        }

        return _workspaceBridge.GetDebugCallStackAsync(
            new DebugCallStackRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                ThreadId = threadId,
                MaxFrames = maxFrames,
            },
            cancellationToken);
    }

    [Description("Return arguments and locals for a debugger stack frame. Requires break mode.")]
    public Task<WorkspaceQueryResult<DebugVariableInfo>> GetDebugStackFrameVariables(
        [Description("Optional frame id returned by get_debug_call_stack. When omitted, the current frame is used.")]
        string? frameId = null,
        [Description("Maximum children to include per expandable variable.")]
        int maxChildren = 50,
        [Description("Maximum string length for variable values.")]
        int maxStringLength = 500,
        [Description("Whether private data members should be included when expanded by the debugger.")]
        bool includePrivate = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateDebugValueLimits(maxChildren, maxStringLength);
        if (validation is not null)
        {
            return Task.FromResult(Failure<DebugVariableInfo>(validation));
        }

        return _workspaceBridge.GetDebugStackFrameVariablesAsync(
            new DebugStackFrameVariablesRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                FrameId = string.IsNullOrWhiteSpace(frameId) ? null : frameId,
                MaxChildren = maxChildren,
                MaxStringLength = maxStringLength,
                IncludePrivate = includePrivate,
            },
            cancellationToken);
    }

    [Description("Evaluate an expression in the current Visual Studio debugger frame. Requires break mode; side effects must be explicitly allowed.")]
    public Task<WorkspaceQueryResult<DebugExpressionResult>> EvaluateDebugExpression(
        [Description("Expression to evaluate in the selected debugger stack frame.")]
        string expression,
        [Description("Optional frame id returned by get_debug_call_stack. When omitted, the current frame is used.")]
        string? frameId = null,
        [Description("Maximum children to include for expandable values.")]
        int maxChildren = 50,
        [Description("Maximum string length for expression values.")]
        int maxStringLength = 500,
        [Description("Whether evaluation may invoke property getters, ToString, or other debugger-side effects.")]
        bool allowSideEffects = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Task.FromResult(Failure<DebugExpressionResult>("Expression is required."));
        }

        var validation = ValidateDebugValueLimits(maxChildren, maxStringLength);
        if (validation is not null)
        {
            return Task.FromResult(Failure<DebugExpressionResult>(validation));
        }

        return _workspaceBridge.EvaluateDebugExpressionAsync(
            new DebugExpressionRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                Expression = expression,
                FrameId = string.IsNullOrWhiteSpace(frameId) ? null : frameId,
                MaxChildren = maxChildren,
                MaxStringLength = maxStringLength,
                AllowSideEffects = allowSideEffects,
            },
            cancellationToken);
    }

    public async Task<WorkspaceQueryResult<DebugExpressionResult>> BatchEvaluateDebugExpressions(
        string[] expressions,
        string? frameId = null,
        int maxChildren = 20,
        int maxStringLength = 500,
        bool allowSideEffects = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (expressions is null || expressions.Length is < 1 or > 20)
        {
            return Failure<DebugExpressionResult>("Expressions must contain between 1 and 20 items.");
        }

        if (expressions.Any(string.IsNullOrWhiteSpace))
        {
            return Failure<DebugExpressionResult>("Expressions must not contain empty items.");
        }

        var results = new List<DebugExpressionResult>();
        var diagnostics = new List<string>();
        var isPartial = false;
        foreach (var expression in expressions.Distinct(StringComparer.Ordinal))
        {
            var result = await EvaluateDebugExpression(
                    expression,
                    frameId,
                    maxChildren,
                    maxStringLength,
                    allowSideEffects,
                    targetPipeName,
                    targetInstanceId,
                    targetSolutionPath,
                    cancellationToken)
                .ConfigureAwait(false);
            results.AddRange(result.Items);
            diagnostics.AddRange(result.Diagnostics.Select(diagnostic => $"{expression}: {diagnostic}"));
            isPartial |= result.IsPartial;
        }

        return new WorkspaceQueryResult<DebugExpressionResult>
        {
            Items = results,
            Diagnostics = diagnostics,
            IsPartial = isPartial,
        };
    }

    [Description("List Visual Studio debugger threads. Requires an active debugging session.")]
    public Task<WorkspaceQueryResult<DebugThreadInfo>> ListDebugThreads(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _workspaceBridge.ListDebugThreadsAsync(
            new DebugThreadsRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
            },
            cancellationToken);
    }

    [Description("List Visual Studio breakpoints without mutating debugger state.")]
    public Task<WorkspaceQueryResult<DebugBreakpointInfo>> ListDebugBreakpoints(
        [Description("Maximum breakpoints to return.")]
        int maxResults = 500,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxResults is < 1 or > 5000)
        {
            return Task.FromResult(Failure<DebugBreakpointInfo>("MaxResults must be between 1 and 5000."));
        }

        return _workspaceBridge.ListDebugBreakpointsAsync(
            new DebugBreakpointsRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                MaxResults = maxResults,
            },
            cancellationToken);
    }

    [Description("Start debugging in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StartDebugging(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for the debugger state transition, in milliseconds. Use 0 to submit without waiting.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteDebugControl(
            DebugControlAction.Start,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.StartDebuggingAsync,
            cancellationToken);
    }

    [Description("Continue the paused debugger in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> ContinueDebugging(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for the debugger state transition, in milliseconds. Use 0 to submit without waiting.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteDebugControl(
            DebugControlAction.Continue,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.ContinueDebuggingAsync,
            cancellationToken);
    }

    [Description("Break into a running debugger session in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> BreakDebugging(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for break mode, in milliseconds. Use 0 to submit without waiting.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteDebugControl(
            DebugControlAction.Break,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.BreakDebuggingAsync,
            cancellationToken);
    }

    [Description("Stop the debugger session in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StopDebugging(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for design mode, in milliseconds. Use 0 to submit without waiting.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteDebugControl(
            DebugControlAction.Stop,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.StopDebuggingAsync,
            cancellationToken);
    }

    [Description("Step over the current debugger frame in the targeted Visual Studio instance. Requires break mode and an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StepOver(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for the step to return to break or design mode, in milliseconds. Use 0 to submit without waiting.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteDebugControl(
            DebugControlAction.StepOver,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.StepOverAsync,
            cancellationToken);
    }

    [Description("Step into from the current debugger frame in the targeted Visual Studio instance. Requires break mode and an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StepInto(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for the step to return to break or design mode, in milliseconds. Use 0 to submit without waiting.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteDebugControl(
            DebugControlAction.StepInto,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.StepIntoAsync,
            cancellationToken);
    }

    [Description("Step out from the current debugger frame in the targeted Visual Studio instance. Requires break mode and an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StepOut(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for the step to return to break or design mode, in milliseconds. Use 0 to submit without waiting.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteDebugControl(
            DebugControlAction.StepOut,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.StepOutAsync,
            cancellationToken);
    }

    [Description("Set a source breakpoint by absolute file path and line in the targeted Visual Studio instance. Requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> SetDebugBreakpoint(
        [Description("Absolute source file path for the breakpoint.")]
        string filePath,
        [Description("One-based source line for the breakpoint.")]
        int line,
        [Description("One-based source column for the breakpoint.")]
        int column = 1,
        [Description("Optional breakpoint condition.")]
        string? condition = null,
        [Description("Breakpoint condition mode. When condition is provided, defaults to WhenTrue.")]
        DebugBreakpointConditionMode conditionMode = DebugBreakpointConditionMode.WhenTrue,
        [Description("Optional breakpoint hit count target. Use with hitCountMode other than None.")]
        int hitCountTarget = 0,
        [Description("Breakpoint hit count mode.")]
        DebugBreakpointHitCountMode hitCountMode = DebugBreakpointHitCountMode.None,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for the mutation result, in milliseconds.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteBreakpointMutation(
            DebugControlAction.SetBreakpoint,
            filePath,
            line,
            column,
            breakpointName: null,
            enabled: true,
            condition,
            conditionMode,
            hitCountTarget,
            hitCountMode,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.SetDebugBreakpointAsync,
            cancellationToken);
    }

    [Description("Remove exactly one breakpoint by breakpoint name or absolute file path and line in the targeted Visual Studio instance. Requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> RemoveDebugBreakpoint(
        [Description("Optional breakpoint name returned by list_debug_breakpoints or set_debug_breakpoint.")]
        string? breakpointName = null,
        [Description("Optional absolute source file path. Required when breakpointName is omitted.")]
        string? filePath = null,
        [Description("Optional one-based source line. Required when breakpointName is omitted.")]
        int? line = null,
        [Description("Optional one-based source column used to disambiguate file/line matches.")]
        int? column = null,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for the mutation result, in milliseconds.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteBreakpointMutation(
            DebugControlAction.RemoveBreakpoint,
            filePath,
            line ?? 0,
            column ?? 0,
            breakpointName,
            enabled: false,
            condition: null,
            conditionMode: DebugBreakpointConditionMode.WhenTrue,
            hitCountTarget: 0,
            hitCountMode: DebugBreakpointHitCountMode.None,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.RemoveDebugBreakpointAsync,
            cancellationToken);
    }

    [Description("Enable or disable exactly one breakpoint by breakpoint name or absolute file path and line in the targeted Visual Studio instance. Requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> EnableDebugBreakpoint(
        [Description("Whether the matched breakpoint should be enabled.")]
        bool enabled,
        [Description("Optional breakpoint name returned by list_debug_breakpoints or set_debug_breakpoint.")]
        string? breakpointName = null,
        [Description("Optional absolute source file path. Required when breakpointName is omitted.")]
        string? filePath = null,
        [Description("Optional one-based source line. Required when breakpointName is omitted.")]
        int? line = null,
        [Description("Optional one-based source column used to disambiguate file/line matches.")]
        int? column = null,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        [Description("Maximum time to wait for the mutation result, in milliseconds.")]
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteBreakpointMutation(
            DebugControlAction.EnableBreakpoint,
            filePath,
            line ?? 0,
            column ?? 0,
            breakpointName,
            enabled,
            condition: null,
            conditionMode: DebugBreakpointConditionMode.WhenTrue,
            hitCountTarget: 0,
            hitCountMode: DebugBreakpointHitCountMode.None,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            timeoutMilliseconds,
            _workspaceBridge.EnableDebugBreakpointAsync,
            cancellationToken);
    }
    [Description("List generated C# documents visible in the active Visual Studio workspace, including path-based generated files and Roslyn source-generated documents when available.")]
    public Task<WorkspaceQueryResult<GeneratedDocumentDescriptor>> ListCSharpGeneratedDocuments(
        [Description("Optional Visual Studio project name. When omitted, all C# projects are scanned.")]
        string? projectName = null,
        [Description("Maximum generated documents to return.")]
        int maxResults = 1000,
        [Description("Whether Roslyn source-generated documents should be queried when the workspace exposes them.")]
        bool includeSourceGeneratedDocuments = true,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxResults is < 1 or > 10000)
        {
            return Task.FromResult(Failure<GeneratedDocumentDescriptor>("MaxResults must be between 1 and 10000."));
        }

        return _workspaceBridge.ListGeneratedDocumentsAsync(
            new GeneratedDocumentsRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
                MaxResults = maxResults,
                IncludeSourceGeneratedDocuments = includeSourceGeneratedDocuments,
            },
            cancellationToken);
    }
    [Description("Scan C# syntax trivia for temporary or simplified implementation markers such as TODO, FIXME, HACK, TEMP, WORKAROUND, 临时, and 简化.")]
    public Task<WorkspaceQueryResult<TemporaryMarker>> FindCSharpTemporaryMarkers(
        [Description("Optional absolute source file path. When omitted, matching C# documents are scanned.")]
        string? filePath = null,
        [Description("Optional Visual Studio project name.")]
        string? projectName = null,
        [Description("Maximum markers to return.")]
        int maxResults = 500,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxResults is < 1 or > 10000)
        {
            return Task.FromResult(Failure<TemporaryMarker>("MaxResults must be between 1 and 10000."));
        }

        return _workspaceBridge.FindTemporaryMarkersAsync(
            new TemporaryMarkersRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath,
                ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
                MaxResults = maxResults,
                IncludeGeneratedCode = includeGeneratedCode,
            },
            cancellationToken);
    }
    [Description("Return namespace/type/member/local-function/lambda context for a C# source position.")]
    public Task<WorkspaceQueryResult<EnclosingContext>> GetCSharpEnclosingContext(
        [Description("Absolute source file path visible in the active Visual Studio solution.")]
        string filePath,
        [Description("One-based source line.")]
        int line,
        [Description("One-based source column.")]
        int column,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult(Failure<EnclosingContext>("FilePath is required."));
        }

        if (line <= 0 || column <= 0)
        {
            return Task.FromResult(Failure<EnclosingContext>("Line and column must be one-based positive integers."));
        }

        return _workspaceBridge.GetEnclosingContextAsync(
            new EnclosingContextRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                Position = new SourceSpan
                {
                    FilePath = filePath,
                    StartLine = line,
                    StartColumn = column,
                    EndLine = line,
                    EndColumn = column,
                },
                IncludeGeneratedCode = includeGeneratedCode,
            },
            cancellationToken);
    }

    private static Task<WorkspaceQueryResult<DebugControlResult>> ExecuteDebugControl(
        DebugControlAction action,
        string? targetPipeName,
        string? targetInstanceId,
        string? targetSolutionPath,
        int timeoutMilliseconds,
        Func<DebugControlRequest, CancellationToken, Task<WorkspaceQueryResult<DebugControlResult>>> execute,
        CancellationToken cancellationToken)
    {
        var targetValidation = ValidateDebugControlTarget(targetPipeName, targetInstanceId, targetSolutionPath);
        if (targetValidation is not null)
        {
            return Task.FromResult(Failure<DebugControlResult>(targetValidation));
        }

        var timeoutValidation = ValidateDebugControlTimeout(timeoutMilliseconds);
        if (timeoutValidation is not null)
        {
            return Task.FromResult(Failure<DebugControlResult>(timeoutValidation));
        }

        return execute(
            new DebugControlRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                Action = action,
                TimeoutMilliseconds = timeoutMilliseconds,
            },
            cancellationToken);
    }

    private static Task<WorkspaceQueryResult<DebugControlResult>> ExecuteBreakpointMutation(
        DebugControlAction action,
        string? filePath,
        int line,
        int column,
        string? breakpointName,
        bool enabled,
        string? condition,
        DebugBreakpointConditionMode conditionMode,
        int hitCountTarget,
        DebugBreakpointHitCountMode hitCountMode,
        string? targetPipeName,
        string? targetInstanceId,
        string? targetSolutionPath,
        int timeoutMilliseconds,
        Func<DebugBreakpointMutationRequest, CancellationToken, Task<WorkspaceQueryResult<DebugControlResult>>> execute,
        CancellationToken cancellationToken)
    {
        var targetValidation = ValidateDebugControlTarget(targetPipeName, targetInstanceId, targetSolutionPath);
        if (targetValidation is not null)
        {
            return Task.FromResult(Failure<DebugControlResult>(targetValidation));
        }

        var timeoutValidation = ValidateDebugControlTimeout(timeoutMilliseconds);
        if (timeoutValidation is not null)
        {
            return Task.FromResult(Failure<DebugControlResult>(timeoutValidation));
        }

        var validation = ValidateBreakpointMutation(
            action,
            filePath,
            line,
            column,
            breakpointName,
            condition,
            conditionMode,
            hitCountTarget,
            hitCountMode);
        if (validation is not null)
        {
            return Task.FromResult(Failure<DebugControlResult>(validation));
        }

        return execute(
            new DebugBreakpointMutationRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                Action = action,
                FilePath = string.IsNullOrWhiteSpace(filePath) ? string.Empty : filePath,
                Line = line,
                Column = action == DebugControlAction.SetBreakpoint
                    ? (column <= 0 ? 1 : column)
                    : column,
                BreakpointName = string.IsNullOrWhiteSpace(breakpointName) ? string.Empty : breakpointName,
                Enabled = enabled,
                Condition = string.IsNullOrWhiteSpace(condition) ? string.Empty : condition,
                ConditionMode = conditionMode,
                HitCountTarget = hitCountTarget,
                HitCountMode = hitCountMode,
                TimeoutMilliseconds = timeoutMilliseconds,
            },
            cancellationToken);
    }

    private static string? ValidateDebugControlTarget(
        string? targetPipeName,
        string? targetInstanceId,
        string? targetSolutionPath)
    {
        return ValidateMutationTarget(targetPipeName, targetInstanceId, targetSolutionPath, "Debug Control tools");
    }

    private static string? ValidateMutationTarget(
        string? targetPipeName,
        string? targetInstanceId,
        string? targetSolutionPath,
        string toolName)
    {
        return string.IsNullOrWhiteSpace(targetPipeName)
            && string.IsNullOrWhiteSpace(targetInstanceId)
            && string.IsNullOrWhiteSpace(targetSolutionPath)
                ? $"{toolName} has side effects and requires an explicit targetPipeName, targetInstanceId, or targetSolutionPath."
                : null;
    }

    private static string? ValidateDebugControlTimeout(int timeoutMilliseconds)
    {
        return timeoutMilliseconds is < 0 or > 60000
            ? "TimeoutMilliseconds must be between 0 and 60000."
            : null;
    }

    private static string? ValidateBreakpointMutation(
        DebugControlAction action,
        string? filePath,
        int line,
        int column,
        string? breakpointName,
        string? condition,
        DebugBreakpointConditionMode conditionMode,
        int hitCountTarget,
        DebugBreakpointHitCountMode hitCountMode)
    {
        if (action == DebugControlAction.SetBreakpoint)
        {
            var sourceValidation = ValidateBreakpointSource(filePath, line, column, requireColumn: true);
            if (sourceValidation is not null)
            {
                return sourceValidation;
            }

            return ValidateBreakpointOptions(condition, conditionMode, hitCountTarget, hitCountMode);
        }

        if (!string.IsNullOrWhiteSpace(breakpointName))
        {
            return null;
        }

        return ValidateBreakpointSource(filePath, line, column, requireColumn: false);
    }

    private static string? ValidateBreakpointSource(
        string? filePath,
        int line,
        int column,
        bool requireColumn)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "Provide either breakpointName or an absolute filePath with line.";
        }

        if (!Path.IsPathRooted(filePath))
        {
            return "Breakpoint filePath must be an absolute path.";
        }

        if (line <= 0)
        {
            return "Breakpoint line must be a one-based positive integer.";
        }

        if ((requireColumn || column != 0) && column <= 0)
        {
            return "Breakpoint column must be a one-based positive integer.";
        }

        return null;
    }

    private static string? ValidateBreakpointOptions(
        string? condition,
        DebugBreakpointConditionMode conditionMode,
        int hitCountTarget,
        DebugBreakpointHitCountMode hitCountMode)
    {
        if (!Enum.IsDefined(typeof(DebugBreakpointConditionMode), conditionMode))
        {
            return "Breakpoint conditionMode is not supported.";
        }

        if (!Enum.IsDefined(typeof(DebugBreakpointHitCountMode), hitCountMode))
        {
            return "Breakpoint hitCountMode is not supported.";
        }

        if (!string.IsNullOrWhiteSpace(condition) && conditionMode == DebugBreakpointConditionMode.None)
        {
            return "Breakpoint conditionMode must be WhenTrue or WhenChanged when condition is provided.";
        }

        if (string.IsNullOrWhiteSpace(condition) && conditionMode == DebugBreakpointConditionMode.WhenChanged)
        {
            return "Breakpoint condition is required when conditionMode is WhenChanged.";
        }

        if (hitCountTarget < 0)
        {
            return "Breakpoint hitCountTarget cannot be negative.";
        }

        if (hitCountMode == DebugBreakpointHitCountMode.None)
        {
            return hitCountTarget == 0
                ? null
                : "Breakpoint hitCountTarget requires hitCountMode other than None.";
        }

        return hitCountTarget > 0
            ? null
            : "Breakpoint hitCountTarget must be a positive integer when hitCountMode is not None.";
    }

    private string? ResolveDevenvPath(string? devenvPath)
    {
        if (!string.IsNullOrWhiteSpace(devenvPath))
        {
            var normalized = NormalizeHealthPath(devenvPath);
            return File.Exists(normalized) ? normalized : null;
        }

        return _solutionLauncher.FindDevenvPath();
    }

    private async Task<VisualStudioBridgeWaitReport> WaitForVisualStudioBridgeCore(
        string solutionPath,
        int timeoutMilliseconds,
        int pollIntervalMilliseconds,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        VisualStudioBridgeInstanceDescriptor[] matchingInstances = Array.Empty<VisualStudioBridgeInstanceDescriptor>();

        while (true)
        {
            matchingInstances = await GetActiveInstancesForSolution(
                    solutionPath,
                    diagnostics,
                    cancellationToken)
                .ConfigureAwait(false);

            if (matchingInstances.Length == 1)
            {
                stopwatch.Stop();
                return new VisualStudioBridgeWaitReport
                {
                    Status = "Ready",
                    SolutionPath = solutionPath,
                    TimeoutMilliseconds = timeoutMilliseconds,
                    PollIntervalMilliseconds = pollIntervalMilliseconds,
                    ElapsedMilliseconds = (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue),
                    MatchingInstances = matchingInstances,
                    SelectedInstance = matchingInstances[0],
                    IsReady = true,
                    SuggestedNextSteps = new[]
                    {
                        "Use selectedInstance.instanceId as targetInstanceId for subsequent C# workflow tools.",
                    },
                };
            }

            if (matchingInstances.Length > 1)
            {
                stopwatch.Stop();
                return new VisualStudioBridgeWaitReport
                {
                    Status = "AmbiguousBridge",
                    SolutionPath = solutionPath,
                    TimeoutMilliseconds = timeoutMilliseconds,
                    PollIntervalMilliseconds = pollIntervalMilliseconds,
                    ElapsedMilliseconds = (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue),
                    MatchingInstances = matchingInstances,
                    IsAmbiguous = true,
                    SuggestedNextSteps = new[]
                    {
                        "Multiple Visual Studio instances have this solution open. Use targetInstanceId or targetPipeName to disambiguate.",
                    },
                };
            }

            if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
            {
                stopwatch.Stop();
                return new VisualStudioBridgeWaitReport
                {
                    Status = "TimedOut",
                    SolutionPath = solutionPath,
                    TimeoutMilliseconds = timeoutMilliseconds,
                    PollIntervalMilliseconds = pollIntervalMilliseconds,
                    ElapsedMilliseconds = (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue),
                    MatchingInstances = matchingInstances,
                    SuggestedNextSteps = new[]
                    {
                        "If Visual Studio is still loading, retry wait_for_visual_studio_bridge with a longer timeout.",
                        "If Visual Studio is open with this solution and discovery is absent, check whether the VSIX is installed and loaded.",
                    },
                };
            }

            await Task.Delay(pollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<VisualStudioBridgeInstanceDescriptor[]> GetActiveInstancesForSolution(
        string solutionPath,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var instancesResult = await _workspaceBridge.ListVisualStudioInstancesAsync(
                new VisualStudioInstancesRequest { IncludeStale = false },
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(instancesResult.Diagnostics);

        var normalizedSolutionPath = NormalizeHealthPath(solutionPath);
        return instancesResult.Items
            .Where(instance => instance.IsAlive && !instance.IsStale)
            .Where(instance => string.Equals(
                NormalizeHealthPath(instance.SolutionPath),
                normalizedSolutionPath,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string? ValidateSolutionOpenRequest(
        string? solutionPath,
        int timeoutMilliseconds,
        int pollIntervalMilliseconds,
        out string normalizedSolutionPath)
    {
        var waitValidation = ValidateSolutionWaitRequest(
            solutionPath,
            timeoutMilliseconds,
            pollIntervalMilliseconds,
            out normalizedSolutionPath);
        if (waitValidation is not null)
        {
            return waitValidation;
        }

        return File.Exists(normalizedSolutionPath)
            ? null
            : $"Solution file does not exist: {solutionPath}";
    }

    private static string? ValidateSolutionWaitRequest(
        string? solutionPath,
        int timeoutMilliseconds,
        int pollIntervalMilliseconds,
        out string normalizedSolutionPath)
    {
        normalizedSolutionPath = string.Empty;
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return "SolutionPath is required.";
        }

        normalizedSolutionPath = NormalizeHealthPath(solutionPath);
        if (!Path.IsPathRooted(normalizedSolutionPath))
        {
            return "SolutionPath must be an absolute path.";
        }

        var extension = Path.GetExtension(normalizedSolutionPath);
        if (!string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return "SolutionPath must point to a .sln or .slnx file.";
        }

        if (timeoutMilliseconds is < 0 or > 300000)
        {
            return "TimeoutMilliseconds must be between 0 and 300000.";
        }

        if (pollIntervalMilliseconds is < 100 or > 10000)
        {
            return "PollIntervalMilliseconds must be between 100 and 10000.";
        }

        return null;
    }

    private static VisualStudioBridgeTarget? CreateTarget(
        string? targetPipeName,
        string? targetInstanceId,
        string? targetSolutionPath)
    {
        if (string.IsNullOrWhiteSpace(targetPipeName)
            && string.IsNullOrWhiteSpace(targetInstanceId)
            && string.IsNullOrWhiteSpace(targetSolutionPath))
        {
            return null;
        }

        return new VisualStudioBridgeTarget
        {
            PipeName = string.IsNullOrWhiteSpace(targetPipeName) ? string.Empty : targetPipeName,
            InstanceId = string.IsNullOrWhiteSpace(targetInstanceId) ? string.Empty : targetInstanceId,
            SolutionPath = string.IsNullOrWhiteSpace(targetSolutionPath) ? string.Empty : targetSolutionPath,
        };
    }

    private static SymbolReferenceRequest CreateSymbolReferenceRequest(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxResults,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target)
    {
        var request = new SymbolReferenceRequest
        {
            Target = target,
            MaxResults = maxResults,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }

        return request;
    }

    private static SourceContextRequest CreateSourceContextRequest(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int contextLines,
        int maxChars,
        int maxSnippets,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target)
    {
        var request = new SourceContextRequest
        {
            Target = target,
            ContextLines = contextLines,
            MaxChars = maxChars,
            MaxSnippets = maxSnippets,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }

        return request;
    }

    private static string[] NormalizePatterns(string[]? patterns)
    {
        return patterns?
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .ToArray()
            ?? Array.Empty<string>();
    }

    private static string? ValidateInvestigationLimits(
        int maxBuildIssues,
        int maxDiagnostics,
        int maxSymbols,
        int maxRelatedItems,
        int maxVisualStudioBuildOutputCharacters)
    {
        if (maxBuildIssues is < 1 or > 500)
        {
            return "MaxBuildIssues must be between 1 and 500.";
        }

        if (maxDiagnostics is < 0 or > 500)
        {
            return "MaxDiagnostics must be between 0 and 500.";
        }

        if (maxSymbols is < 0 or > 500)
        {
            return "MaxSymbols must be between 0 and 500.";
        }

        if (maxRelatedItems is < 0 or > 500)
        {
            return "MaxRelatedItems must be between 0 and 500.";
        }

        if (maxVisualStudioBuildOutputCharacters is < 1 or > 200000)
        {
            return "MaxVisualStudioBuildOutputCharacters must be between 1 and 200000.";
        }

        return null;
    }

    private static string? ValidateTaskContextLimits(
        int maxBuildIssues,
        int maxDiagnostics,
        int maxSymbols,
        int maxRelatedItems,
        int maxVisualStudioBuildOutputCharacters,
        int maxSourceSnippets,
        int contextLines,
        int maxCharsPerSnippet)
    {
        var investigationValidation = ValidateInvestigationLimits(
            maxBuildIssues,
            maxDiagnostics,
            maxSymbols,
            maxRelatedItems,
            maxVisualStudioBuildOutputCharacters);
        if (investigationValidation is not null)
        {
            return investigationValidation;
        }

        if (maxSourceSnippets is < 0 or > 50)
        {
            return "MaxSourceSnippets must be between 0 and 50.";
        }

        if (contextLines is < 0 or > 50)
        {
            return "ContextLines must be between 0 and 50.";
        }

        if (maxCharsPerSnippet is < 1 or > 200000)
        {
            return "MaxCharsPerSnippet must be between 1 and 200000.";
        }

        return null;
    }

    private static string? ValidateFindSolutionsLimits(int maxDepth, int maxResults)
    {
        if (maxDepth is < 0 or > 12)
        {
            return "maxDepth must be between 0 and 12.";
        }

        if (maxResults is < 1 or > 200)
        {
            return "maxResults must be between 1 and 200.";
        }

        return null;
    }

    private static string? ValidateVerificationPlanLimits(
        int maxBuildIssues,
        int maxDiagnostics,
        int maxRelatedTests,
        int maxVisualStudioBuildOutputCharacters)
    {
        if (maxBuildIssues is < 1 or > 500)
        {
            return "MaxBuildIssues must be between 1 and 500.";
        }

        if (maxDiagnostics is < 0 or > 5000)
        {
            return "MaxDiagnostics must be between 0 and 5000.";
        }

        if (maxRelatedTests is < 0 or > 1000)
        {
            return "MaxRelatedTests must be between 0 and 1000.";
        }

        if (maxVisualStudioBuildOutputCharacters is < 1 or > 200000)
        {
            return "MaxVisualStudioBuildOutputCharacters must be between 1 and 200000.";
        }

        return null;
    }

    private static BuildTriageReport? TryAnalyzeBuildOutput(
        string? buildOutput,
        string? buildLogFilePath,
        string[]? includePathPatterns,
        string[]? excludePathPatterns,
        string[]? changedFiles,
        int maxBuildIssues,
        ICollection<string> diagnostics)
    {
        var combinedBuildOutput = ReadBuildOutputInput(buildOutput, buildLogFilePath, diagnostics, out var readFailure);
        if (readFailure is not null)
        {
            diagnostics.Add(readFailure);
            return null;
        }

        if (string.IsNullOrWhiteSpace(combinedBuildOutput))
        {
            return null;
        }

        var result = BuildLogTriage.Analyze(
            combinedBuildOutput,
            NormalizePatterns(includePathPatterns),
            NormalizePatterns(excludePathPatterns),
            NormalizePatterns(changedFiles),
            maxBuildIssues);
        foreach (var diagnostic in result.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        return result.Items.FirstOrDefault();
    }

    private static bool HasExplicitBuildInput(string? buildOutput, string? buildLogFilePath)
    {
        return !string.IsNullOrWhiteSpace(buildOutput)
            || !string.IsNullOrWhiteSpace(buildLogFilePath);
    }

    private async Task<BuildTriageReport?> TryAnalyzeVisualStudioBuildOutputAsync(
        VisualStudioBridgeTarget target,
        string[]? includePathPatterns,
        string[]? excludePathPatterns,
        string[]? changedFiles,
        int maxBuildIssues,
        int maxVisualStudioBuildOutputCharacters,
        ICollection<string> diagnostics,
        Action markPartial,
        CancellationToken cancellationToken)
    {
        var outputResult = await _workspaceBridge.GetOutputWindowAsync(
                new OutputWindowRequest
                {
                    Target = target,
                    PaneName = "Build",
                    MaxCharacters = maxVisualStudioBuildOutputCharacters,
                },
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var diagnostic in outputResult.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        if (outputResult.IsPartial)
        {
            markPartial();
        }

        var snapshot = outputResult.Items.FirstOrDefault();
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Text))
        {
            return null;
        }

        diagnostics.Add($"VisualStudioBuildOutputUsed: read {snapshot.ReturnedCharacters} trailing character(s) from the Visual Studio Output Window '{snapshot.PaneName}' pane for build triage.");
        if (snapshot.IsTruncated)
        {
            diagnostics.Add($"VisualStudioBuildOutputTruncated: Output Window '{snapshot.PaneName}' pane had {snapshot.TotalCharacters} character(s); triage used the last {snapshot.ReturnedCharacters}.");
            markPartial();
        }

        return TryAnalyzeBuildOutput(
            buildOutput: snapshot.Text,
            buildLogFilePath: null,
            includePathPatterns: includePathPatterns,
            excludePathPatterns: excludePathPatterns,
            changedFiles: changedFiles,
            maxBuildIssues: maxBuildIssues,
            diagnostics: diagnostics);
    }

    private static string? ReadBuildOutputInput(
        string? buildOutput,
        string? buildLogFilePath,
        ICollection<string> diagnostics,
        out string? failure)
    {
        failure = null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(buildOutput))
        {
            parts.Add(buildOutput);
        }

        if (!string.IsNullOrWhiteSpace(buildLogFilePath))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(buildLogFilePath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                failure = $"BuildLogFilePathInvalid: {ex.Message}";
                return null;
            }

            if (!File.Exists(fullPath))
            {
                failure = $"BuildLogFileNotFound: {fullPath}";
                return null;
            }

            try
            {
                parts.Add(File.ReadAllText(fullPath));
                diagnostics.Add($"BuildLogFileRead: {fullPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = $"BuildLogFileReadFailed: {ex.Message}";
                return null;
            }
        }

        return parts.Count == 0
            ? null
            : string.Join(Environment.NewLine, parts);
    }

    private static CSharpInvestigationReport CreateInvestigationReport(
        string status,
        NavigatorHealthReport health,
        BuildTriageReport? buildTriage,
        IEnumerable<string> nextSteps,
        TaskContextSummary? taskContext = null,
        WorkflowEvidenceLevel evidenceLevel = WorkflowEvidenceLevel.Unknown,
        RecommendedNextAction[]? recommendedNextActions = null)
    {
        return new CSharpInvestigationReport
        {
            Status = status,
            TaskContext = taskContext ?? new TaskContextSummary(),
            EvidenceLevel = evidenceLevel,
            Health = health,
            BuildTriage = buildTriage,
            RecommendedNextActions = recommendedNextActions ?? Array.Empty<RecommendedNextAction>(),
            SuggestedNextSteps = nextSteps.ToArray(),
        };
    }

    private static TaskContextSummary CreateTaskContextSummary(
        string? problemText,
        string? symbolQuery,
        string? filePath,
        string? projectName,
        string[] changedFiles,
        string[]? includePathPatterns,
        string[]? excludePathPatterns,
        bool hasExplicitBuildInput,
        bool usedVisualStudioBuildOutput,
        WorkspaceStatus? workspaceStatus,
        VisualStudioBridgeTarget? target)
    {
        return new TaskContextSummary
        {
            ProblemText = problemText?.Trim() ?? string.Empty,
            SymbolQuery = symbolQuery?.Trim() ?? string.Empty,
            FilePath = filePath?.Trim() ?? string.Empty,
            ProjectName = projectName?.Trim() ?? string.Empty,
            ChangedFiles = changedFiles,
            IncludePathPatterns = NormalizePatterns(includePathPatterns),
            ExcludePathPatterns = NormalizePatterns(excludePathPatterns),
            HasExplicitBuildInput = hasExplicitBuildInput,
            UsedVisualStudioBuildOutput = usedVisualStudioBuildOutput,
            BuildEvidenceSource = hasExplicitBuildInput
                ? "explicit-build-input"
                : usedVisualStudioBuildOutput
                    ? "visual-studio-build-output"
                    : "none",
            TargetInstanceId = target?.InstanceId ?? workspaceStatus?.InstanceId ?? string.Empty,
            TargetSolutionPath = target?.SolutionPath ?? workspaceStatus?.SolutionPath ?? string.Empty,
            EvidenceLevel = hasExplicitBuildInput || changedFiles.Length > 0 || !string.IsNullOrWhiteSpace(filePath)
                ? WorkflowEvidenceLevel.Fact
                : usedVisualStudioBuildOutput
                    ? WorkflowEvidenceLevel.Inference
                    : WorkflowEvidenceLevel.Unknown,
        };
    }

    private static VisualStudioBridgeTarget? CreateTaskContextTarget(
        CSharpInvestigationReport investigation,
        string? targetPipeName,
        string? targetInstanceId,
        string? targetSolutionPath)
    {
        var explicitTarget = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);
        if (explicitTarget is not null)
        {
            return explicitTarget;
        }

        var instanceId = investigation.TaskContext.TargetInstanceId;
        var solutionPath = investigation.TaskContext.TargetSolutionPath;
        var matchedInstance = investigation.Health.Instances.FirstOrDefault(instance =>
                instance.IsAlive
                && !instance.IsStale
                && !string.IsNullOrWhiteSpace(instanceId)
                && string.Equals(instance.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            ?? investigation.Health.Instances.FirstOrDefault(instance =>
                instance.IsAlive
                && !instance.IsStale
                && !string.IsNullOrWhiteSpace(solutionPath)
                && string.Equals(
                    NormalizeHealthPath(instance.SolutionPath),
                    NormalizeHealthPath(solutionPath),
                    StringComparison.OrdinalIgnoreCase));

        if (matchedInstance is not null)
        {
            return new VisualStudioBridgeTarget
            {
                InstanceId = matchedInstance.InstanceId,
                PipeName = matchedInstance.PipeName,
                SolutionPath = matchedInstance.SolutionPath,
            };
        }

        if (string.IsNullOrWhiteSpace(instanceId) && string.IsNullOrWhiteSpace(solutionPath))
        {
            return null;
        }

        return new VisualStudioBridgeTarget
        {
            InstanceId = instanceId,
            SolutionPath = solutionPath,
        };
    }

    private async Task<TaskContextSourceSnippetResult> CollectTaskContextSourceSnippetsAsync(
        CSharpInvestigationReport investigation,
        VisualStudioBridgeTarget? target,
        int maxSourceSnippets,
        int contextLines,
        int maxCharsPerSnippet,
        bool includeGeneratedCode,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (maxSourceSnippets == 0 || !string.Equals(investigation.Status, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return new TaskContextSourceSnippetResult(Array.Empty<SourceContextSnippet>(), isPartial: false);
        }

        var snippets = new List<SourceContextSnippet>();
        var seenSnippetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isPartial = false;

        foreach (var symbol in CreateTaskContextSymbolRequests(investigation))
        {
            if (snippets.Count >= maxSourceSnippets)
            {
                isPartial = true;
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = await _workspaceBridge.GetSymbolSourceAsync(
                    CreateSourceContextRequest(
                        symbol.SymbolKey,
                        symbol.FilePath,
                        symbol.Line,
                        symbol.Column,
                        contextLines,
                        maxCharsPerSnippet,
                        maxSnippets: 1,
                        includeGeneratedCode,
                        target),
                    cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(result.Diagnostics.Select(diagnostic =>
                $"TaskContextSymbolSource[{DescribeSourcePositionRequest(symbol)}]: {diagnostic}"));
            isPartial |= result.IsPartial;
            AddTaskContextSnippets(snippets, seenSnippetKeys, result.Items, maxSourceSnippets, ref isPartial);
        }

        foreach (var position in CreateTaskContextSourcePositions(investigation))
        {
            if (snippets.Count >= maxSourceSnippets)
            {
                isPartial = true;
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = await _workspaceBridge.GetSourceContextAsync(
                    CreateSourceContextRequest(
                        symbolKey: null,
                        position.FilePath,
                        position.Line,
                        position.Column,
                        contextLines,
                        maxCharsPerSnippet,
                        maxSnippets: 1,
                        includeGeneratedCode,
                        target),
                    cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(result.Diagnostics.Select(diagnostic =>
                $"TaskContextSourceContext[{DescribeSourcePositionRequest(position)}]: {diagnostic}"));
            isPartial |= result.IsPartial;
            AddTaskContextSnippets(snippets, seenSnippetKeys, result.Items, maxSourceSnippets, ref isPartial);
        }

        return new TaskContextSourceSnippetResult(snippets.ToArray(), isPartial);
    }

    private static IEnumerable<SourcePositionRequest> CreateTaskContextSymbolRequests(CSharpInvestigationReport investigation)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var primarySymbol in investigation.PrimarySymbols.OrderByDescending(symbol => symbol.RelevanceScore))
        {
            var symbol = primarySymbol.Symbol;
            if (symbol.Key is not null && seen.Add("key:" + symbol.Key.Value))
            {
                yield return new SourcePositionRequest
                {
                    SymbolKey = symbol.Key.Value,
                };
            }
            else if (primarySymbol.Span is not null)
            {
                var key = CreateSourcePositionKey(primarySymbol.Span);
                if (seen.Add(key))
                {
                    yield return CreateSourcePositionRequest(primarySymbol.Span);
                }
            }
        }
    }

    private static IEnumerable<SourcePositionRequest> CreateTaskContextSourcePositions(CSharpInvestigationReport investigation)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var diagnostic in investigation.PrimaryDiagnostics.OrderByDescending(diagnostic => diagnostic.RelevanceScore))
        {
            if (diagnostic.Span is not null && seen.Add(CreateSourcePositionKey(diagnostic.Span)))
            {
                yield return CreateSourcePositionRequest(diagnostic.Span);
            }
        }

        foreach (var file in investigation.PrimaryFiles.OrderByDescending(file => file.RelevanceScore))
        {
            if (file.Span is not null && seen.Add(CreateSourcePositionKey(file.Span)))
            {
                yield return CreateSourcePositionRequest(file.Span);
            }
        }
    }

    private static SourcePositionRequest CreateSourcePositionRequest(SourceSpan span)
    {
        return new SourcePositionRequest
        {
            FilePath = span.FilePath,
            Line = span.StartLine <= 0 ? 1 : span.StartLine,
            Column = span.StartColumn <= 0 ? 1 : span.StartColumn,
        };
    }

    private static string CreateSourcePositionKey(SourceSpan span)
    {
        return $"{span.FilePath}:{span.StartLine}:{span.StartColumn}";
    }

    private static void AddTaskContextSnippets(
        List<SourceContextSnippet> snippets,
        HashSet<string> seenSnippetKeys,
        IEnumerable<SourceContextSnippet> candidates,
        int maxSourceSnippets,
        ref bool isPartial)
    {
        foreach (var candidate in candidates)
        {
            var key = $"{candidate.FilePath}:{candidate.FocusSpan.StartLine}:{candidate.FocusSpan.StartColumn}:{candidate.ContextKind}";
            if (!seenSnippetKeys.Add(key))
            {
                continue;
            }

            if (snippets.Count >= maxSourceSnippets)
            {
                isPartial = true;
                break;
            }

            snippets.Add(candidate);
        }
    }

    private static string[] CreateTaskContextSnippetNextSteps(
        SourceContextSnippet[] snippets,
        int maxSourceSnippets)
    {
        if (maxSourceSnippets == 0)
        {
            return new[] { "Source snippets were disabled; call get_csharp_symbol_source or get_csharp_source_context for the selected edit target." };
        }

        return snippets.Length == 0
            ? new[] { "No source snippets were collected; inspect primary files or symbols with focused source-context tools before editing." }
            : new[] { "Use sourceSnippets as the first editing context; read the full file only when file-level structure or neighboring members are needed." };
    }

    private static WorkflowEvidenceLevel CreateEvidenceLevel(
        BuildTriageReport? buildTriage,
        IEnumerable<CodeDiagnostic> diagnostics,
        IEnumerable<SymbolDescriptor> symbols,
        TaskContextSummary taskContext)
    {
        if (buildTriage?.Issues.Length > 0 || diagnostics.Any())
        {
            return WorkflowEvidenceLevel.Fact;
        }

        if (taskContext.ChangedFiles.Length > 0 || !string.IsNullOrWhiteSpace(taskContext.FilePath))
        {
            return WorkflowEvidenceLevel.Fact;
        }

        return symbols.Any()
            ? WorkflowEvidenceLevel.Inference
            : WorkflowEvidenceLevel.Unknown;
    }

    private static PrimaryFile[] CreatePrimaryFiles(
        WorkspaceStatus workspaceStatus,
        string? requestedFilePath,
        string[] changedFiles,
        BuildTriageReport? buildTriage,
        IEnumerable<CodeDiagnostic> diagnostics,
        IEnumerable<SymbolDescriptor> symbols,
        IEnumerable<SymbolDescriptor> definitions,
        IEnumerable<SymbolReference> references,
        IEnumerable<RelatedTestDescriptor> relatedTests)
    {
        var files = new Dictionary<string, PrimaryFileAccumulator>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(requestedFilePath))
        {
            AddPrimaryFile(files, ResolvePathForWorkspace(workspaceStatus, requestedFilePath!), string.Empty, null, 100, WorkflowEvidenceLevel.Fact, "requested file");
        }

        foreach (var changedFile in changedFiles)
        {
            AddPrimaryFile(files, ResolvePathForWorkspace(workspaceStatus, changedFile), string.Empty, null, 95, WorkflowEvidenceLevel.Fact, $"changed file: {changedFile}");
        }

        if (buildTriage is not null)
        {
            foreach (var issue in buildTriage.Issues)
            {
                var score = issue.IsLikelyCascade ? 35 : 90;
                if (issue.IsInChangedFile)
                {
                    score += 20;
                }

                AddPrimaryFile(files, issue.Span?.FilePath, issue.ProjectName, issue.Span, score, WorkflowEvidenceLevel.Fact, $"build issue: {issue.Id}");
            }
        }

        foreach (var diagnostic in diagnostics)
        {
            AddPrimaryFile(files, diagnostic.Span?.FilePath, diagnostic.ProjectName, diagnostic.Span, 70 + diagnostic.RelevanceScore, WorkflowEvidenceLevel.Fact, $"diagnostic: {diagnostic.Id}");
        }

        foreach (var symbol in symbols.Concat(definitions))
        {
            AddPrimaryFile(files, symbol.Span?.FilePath, symbol.ProjectName, symbol.Span, 55, WorkflowEvidenceLevel.Inference, $"symbol: {symbol.Name}");
        }

        foreach (var reference in references)
        {
            AddPrimaryFile(files, reference.Span.FilePath, reference.Symbol.ProjectName, reference.Span, 40, WorkflowEvidenceLevel.Inference, $"symbol reference: {reference.Role}");
        }

        foreach (var test in relatedTests)
        {
            AddPrimaryFile(files, test.Span.FilePath, test.ProjectName, test.Span, 30, WorkflowEvidenceLevel.Heuristic, "related test");
        }

        return files.Values
            .Select(file => file.ToPrimaryFile())
            .OrderByDescending(file => file.RelevanceScore)
            .ThenBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
    }

    private static void AddPrimaryFile(
        IDictionary<string, PrimaryFileAccumulator> files,
        string? filePath,
        string projectName,
        SourceSpan? span,
        int score,
        WorkflowEvidenceLevel evidenceLevel,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var key = CreateFileKey(filePath);
        if (!files.TryGetValue(key, out var accumulator))
        {
            accumulator = new PrimaryFileAccumulator(filePath.Trim());
            files.Add(key, accumulator);
        }

        accumulator.Add(projectName, span, score, evidenceLevel, reason);
    }

    private static string CreateFileKey(string filePath)
    {
        try
        {
            return Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return filePath.Trim();
        }
    }

    private static bool MatchesPathPattern(string filePath, string pattern)
    {
        var normalizedPath = NormalizePathForMatching(filePath);
        var normalizedPattern = NormalizePathForMatching(pattern);
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            return false;
        }

        if (normalizedPattern.Contains('*') || normalizedPattern.Contains('?'))
        {
            var escapedPattern = Regex.Escape(normalizedPattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            var regex = IsRootedPathPattern(normalizedPattern)
                ? "^" + escapedPattern + "$"
                : "(^|.*/)" + escapedPattern + "$";
            return Regex.IsMatch(normalizedPath, regex, RegexOptions.IgnoreCase);
        }

        if (normalizedPath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var directoryPattern = normalizedPattern.EndsWith("/", StringComparison.Ordinal)
            ? normalizedPattern
            : normalizedPattern + "/";

        return normalizedPath.StartsWith(directoryPattern, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/" + directoryPattern, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForMatching(string path)
    {
        return path.Trim().Replace('\\', '/');
    }

    private static bool IsRootedPathPattern(string pattern)
    {
        return pattern.StartsWith("/", StringComparison.Ordinal)
            || (pattern.Length >= 2 && pattern[1] == ':');
    }

    private static PrimarySymbol[] CreatePrimarySymbols(
        IEnumerable<SymbolDescriptor> symbols,
        IEnumerable<SymbolDescriptor> definitions)
    {
        var items = new Dictionary<string, PrimarySymbolAccumulator>(StringComparer.OrdinalIgnoreCase);
        var symbolArray = symbols.ToArray();
        foreach (var symbol in symbolArray)
        {
            AddPrimarySymbol(
                items,
                symbol,
                symbolArray.Length == 1 ? 95 : 55,
                symbolArray.Length == 1 ? WorkflowEvidenceLevel.Fact : WorkflowEvidenceLevel.Inference,
                symbolArray.Length == 1 ? "unique symbol query match" : "symbol query candidate");
        }

        foreach (var definition in definitions)
        {
            AddPrimarySymbol(items, definition, 90, WorkflowEvidenceLevel.Fact, "definition");
        }

        return items.Values
            .Select(symbol => symbol.ToPrimarySymbol())
            .OrderByDescending(symbol => symbol.RelevanceScore)
            .ThenBy(symbol => symbol.Symbol.Name, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
    }

    private static void AddPrimarySymbol(
        IDictionary<string, PrimarySymbolAccumulator> symbols,
        SymbolDescriptor symbol,
        int score,
        WorkflowEvidenceLevel evidenceLevel,
        string reason)
    {
        var key = CreateSymbolKey(symbol);
        if (!symbols.TryGetValue(key, out var accumulator))
        {
            accumulator = new PrimarySymbolAccumulator(symbol);
            symbols.Add(key, accumulator);
        }

        accumulator.Add(score, evidenceLevel, reason);
    }

    private static string CreateSymbolKey(SymbolDescriptor symbol)
    {
        if (symbol.Key is not null)
        {
            return symbol.Key.Value;
        }

        return string.Join(
            "|",
            symbol.ProjectName,
            symbol.ContainingNamespace,
            symbol.ContainingType,
            symbol.Name,
            symbol.Kind.ToString(),
            symbol.Span?.FilePath ?? string.Empty,
            symbol.Span?.StartLine.ToString() ?? string.Empty);
    }

    private static PrimaryDiagnostic[] CreatePrimaryDiagnostics(
        BuildTriageReport? buildTriage,
        IEnumerable<CodeDiagnostic> diagnostics)
    {
        var items = new List<PrimaryDiagnostic>();
        if (buildTriage is not null)
        {
            foreach (var issue in buildTriage.Issues)
            {
                var reasons = new List<string> { "build issue" };
                reasons.AddRange(issue.RankReasons);
                if (issue.IsInChangedFile)
                {
                    reasons.Add("changed file");
                }

                if (issue.IsLikelyCascade)
                {
                    reasons.Add("likely cascade");
                }

                items.Add(new PrimaryDiagnostic
                {
                    Source = "build",
                    Id = issue.Id,
                    Severity = issue.Severity,
                    Message = issue.Message,
                    ProjectName = issue.ProjectName,
                    Span = issue.Span,
                    IsLikelyCascade = issue.IsLikelyCascade,
                    BaselineKind = issue.IsLikelyCascade
                        ? DiagnosticBaselineKind.Cascade
                        : issue.IsInChangedFile
                            ? DiagnosticBaselineKind.IntroducedByCurrentChange
                            : DiagnosticBaselineKind.PreExisting,
                    RelevanceScore = issue.RootCauseScore > 0
                        ? issue.RootCauseScore
                        : issue.IsLikelyCascade ? 35 : issue.IsInChangedFile ? 110 : 90,
                    EvidenceLevel = WorkflowEvidenceLevel.Fact,
                    BuildIssueKind = issue.Kind,
                    Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                });
            }
        }

        foreach (var diagnostic in diagnostics)
        {
            var isNoise = diagnostic.ScopeReasons.Any(reason => reason.Contains("noise", StringComparison.OrdinalIgnoreCase));
            items.Add(new PrimaryDiagnostic
            {
                Source = "diagnostic",
                Id = diagnostic.Id,
                Severity = diagnostic.Severity,
                Message = diagnostic.Message,
                ProjectName = diagnostic.ProjectName,
                Span = diagnostic.Span,
                IsBackgroundNoise = isNoise,
                BaselineKind = ClassifyDiagnosticBaseline(diagnostic, isNoise),
                RelevanceScore = diagnostic.RelevanceScore + (diagnostic.Severity == CodeDiagnosticSeverity.Error ? 25 : 0) - (isNoise ? 50 : 0),
                EvidenceLevel = isNoise ? WorkflowEvidenceLevel.Background : WorkflowEvidenceLevel.Fact,
                Reasons = diagnostic.ScopeReasons.Length == 0 ? new[] { "Roslyn diagnostic" } : diagnostic.ScopeReasons,
            });
        }

        return items
            .OrderByDescending(item => item.RelevanceScore)
            .ThenByDescending(item => item.Severity)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
    }

    private static DiagnosticBaselineKind ClassifyDiagnosticBaseline(CodeDiagnostic diagnostic, bool isNoise)
    {
        if (isNoise)
        {
            return DiagnosticBaselineKind.UnrelatedNoise;
        }

        if (diagnostic.ScopeReasons.Any(reason => reason.Contains("changed file", StringComparison.OrdinalIgnoreCase)))
        {
            return DiagnosticBaselineKind.IntroducedByCurrentChange;
        }

        return DiagnosticBaselineKind.PreExisting;
    }

    private static RecommendedNextAction[] CreateInvestigationRecommendedNextActions(
        string status,
        BuildTriageReport? buildTriage,
        IEnumerable<CodeDiagnostic> diagnostics,
        PrimaryFile[] primaryFiles,
        PrimarySymbol[] primarySymbols,
        PrimaryDiagnostic[] primaryDiagnostics,
        SymbolDescriptor[] symbols,
        SymbolImpactSummary? impactSummary,
        TaskContextSummary taskContext)
    {
        var actions = new List<RecommendedNextAction>();
        if (status == "NoActiveBridge" || status == "SolutionNotLoaded")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.NarrowScope,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = "high",
                Reason = "No loaded Visual Studio C# workspace is available for semantic investigation.",
                SuggestedCommand = string.IsNullOrWhiteSpace(taskContext.TargetSolutionPath)
                    ? string.Empty
                    : $"Start-Process -FilePath devenv.exe -ArgumentList {QuoteArgument(taskContext.TargetSolutionPath)}",
            });
        }

        if (status == "AmbiguousTarget")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.NarrowScope,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = "high",
                Reason = "Multiple Visual Studio bridge instances are active; select targetInstanceId, targetPipeName, or targetSolutionPath.",
                SuggestedTool = "list_visual_studio_instances",
            });
        }

        var firstRootCause = primaryDiagnostics.FirstOrDefault(diagnostic => !diagnostic.IsLikelyCascade && !diagnostic.IsBackgroundNoise);
        if (firstRootCause is not null)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectDiagnostic,
                EvidenceLevel = firstRootCause.EvidenceLevel,
                Confidence = firstRootCause.Source == "build" ? "high" : "medium",
                Reason = $"Inspect the top {firstRootCause.Source} diagnostic {firstRootCause.Id}.",
                SuggestedTool = firstRootCause.Span is null ? "analyze_csharp_build_errors" : "get_csharp_enclosing_context",
                TargetFilePath = firstRootCause.Span?.FilePath ?? string.Empty,
                TargetSpan = firstRootCause.Span,
                TargetProjectName = firstRootCause.ProjectName,
            });
        }

        if (buildTriage is not null)
        {
            actions.AddRange(buildTriage.RecommendedNextActions.Take(3));
        }

        if (symbols.Length > 1)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.NarrowScope,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Confidence = "high",
                Reason = "The symbol query is ambiguous; narrow by containing type, project, or file before expanding references.",
                SuggestedTool = "search_csharp_symbols",
            });
        }
        else if (primarySymbols.Length > 0)
        {
            var symbol = primarySymbols[0];
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectSymbol,
                EvidenceLevel = symbol.EvidenceLevel,
                Confidence = "high",
                Reason = "A primary symbol is available; inspect definition and limited references before editing.",
                SuggestedTool = "find_csharp_definitions",
                TargetSymbol = symbol.Symbol,
                TargetProjectName = symbol.Symbol.ProjectName,
                TargetSpan = symbol.Span,
                TargetFilePath = symbol.Span?.FilePath ?? string.Empty,
            });
        }

        if (impactSummary is not null && impactSummary.TotalReferences > 0)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectSymbol,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = impactSummary.HasCrossProjectImpact || impactSummary.DistinctFileCount > 3 ? "high" : "medium",
                Reason = impactSummary.HasCrossProjectImpact
                    ? "Symbol impact crosses project boundaries; inspect impact summary before editing."
                    : "Symbol impact summary is available; inspect affected files and containing types before editing.",
                SuggestedTool = "analyze_csharp_symbol_impact",
                TargetSymbol = impactSummary.Symbol,
                TargetProjectName = impactSummary.Symbol.ProjectName,
                TargetFilePath = impactSummary.Symbol.Span?.FilePath ?? string.Empty,
                TargetSpan = impactSummary.Symbol.Span,
            });
        }

        if (primaryFiles.Length > 0)
        {
            var file = primaryFiles[0];
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectFile,
                EvidenceLevel = file.EvidenceLevel,
                Confidence = "medium",
                Reason = string.Join("; ", file.Reasons.Take(3)),
                SuggestedTool = file.Span is null ? "list_csharp_document_symbols" : "get_csharp_enclosing_context",
                TargetFilePath = file.FilePath,
                TargetSpan = file.Span,
                TargetProjectName = file.ProjectName,
            });
        }

        if (buildTriage is null && !diagnostics.Any() && primarySymbols.Length == 0)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RequestBuildOutput,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "medium",
                Reason = "No build, diagnostic, or symbol evidence was found; provide build output or changed files to focus the next investigation.",
                SuggestedTool = "analyze_csharp_build_errors",
            });
        }

        actions.Add(new RecommendedNextAction
        {
            Kind = WorkflowActionKind.RunBuild,
            EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
            Confidence = "medium",
            Reason = "Use the verification planner to choose the smallest build/test command before running broad solution validation.",
            SuggestedTool = "plan_csharp_verification",
        });

        return actions
            .Where(action => action.Kind != WorkflowActionKind.Unknown)
            .GroupBy(action => string.Join("|", action.Kind.ToString(), action.SuggestedTool, action.TargetFilePath, action.TargetProjectName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToArray();
    }

    private sealed class PrimaryFileAccumulator
    {
        public PrimaryFileAccumulator(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; }

        public string ProjectName { get; set; } = string.Empty;

        public SourceSpan? Span { get; set; }

        public int Score { get; set; }

        public WorkflowEvidenceLevel EvidenceLevel { get; set; }

        public List<string> Reasons { get; } = new();

        public void Add(string projectName, SourceSpan? span, int score, WorkflowEvidenceLevel evidenceLevel, string reason)
        {
            if (string.IsNullOrWhiteSpace(ProjectName) && !string.IsNullOrWhiteSpace(projectName))
            {
                ProjectName = projectName;
            }

            Span ??= span;
            Score = Math.Max(Score, score);
            EvidenceLevel = StrongerEvidence(EvidenceLevel, evidenceLevel);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Reasons.Add(reason);
            }
        }

        public PrimaryFile ToPrimaryFile()
        {
            return new PrimaryFile
            {
                FilePath = FilePath,
                ProjectName = ProjectName,
                Span = Span,
                Score = Score,
                RelevanceScore = Score,
                EvidenceLevel = EvidenceLevel,
                Reasons = Reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            };
        }
    }

    private sealed class PrimarySymbolAccumulator
    {
        public PrimarySymbolAccumulator(SymbolDescriptor symbol)
        {
            Symbol = symbol;
            EvidenceLevel = WorkflowEvidenceLevel.Unknown;
        }

        public SymbolDescriptor Symbol { get; }

        public int Score { get; set; }

        public WorkflowEvidenceLevel EvidenceLevel { get; set; }

        public List<string> Reasons { get; } = new();

        public void Add(int score, WorkflowEvidenceLevel evidenceLevel, string reason)
        {
            Score = Math.Max(Score, score);
            EvidenceLevel = StrongerEvidence(EvidenceLevel, evidenceLevel);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Reasons.Add(reason);
            }
        }

        public PrimarySymbol ToPrimarySymbol()
        {
            return new PrimarySymbol
            {
                Symbol = Symbol,
                Span = Symbol.Span,
                Score = Score,
                RelevanceScore = Score,
                EvidenceLevel = EvidenceLevel,
                Reasons = Reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            };
        }
    }

    private static WorkflowEvidenceLevel StrongerEvidence(WorkflowEvidenceLevel left, WorkflowEvidenceLevel right)
    {
        if (left == WorkflowEvidenceLevel.Fact || right == WorkflowEvidenceLevel.Fact)
        {
            return WorkflowEvidenceLevel.Fact;
        }

        if (left == WorkflowEvidenceLevel.Inference || right == WorkflowEvidenceLevel.Inference)
        {
            return WorkflowEvidenceLevel.Inference;
        }

        if (left == WorkflowEvidenceLevel.Heuristic || right == WorkflowEvidenceLevel.Heuristic)
        {
            return WorkflowEvidenceLevel.Heuristic;
        }

        if (left == WorkflowEvidenceLevel.Background || right == WorkflowEvidenceLevel.Background)
        {
            return WorkflowEvidenceLevel.Background;
        }

        return WorkflowEvidenceLevel.Unknown;
    }

    private static CSharpVerificationPlan CreateVerificationPlan(
        string status,
        WorkspaceStatus? workspaceStatus,
        string[] changedFiles,
        BuildTriageReport? buildTriage,
        IEnumerable<string> nextSteps,
        TaskContextSummary? taskContext = null,
        WorkflowEvidenceLevel evidenceLevel = WorkflowEvidenceLevel.Unknown,
        RecommendedNextAction[]? recommendedNextActions = null)
    {
        return new CSharpVerificationPlan
        {
            Status = status,
            TaskContext = taskContext ?? new TaskContextSummary(),
            EvidenceLevel = evidenceLevel,
            WorkspaceStatus = workspaceStatus,
            ChangedFiles = changedFiles,
            BuildTriage = buildTriage,
            RecommendedNextActions = recommendedNextActions ?? Array.Empty<RecommendedNextAction>(),
            SuggestedNextSteps = nextSteps.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    private static RecommendedNextAction[] CreateVerificationRecommendedNextActions(
        string status,
        BuildTriageReport? buildTriage,
        CodeDiagnostic[] diagnostics,
        VerificationProject[] affectedProjects,
        VerificationCommand[] commands,
        TaskContextSummary taskContext,
        bool diagnosticsArePartial)
    {
        var actions = new List<RecommendedNextAction>();
        if (status == "AmbiguousTarget")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.SelectTarget,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = "high",
                Reason = "Multiple Visual Studio bridge instances are active; select a target before planning verification.",
                SuggestedTool = "list_visual_studio_instances",
            });
        }
        else if (status == "NoActiveBridge" || status == "SolutionNotLoaded")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.OpenVisualStudio,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = "high",
                Reason = "A loaded Visual Studio C# workspace is required for project graph and related-test verification planning.",
                SuggestedCommand = string.IsNullOrWhiteSpace(taskContext.TargetSolutionPath)
                    ? string.Empty
                    : $"Start-Process -FilePath devenv.exe -ArgumentList {QuoteArgument(taskContext.TargetSolutionPath)}",
            });
        }

        if (diagnostics.Length > 0)
        {
            var diagnostic = diagnostics
                .OrderByDescending(item => item.RelevanceScore)
                .ThenByDescending(item => item.Severity)
                .First();
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectDiagnostic,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = diagnostic.Severity == CodeDiagnosticSeverity.Error ? "high" : "medium",
                Reason = $"Inspect the top scoped diagnostic {diagnostic.Id} before running broad verification.",
                SuggestedTool = diagnostic.Span is null ? "get_csharp_diagnostics" : "get_csharp_enclosing_context",
                TargetFilePath = diagnostic.Span?.FilePath ?? string.Empty,
                TargetSpan = diagnostic.Span,
                TargetProjectName = diagnostic.ProjectName,
            });
        }

        if (diagnosticsArePartial)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.NarrowScope,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Confidence = "medium",
                Reason = "Diagnostics were partial; rerun diagnostics with a narrower filePath, projectName, includePathPatterns, maxProjects, or maxElapsedMilliseconds before trusting absence of errors.",
                SuggestedTool = "get_csharp_diagnostics",
            });
        }

        if (buildTriage is not null)
        {
            actions.AddRange(buildTriage.RecommendedNextActions.Take(3));
        }

        var command = commands
            .OrderBy(command => command.Scope.Equals("single-test", StringComparison.OrdinalIgnoreCase) ? 0
                : command.Scope.Equals("test-project", StringComparison.OrdinalIgnoreCase) ? 1
                : 2)
            .FirstOrDefault();
        if (command is not null)
        {
            var isTestCommand = command.Scope.Contains("test", StringComparison.OrdinalIgnoreCase);
            actions.Add(new RecommendedNextAction
            {
                Kind = isTestCommand ? WorkflowActionKind.RunTests : WorkflowActionKind.RunBuild,
                EvidenceLevel = taskContext.EvidenceLevel == WorkflowEvidenceLevel.Unknown
                    ? WorkflowEvidenceLevel.Heuristic
                    : taskContext.EvidenceLevel,
                Confidence = string.IsNullOrWhiteSpace(command.Confidence) ? "medium" : command.Confidence,
                Reason = $"Run the highest-ranked {command.Scope} verification command before broader validation.",
                TargetProjectName = affectedProjects.FirstOrDefault(project => !project.IsTestProject)?.ProjectName ?? string.Empty,
                SuggestedCommand = command.Command,
            });
        }
        else
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RunBuild,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "low",
                Reason = "No focused verification command could be derived; run a solution build and feed any failures back into build triage.",
                SuggestedCommand = string.IsNullOrWhiteSpace(taskContext.TargetSolutionPath)
                    ? "dotnet build"
                    : $"dotnet build {QuoteArgument(taskContext.TargetSolutionPath)} -v:minimal",
            });
        }

        return actions
            .Where(action => action.Kind != WorkflowActionKind.Unknown)
            .GroupBy(action => string.Join("|", action.Kind.ToString(), action.SuggestedTool, action.SuggestedCommand, action.TargetFilePath, action.TargetProjectName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToArray();
    }

    private static string? ValidateAreaAuditLimits(
        int maxDiagnostics,
        int maxSymbols,
        int maxRelatedTests,
        int maxMarkers,
        int maxFiles,
        int maxDiagnosticProjects,
        int maxDiagnosticElapsedMilliseconds)
    {
        if (maxDiagnostics is < 0 or > 5000)
        {
            return "MaxDiagnostics must be between 0 and 5000.";
        }

        if (maxSymbols is < 0 or > 500)
        {
            return "MaxSymbols must be between 0 and 500.";
        }

        if (maxRelatedTests is < 0 or > 500)
        {
            return "MaxRelatedTests must be between 0 and 500.";
        }

        if (maxMarkers is < 0 or > 500)
        {
            return "MaxMarkers must be between 0 and 500.";
        }

        if (maxFiles is < 1 or > 500)
        {
            return "MaxFiles must be between 1 and 500.";
        }

        if (maxDiagnosticProjects is < 0 or > 1000)
        {
            return "MaxDiagnosticProjects must be between 0 and 1000.";
        }

        return maxDiagnosticElapsedMilliseconds is < 1000 or > 55000
            ? "MaxDiagnosticElapsedMilliseconds must be between 1000 and 55000."
            : null;
    }

    private static string[] CreateAuditScopePatterns(
        string[] areaPaths,
        string[] includePathPatterns,
        string[] changedFiles)
    {
        return areaPaths
            .Concat(includePathPatterns)
            .Concat(changedFiles)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<SymbolDescriptor[]> QueryAreaAuditSymbolsAsync(
        VisualStudioBridgeTarget target,
        WorkspaceStatus workspaceStatus,
        string[] queryTerms,
        string[] auditScopes,
        string? projectName,
        int maxSymbols,
        bool includeGeneratedCode,
        ICollection<string> diagnostics,
        Action? markPartial,
        CancellationToken cancellationToken)
    {
        if (maxSymbols == 0 || queryTerms.Length == 0)
        {
            return Array.Empty<SymbolDescriptor>();
        }

        var symbols = new Dictionary<string, SymbolDescriptor>(StringComparer.OrdinalIgnoreCase);
        var perTermLimit = Math.Max(1, Math.Min(20, maxSymbols));
        foreach (var queryTerm in queryTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Take(20))
        {
            if (symbols.Count >= maxSymbols)
            {
                break;
            }

            var result = await _workspaceBridge.SearchSymbolsAsync(
                    new SymbolSearchRequest
                    {
                        Target = target,
                        QueryText = queryTerm,
                        MaxResults = perTermLimit,
                        IncludeGeneratedCode = includeGeneratedCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            AddDiagnostics(diagnostics, result.Diagnostics);
            if (result.IsPartial)
            {
                markPartial?.Invoke();
            }

            foreach (var symbol in result.Items)
            {
                if (!IsSymbolInAuditScope(symbol, workspaceStatus, auditScopes, projectName))
                {
                    continue;
                }

                symbols.TryAdd(CreateSymbolKey(symbol), symbol);
                if (symbols.Count >= maxSymbols)
                {
                    break;
                }
            }
        }

        return symbols.Values.ToArray();
    }

    private static bool IsSymbolInAuditScope(
        SymbolDescriptor symbol,
        WorkspaceStatus workspaceStatus,
        string[] auditScopes,
        string? projectName)
    {
        if (!string.IsNullOrWhiteSpace(projectName)
            && !string.Equals(symbol.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (auditScopes.Length == 0)
        {
            return true;
        }

        var filePath = symbol.Span?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var resolvedPath = ResolvePathForWorkspace(workspaceStatus, filePath);
        return auditScopes.Any(scope => MatchesPathPattern(resolvedPath, ResolvePathForWorkspace(workspaceStatus, scope)));
    }

    private async Task<RelatedTestDescriptor[]> QueryAreaAuditRelatedTestsAsync(
        VisualStudioBridgeTarget target,
        WorkspaceStatus workspaceStatus,
        string[] areaPaths,
        string[] changedFiles,
        string[] testPatterns,
        int maxRelatedTests,
        bool includeGeneratedCode,
        ICollection<string> diagnostics,
        Action? markPartial,
        CancellationToken cancellationToken)
    {
        if (maxRelatedTests == 0)
        {
            return Array.Empty<RelatedTestDescriptor>();
        }

        var relatedTests = new List<RelatedTestDescriptor>();
        foreach (var filePath in CreateAuditFileScopes(workspaceStatus, areaPaths.Concat(changedFiles)))
        {
            if (relatedTests.Count >= maxRelatedTests)
            {
                break;
            }

            var result = await _workspaceBridge.FindRelatedTestsAsync(
                    new RelatedTestsRequest
                    {
                        Target = target,
                        FilePath = filePath,
                        MaxResults = maxRelatedTests - relatedTests.Count,
                        IncludeGeneratedCode = includeGeneratedCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            AddDiagnostics(diagnostics, result.Diagnostics);
            if (result.IsPartial)
            {
                markPartial?.Invoke();
            }

            var filteredTests = result.Items.Where(test => MatchesTestPatterns(test, testPatterns));
            AddUniqueRelatedTests(relatedTests, filteredTests, maxRelatedTests);
        }

        return relatedTests.ToArray();
    }

    private async Task<TemporaryMarker[]> QueryAreaAuditTemporaryMarkersAsync(
        VisualStudioBridgeTarget target,
        WorkspaceStatus workspaceStatus,
        string[] areaPaths,
        string[] changedFiles,
        string? projectName,
        int maxMarkers,
        bool includeGeneratedCode,
        ICollection<string> diagnostics,
        Action? markPartial,
        CancellationToken cancellationToken)
    {
        if (maxMarkers == 0)
        {
            return Array.Empty<TemporaryMarker>();
        }

        var markers = new List<TemporaryMarker>();
        var fileScopes = CreateAuditFileScopes(workspaceStatus, areaPaths.Concat(changedFiles)).ToArray();
        if (fileScopes.Length == 0 && !string.IsNullOrWhiteSpace(projectName))
        {
            var projectResult = await _workspaceBridge.FindTemporaryMarkersAsync(
                    new TemporaryMarkersRequest
                    {
                        Target = target,
                        ProjectName = projectName,
                        MaxResults = maxMarkers,
                        IncludeGeneratedCode = includeGeneratedCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            AddDiagnostics(diagnostics, projectResult.Diagnostics);
            if (projectResult.IsPartial)
            {
                markPartial?.Invoke();
            }

            return projectResult.Items.Take(maxMarkers).ToArray();
        }

        foreach (var filePath in fileScopes)
        {
            if (markers.Count >= maxMarkers)
            {
                break;
            }

            var result = await _workspaceBridge.FindTemporaryMarkersAsync(
                    new TemporaryMarkersRequest
                    {
                        Target = target,
                        FilePath = filePath,
                        ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
                        MaxResults = maxMarkers - markers.Count,
                        IncludeGeneratedCode = includeGeneratedCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            AddDiagnostics(diagnostics, result.Diagnostics);
            if (result.IsPartial)
            {
                markPartial?.Invoke();
            }

            markers.AddRange(result.Items);
        }

        return markers.Take(maxMarkers).ToArray();
    }

    private static IEnumerable<string> CreateAuditFileScopes(
        WorkspaceStatus workspaceStatus,
        IEnumerable<string> paths)
    {
        return NormalizePatterns(paths.ToArray())
            .Select(path => ResolvePathForWorkspace(workspaceStatus, path))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesTestPatterns(RelatedTestDescriptor test, string[] testPatterns)
    {
        if (testPatterns.Length == 0)
        {
            return true;
        }

        var candidates = new[]
        {
            test.ProjectName,
            test.TestClass,
            test.TestMethod,
            test.Span.FilePath,
            test.TestSymbol.Name,
        };
        return testPatterns.Any(pattern => candidates.Any(candidate => !string.IsNullOrWhiteSpace(candidate) && MatchesPathPattern(candidate, pattern)));
    }

    private static AreaAuditFinding[] CreateAreaAuditFindings(
        PrimaryDiagnostic[] primaryDiagnostics,
        PrimarySymbol[] primarySymbols,
        RelatedTestDescriptor[] relatedTests,
        TemporaryMarker[] markers,
        int maxFindings)
    {
        var findings = new List<AreaAuditFinding>();
        findings.AddRange(primaryDiagnostics.Take(maxFindings).Select(diagnostic => new AreaAuditFinding
        {
            Category = "diagnostic",
            Title = diagnostic.Id,
            Summary = diagnostic.Message,
            EvidenceLevel = diagnostic.EvidenceLevel,
            Confidence = diagnostic.Severity == CodeDiagnosticSeverity.Error ? "high" : "medium",
            ProjectName = diagnostic.ProjectName,
            FilePath = diagnostic.Span?.FilePath ?? string.Empty,
            Span = diagnostic.Span,
            Reasons = diagnostic.Reasons,
        }));
        findings.AddRange(primarySymbols.Take(Math.Max(0, maxFindings - findings.Count)).Select(symbol => new AreaAuditFinding
        {
            Category = "symbol-candidate",
            Title = symbol.Symbol.Name,
            Summary = string.IsNullOrWhiteSpace(symbol.Symbol.ContainingType)
                ? $"{symbol.Symbol.Kind} in {symbol.Symbol.ProjectName}"
                : $"{symbol.Symbol.Kind} in {symbol.Symbol.ContainingType}",
            EvidenceLevel = symbol.EvidenceLevel,
            Confidence = "medium",
            ProjectName = symbol.Symbol.ProjectName,
            FilePath = symbol.Span?.FilePath ?? string.Empty,
            Span = symbol.Span,
            Reasons = symbol.Reasons,
        }));
        findings.AddRange(relatedTests.Take(Math.Max(0, maxFindings - findings.Count)).Select(test => new AreaAuditFinding
        {
            Category = "related-test",
            Title = string.IsNullOrWhiteSpace(test.TestMethod) ? test.TestClass : $"{test.TestClass}.{test.TestMethod}",
            Summary = "Related test evidence from symbol/file matching.",
            EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
            Confidence = "medium",
            ProjectName = test.ProjectName,
            FilePath = test.Span.FilePath,
            Span = test.Span,
            Reasons = test.MatchReasons.ToArray(),
        }));
        findings.AddRange(markers.Take(Math.Max(0, maxFindings - findings.Count)).Select(marker => new AreaAuditFinding
        {
            Category = "temporary-marker",
            Title = marker.Marker,
            Summary = marker.Text,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Confidence = "medium",
            ProjectName = marker.ProjectName,
            FilePath = marker.Span.FilePath,
            Span = marker.Span,
            Reasons = string.IsNullOrWhiteSpace(marker.EnclosingSymbol)
                ? Array.Empty<string>()
                : new[] { marker.EnclosingSymbol },
        }));

        return findings.Take(maxFindings).ToArray();
    }

    private static AreaAuditFinding[] CreateAreaAuditCoveredBehaviors(
        RelatedTestDescriptor[] relatedTests,
        int maxItems)
    {
        return relatedTests
            .Take(maxItems)
            .Select(test => new AreaAuditFinding
            {
                Category = "covered-behavior",
                Title = string.IsNullOrWhiteSpace(test.TestMethod) ? test.TestClass : $"{test.TestClass}.{test.TestMethod}",
                Summary = "Related test evidence suggests this behavior or area has existing test coverage. Treat this as heuristic until the test scenario is inspected.",
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "medium",
                ProjectName = test.ProjectName,
                FilePath = test.Span.FilePath,
                Span = test.Span,
                Reasons = test.MatchReasons.ToArray(),
            })
            .ToArray();
    }

    private static AreaAuditFinding[] CreateAreaAuditCoverageGaps(
        PrimaryDiagnostic[] primaryDiagnostics,
        PrimarySymbol[] primarySymbols,
        RelatedTestDescriptor[] relatedTests,
        TemporaryMarker[] markers,
        string[] queryTerms,
        bool diagnosticsArePartial,
        int maxItems)
    {
        var gaps = new List<AreaAuditFinding>();
        if (diagnosticsArePartial)
        {
            gaps.Add(new AreaAuditFinding
            {
                Category = "coverage-gap",
                Title = "Diagnostics incomplete",
                Summary = "Scoped diagnostics returned a partial result, so absence of compiler/analyzer issues is not proven.",
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Confidence = "medium",
                Reasons = new[] { "diagnostics partial" },
            });
        }

        if (relatedTests.Length == 0 && (primarySymbols.Length > 0 || primaryDiagnostics.Length > 0 || queryTerms.Length > 0))
        {
            gaps.Add(new AreaAuditFinding
            {
                Category = "coverage-gap",
                Title = "No related test evidence",
                Summary = "No related tests were found for the audited area. This may be a real test gap or a heuristic matching miss.",
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "medium",
                FilePath = primarySymbols.FirstOrDefault()?.Span?.FilePath
                    ?? primaryDiagnostics.FirstOrDefault()?.Span?.FilePath
                    ?? string.Empty,
                Span = primarySymbols.FirstOrDefault()?.Span ?? primaryDiagnostics.FirstOrDefault()?.Span,
                Reasons = queryTerms.Length == 0
                    ? new[] { "no related tests" }
                    : queryTerms.Select(term => $"query term: {term}").Concat(new[] { "no related tests" }).ToArray(),
            });
        }

        gaps.AddRange(markers.Take(Math.Max(0, maxItems - gaps.Count)).Select(marker => new AreaAuditFinding
        {
            Category = "coverage-gap",
            Title = $"Temporary marker: {marker.Marker}",
            Summary = marker.Text,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Confidence = "medium",
            ProjectName = marker.ProjectName,
            FilePath = marker.Span.FilePath,
            Span = marker.Span,
            Reasons = string.IsNullOrWhiteSpace(marker.EnclosingSymbol)
                ? new[] { "temporary marker" }
                : new[] { "temporary marker", marker.EnclosingSymbol },
        }));

        if (queryTerms.Length > 0 && primarySymbols.Length == 0)
        {
            gaps.Add(new AreaAuditFinding
            {
                Category = "coverage-gap",
                Title = "No symbol candidates for query terms",
                Summary = "Semantic symbol search found no candidates in the audited scope. Broaden areaPaths or use more specific C# symbol names.",
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Confidence = "medium",
                Reasons = queryTerms.Select(term => $"query term: {term}").ToArray(),
            });
        }

        return gaps.Take(maxItems).ToArray();
    }

    private static AreaAuditFinding[] CreateAreaAuditSuggestedTests(
        string? problemText,
        string[] queryTerms,
        PrimarySymbol[] primarySymbols,
        PrimaryDiagnostic[] primaryDiagnostics,
        RelatedTestDescriptor[] relatedTests,
        int maxItems)
    {
        if (relatedTests.Length > 0)
        {
            return Array.Empty<AreaAuditFinding>();
        }

        var titleSeed = primarySymbols.FirstOrDefault()?.Symbol.Name
            ?? primaryDiagnostics.FirstOrDefault()?.Id
            ?? queryTerms.FirstOrDefault()
            ?? "audited area";
        var file = primarySymbols.FirstOrDefault()?.Span?.FilePath
            ?? primaryDiagnostics.FirstOrDefault()?.Span?.FilePath
            ?? string.Empty;
        var span = primarySymbols.FirstOrDefault()?.Span ?? primaryDiagnostics.FirstOrDefault()?.Span;
        var summary = string.IsNullOrWhiteSpace(problemText)
            ? $"Add focused tests for {titleSeed}."
            : $"Add focused tests for: {problemText.Trim()}";

        return new[]
        {
            new AreaAuditFinding
            {
                Category = "suggested-test",
                Title = $"Add focused test for {titleSeed}",
                Summary = summary,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "medium",
                FilePath = file,
                Span = span,
                Reasons = queryTerms.Length == 0
                    ? new[] { "no related tests" }
                    : queryTerms.Select(term => $"query term: {term}").Concat(new[] { "no related tests" }).ToArray(),
            },
        }.Take(maxItems).ToArray();
    }

    private static AreaAuditFinding[] CreateAreaAuditCandidateEditLocations(
        PrimaryDiagnostic[] primaryDiagnostics,
        PrimarySymbol[] primarySymbols,
        TemporaryMarker[] markers,
        int maxItems)
    {
        var locations = new List<AreaAuditFinding>();
        locations.AddRange(primaryDiagnostics.Take(maxItems).Select(diagnostic => new AreaAuditFinding
        {
            Category = "candidate-edit-location",
            Title = diagnostic.Id,
            Summary = diagnostic.Message,
            EvidenceLevel = diagnostic.EvidenceLevel,
            Confidence = diagnostic.Severity == CodeDiagnosticSeverity.Error ? "high" : "medium",
            ProjectName = diagnostic.ProjectName,
            FilePath = diagnostic.Span?.FilePath ?? string.Empty,
            Span = diagnostic.Span,
            Reasons = diagnostic.Reasons,
        }));
        locations.AddRange(markers.Take(Math.Max(0, maxItems - locations.Count)).Select(marker => new AreaAuditFinding
        {
            Category = "candidate-edit-location",
            Title = $"Temporary marker: {marker.Marker}",
            Summary = marker.Text,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Confidence = "medium",
            ProjectName = marker.ProjectName,
            FilePath = marker.Span.FilePath,
            Span = marker.Span,
            Reasons = string.IsNullOrWhiteSpace(marker.EnclosingSymbol)
                ? new[] { "temporary marker" }
                : new[] { "temporary marker", marker.EnclosingSymbol },
        }));
        locations.AddRange(primarySymbols.Take(Math.Max(0, maxItems - locations.Count)).Select(symbol => new AreaAuditFinding
        {
            Category = "candidate-edit-location",
            Title = symbol.Symbol.Name,
            Summary = string.IsNullOrWhiteSpace(symbol.Symbol.ContainingType)
                ? $"{symbol.Symbol.Kind} in {symbol.Symbol.ProjectName}"
                : $"{symbol.Symbol.Kind} in {symbol.Symbol.ContainingType}",
            EvidenceLevel = symbol.EvidenceLevel,
            Confidence = "low",
            ProjectName = symbol.Symbol.ProjectName,
            FilePath = symbol.Span?.FilePath ?? string.Empty,
            Span = symbol.Span,
            Reasons = symbol.Reasons,
        }));

        return locations.Take(maxItems).ToArray();
    }

    private static AreaAuditFinding[] CreateChangeReviewPublicApiRisks(
        SymbolImpactSummary? impactSummary,
        SymbolDescriptor[] symbols,
        int maxItems)
    {
        var risks = new List<AreaAuditFinding>();
        if (impactSummary is not null && impactSummary.TotalReferences > 0)
        {
            risks.Add(new AreaAuditFinding
            {
                Category = "impact-risk",
                Title = impactSummary.HasCrossProjectImpact ? "Cross-project symbol impact" : "Symbol impact",
                Summary = $"Symbol has {impactSummary.TotalReferences} references across {impactSummary.DistinctFileCount} files and {impactSummary.DistinctProjectCount} projects.",
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = impactSummary.HasCrossProjectImpact || impactSummary.DistinctProjectCount > 1 ? "high" : "medium",
                ProjectName = impactSummary.Symbol.ProjectName,
                FilePath = impactSummary.Symbol.Span?.FilePath ?? string.Empty,
                Span = impactSummary.Symbol.Span,
                Reasons = new[]
                {
                    $"references: {impactSummary.TotalReferences}",
                    $"files: {impactSummary.DistinctFileCount}",
                    $"projects: {impactSummary.DistinctProjectCount}",
                },
            });
        }

        risks.AddRange(symbols
            .Where(symbol => symbol.Kind is CodeSymbolKind.Method or CodeSymbolKind.Property or CodeSymbolKind.Event or CodeSymbolKind.Type)
            .Take(Math.Max(0, maxItems - risks.Count))
            .Select(symbol => new AreaAuditFinding
            {
                Category = "public-api-candidate",
                Title = symbol.Name,
                Summary = string.IsNullOrWhiteSpace(symbol.ContainingType)
                    ? $"{symbol.Kind} candidate in {symbol.ProjectName}. Inspect declaration accessibility before changing signature or behavior."
                    : $"{symbol.Kind} candidate in {symbol.ContainingType}. Inspect declaration accessibility before changing signature or behavior.",
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "low",
                ProjectName = symbol.ProjectName,
                FilePath = symbol.Span?.FilePath ?? string.Empty,
                Span = symbol.Span,
                Reasons = new[] { "symbol candidate", "accessibility not resolved by review package" },
            }));

        return risks.Take(maxItems).ToArray();
    }

    private static RecommendedNextAction[] CreateChangeReviewRecommendedNextActions(
        string status,
        BuildTriageReport? buildTriage,
        PrimaryDiagnostic[] primaryDiagnostics,
        PrimarySymbol[] primarySymbols,
        RelatedTestDescriptor[] relatedTests,
        SymbolImpactSummary? impactSummary,
        AreaAuditFinding[] publicApiRisks,
        TaskContextSummary? taskContext)
    {
        var actions = new List<RecommendedNextAction>();
        if (status == "AmbiguousTarget")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.SelectTarget,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = "high",
                Reason = "Multiple Visual Studio bridge instances are active; select a target before reviewing changes.",
                SuggestedTool = "list_visual_studio_instances",
            });
        }
        else if (status == "NoActiveBridge" || status == "SolutionNotLoaded")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.OpenVisualStudio,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = "high",
                Reason = "A loaded Visual Studio C# workspace is required for semantic change review.",
                SuggestedCommand = taskContext is null || string.IsNullOrWhiteSpace(taskContext.TargetSolutionPath)
                    ? string.Empty
                    : $"Start-Process -FilePath devenv.exe -ArgumentList {QuoteArgument(taskContext.TargetSolutionPath)}",
            });
        }
        else if (status == "NoScope")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.NarrowScope,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "high",
                Reason = "Provide changedFiles, areaPaths, symbolQuery, projectName, or build output before reviewing changes.",
            });
        }

        var diagnostic = primaryDiagnostics.FirstOrDefault(item => !item.IsLikelyCascade && !item.IsBackgroundNoise);
        if (diagnostic is not null)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectDiagnostic,
                EvidenceLevel = diagnostic.EvidenceLevel,
                Confidence = diagnostic.Severity == CodeDiagnosticSeverity.Error ? "high" : "medium",
                Reason = $"Inspect top review diagnostic {diagnostic.Id}.",
                SuggestedTool = diagnostic.Span is null ? "analyze_csharp_build_errors" : "get_csharp_enclosing_context",
                TargetFilePath = diagnostic.Span?.FilePath ?? string.Empty,
                TargetSpan = diagnostic.Span,
                TargetProjectName = diagnostic.ProjectName,
            });
        }

        if (buildTriage is not null)
        {
            actions.AddRange(buildTriage.RecommendedNextActions.Take(3));
        }

        if (impactSummary is not null && impactSummary.TotalReferences > 0)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectSymbol,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = impactSummary.HasCrossProjectImpact ? "high" : "medium",
                Reason = "Inspect symbol impact before changing public behavior or signature.",
                SuggestedTool = "analyze_csharp_symbol_impact",
                TargetSymbol = impactSummary.Symbol,
                TargetProjectName = impactSummary.Symbol.ProjectName,
                TargetFilePath = impactSummary.Symbol.Span?.FilePath ?? string.Empty,
                TargetSpan = impactSummary.Symbol.Span,
            });
        }
        else if (primarySymbols.Length > 0)
        {
            var symbol = primarySymbols[0];
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectSymbol,
                EvidenceLevel = symbol.EvidenceLevel,
                Confidence = "medium",
                Reason = "Inspect the primary symbol before changing signatures, overrides, or behavior.",
                SuggestedTool = "describe_csharp_symbol",
                TargetSymbol = symbol.Symbol,
                TargetProjectName = symbol.Symbol.ProjectName,
                TargetFilePath = symbol.Span?.FilePath ?? string.Empty,
                TargetSpan = symbol.Span,
            });
        }

        if (relatedTests.Length == 0 && (primarySymbols.Length > 0 || primaryDiagnostics.Length > 0 || publicApiRisks.Length > 0))
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RunTests,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "low",
                Reason = "No related test evidence was found; use verification planning to identify the smallest reliable validation command.",
                SuggestedTool = "plan_csharp_verification",
            });
        }
        else if (relatedTests.Length > 0)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RunTests,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = relatedTests.Any(IsDirectReferenceRelatedTest) ? "high" : "low",
                Reason = relatedTests.Any(IsDirectReferenceRelatedTest)
                    ? "Direct related test evidence exists; use verification planning for the smallest runnable command."
                    : "Only heuristic related test evidence exists; prefer project-level verification before trusting single-test commands.",
                SuggestedTool = "plan_csharp_verification",
                TargetProjectName = relatedTests[0].ProjectName,
                TargetFilePath = relatedTests[0].Span.FilePath,
                TargetSpan = relatedTests[0].Span,
            });
        }

        return actions
            .Where(action => action.Kind != WorkflowActionKind.Unknown)
            .GroupBy(action => string.Join("|", action.Kind.ToString(), action.SuggestedTool, action.SuggestedCommand, action.TargetFilePath, action.TargetProjectName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToArray();
    }

    private static RecommendedNextAction[] CreateAreaAuditRecommendedNextActions(
        string status,
        PrimaryDiagnostic[] primaryDiagnostics,
        PrimarySymbol[] primarySymbols,
        RelatedTestDescriptor[] relatedTests,
        bool diagnosticsArePartial,
        AreaAuditFinding? firstFinding)
    {
        var actions = new List<RecommendedNextAction>();
        if (status == "AmbiguousTarget")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.SelectTarget,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = "high",
                Reason = "Multiple Visual Studio bridge instances are active; select a target before auditing.",
                SuggestedTool = "list_visual_studio_instances",
            });
        }
        else if (status == "NoActiveBridge" || status == "SolutionNotLoaded")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.OpenVisualStudio,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = "high",
                Reason = "A loaded Visual Studio C# workspace is required for semantic area audit.",
            });
        }
        else if (status == "NoScope")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.NarrowScope,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "high",
                Reason = "Provide areaPaths, queryTerms, changedFiles, includePathPatterns, or projectName before auditing.",
            });
        }

        var topDiagnostic = primaryDiagnostics.FirstOrDefault(diagnostic => !diagnostic.IsBackgroundNoise);
        if (topDiagnostic is not null)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectDiagnostic,
                EvidenceLevel = topDiagnostic.EvidenceLevel,
                Confidence = topDiagnostic.Severity == CodeDiagnosticSeverity.Error ? "high" : "medium",
                Reason = $"Inspect top area diagnostic {topDiagnostic.Id}.",
                SuggestedTool = topDiagnostic.Span is null ? "get_csharp_diagnostics" : "get_csharp_enclosing_context",
                TargetFilePath = topDiagnostic.Span?.FilePath ?? string.Empty,
                TargetSpan = topDiagnostic.Span,
                TargetProjectName = topDiagnostic.ProjectName,
            });
        }

        if (diagnosticsArePartial)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.NarrowScope,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Confidence = "medium",
                Reason = "Diagnostics were partial; rerun with narrower areaPaths/projectName or smaller maxDiagnosticProjects before trusting absence of errors.",
                SuggestedTool = "get_csharp_diagnostics",
            });
        }

        var topSymbol = primarySymbols.FirstOrDefault();
        if (topSymbol is not null)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectSymbol,
                EvidenceLevel = topSymbol.EvidenceLevel,
                Confidence = "medium",
                Reason = "Inspect the highest-ranked area symbol before broad text search.",
                SuggestedTool = "find_csharp_definitions",
                TargetSymbol = topSymbol.Symbol,
                TargetProjectName = topSymbol.Symbol.ProjectName,
                TargetFilePath = topSymbol.Span?.FilePath ?? string.Empty,
                TargetSpan = topSymbol.Span,
            });
        }

        var test = relatedTests.FirstOrDefault();
        if (test is not null)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RunTests,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "medium",
                Reason = "Related test evidence exists; use the verification planner to choose the smallest runnable test command.",
                SuggestedTool = "plan_csharp_verification",
                TargetProjectName = test.ProjectName,
                TargetFilePath = test.Span.FilePath,
                TargetSpan = test.Span,
            });
        }

        if (firstFinding is null && status == "Ready")
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.BroadenScope,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "medium",
                Reason = "No audit evidence was found; broaden queryTerms or provide build output/changed files.",
                SuggestedTool = "start_csharp_investigation",
            });
        }

        return actions
            .Where(action => action.Kind != WorkflowActionKind.Unknown)
            .GroupBy(action => string.Join("|", action.Kind.ToString(), action.SuggestedTool, action.TargetFilePath, action.TargetProjectName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToArray();
    }

    private static CSharpAreaAuditReport CreateAreaAuditReport(
        string status,
        WorkspaceStatus? workspaceStatus,
        TaskContextSummary taskContext,
        PrimaryFile[] primaryFiles,
        PrimarySymbol[] primarySymbols,
        PrimaryDiagnostic[] primaryDiagnostics,
        AreaAuditFinding[] findings,
        AreaAuditFinding[] coveredBehaviors,
        AreaAuditFinding[] coverageGaps,
        AreaAuditFinding[] suggestedTests,
        AreaAuditFinding[] candidateEditLocations,
        CodeDiagnostic[] diagnostics,
        SymbolDescriptor[] symbols,
        RelatedTestDescriptor[] relatedTests,
        TemporaryMarker[] markers,
        RecommendedNextAction[] recommendedNextActions,
        IEnumerable<string> nextSteps)
    {
        return new CSharpAreaAuditReport
        {
            Status = status,
            TaskContext = taskContext,
            EvidenceLevel = CreateEvidenceLevel(null, diagnostics, symbols, taskContext),
            WorkspaceStatus = workspaceStatus,
            PrimaryFiles = primaryFiles,
            PrimarySymbols = primarySymbols,
            PrimaryDiagnostics = primaryDiagnostics,
            Findings = findings,
            CoveredBehaviors = coveredBehaviors,
            CoverageGaps = coverageGaps,
            SuggestedTests = suggestedTests,
            CandidateEditLocations = candidateEditLocations,
            Diagnostics = diagnostics,
            Symbols = symbols,
            RelatedTests = relatedTests,
            TemporaryMarkers = markers,
            RecommendedNextActions = recommendedNextActions,
            SuggestedNextSteps = nextSteps.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    private static CSharpChangeReviewReport CreateChangeReviewReport(
        string status,
        WorkspaceStatus? workspaceStatus,
        BuildTriageReport? buildTriage,
        TaskContextSummary taskContext,
        PrimaryFile[] primaryFiles,
        PrimarySymbol[] primarySymbols,
        PrimaryDiagnostic[] primaryDiagnostics,
        SymbolImpactSummary? impactSummary,
        AreaAuditFinding[] findings,
        AreaAuditFinding[] publicApiRisks,
        AreaAuditFinding[] testGaps,
        AreaAuditFinding[] candidateEditLocations,
        CodeDiagnostic[] diagnostics,
        SymbolDescriptor[] symbols,
        RelatedTestDescriptor[] relatedTests,
        TemporaryMarker[] markers,
        RecommendedNextAction[] recommendedNextActions,
        IEnumerable<string> nextSteps)
    {
        return new CSharpChangeReviewReport
        {
            Status = status,
            TaskContext = taskContext,
            EvidenceLevel = CreateEvidenceLevel(buildTriage, diagnostics, symbols, taskContext),
            WorkspaceStatus = workspaceStatus,
            BuildTriage = buildTriage,
            PrimaryFiles = primaryFiles,
            PrimarySymbols = primarySymbols,
            PrimaryDiagnostics = primaryDiagnostics,
            ImpactSummary = impactSummary,
            Findings = findings,
            PublicApiRisks = publicApiRisks,
            TestGaps = testGaps,
            CandidateEditLocations = candidateEditLocations,
            Diagnostics = diagnostics,
            Symbols = symbols,
            RelatedTests = relatedTests,
            TemporaryMarkers = markers,
            RecommendedNextActions = recommendedNextActions,
            SuggestedNextSteps = nextSteps.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    private static string[] CreateInvestigationIncludePatterns(
        string[]? includePathPatterns,
        string[]? changedFiles,
        string? filePath,
        BuildTriageReport? buildTriage)
    {
        var patterns = new List<string>();
        patterns.AddRange(NormalizePatterns(includePathPatterns));
        patterns.AddRange(NormalizePatterns(changedFiles));
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            patterns.Add(filePath.Trim());
        }

        if (buildTriage is not null)
        {
            patterns.AddRange(buildTriage.Issues
                .Where(issue => !issue.IsLikelyCascade)
                .Select(issue => issue.Span?.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))!);
        }

        return patterns
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<RelatedTestDescriptor[]> QueryVerificationRelatedTestsAsync(
        VisualStudioBridgeTarget target,
        WorkspaceStatus workspaceStatus,
        string? symbolQuery,
        string? filePath,
        string[] changedFiles,
        int maxRelatedTests,
        bool includeGeneratedCode,
        ICollection<string> diagnostics,
        Action? markPartial,
        CancellationToken cancellationToken)
    {
        if (maxRelatedTests == 0)
        {
            return Array.Empty<RelatedTestDescriptor>();
        }

        var relatedTests = new List<RelatedTestDescriptor>();
        var remaining = maxRelatedTests;
        if (!string.IsNullOrWhiteSpace(symbolQuery))
        {
            var searchResult = await _workspaceBridge.SearchSymbolsAsync(
                    new SymbolSearchRequest
                    {
                        Target = target,
                        QueryText = symbolQuery,
                        MaxResults = 5,
                        IncludeGeneratedCode = includeGeneratedCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            AddDiagnostics(diagnostics, searchResult.Diagnostics);
            if (searchResult.IsPartial)
            {
                markPartial?.Invoke();
            }

            var symbols = searchResult.Items.ToArray();
            if (symbols.Length == 1 && symbols[0].Key is not null)
            {
                var tests = await QueryRelatedTestsAsync(
                        target,
                        symbols[0].Key!,
                        filePath,
                        remaining,
                        includeGeneratedCode,
                        diagnostics,
                        markPartial,
                        cancellationToken)
                    .ConfigureAwait(false);
                AddUniqueRelatedTests(relatedTests, tests, maxRelatedTests);
                remaining = maxRelatedTests - relatedTests.Count;
            }
            else if (symbols.Length > 1)
            {
                diagnostics.Add("VerificationRelatedTestsSkipped: symbolQuery is ambiguous; narrow it before using symbol-based related test planning.");
            }
        }

        foreach (var path in CreateVerificationFileScopes(workspaceStatus, filePath, changedFiles))
        {
            if (remaining <= 0)
            {
                break;
            }

            var result = await _workspaceBridge.FindRelatedTestsAsync(
                    new RelatedTestsRequest
                    {
                        Target = target,
                        FilePath = path,
                        MaxResults = remaining,
                        IncludeGeneratedCode = includeGeneratedCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            AddDiagnostics(diagnostics, result.Diagnostics);
            if (result.IsPartial)
            {
                markPartial?.Invoke();
            }

            AddUniqueRelatedTests(relatedTests, result.Items, maxRelatedTests);
            remaining = maxRelatedTests - relatedTests.Count;
        }

        return relatedTests.ToArray();
    }

    private static string[] CreateVerificationFileScopes(
        WorkspaceStatus workspaceStatus,
        string? filePath,
        string[] changedFiles)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            paths.Add(ResolvePathForWorkspace(workspaceStatus, filePath!));
        }

        foreach (var changedFile in changedFiles)
        {
            paths.Add(ResolvePathForWorkspace(workspaceStatus, changedFile));
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddUniqueRelatedTests(
        ICollection<RelatedTestDescriptor> target,
        IEnumerable<RelatedTestDescriptor> source,
        int maxResults)
    {
        var seen = new HashSet<string>(
            target.Select(CreateRelatedTestKey),
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            if (target.Count >= maxResults)
            {
                return;
            }

            if (seen.Add(CreateRelatedTestKey(item)))
            {
                target.Add(item);
            }
        }
    }

    private static string CreateRelatedTestKey(RelatedTestDescriptor descriptor)
    {
        return string.Join(
            "|",
            descriptor.ProjectName,
            descriptor.TestClass,
            descriptor.TestMethod,
            descriptor.Span.FilePath,
            descriptor.Span.StartLine.ToString());
    }

    private static VerificationProject[] CreateAffectedProjects(
        ProjectGraph projectGraph,
        WorkspaceStatus workspaceStatus,
        string[] changedFiles,
        BuildTriageReport? buildTriage,
        IEnumerable<CodeDiagnostic> diagnostics,
        IEnumerable<RelatedTestDescriptor> relatedTests,
        string? projectName)
    {
        var projects = new Dictionary<string, VerificationProjectAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var changedFile in changedFiles)
        {
            var resolved = ResolvePathForWorkspace(workspaceStatus, changedFile);
            var node = FindProjectForFile(projectGraph, resolved);
            if (node is not null)
            {
                AddAffectedProject(projects, node, $"changed file: {changedFile}");
            }
        }

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var node = FindProjectByName(projectGraph, projectName!);
            if (node is not null)
            {
                AddAffectedProject(projects, node, "requested project");
            }
        }

        if (buildTriage is not null)
        {
            foreach (var issue in buildTriage.Issues.Where(issue => !string.IsNullOrWhiteSpace(issue.ProjectName)))
            {
                var node = FindProjectByName(projectGraph, issue.ProjectName);
                if (node is not null)
                {
                    AddAffectedProject(projects, node, $"build issue: {issue.Id}");
                }
            }
        }

        foreach (var diagnostic in diagnostics.Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.ProjectName)))
        {
            var node = FindProjectByName(projectGraph, diagnostic.ProjectName);
            if (node is not null)
            {
                AddAffectedProject(projects, node, $"diagnostic: {diagnostic.Id}");
            }
        }

        foreach (var test in relatedTests.Where(test => !string.IsNullOrWhiteSpace(test.ProjectName)))
        {
            var node = FindProjectByName(projectGraph, test.ProjectName);
            if (node is not null)
            {
                AddAffectedProject(projects, node, "related test project");
            }
        }

        return projects.Values
            .Select(item => item.ToProject())
            .OrderBy(project => project.IsTestProject)
            .ThenBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static VerificationCommand[] CreateVerificationCommands(
        WorkspaceStatus workspaceStatus,
        ProjectGraph projectGraph,
        VerificationProject[] affectedProjects,
        RelatedTestDescriptor[] relatedTests)
    {
        var commands = new List<VerificationCommand>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in affectedProjects.Where(project => !project.IsTestProject && !string.IsNullOrWhiteSpace(project.ProjectFilePath)))
        {
            AddVerificationCommand(
                commands,
                seen,
                $"dotnet build {QuoteArgument(project.ProjectFilePath)} -v:minimal --no-restore",
                "project-build",
                "high",
                new[] { $"affected project: {project.ProjectName}" });
        }

        foreach (var group in relatedTests.GroupBy(test => test.ProjectName, StringComparer.OrdinalIgnoreCase))
        {
            var project = FindProjectByName(projectGraph, group.Key);
            var projectPath = project?.FilePath ?? string.Empty;
            var tests = group.ToArray();
            var directReferenceTests = tests
                .Where(IsDirectReferenceRelatedTest)
                .Where(test => !string.IsNullOrWhiteSpace(test.TestClass) && !string.IsNullOrWhiteSpace(test.TestMethod))
                .Take(5)
                .ToArray();
            foreach (var test in directReferenceTests)
            {
                var filter = $"{test.TestClass}.{test.TestMethod}";
                var command = string.IsNullOrWhiteSpace(projectPath)
                    ? $"dotnet test --filter {QuoteArgument("FullyQualifiedName~" + filter)} -v:minimal --no-restore"
                    : $"dotnet test {QuoteArgument(projectPath)} --filter {QuoteArgument("FullyQualifiedName~" + filter)} -v:minimal --no-restore";
                AddVerificationCommand(
                    commands,
                    seen,
                    command,
                    "single-test",
                    string.IsNullOrWhiteSpace(projectPath) ? "medium" : "high",
                    new[] { $"related test: {filter}" });
            }

            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                var hasDirectReferenceEvidence = tests.Any(IsDirectReferenceRelatedTest);
                AddVerificationCommand(
                    commands,
                    seen,
                    $"dotnet test {QuoteArgument(projectPath)} -v:minimal --no-restore",
                    "test-project",
                    hasDirectReferenceEvidence ? "medium" : "low",
                    hasDirectReferenceEvidence
                        ? new[] { $"related test project: {group.Key}", "direct reference test evidence" }
                        : new[] { $"related test project: {group.Key}", "heuristic test evidence only" });
            }
        }

        foreach (var project in affectedProjects.Where(project => project.IsTestProject && !string.IsNullOrWhiteSpace(project.ProjectFilePath)))
        {
            AddVerificationCommand(
                commands,
                seen,
                $"dotnet test {QuoteArgument(project.ProjectFilePath)} -v:minimal --no-restore",
                "test-project",
                "medium",
                new[] { $"affected test project: {project.ProjectName}" });
        }

        if (!string.IsNullOrWhiteSpace(workspaceStatus.SolutionPath))
        {
            AddVerificationCommand(
                commands,
                seen,
                $"dotnet build {QuoteArgument(workspaceStatus.SolutionPath)} -v:minimal --no-restore",
                "solution-build",
                affectedProjects.Length == 0 ? "medium" : "low",
                new[] { "baseline solution build" });
        }

        if (relatedTests.Length == 0 && !string.IsNullOrWhiteSpace(workspaceStatus.SolutionPath))
        {
            AddVerificationCommand(
                commands,
                seen,
                $"dotnet test {QuoteArgument(workspaceStatus.SolutionPath)} -v:minimal --no-restore",
                "solution-test",
                "low",
                new[] { "no specific related tests found" });
        }

        return commands.ToArray();
    }

    private static bool IsDirectReferenceRelatedTest(RelatedTestDescriptor test)
    {
        return test.MatchReasons.Any(reason => reason.Equals("ReferenceMatch", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddVerificationCommand(
        ICollection<VerificationCommand> commands,
        ISet<string> seen,
        string command,
        string scope,
        string confidence,
        string[] reasons)
    {
        if (!seen.Add(command))
        {
            return;
        }

        commands.Add(new VerificationCommand
        {
            Command = command,
            Scope = scope,
            Confidence = confidence,
            Reasons = reasons,
        });
    }

    private static void AddAffectedProject(
        IDictionary<string, VerificationProjectAccumulator> projects,
        ProjectGraphNode node,
        string reason)
    {
        var key = !string.IsNullOrWhiteSpace(node.ProjectId)
            ? node.ProjectId
            : node.ProjectName;
        if (!projects.TryGetValue(key, out var accumulator))
        {
            accumulator = new VerificationProjectAccumulator(node);
            projects.Add(key, accumulator);
        }

        accumulator.AddReason(reason);
    }

    private static ProjectGraphNode? FindProjectForFile(ProjectGraph projectGraph, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var normalizedFile = Path.GetFullPath(filePath);
        return projectGraph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.FilePath))
            .Select(node => new
            {
                Node = node,
                Directory = Path.GetDirectoryName(Path.GetFullPath(node.FilePath)) ?? string.Empty,
            })
            .Where(item => normalizedFile.StartsWith(item.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || normalizedFile.Equals(item.Node.FilePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Directory.Length)
            .Select(item => item.Node)
            .FirstOrDefault();
    }

    private static ProjectGraphNode? FindProjectByName(ProjectGraph projectGraph, string projectName)
    {
        return projectGraph.Nodes.FirstOrDefault(node => string.Equals(node.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePathForWorkspace(WorkspaceStatus workspaceStatus, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(workspaceStatus.SolutionPath))
        {
            return path;
        }

        var solutionDirectory = Path.GetDirectoryName(workspaceStatus.SolutionPath);
        return string.IsNullOrWhiteSpace(solutionDirectory)
            ? path
            : Path.GetFullPath(Path.Combine(solutionDirectory, path));
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private async Task<WorkspaceQueryResult<CodeDiagnostic>> GetInvestigationDiagnosticsAsync(
        VisualStudioBridgeTarget target,
        string? filePath,
        string[] includePathPatterns,
        string[]? excludePathPatterns,
        string[]? changedFiles,
        string? projectName,
        CodeDiagnosticSeverity? minimumSeverity,
        CodeDiagnosticNoiseProfile noiseProfile,
        bool includeWholeSolutionDiagnostics,
        int maxDiagnostics,
        bool includeGeneratedCode,
        CancellationToken cancellationToken)
    {
        if (maxDiagnostics == 0)
        {
            return EmptyResult<CodeDiagnostic>();
        }

        var hasScope = !string.IsNullOrWhiteSpace(filePath)
            || includePathPatterns.Length > 0
            || !string.IsNullOrWhiteSpace(projectName);
        if (!hasScope && !includeWholeSolutionDiagnostics)
        {
            return new WorkspaceQueryResult<CodeDiagnostic>
            {
                Items = Array.Empty<CodeDiagnostic>(),
                Diagnostics = new[] { "DiagnosticsSkipped: no file, project, path, build issue, or changed-file scope was provided." },
                IsPartial = false,
            };
        }

        return await _workspaceBridge.GetDiagnosticsAsync(
                new DiagnosticsRequest
                {
                    Target = target,
                    FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath,
                    IncludePathPatterns = includePathPatterns,
                    ExcludePathPatterns = NormalizePatterns(excludePathPatterns),
                    ChangedFiles = NormalizePatterns(changedFiles),
                    ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
                    MinimumSeverity = minimumSeverity,
                    NoiseProfile = noiseProfile,
                    MaxResults = maxDiagnostics,
                    IncludeGeneratedCode = includeGeneratedCode,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<SymbolDescriptor[]> QueryDefinitionsAsync(
        VisualStudioBridgeTarget target,
        SymbolKey symbolKey,
        bool includeGeneratedCode,
        ICollection<string> diagnostics,
        Action? markPartial,
        CancellationToken cancellationToken)
    {
        var result = await _workspaceBridge.FindDefinitionsAsync(
                new SymbolReferenceRequest
                {
                    Target = target,
                    SymbolKey = symbolKey,
                    IncludeGeneratedCode = includeGeneratedCode,
                },
                cancellationToken)
            .ConfigureAwait(false);
        AddDiagnostics(diagnostics, result.Diagnostics);
        if (result.IsPartial)
        {
            markPartial?.Invoke();
        }

        return result.Items.ToArray();
    }

    private async Task<SymbolReference[]> QueryReferencesAsync(
        VisualStudioBridgeTarget target,
        SymbolKey symbolKey,
        int maxRelatedItems,
        bool includeGeneratedCode,
        ICollection<string> diagnostics,
        Action? markPartial,
        CancellationToken cancellationToken)
    {
        if (maxRelatedItems == 0)
        {
            return Array.Empty<SymbolReference>();
        }

        var result = await _workspaceBridge.FindReferencesAsync(
                new SymbolReferenceRequest
                {
                    Target = target,
                    SymbolKey = symbolKey,
                    MaxResults = maxRelatedItems,
                    IncludeGeneratedCode = includeGeneratedCode,
                },
                cancellationToken)
            .ConfigureAwait(false);
        AddDiagnostics(diagnostics, result.Diagnostics);
        if (result.IsPartial)
        {
            markPartial?.Invoke();
        }

        return result.Items.ToArray();
    }

    private async Task<CallGraphEdge[]> QueryCallGraphAsync(
        VisualStudioBridgeTarget target,
        SymbolKey symbolKey,
        bool callers,
        int maxRelatedItems,
        bool includeGeneratedCode,
        ICollection<string> diagnostics,
        Action? markPartial,
        CancellationToken cancellationToken)
    {
        if (maxRelatedItems == 0)
        {
            return Array.Empty<CallGraphEdge>();
        }

        var request = new CallGraphRequest
        {
            Target = target,
            SymbolKey = symbolKey,
            MaxDepth = 1,
            MaxResults = maxRelatedItems,
            IncludeGeneratedCode = includeGeneratedCode,
        };
        var result = callers
            ? await _workspaceBridge.FindCallersAsync(request, cancellationToken).ConfigureAwait(false)
            : await _workspaceBridge.FindCalleesAsync(request, cancellationToken).ConfigureAwait(false);
        AddDiagnostics(diagnostics, result.Diagnostics);
        if (result.IsPartial)
        {
            markPartial?.Invoke();
        }

        return result.Items.ToArray();
    }

    private async Task<RelatedTestDescriptor[]> QueryRelatedTestsAsync(
        VisualStudioBridgeTarget target,
        SymbolKey symbolKey,
        string? filePath,
        int maxRelatedItems,
        bool includeGeneratedCode,
        ICollection<string> diagnostics,
        Action? markPartial,
        CancellationToken cancellationToken)
    {
        if (maxRelatedItems == 0)
        {
            return Array.Empty<RelatedTestDescriptor>();
        }

        var result = await _workspaceBridge.FindRelatedTestsAsync(
                new RelatedTestsRequest
                {
                    Target = target,
                    SymbolKey = symbolKey,
                    FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath,
                    MaxResults = maxRelatedItems,
                    IncludeGeneratedCode = includeGeneratedCode,
                },
                cancellationToken)
            .ConfigureAwait(false);
        AddDiagnostics(diagnostics, result.Diagnostics);
        if (result.IsPartial)
        {
            markPartial?.Invoke();
        }

        return result.Items.ToArray();
    }

    private async Task<SymbolImpactSummary?> QuerySymbolImpactAsync(
        VisualStudioBridgeTarget target,
        SymbolKey symbolKey,
        int maxRelatedItems,
        bool includeGeneratedCode,
        ICollection<string> diagnostics,
        Action? markPartial,
        CancellationToken cancellationToken)
    {
        if (maxRelatedItems == 0)
        {
            return null;
        }

        var result = await _workspaceBridge.AnalyzeSymbolImpactAsync(
                new SymbolImpactRequest
                {
                    Target = target,
                    SymbolKey = symbolKey,
                    MaxDepth = 2,
                    MaxResults = Math.Max(1, maxRelatedItems),
                    MaxProjects = Math.Min(20, Math.Max(1, maxRelatedItems)),
                    MaxFiles = Math.Min(20, Math.Max(1, maxRelatedItems)),
                    MaxContainingTypes = Math.Min(20, Math.Max(1, maxRelatedItems)),
                    IncludeGeneratedCode = includeGeneratedCode,
                },
                cancellationToken)
            .ConfigureAwait(false);
        AddDiagnostics(diagnostics, result.Diagnostics);
        if (result.IsPartial)
        {
            markPartial?.Invoke();
        }

        return result.Items.FirstOrDefault();
    }

    private static void AddDiagnostics(ICollection<string> diagnostics, IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            diagnostics.Add(item);
        }
    }

    private static WorkspaceQueryResult<T> EmptyResult<T>()
    {
        return new WorkspaceQueryResult<T>
        {
            Items = Array.Empty<T>(),
            Diagnostics = Array.Empty<string>(),
            IsPartial = false,
        };
    }

    private static IEnumerable<SymbolDescriptor> FilterSymbolCandidates(
        IEnumerable<SymbolDescriptor> symbols,
        string queryText,
        string? containingType,
        string? projectName,
        CodeSymbolKind? kind)
    {
        var candidates = symbols
            .Where(symbol => string.IsNullOrWhiteSpace(containingType)
                || string.Equals(symbol.ContainingType, containingType, StringComparison.OrdinalIgnoreCase))
            .Where(symbol => string.IsNullOrWhiteSpace(projectName)
                || string.Equals(symbol.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
            .Where(symbol => kind is null || symbol.Kind == kind.Value)
            .ToArray();

        var exactNameCandidates = candidates
            .Where(symbol => string.Equals(symbol.Name, queryText, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return exactNameCandidates.Length > 0 ? exactNameCandidates : candidates;
    }

    private static string FormatSymbolCandidate(SymbolDescriptor symbol)
    {
        var location = symbol.Span is null
            ? "no source span"
            : $"{symbol.Span.FilePath}:{symbol.Span.StartLine}:{symbol.Span.StartColumn}";
        var owner = string.IsNullOrWhiteSpace(symbol.ContainingType)
            ? symbol.ContainingNamespace
            : $"{symbol.ContainingNamespace}.{symbol.ContainingType}";
        return $"{symbol.Kind} {owner}.{symbol.Name} [{symbol.ProjectName}] {location}";
    }

    private static SymbolDescriptionRequest CreateSymbolDescriptionRequest(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target)
    {
        var request = new SymbolDescriptionRequest
        {
            Target = target,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }

        return request;
    }

    private static CallGraphRequest CreateCallGraphRequest(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxDepth,
        int maxResults,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target)
    {
        var request = new CallGraphRequest
        {
            Target = target,
            MaxDepth = maxDepth,
            MaxResults = maxResults,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }

        return request;
    }

    private static SymbolImpactRequest CreateSymbolImpactRequest(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxDepth,
        int maxResults,
        int maxProjects,
        int maxFiles,
        int maxContainingTypes,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target)
    {
        var request = new SymbolImpactRequest
        {
            Target = target,
            MaxDepth = maxDepth,
            MaxResults = maxResults,
            MaxProjects = maxProjects,
            MaxFiles = maxFiles,
            MaxContainingTypes = maxContainingTypes,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }

        return request;
    }

    private static RelatedTestsRequest CreateRelatedTestsRequest(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxResults,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target)
    {
        var request = new RelatedTestsRequest
        {
            Target = target,
            MaxResults = maxResults,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }
        else if (!string.IsNullOrWhiteSpace(filePath))
        {
            request.FilePath = filePath;
        }

        return request;
    }

    private static RenamePreviewRequest CreateRenamePreviewRequest(
        string newName,
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        bool renameOverloads,
        bool renameInStrings,
        bool renameInComments,
        bool renameFile,
        int maxTextChanges,
        int maxSnippetLength,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target)
    {
        var request = new RenamePreviewRequest
        {
            Target = target,
            NewName = newName,
            RenameOverloads = renameOverloads,
            RenameInStrings = renameInStrings,
            RenameInComments = renameInComments,
            RenameFile = renameFile,
            MaxTextChanges = maxTextChanges,
            MaxSnippetLength = maxSnippetLength,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }

        return request;
    }

    private static RenameApplyRequest CreateRenameApplyRequest(
        string newName,
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        bool renameOverloads,
        bool renameInStrings,
        bool renameInComments,
        bool renameFile,
        int maxTextChanges,
        int maxSnippetLength,
        bool includeGeneratedCode,
        bool allowConflicts,
        bool allowGeneratedDocumentChanges,
        bool allowUnsupportedDocumentChanges,
        bool allowTruncatedPreview,
        VisualStudioBridgeTarget? target)
    {
        var request = new RenameApplyRequest
        {
            Target = target,
            NewName = newName,
            RenameOverloads = renameOverloads,
            RenameInStrings = renameInStrings,
            RenameInComments = renameInComments,
            RenameFile = renameFile,
            MaxTextChanges = maxTextChanges,
            MaxSnippetLength = maxSnippetLength,
            IncludeGeneratedCode = includeGeneratedCode,
            AllowConflicts = allowConflicts,
            AllowGeneratedDocumentChanges = allowGeneratedDocumentChanges,
            AllowUnsupportedDocumentChanges = allowUnsupportedDocumentChanges,
            AllowTruncatedPreview = allowTruncatedPreview,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }

        return request;
    }

    private static DerivedTypesRequest CreateDerivedTypesRequest(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        bool transitive,
        int maxResults,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target)
    {
        var request = new DerivedTypesRequest
        {
            Target = target,
            Transitive = transitive,
            MaxResults = maxResults,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }

        return request;
    }

    private static InheritanceChainRequest CreateInheritanceChainRequest(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target)
    {
        var request = new InheritanceChainRequest
        {
            Target = target,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        if (!string.IsNullOrWhiteSpace(symbolKey))
        {
            request.SymbolKey = new SymbolKey(symbolKey);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            request.Position = new SourceSpan
            {
                FilePath = filePath,
                StartLine = line.Value,
                StartColumn = column.Value,
                EndLine = line.Value,
                EndColumn = column.Value,
            };
        }

        return request;
    }

    private static string? ValidateSymbolLookup(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column)
    {
        if (!HasSymbolKeyOrPosition(symbolKey, filePath, line, column))
        {
            return "Provide either a symbol key or a complete source position: filePath, line, and column.";
        }

        if (HasPartialPosition(filePath, line, column))
        {
            return "Source position must include all three values: filePath, line, and column.";
        }

        if (HasInvalidPosition(line, column))
        {
            return "Line and column must be one-based positive integers.";
        }

        return null;
    }

    private static string? ValidateSourceContextLookup(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int contextLines,
        int maxChars,
        int maxSnippets,
        bool requireSymbol)
    {
        if (requireSymbol)
        {
            var symbolValidation = ValidateSymbolLookup(symbolKey, filePath, line, column);
            if (symbolValidation is not null)
            {
                return symbolValidation;
            }
        }
        else if (HasPartialPosition(filePath, line, column) || !HasCompletePosition(filePath, line, column))
        {
            return "Source context requires a complete source position: filePath, line, and column.";
        }

        if (HasPartialPosition(filePath, line, column))
        {
            return "Source position must include all three values: filePath, line, and column.";
        }

        if (HasInvalidPosition(line, column))
        {
            return "Line and column must be one-based positive integers.";
        }

        if (contextLines is < 0 or > 50)
        {
            return "ContextLines must be between 0 and 50.";
        }

        if (maxChars is < 1 or > 200000)
        {
            return "MaxChars must be between 1 and 200000.";
        }

        if (maxSnippets is < 1 or > 20)
        {
            return "MaxSnippets must be between 1 and 20.";
        }

        return null;
    }

    private static string? ValidateBatchSourceContextLookup(
        SourcePositionRequest[]? positions,
        int contextLines,
        int maxCharsPerPosition,
        int maxPositions)
    {
        if (positions is null || positions.Length == 0)
        {
            return "Positions must include at least one source position.";
        }

        if (maxPositions is < 1 or > 100)
        {
            return "MaxPositions must be between 1 and 100.";
        }

        if (contextLines is < 0 or > 50)
        {
            return "ContextLines must be between 0 and 50.";
        }

        if (maxCharsPerPosition is < 1 or > 200000)
        {
            return "MaxCharsPerPosition must be between 1 and 200000.";
        }

        for (var i = 0; i < positions.Length; i++)
        {
            var position = positions[i];
            if (position is null)
            {
                return $"Positions[{i}] is required.";
            }

            var validation = ValidateSourceContextLookup(
                symbolKey: null,
                position.FilePath,
                position.Line,
                position.Column,
                contextLines,
                maxCharsPerPosition,
                maxSnippets: 1,
                requireSymbol: false);
            if (validation is not null)
            {
                return $"Positions[{i}]: {validation}";
            }
        }

        return null;
    }

    private static string? ValidateBatchSymbolSourceLookup(
        SourcePositionRequest[]? symbols,
        int contextLines,
        int maxCharsPerSymbol,
        int maxSnippetsPerSymbol,
        int maxSymbols)
    {
        if (symbols is null || symbols.Length == 0)
        {
            return "Symbols must include at least one symbol key or source position.";
        }

        if (maxSymbols is < 1 or > 100)
        {
            return "MaxSymbols must be between 1 and 100.";
        }

        if (contextLines is < 0 or > 50)
        {
            return "ContextLines must be between 0 and 50.";
        }

        if (maxCharsPerSymbol is < 1 or > 200000)
        {
            return "MaxCharsPerSymbol must be between 1 and 200000.";
        }

        if (maxSnippetsPerSymbol is < 1 or > 20)
        {
            return "MaxSnippetsPerSymbol must be between 1 and 20.";
        }

        for (var i = 0; i < symbols.Length; i++)
        {
            var symbol = symbols[i];
            if (symbol is null)
            {
                return $"Symbols[{i}] is required.";
            }

            var hasSymbolKey = !string.IsNullOrWhiteSpace(symbol.SymbolKey);
            var hasAnyPositionValue = !string.IsNullOrWhiteSpace(symbol.FilePath)
                || symbol.Line != 0
                || symbol.Column != 0;
            var hasCompletePosition = !string.IsNullOrWhiteSpace(symbol.FilePath)
                && symbol.Line > 0
                && symbol.Column > 0;

            if (!hasSymbolKey && !hasCompletePosition)
            {
                return $"Symbols[{i}]: Provide either symbolKey or a complete source position: filePath, line, and column.";
            }

            if (hasAnyPositionValue && !hasCompletePosition)
            {
                return $"Symbols[{i}]: Source position must include filePath plus one-based positive line and column.";
            }
        }

        return null;
    }

    private static string DescribeSourcePositionRequest(SourcePositionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SymbolKey))
        {
            return "symbolKey:" + request.SymbolKey.Trim();
        }

        return string.IsNullOrWhiteSpace(request.FilePath)
            ? "unknown"
            : $"{request.FilePath}:{request.Line}:{request.Column}";
    }

    private static string? ValidateVisualStudioDocumentsLookup(int maxResults)
    {
        return maxResults is < 1 or > 500
            ? "MaxResults must be between 1 and 500."
            : null;
    }

    private static string? ValidateSourceNavigation(string? filePath, int line, int column)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "FilePath is required.";
        }

        if (!Path.IsPathRooted(filePath))
        {
            return "FilePath must be an absolute path.";
        }

        if (line <= 0 || column <= 0)
        {
            return "Line and column must be one-based positive integers.";
        }

        return null;
    }

    private static string? ValidateSymbolReferenceLookup(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxResults)
    {
        var symbolValidation = ValidateSymbolLookup(symbolKey, filePath, line, column);
        if (symbolValidation is not null)
        {
            return symbolValidation;
        }

        if (maxResults is < 1 or > 10000)
        {
            return "MaxResults must be between 1 and 10000.";
        }

        return null;
    }

    private static string? ValidateCallGraphLookup(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxDepth,
        int maxResults)
    {
        var symbolValidation = ValidateSymbolLookup(symbolKey, filePath, line, column);
        if (symbolValidation is not null)
        {
            return symbolValidation;
        }

        if (maxDepth is < 1 or > 10)
        {
            return "MaxDepth must be between 1 and 10.";
        }

        if (maxResults is < 1 or > 1000)
        {
            return "MaxResults must be between 1 and 1000.";
        }

        return null;
    }

    private static string? ValidateSymbolImpactLookup(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxDepth,
        int maxResults,
        int maxProjects,
        int maxFiles,
        int maxContainingTypes)
    {
        var symbolValidation = ValidateSymbolLookup(symbolKey, filePath, line, column);
        if (symbolValidation is not null)
        {
            return symbolValidation;
        }

        if (maxDepth is < 1 or > 10)
        {
            return "MaxDepth must be between 1 and 10.";
        }

        if (maxResults is < 1 or > 10000)
        {
            return "MaxResults must be between 1 and 10000.";
        }

        if (maxProjects is < 1 or > 200)
        {
            return "MaxProjects must be between 1 and 200.";
        }

        if (maxFiles is < 1 or > 200)
        {
            return "MaxFiles must be between 1 and 200.";
        }

        if (maxContainingTypes is < 1 or > 200)
        {
            return "MaxContainingTypes must be between 1 and 200.";
        }

        return null;
    }

    private static string? ValidateRelatedTestsLookup(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxResults)
    {
        if (string.IsNullOrWhiteSpace(symbolKey) && string.IsNullOrWhiteSpace(filePath))
        {
            return "Provide either a symbol key or a source file path.";
        }

        if ((line.HasValue || column.HasValue) && string.IsNullOrWhiteSpace(filePath))
        {
            return "Source position requires filePath, line, and column.";
        }

        if (line.HasValue != column.HasValue)
        {
            return "Source position must include both line and column when filePath is used for symbol lookup.";
        }

        if (HasInvalidPosition(line, column))
        {
            return "Line and column must be one-based positive integers.";
        }

        if (maxResults is < 1 or > 1000)
        {
            return "MaxResults must be between 1 and 1000.";
        }

        return null;
    }

    private static string? ValidateRenamePreviewLookup(
        string newName,
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxTextChanges,
        int maxSnippetLength)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return "NewName is required.";
        }

        var symbolValidation = ValidateSymbolLookup(symbolKey, filePath, line, column);
        if (symbolValidation is not null)
        {
            return symbolValidation;
        }

        if (maxTextChanges is < 1 or > 10000)
        {
            return "MaxTextChanges must be between 1 and 10000.";
        }

        if (maxSnippetLength is < 0 or > 2000)
        {
            return "MaxSnippetLength must be between 0 and 2000.";
        }

        return null;
    }

    private static string? ValidateDerivedTypesLookup(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column,
        int maxResults)
    {
        var symbolValidation = ValidateSymbolLookup(symbolKey, filePath, line, column);
        if (symbolValidation is not null)
        {
            return symbolValidation;
        }

        if (maxResults is < 1 or > 5000)
        {
            return "MaxResults must be between 1 and 5000.";
        }

        return null;
    }

    private static string? ValidateDebugValueLimits(int maxChildren, int maxStringLength)
    {
        if (maxChildren is < 0 or > 500)
        {
            return "MaxChildren must be between 0 and 500.";
        }

        if (maxStringLength is < 1 or > 10000)
        {
            return "MaxStringLength must be between 1 and 10000.";
        }

        return null;
    }

    private static bool HasSymbolKeyOrPosition(
        string? symbolKey,
        string? filePath,
        int? line,
        int? column)
    {
        return !string.IsNullOrWhiteSpace(symbolKey)
            || (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue);
    }

    private static bool HasInvalidPosition(int? line, int? column)
    {
        return line is <= 0 || column is <= 0;
    }

    private static bool HasCompletePosition(string? filePath, int? line, int? column)
    {
        return !string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue;
    }

    private static bool HasPartialPosition(string? filePath, int? line, int? column)
    {
        var providedCount = 0;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            providedCount++;
        }

        if (line.HasValue)
        {
            providedCount++;
        }

        if (column.HasValue)
        {
            providedCount++;
        }

        return providedCount is > 0 and < 3;
    }

    private static DirectoryInfo? ResolveSolutionSearchRoot(string? rootDirectory, List<string> diagnostics)
    {
        var candidate = string.IsNullOrWhiteSpace(rootDirectory)
            ? Environment.CurrentDirectory
            : rootDirectory;

        try
        {
            var fullPath = Path.GetFullPath(candidate);
            var directory = new DirectoryInfo(fullPath);
            if (!directory.Exists)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                diagnostics.Add($"SolutionSearchRootDefaulted: rootDirectory was omitted; using {directory.FullName}.");
            }

            return directory;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add("SolutionSearchRootFailed: " + ex.Message);
            return null;
        }
    }

    private static IReadOnlyList<CSharpSolutionCandidate> FindSolutionCandidates(
        DirectoryInfo rootDirectory,
        string? preferredName,
        int maxDepth,
        bool includeSlnx,
        int maxResults,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var rootName = rootDirectory.Name;
        var normalizedPreferredName = string.IsNullOrWhiteSpace(preferredName)
            ? string.Empty
            : preferredName.Trim();
        var candidates = new List<CSharpSolutionCandidate>();

        foreach (var file in EnumerateSolutionFiles(rootDirectory, includeSlnx, maxDepth, diagnostics, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates.Add(CreateSolutionCandidate(file, rootDirectory, rootName, normalizedPreferredName));
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Depth)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.Length > maxResults)
        {
            diagnostics.Add($"SolutionCandidatesTruncated: returning {maxResults} of {ordered.Length} candidate(s).");
        }

        return ordered.Take(maxResults).ToArray();
    }

    private static IEnumerable<FileInfo> EnumerateSolutionFiles(
        DirectoryInfo rootDirectory,
        bool includeSlnx,
        int maxDepth,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<(DirectoryInfo Directory, int Depth)>();
        stack.Push((rootDirectory, 0));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = stack.Pop();
            FileInfo[] files;
            try
            {
                files = directory.GetFiles();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
            {
                diagnostics.Add($"SolutionSearchSkippedDirectory: {directory.FullName}: {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                if (IsSolutionFile(file, includeSlnx))
                {
                    yield return file;
                }
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            DirectoryInfo[] children;
            try
            {
                children = directory.GetDirectories();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
            {
                diagnostics.Add($"SolutionSearchSkippedDirectory: {directory.FullName}: {ex.Message}");
                continue;
            }

            foreach (var child in children.OrderByDescending(child => child.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldSkipSolutionSearchDirectory(child.Name))
                {
                    stack.Push((child, depth + 1));
                }
            }
        }
    }

    private static bool IsSolutionFile(FileInfo file, bool includeSlnx)
    {
        return string.Equals(file.Extension, ".sln", StringComparison.OrdinalIgnoreCase)
            || (includeSlnx && string.Equals(file.Extension, ".slnx", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldSkipSolutionSearchDirectory(string directoryName)
    {
        return directoryName.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("packages", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("artifacts", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("TestResults", StringComparison.OrdinalIgnoreCase);
    }

    private static CSharpSolutionCandidate CreateSolutionCandidate(
        FileInfo file,
        DirectoryInfo rootDirectory,
        string rootName,
        string preferredName)
    {
        var relativePath = Path.GetRelativePath(rootDirectory.FullName, file.FullName);
        var depth = CountRelativeDepth(relativePath);
        var solutionName = Path.GetFileNameWithoutExtension(file.Name);
        var reasons = new List<string>();
        var score = 0;

        if (depth == 0)
        {
            score += 100;
            reasons.Add("root-level solution");
        }
        else
        {
            score += Math.Max(0, 40 - (depth * 5));
            reasons.Add($"depth {depth}");
        }

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            if (string.Equals(solutionName, Path.GetFileNameWithoutExtension(preferredName), StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.FullName, preferredName, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
                reasons.Add("preferred exact match");
            }
            else if (file.FullName.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0
                || solutionName.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 40;
                reasons.Add("preferred text match");
            }
        }

        if (string.Equals(solutionName, rootName, StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
            reasons.Add("matches repository directory name");
        }

        if (string.Equals(file.Extension, ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
            reasons.Add("modern .slnx solution");
        }

        return new CSharpSolutionCandidate
        {
            FilePath = file.FullName,
            FileName = file.Name,
            SolutionName = solutionName,
            Extension = file.Extension,
            DirectoryPath = file.DirectoryName ?? string.Empty,
            RelativePath = relativePath,
            Depth = depth,
            Score = score,
            IsRootLevel = depth == 0,
            IsSlnx = string.Equals(file.Extension, ".slnx", StringComparison.OrdinalIgnoreCase),
            LastWriteTimeUtc = file.LastWriteTimeUtc,
            Reasons = reasons.ToArray(),
        };
    }

    private static int CountRelativeDepth(string relativePath)
    {
        var directory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(directory) || directory == ".")
        {
            return 0;
        }

        return directory
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    private static WorkspacePreparationReport CreateWorkspacePreparationReport(
        DirectoryInfo rootDirectory,
        string? preferredName,
        CSharpSolutionCandidate[] solutionCandidates,
        VisualStudioBridgeInstanceDescriptor[] activeInstances)
    {
        var nextSteps = new List<string>();
        if (solutionCandidates.Length == 0)
        {
            nextSteps.Add("No .sln or .slnx file was found under the search root. Use normal file search, or pass a different rootDirectory.");
            return new WorkspacePreparationReport
            {
                Status = "NoSolutionFound",
                RootDirectory = rootDirectory.FullName,
                PreferredName = preferredName ?? string.Empty,
                SolutionCandidates = solutionCandidates,
                MatchingInstances = activeInstances,
                SuggestedNextSteps = nextSteps.ToArray(),
            };
        }

        var selectedSolution = SelectPreparationSolution(solutionCandidates, preferredName);
        if (selectedSolution is null)
        {
            nextSteps.Add("Multiple plausible solution files were found. Pass preferredName or an explicit solution path fragment to disambiguate.");
            return new WorkspacePreparationReport
            {
                Status = "AmbiguousSolution",
                RootDirectory = rootDirectory.FullName,
                PreferredName = preferredName ?? string.Empty,
                SolutionCandidates = solutionCandidates,
                MatchingInstances = activeInstances,
                IsAmbiguous = true,
                SuggestedNextSteps = nextSteps.ToArray(),
            };
        }

        var matchingInstances = activeInstances
            .Where(instance => string.Equals(
                NormalizeHealthPath(instance.SolutionPath),
                NormalizeHealthPath(selectedSolution.FilePath),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingInstances.Length == 1)
        {
            nextSteps.Add("Use selectedInstance.instanceId as targetInstanceId for subsequent C# workflow tools.");
            return new WorkspacePreparationReport
            {
                Status = "Ready",
                RootDirectory = rootDirectory.FullName,
                PreferredName = preferredName ?? string.Empty,
                SolutionCandidates = solutionCandidates,
                SelectedSolution = selectedSolution,
                MatchingInstances = matchingInstances,
                SelectedInstance = matchingInstances[0],
                IsReady = true,
                SuggestedNextSteps = nextSteps.ToArray(),
            };
        }

        if (matchingInstances.Length > 1)
        {
            nextSteps.Add("Multiple Visual Studio instances already have the selected solution open. Use targetInstanceId or targetPipeName to disambiguate.");
            return new WorkspacePreparationReport
            {
                Status = "AmbiguousBridge",
                RootDirectory = rootDirectory.FullName,
                PreferredName = preferredName ?? string.Empty,
                SolutionCandidates = solutionCandidates,
                SelectedSolution = selectedSolution,
                MatchingInstances = matchingInstances,
                IsAmbiguous = true,
                SuggestedNextSteps = nextSteps.ToArray(),
            };
        }

        nextSteps.Add("Open Visual Studio with selectedSolution.filePath, then wait for a VSIX bridge discovery record.");
        nextSteps.Add("After Visual Studio loads the solution, call list_visual_studio_instances or prepare_csharp_workspace again.");
        return new WorkspacePreparationReport
        {
            Status = "VisualStudioLaunchRequired",
            RootDirectory = rootDirectory.FullName,
            PreferredName = preferredName ?? string.Empty,
            SolutionCandidates = solutionCandidates,
            SelectedSolution = selectedSolution,
            MatchingInstances = Array.Empty<VisualStudioBridgeInstanceDescriptor>(),
            RequiresVisualStudioLaunch = true,
            SuggestedNextSteps = nextSteps.ToArray(),
        };
    }

    private static CSharpSolutionCandidate? SelectPreparationSolution(
        CSharpSolutionCandidate[] candidates,
        string? preferredName)
    {
        if (candidates.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferredMatches = candidates
                .Where(candidate => candidate.Reasons.Any(reason => reason.Contains("preferred", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (preferredMatches.Length == 1)
            {
                return preferredMatches[0];
            }
        }

        var topScore = candidates[0].Score;
        var topCandidates = candidates
            .Where(candidate => candidate.Score == topScore)
            .ToArray();
        return topCandidates.Length == 1 ? topCandidates[0] : null;
    }

    private sealed class VerificationProjectAccumulator
    {
        private readonly ProjectGraphNode _node;
        private readonly HashSet<string> _reasons = new(StringComparer.OrdinalIgnoreCase);

        public VerificationProjectAccumulator(ProjectGraphNode node)
        {
            _node = node;
        }

        public void AddReason(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _reasons.Add(reason);
            }
        }

        public VerificationProject ToProject()
        {
            return new VerificationProject
            {
                ProjectName = _node.ProjectName,
                ProjectFilePath = _node.FilePath,
                IsTestProject = IsLikelyTestProject(_node),
                Reasons = _reasons.OrderBy(reason => reason, StringComparer.OrdinalIgnoreCase).ToArray(),
            };
        }

        private static bool IsLikelyTestProject(ProjectGraphNode node)
        {
            return node.ProjectName.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0
                || node.FilePath.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private sealed class BuildFailureProjectBindingAccumulator
    {
        private readonly HashSet<string> _issueIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reasons = new(StringComparer.OrdinalIgnoreCase);

        public BuildFailureProjectBindingAccumulator(string projectName, string projectFilePath, string confidence)
        {
            ProjectName = projectName;
            ProjectFilePath = projectFilePath;
            Confidence = confidence;
        }

        public string ProjectName { get; }

        public string ProjectFilePath { get; }

        public string Confidence { get; private set; }

        public void AddIssue(string issueId)
        {
            if (!string.IsNullOrWhiteSpace(issueId))
            {
                _issueIds.Add(issueId);
            }
        }

        public void AddReasons(IEnumerable<string> reasons)
        {
            foreach (var reason in reasons.Where(reason => !string.IsNullOrWhiteSpace(reason)))
            {
                _reasons.Add(reason);
            }
        }

        public BuildFailureProjectBinding ToProjectBinding()
        {
            return new BuildFailureProjectBinding
            {
                ProjectName = ProjectName,
                ProjectFilePath = ProjectFilePath,
                IssueIds = _issueIds.OrderBy(issueId => issueId, StringComparer.OrdinalIgnoreCase).ToArray(),
                BindingReasons = _reasons.OrderBy(reason => reason, StringComparer.OrdinalIgnoreCase).ToArray(),
                Confidence = Confidence,
            };
        }
    }

    private sealed class BuildFailureSessionBindingResult
    {
        public BuildFailureSessionBindingResult(BuildFailureSession session, string[] diagnostics, bool isPartial)
        {
            Session = session;
            Diagnostics = diagnostics;
            IsPartial = isPartial;
        }

        public BuildFailureSession Session { get; }

        public string[] Diagnostics { get; }

        public bool IsPartial { get; }
    }

    private sealed class BuildFailureIssueBindingResult
    {
        public BuildFailureIssueBindingResult(BuildFailureIssueBinding binding, SourceContextSnippet? sourceContext)
        {
            Binding = binding;
            SourceContext = sourceContext;
        }

        public BuildFailureIssueBinding Binding { get; }

        public SourceContextSnippet? SourceContext { get; }
    }

    private sealed class TaskContextSourceSnippetResult
    {
        public TaskContextSourceSnippetResult(SourceContextSnippet[] snippets, bool isPartial)
        {
            Snippets = snippets;
            IsPartial = isPartial;
        }

        public SourceContextSnippet[] Snippets { get; }

        public bool IsPartial { get; }
    }

    public async Task<WorkspaceQueryResult<CSharpBuildFailureContext>> InvestigateCSharpBuildFailure(
        string? problemText = null,
        string? buildOutput = null,
        string? buildLogFilePath = null,
        string[]? changedFiles = null,
        string? filePath = null,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        string? projectName = null,
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        bool includeVisualStudioBuildOutput = true,
        int maxVisualStudioBuildOutputCharacters = 20000,
        int maxBuildIssues = 20,
        int maxDiagnostics = 30,
        int maxErrorListItems = 50,
        int maxSourceSnippets = 8,
        int contextLines = 3,
        int maxCharsPerSnippet = 8000,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxErrorListItems is < 0 or > 500)
        {
            return Failure<CSharpBuildFailureContext>("MaxErrorListItems must be between 0 and 500.");
        }

        var diagnostics = new List<string>();
        var taskContextResult = await GetCSharpTaskContext(
                problemText,
                buildOutput,
                buildLogFilePath,
                symbolQuery: null,
                filePath,
                includePathPatterns,
                excludePathPatterns,
                changedFiles,
                projectName,
                minimumSeverity,
                noiseProfile,
                includeWholeSolutionDiagnostics: false,
                includeVisualStudioBuildOutput,
                maxVisualStudioBuildOutputCharacters,
                maxBuildIssues,
                maxDiagnostics,
                maxSymbols: 10,
                maxRelatedItems: 10,
                maxSourceSnippets,
                contextLines,
                maxCharsPerSnippet,
                includeGeneratedCode,
                targetPipeName,
                targetInstanceId,
                targetSolutionPath,
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(taskContextResult.Diagnostics);
        var taskContext = taskContextResult.Items.FirstOrDefault();

        VisualStudioOutputWindowSnapshot? outputWindow = null;
        if (includeVisualStudioBuildOutput)
        {
            var outputResult = await GetVisualStudioOutputWindow(
                    "Build",
                    maxVisualStudioBuildOutputCharacters,
                    targetPipeName,
                    targetInstanceId,
                    targetSolutionPath,
                    cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(outputResult.Diagnostics.Select(diagnostic => "BuildFailureOutputWindow: " + diagnostic));
            outputWindow = outputResult.Items.FirstOrDefault();
        }

        var errorListItems = Array.Empty<VisualStudioErrorListItem>();
        if (maxErrorListItems > 0)
        {
            var errorListResult = await GetVisualStudioErrorList(
                    maxErrorListItems,
                    targetPipeName,
                    targetInstanceId,
                    targetSolutionPath,
                    cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(errorListResult.Diagnostics.Select(diagnostic => "BuildFailureErrorList: " + diagnostic));
            errorListItems = errorListResult.Items.ToArray();
        }

        var nextSteps = new List<string>();
        if (taskContext?.BuildTriage?.Issues.Length > 0)
        {
            nextSteps.Add("Start from BuildTriage root-cause candidates before reading broad logs or whole-solution diagnostics.");
        }

        if (taskContext?.PrimaryDiagnostics.Length > 0)
        {
            nextSteps.Add("Use scoped diagnostics as supporting evidence and prefer current-task files over whole-solution noise.");
        }

        if (outputWindow?.IsTruncated == true)
        {
            nextSteps.Add("The Visual Studio Build output tail was truncated; rerun with explicit build output or a build log file for stronger evidence.");
        }

        var actions = new List<RecommendedNextAction>();
        if (taskContext is not null)
        {
            actions.AddRange(taskContext.RecommendedNextActions);
        }

        if (outputWindow is not null && !string.IsNullOrWhiteSpace(outputWindow.Text))
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RequestBuildOutput,
                EvidenceLevel = outputWindow.IsTruncated ? WorkflowEvidenceLevel.Inference : WorkflowEvidenceLevel.Fact,
                Confidence = outputWindow.IsTruncated ? "medium" : "high",
                Reason = "Visual Studio Build output was available as supporting evidence.",
                SuggestedTool = "get_visual_studio_output_window",
            });
        }

        var context = new CSharpBuildFailureContext
        {
            Status = taskContext?.Status ?? "TaskContextUnavailable",
            TaskContext = taskContext?.TaskContext ?? new TaskContextSummary(),
            EvidenceLevel = taskContext?.EvidenceLevel ?? WorkflowEvidenceLevel.Unknown,
            BuildTriage = taskContext?.BuildTriage,
            Diagnostics = taskContext?.PrimaryDiagnostics.Select(ToCodeDiagnostic).ToArray() ?? Array.Empty<CodeDiagnostic>(),
            BuildOutputWindow = outputWindow,
            ErrorListItems = errorListItems,
            SourceSnippets = taskContext?.SourceSnippets ?? Array.Empty<SourceContextSnippet>(),
            RecommendedNextActions = actions
                .GroupBy(action => string.Join("|", action.Kind.ToString(), action.SuggestedTool, action.SuggestedCommand, action.TargetFilePath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(12)
                .ToArray(),
            SuggestedNextSteps = nextSteps
                .Concat(taskContext?.SuggestedNextSteps ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
        var isPartial = taskContextResult.IsPartial || outputWindow?.IsTruncated == true;
        var taskId = CreateAgenticTaskId();
        context.BuildFailureSession = CreateBuildFailureSession(taskId, context);
        var bindingResult = await BindBuildFailureSessionAsync(
                context.BuildFailureSession,
                context.TaskContext,
                contextLines,
                maxCharsPerSnippet,
                includeGeneratedCode,
                CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(bindingResult.Diagnostics);
        isPartial |= bindingResult.IsPartial;
        context.BuildFailureSession = bindingResult.Session;
        context.EvidencePacket = _evidencePacketBuilder.BuildBuildFailurePacket(
            taskId,
            context,
            context.BuildFailureSession,
            CreateBuildFailureBudget(maxDiagnostics, maxSourceSnippets, maxCharsPerSnippet),
            CreateBuildFailureTelemetry(context, diagnostics, isPartial),
            Array.Empty<SafetyBlocker>(),
            diagnostics.ToArray(),
            isPartial);

        return Success(
            context,
            diagnostics,
            isPartial);
    }

    public Task<WorkspaceQueryResult<ArtifactEvidenceReport>> CollectArtifactEvidence(
        string[]? artifactPaths = null,
        string[]? rootDirectories = null,
        string[]? searchPatterns = null,
        string[]? includeTextPatterns = null,
        string[]? excludePathPatterns = null,
        int maxArtifacts = 20,
        int maxLinesPerArtifact = 20,
        int maxCharsPerArtifact = 12000,
        CancellationToken cancellationToken = default)
    {
        if (maxArtifacts is < 1 or > 200)
        {
            return Task.FromResult(Failure<ArtifactEvidenceReport>("MaxArtifacts must be between 1 and 200."));
        }

        if (maxLinesPerArtifact is < 0 or > 200)
        {
            return Task.FromResult(Failure<ArtifactEvidenceReport>("MaxLinesPerArtifact must be between 0 and 200."));
        }

        if (maxCharsPerArtifact is < 100 or > 200000)
        {
            return Task.FromResult(Failure<ArtifactEvidenceReport>("MaxCharsPerArtifact must be between 100 and 200000."));
        }

        var diagnostics = new List<string>();
        var candidates = FindArtifactCandidates(
                NormalizePatterns(artifactPaths),
                NormalizePatterns(rootDirectories),
                NormalizePatterns(searchPatterns),
                NormalizePatterns(excludePathPatterns),
                maxArtifacts,
                diagnostics,
                cancellationToken)
            .ToArray();
        var items = candidates
            .Select(path => ReadArtifactEvidence(path, NormalizePatterns(includeTextPatterns), maxLinesPerArtifact, maxCharsPerArtifact, diagnostics))
            .Where(item => item is not null)
            .Cast<ArtifactEvidenceItem>()
            .ToArray();
        var nextSteps = items.Length == 0
            ? new[] { "Provide artifactPaths or rootDirectories/searchPatterns that point at report, trace, log, JSON, or text outputs." }
            : new[] { "Use artifact summaries as supporting evidence; prefer structured report or trace files over raw UI automation observations." };

        return Task.FromResult(Success(
            new ArtifactEvidenceReport
            {
                Status = items.Length == 0 ? "NoArtifacts" : "Ready",
                Artifacts = items,
                RecommendedNextActions = items
                    .Where(item => item.ErrorLines.Length > 0 || item.WarningLines.Length > 0)
                    .Take(5)
                    .Select(item => new RecommendedNextAction
                    {
                        Kind = WorkflowActionKind.InspectArtifact,
                        EvidenceLevel = WorkflowEvidenceLevel.Fact,
                        Confidence = item.ErrorLines.Length > 0 ? "high" : "medium",
                        Reason = item.ErrorLines.Length > 0
                            ? "Artifact contains error-like evidence."
                            : "Artifact contains warning-like evidence.",
                        TargetFilePath = item.FilePath,
                    })
                    .ToArray(),
                SuggestedNextSteps = nextSteps,
            },
            diagnostics,
            diagnostics.Any(diagnostic => diagnostic.StartsWith("ArtifactCandidatesTruncated:", StringComparison.Ordinal))));
    }

    public async Task<WorkspaceQueryResult<ArtifactEvidenceReport>> WaitForArtifactEvidence(
        string[]? artifactPaths = null,
        string[]? rootDirectories = null,
        string[]? searchPatterns = null,
        string[]? includeTextPatterns = null,
        string[]? excludePathPatterns = null,
        int timeoutMilliseconds = 30000,
        int pollIntervalMilliseconds = 1000,
        int maxArtifacts = 20,
        int maxLinesPerArtifact = 20,
        int maxCharsPerArtifact = 12000,
        CancellationToken cancellationToken = default)
    {
        if (timeoutMilliseconds is < 0 or > 300000)
        {
            return Failure<ArtifactEvidenceReport>("TimeoutMilliseconds must be between 0 and 300000.");
        }

        if (pollIntervalMilliseconds is < 100 or > 30000)
        {
            return Failure<ArtifactEvidenceReport>("PollIntervalMilliseconds must be between 100 and 30000.");
        }

        var stopwatch = Stopwatch.StartNew();
        WorkspaceQueryResult<ArtifactEvidenceReport>? latest = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            latest = await CollectArtifactEvidence(
                    artifactPaths,
                    rootDirectories,
                    searchPatterns,
                    includeTextPatterns,
                    excludePathPatterns,
                    maxArtifacts,
                    maxLinesPerArtifact,
                    maxCharsPerArtifact,
                    cancellationToken)
                .ConfigureAwait(false);
            var report = latest.Items.FirstOrDefault();
            if (report?.Artifacts.Length > 0)
            {
                return new WorkspaceQueryResult<ArtifactEvidenceReport>
                {
                    Items = latest.Items,
                    Diagnostics = latest.Diagnostics
                        .Concat(new[] { $"ArtifactWaitElapsedMilliseconds: {stopwatch.ElapsedMilliseconds}" })
                        .ToArray(),
                    IsPartial = latest.IsPartial,
                };
            }

            if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
            {
                break;
            }

            var remaining = timeoutMilliseconds - (int)Math.Min(timeoutMilliseconds, stopwatch.ElapsedMilliseconds);
            await Task.Delay(Math.Min(pollIntervalMilliseconds, Math.Max(100, remaining)), cancellationToken).ConfigureAwait(false);
        }
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds);

        var fallback = latest?.Items.FirstOrDefault() ?? new ArtifactEvidenceReport();
        fallback.Status = "TimedOut";
        fallback.SuggestedNextSteps = fallback.SuggestedNextSteps
            .Concat(new[] { "Artifact wait timed out; verify the application writes the expected artifact path and rerun with a wider timeout or corrected search pattern." })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WorkspaceQueryResult<ArtifactEvidenceReport>
        {
            Items = new[] { fallback },
            Diagnostics = (latest?.Diagnostics ?? Array.Empty<string>())
                .Concat(new[] { $"ArtifactWaitTimedOutMilliseconds: {stopwatch.ElapsedMilliseconds}" })
                .ToArray(),
            IsPartial = true,
        };
    }

    private static BuildFailureSession CreateBuildFailureSession(string sessionId, CSharpBuildFailureContext context)
    {
        var issues = context.BuildTriage?.Issues ?? Array.Empty<BuildIssue>();
        return new BuildFailureSession
        {
            SessionId = sessionId,
            RootCauseCandidates = issues
                .Where(issue => issue.IsRootCauseCandidate || !issue.IsLikelyCascade)
                .OrderByDescending(issue => issue.RootCauseScore)
                .Take(12)
                .ToArray(),
            CascadeIssues = issues
                .Where(issue => issue.IsLikelyCascade)
                .Take(20)
                .ToArray(),
            ScopedDiagnostics = context.Diagnostics,
            SourceSnippets = context.SourceSnippets,
            RelatedTests = Array.Empty<RelatedTestDescriptor>(),
            RecommendedCommands = Array.Empty<VerificationCommand>(),
            BuildOutputWindow = context.BuildOutputWindow,
            ErrorListItems = context.ErrorListItems,
            RecommendedNextActions = context.RecommendedNextActions,
        };
    }

    private async Task<BuildFailureSessionBindingResult> BindBuildFailureSessionAsync(
        BuildFailureSession session,
        TaskContextSummary taskContext,
        int contextLines,
        int maxCharsPerSnippet,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var isPartial = false;
        if (session.RootCauseCandidates.Length == 0)
        {
            return new BuildFailureSessionBindingResult(session, diagnostics.ToArray(), false);
        }

        var projectGraph = await TryGetBuildFailureProjectGraphAsync(
                target,
                diagnostics,
                () => isPartial = true,
                cancellationToken)
            .ConfigureAwait(false);
        var workspaceStatus = CreateWorkspaceStatusFromTaskContext(taskContext, target);
        var issueBindings = new List<BuildFailureIssueBinding>();
        var projectBindings = new Dictionary<string, BuildFailureProjectBindingAccumulator>(StringComparer.OrdinalIgnoreCase);
        var relatedTests = new List<RelatedTestDescriptor>();
        var sourceSnippets = session.SourceSnippets.ToList();

        foreach (var issue in session.RootCauseCandidates
            .Where(issue => issue.Span is not null || !string.IsNullOrWhiteSpace(issue.ProjectName))
            .Take(MaxBuildFailureBindingIssues))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bindingResult = await BindBuildFailureIssueAsync(
                    issue,
                    projectGraph,
                    workspaceStatus,
                    contextLines,
                    maxCharsPerSnippet,
                    includeGeneratedCode,
                    target,
                    diagnostics,
                    () => isPartial = true,
                    cancellationToken)
                .ConfigureAwait(false);
            var binding = bindingResult.Binding;
            issueBindings.Add(binding);
            AddBuildFailureProjectBinding(projectBindings, binding, issue);
            AddUniqueRelatedTests(relatedTests, binding.RelatedTests, 50);
            AddBuildFailureSourceSnippet(sourceSnippets, bindingResult.SourceContext);
        }

        session.IssueBindings = issueBindings.ToArray();
        session.ProjectBindings = projectBindings.Values
            .Select(item => item.ToProjectBinding())
            .OrderBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        session.RelatedTests = relatedTests.ToArray();
        session.SourceSnippets = sourceSnippets
            .Take(20)
            .ToArray();
        session.RecommendedCommands = CreateVerificationCommands(
                workspaceStatus,
                projectGraph,
                CreateBuildFailureVerificationProjects(session.ProjectBindings, projectGraph),
                session.RelatedTests)
            .Take(12)
            .ToArray();
        session.RecommendedNextActions = AddBuildFailureCommandActions(
            session.RecommendedNextActions,
            session.RecommendedCommands);

        return new BuildFailureSessionBindingResult(session, diagnostics.ToArray(), isPartial);
    }

    private static VerificationProject[] CreateBuildFailureVerificationProjects(
        BuildFailureProjectBinding[] projectBindings,
        ProjectGraph projectGraph)
    {
        return projectBindings
            .Where(project => !string.IsNullOrWhiteSpace(project.ProjectName) || !string.IsNullOrWhiteSpace(project.ProjectFilePath))
            .Select(project =>
            {
                var graphNode = FindProjectByName(projectGraph, project.ProjectName)
                    ?? projectGraph.Nodes.FirstOrDefault(node =>
                        !string.IsNullOrWhiteSpace(project.ProjectFilePath)
                        && string.Equals(node.FilePath, project.ProjectFilePath, StringComparison.OrdinalIgnoreCase));
                var projectName = !string.IsNullOrWhiteSpace(project.ProjectName)
                    ? project.ProjectName
                    : graphNode?.ProjectName ?? Path.GetFileNameWithoutExtension(project.ProjectFilePath);
                var projectFilePath = !string.IsNullOrWhiteSpace(project.ProjectFilePath)
                    ? project.ProjectFilePath
                    : graphNode?.FilePath ?? string.Empty;

                return new VerificationProject
                {
                    ProjectName = projectName,
                    ProjectFilePath = projectFilePath,
                    IsTestProject = IsLikelyTestProjectName(projectName) || IsLikelyTestProjectName(projectFilePath),
                    Reasons = project.BindingReasons
                        .Concat(project.IssueIds.Select(issueId => "build issue: " + issueId))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(8)
                        .ToArray(),
                };
            })
            .GroupBy(project =>
                !string.IsNullOrWhiteSpace(project.ProjectFilePath)
                    ? project.ProjectFilePath
                    : project.ProjectName,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static RecommendedNextAction[] AddBuildFailureCommandActions(
        IEnumerable<RecommendedNextAction> existing,
        IEnumerable<VerificationCommand> commands)
    {
        var commandActions = commands
            .Take(5)
            .Select(command => new RecommendedNextAction
            {
                Kind = command.Command.Contains(" test ", StringComparison.OrdinalIgnoreCase)
                    ? WorkflowActionKind.RunTests
                    : WorkflowActionKind.RunBuild,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Reason = "Build failure session generated a focused verification command.",
                Confidence = command.Confidence,
                TargetProjectName = command.Scope,
                SuggestedTool = "shell",
                SuggestedCommand = command.Command,
            });

        return existing
            .Concat(commandActions)
            .GroupBy(action => string.Join("|", action.Kind.ToString(), action.SuggestedTool, action.SuggestedCommand, action.TargetProjectName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(12)
            .ToArray();
    }

    private static bool IsLikelyTestProjectName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(value);
        return name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tests", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<BuildFailureIssueBindingResult> BindBuildFailureIssueAsync(
        BuildIssue issue,
        ProjectGraph projectGraph,
        WorkspaceStatus workspaceStatus,
        int contextLines,
        int maxCharsPerSnippet,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target,
        ICollection<string> diagnostics,
        Action markPartial,
        CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        var project = ResolveBuildFailureProject(issue, projectGraph, workspaceStatus, reasons);
        SourceContextSnippet? sourceContext = null;
        if (issue.Span is not null && !string.IsNullOrWhiteSpace(issue.Span.FilePath))
        {
            var result = await _workspaceBridge.GetSourceContextAsync(
                    CreateSourceContextRequest(
                        symbolKey: null,
                        issue.Span.FilePath,
                        issue.Span.StartLine <= 0 ? 1 : issue.Span.StartLine,
                        issue.Span.StartColumn <= 0 ? 1 : issue.Span.StartColumn,
                        contextLines,
                        maxCharsPerSnippet,
                        maxSnippets: 1,
                        includeGeneratedCode,
                        target),
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var diagnostic in result.Diagnostics)
            {
                diagnostics.Add($"BuildFailureSourceContext[{CreateBuildIssueBindingKey(issue)}]: {diagnostic}");
            }
            if (result.IsPartial)
            {
                markPartial();
            }

            sourceContext = result.Items.FirstOrDefault();
            if (sourceContext is not null)
            {
                reasons.Add(sourceContext.Symbol is null
                    ? "source context resolved"
                    : "enclosing symbol resolved");
                if (!string.IsNullOrWhiteSpace(sourceContext.ProjectName))
                {
                    reasons.Add("project from source context");
                    var sourceContextProject = FindProjectByName(projectGraph, sourceContext.ProjectName);
                    if (sourceContextProject is not null
                        && (project is null || string.Equals(project.FilePath, issue.ProjectName, StringComparison.OrdinalIgnoreCase)))
                    {
                        project = sourceContextProject;
                    }
                }
            }
        }

        var relatedTests = await QueryBuildFailureRelatedTestsAsync(
                issue,
                sourceContext,
                includeGeneratedCode,
                target,
                diagnostics,
                markPartial,
                cancellationToken)
            .ConfigureAwait(false);
        if (relatedTests.Length > 0)
        {
            reasons.Add("related tests resolved");
        }

        var binding = new BuildFailureIssueBinding
        {
            IssueId = issue.Id,
            IssueKind = issue.Kind,
            IssueSpan = issue.Span,
            IssueProjectName = issue.ProjectName,
            BoundProjectName = project?.ProjectName ?? sourceContext?.ProjectName ?? issue.ProjectName,
            BoundProjectFilePath = project?.FilePath ?? string.Empty,
            EnclosingSymbol = sourceContext?.Symbol,
            EnclosingContextKind = sourceContext?.ContextKind ?? string.Empty,
            EnclosingSpan = sourceContext?.FocusSpan,
            RelatedTests = relatedTests,
            Confidence = CreateBuildFailureBindingConfidence(issue, project, sourceContext, relatedTests),
            BindingReasons = reasons
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
        return new BuildFailureIssueBindingResult(binding, sourceContext);
    }

    private async Task<RelatedTestDescriptor[]> QueryBuildFailureRelatedTestsAsync(
        BuildIssue issue,
        SourceContextSnippet? sourceContext,
        bool includeGeneratedCode,
        VisualStudioBridgeTarget? target,
        ICollection<string> diagnostics,
        Action markPartial,
        CancellationToken cancellationToken)
    {
        if (issue.Span is null || string.IsNullOrWhiteSpace(issue.Span.FilePath))
        {
            return Array.Empty<RelatedTestDescriptor>();
        }

        var result = await _workspaceBridge.FindRelatedTestsAsync(
                new RelatedTestsRequest
                {
                    Target = target,
                    SymbolKey = sourceContext?.Symbol?.Key,
                    FilePath = issue.Span.FilePath,
                    Position = new SourceSpan
                    {
                        FilePath = issue.Span.FilePath,
                        StartLine = issue.Span.StartLine <= 0 ? 1 : issue.Span.StartLine,
                        StartColumn = issue.Span.StartColumn <= 0 ? 1 : issue.Span.StartColumn,
                        EndLine = issue.Span.StartLine <= 0 ? 1 : issue.Span.StartLine,
                        EndColumn = issue.Span.StartColumn <= 0 ? 1 : issue.Span.StartColumn,
                    },
                    MaxResults = 10,
                    IncludeGeneratedCode = includeGeneratedCode,
                },
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var diagnostic in result.Diagnostics)
        {
            diagnostics.Add($"BuildFailureRelatedTests[{CreateBuildIssueBindingKey(issue)}]: {diagnostic}");
        }
        if (result.IsPartial)
        {
            markPartial();
        }

        return result.Items.ToArray();
    }

    private async Task<ProjectGraph> TryGetBuildFailureProjectGraphAsync(
        VisualStudioBridgeTarget? target,
        ICollection<string> diagnostics,
        Action markPartial,
        CancellationToken cancellationToken)
    {
        var result = await _workspaceBridge.GetProjectGraphAsync(
                new ProjectGraphRequest
                {
                    Target = target,
                    IncludeMetadataReferences = false,
                    MaxProjects = 500,
                    MaxMetadataReferencesPerProject = 0,
                },
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var diagnostic in result.Diagnostics)
        {
            diagnostics.Add("BuildFailureProjectGraph: " + diagnostic);
        }
        if (result.IsPartial)
        {
            markPartial();
        }

        return result.Items.FirstOrDefault() ?? new ProjectGraph();
    }

    private static ProjectGraphNode? ResolveBuildFailureProject(
        BuildIssue issue,
        ProjectGraph projectGraph,
        WorkspaceStatus workspaceStatus,
        ICollection<string> reasons)
    {
        if (!string.IsNullOrWhiteSpace(issue.ProjectName))
        {
            var byName = FindProjectByName(projectGraph, issue.ProjectName);
            if (byName is not null)
            {
                reasons.Add("project from build issue");
                return byName;
            }

            var byPath = projectGraph.Nodes.FirstOrDefault(node =>
                !string.IsNullOrWhiteSpace(node.FilePath)
                && string.Equals(Path.GetFullPath(node.FilePath), Path.GetFullPath(issue.ProjectName), StringComparison.OrdinalIgnoreCase));
            if (byPath is not null)
            {
                reasons.Add("project file from build issue");
                return byPath;
            }

            var projectFileName = Path.GetFileNameWithoutExtension(issue.ProjectName);
            if (!string.IsNullOrWhiteSpace(projectFileName))
            {
                var byFileName = projectGraph.Nodes.FirstOrDefault(node =>
                    string.Equals(Path.GetFileNameWithoutExtension(node.FilePath), projectFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(node.ProjectName, projectFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(node.AssemblyName, projectFileName, StringComparison.OrdinalIgnoreCase));
                if (byFileName is not null)
                {
                    reasons.Add("project file name from build issue");
                    return byFileName;
                }
            }
        }

        if (issue.Span is not null && !string.IsNullOrWhiteSpace(issue.Span.FilePath))
        {
            var resolvedPath = ResolvePathForWorkspace(workspaceStatus, issue.Span.FilePath);
            var byFile = FindProjectForFile(projectGraph, resolvedPath);
            if (byFile is not null)
            {
                reasons.Add("project inferred from issue file path");
                return byFile;
            }
        }

        return null;
    }

    private static WorkspaceStatus CreateWorkspaceStatusFromTaskContext(TaskContextSummary taskContext, VisualStudioBridgeTarget? target)
    {
        return new WorkspaceStatus
        {
            InstanceId = target?.InstanceId ?? taskContext.TargetInstanceId,
            SolutionPath = target?.SolutionPath ?? taskContext.TargetSolutionPath,
        };
    }

    private static string CreateBuildFailureBindingConfidence(
        BuildIssue issue,
        ProjectGraphNode? project,
        SourceContextSnippet? sourceContext,
        RelatedTestDescriptor[] relatedTests)
    {
        if (sourceContext?.Symbol is not null && project is not null && relatedTests.Length > 0)
        {
            return "high";
        }

        if (sourceContext is not null || project is not null || issue.Span is not null)
        {
            return "medium";
        }

        return "low";
    }

    private static void AddBuildFailureProjectBinding(
        IDictionary<string, BuildFailureProjectBindingAccumulator> projects,
        BuildFailureIssueBinding binding,
        BuildIssue issue)
    {
        if (string.IsNullOrWhiteSpace(binding.BoundProjectName) && string.IsNullOrWhiteSpace(binding.BoundProjectFilePath))
        {
            return;
        }

        var key = !string.IsNullOrWhiteSpace(binding.BoundProjectFilePath)
            ? binding.BoundProjectFilePath
            : binding.BoundProjectName;
        if (!projects.TryGetValue(key, out var accumulator))
        {
            accumulator = new BuildFailureProjectBindingAccumulator(binding.BoundProjectName, binding.BoundProjectFilePath, binding.Confidence);
            projects.Add(key, accumulator);
        }

        accumulator.AddIssue(issue.Id);
        accumulator.AddReasons(binding.BindingReasons);
    }

    private static void AddBuildFailureSourceSnippet(
        ICollection<SourceContextSnippet> sourceSnippets,
        SourceContextSnippet? sourceContext)
    {
        if (sourceContext is null || string.IsNullOrWhiteSpace(sourceContext.FocusSpan.FilePath))
        {
            return;
        }

        var key = string.Join(
            "|",
            sourceContext.FocusSpan.FilePath,
            sourceContext.FocusSpan.StartLine.ToString(),
            sourceContext.FocusSpan.StartColumn.ToString(),
            sourceContext.Symbol?.Key?.Value ?? string.Empty);
        var existing = sourceSnippets.Any(snippet => string.Equals(
            string.Join(
                "|",
                snippet.FocusSpan.FilePath,
                snippet.FocusSpan.StartLine.ToString(),
                snippet.FocusSpan.StartColumn.ToString(),
                snippet.Symbol?.Key?.Value ?? string.Empty),
            key,
            StringComparison.OrdinalIgnoreCase));
        if (!existing)
        {
            sourceSnippets.Add(sourceContext);
        }
    }

    private static string CreateBuildIssueBindingKey(BuildIssue issue)
    {
        return string.Join(
            "|",
            issue.Id,
            issue.Span?.FilePath ?? issue.ProjectName,
            issue.Span?.StartLine.ToString() ?? string.Empty,
            issue.Span?.StartColumn.ToString() ?? string.Empty);
    }

    private static WorkflowBudget CreateBuildFailureBudget(int maxDiagnostics, int maxSourceSnippets, int maxCharsPerSnippet)
    {
        return new WorkflowBudget
        {
            MaxToolCalls = 5,
            MaxReturnedChars = Math.Max(12000, maxSourceSnippets * Math.Max(1000, maxCharsPerSnippet)),
            MaxElapsedMilliseconds = 30000,
            MaxProjects = 20,
            MaxDiagnostics = maxDiagnostics,
            MaxReferences = 0,
            AllowWholeSolution = false,
        };
    }

    private static WorkflowTelemetry CreateBuildFailureTelemetry(CSharpBuildFailureContext context, IReadOnlyCollection<string> diagnostics, bool isPartial)
    {
        var returnedCharacters = context.SourceSnippets.Sum(snippet => snippet.Text?.Length ?? 0)
            + (context.BuildOutputWindow?.Text.Length ?? 0)
            + context.Diagnostics.Sum(diagnostic => diagnostic.Message.Length);
        return new WorkflowTelemetry
        {
            WorkflowName = "investigate_csharp_build_failure",
            ReturnedItemCount = (context.BuildTriage?.Issues.Length ?? 0) + context.Diagnostics.Length + context.SourceSnippets.Length,
            ReturnedCharacterCount = returnedCharacters,
            ResourceLinkCount = 1,
            PartialReasons = isPartial ? diagnostics.Take(8).ToArray() : Array.Empty<string>(),
        };
    }

    private static string CreateAgenticTaskId()
    {
        return $"task-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}".Substring(0, 38);
    }

    public async Task<WorkspaceQueryResult<CSharpRegressionScopePlan>> PlanCSharpRegressionScope(
        string? problemText = null,
        string? buildOutput = null,
        string? buildLogFilePath = null,
        string[]? changedFiles = null,
        string[]? areaPaths = null,
        string? symbolQuery = null,
        string? filePath = null,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        string? projectName = null,
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        bool includeVisualStudioBuildOutput = true,
        int maxVisualStudioBuildOutputCharacters = 20000,
        int maxBuildIssues = 20,
        int maxDiagnostics = 30,
        int maxRelatedTests = 20,
        int maxRelatedItems = 20,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        var verificationResult = await PlanCSharpVerification(
                buildOutput,
                buildLogFilePath,
                changedFiles,
                symbolQuery,
                filePath,
                includePathPatterns,
                excludePathPatterns,
                projectName,
                minimumSeverity,
                noiseProfile,
                includeWholeSolutionDiagnostics: false,
                includeVisualStudioBuildOutput,
                maxVisualStudioBuildOutputCharacters,
                maxBuildIssues,
                maxDiagnostics,
                maxRelatedTests,
                includeGeneratedCode,
                targetPipeName,
                targetInstanceId,
                targetSolutionPath,
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(verificationResult.Diagnostics);
        var verification = verificationResult.Items.FirstOrDefault() ?? new CSharpVerificationPlan { Status = "VerificationUnavailable" };

        var reviewResult = await ReviewCSharpChange(
                problemText,
                buildOutput,
                buildLogFilePath,
                changedFiles,
                areaPaths,
                symbolQuery,
                projectName,
                includePathPatterns,
                excludePathPatterns,
                minimumSeverity,
                noiseProfile,
                maxBuildIssues,
                maxDiagnostics,
                maxSymbols: 20,
                maxRelatedItems,
                maxFiles: 20,
                includeGeneratedCode,
                targetPipeName,
                targetInstanceId,
                targetSolutionPath,
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(reviewResult.Diagnostics.Select(diagnostic => "RegressionReview: " + diagnostic));
        var review = reviewResult.Items.FirstOrDefault();
        var commands = verification.RecommendedCommands;
        var smoke = commands.Where(command => command.Scope.Contains("build", StringComparison.OrdinalIgnoreCase)).Take(3).ToArray();
        var focused = commands.Where(command => command.Scope.Contains("test", StringComparison.OrdinalIgnoreCase)).Take(5).ToArray();
        var broad = commands.Where(command =>
                command.Scope.Contains("solution", StringComparison.OrdinalIgnoreCase)
                || command.Command.Contains(".sln", StringComparison.OrdinalIgnoreCase)
                || command.Command.Contains(".slnx", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToArray();

        return Success(
            new CSharpRegressionScopePlan
            {
                Status = verification.Status == "Ready" ? "Ready" : verification.Status,
                VerificationPlan = verification,
                ChangeReview = review,
                SmokeCommands = smoke,
                FocusedCommands = focused,
                BroadCommands = broad,
                RecommendedNextActions = verification.RecommendedNextActions
                    .Concat(review?.RecommendedNextActions ?? Array.Empty<RecommendedNextAction>())
                    .GroupBy(action => string.Join("|", action.Kind.ToString(), action.SuggestedCommand, action.SuggestedTool, action.TargetFilePath), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Take(12)
                    .ToArray(),
                SuggestedNextSteps = verification.SuggestedNextSteps
                    .Concat(review?.SuggestedNextSteps ?? Array.Empty<string>())
                    .Append("Run smoke/focused commands before broad commands; feed any failures back into investigate_csharp_build_failure.")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            },
            diagnostics,
            verificationResult.IsPartial || reviewResult.IsPartial);
    }

    public async Task<WorkspaceQueryResult<DebugSessionPreparationPlan>> PrepareDebugSession(
        int maxBreakpoints = 100,
        int maxFrames = 50,
        int maxSourceSnippets = 6,
        int contextLines = 3,
        int maxCharsPerSnippet = 8000,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxSourceSnippets is < 0 or > 50)
        {
            return Failure<DebugSessionPreparationPlan>("MaxSourceSnippets must be between 0 and 50.");
        }

        var diagnostics = new List<string>();
        var healthResult = await CheckVisualStudioCSharpNavigatorHealth(
                includeDiagnosticsPreview: false,
                targetPipeName: targetPipeName,
                targetInstanceId: targetInstanceId,
                targetSolutionPath: targetSolutionPath,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(healthResult.Diagnostics);
        var health = healthResult.Items.FirstOrDefault() ?? new NavigatorHealthReport { Status = "HealthUnavailable" };

        var statusResult = await GetDebuggerStatus(targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(statusResult.Diagnostics.Select(diagnostic => "DebugStatus: " + diagnostic));
        var status = statusResult.Items.FirstOrDefault();

        var breakpointResult = await ListDebugBreakpoints(maxBreakpoints, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(breakpointResult.Diagnostics.Select(diagnostic => "DebugBreakpoints: " + diagnostic));

        var frames = Array.Empty<DebugStackFrameInfo>();
        if (status?.IsPaused == true)
        {
            var stackResult = await GetDebugCallStack(null, maxFrames, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(stackResult.Diagnostics.Select(diagnostic => "DebugCallStack: " + diagnostic));
            frames = stackResult.Items.ToArray();
        }

        var target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath);
        var snippets = new List<SourceContextSnippet>();
        foreach (var frame in frames.Where(frame => frame.Span is not null).Take(maxSourceSnippets))
        {
            var span = frame.Span!;
            var snippetResult = await _workspaceBridge.GetSourceContextAsync(
                    CreateSourceContextRequest(null, span.FilePath, span.StartLine <= 0 ? 1 : span.StartLine, span.StartColumn <= 0 ? 1 : span.StartColumn, contextLines, maxCharsPerSnippet, 1, includeGeneratedCode, target),
                    cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(snippetResult.Diagnostics.Select(diagnostic => "DebugSourceContext: " + diagnostic));
            snippets.AddRange(snippetResult.Items);
        }

        var actions = new List<RecommendedNextAction>();
        if (status is null || !status.IsDebugging)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.StartDebugging,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Confidence = "medium",
                Reason = "No active debug session was detected. Start debugging only after target selection and expected breakpoints are clear.",
                SuggestedTool = "start_debugging",
            });
        }
        else if (status.IsPaused)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.CollectDebugContext,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Confidence = "high",
                Reason = "Debugger is paused; collect call stack, variables, and source context before continuing.",
                SuggestedTool = "get_debug_stack_frame_variables",
            });
        }

        return Success(
            new DebugSessionPreparationPlan
            {
                Status = health.Status == "Ready" ? "Ready" : health.Status,
                Health = health,
                DebuggerStatus = status,
                Breakpoints = breakpointResult.Items.ToArray(),
                CallStack = frames,
                SourceSnippets = snippets.Take(maxSourceSnippets).ToArray(),
                RecommendedNextActions = actions.ToArray(),
                SuggestedNextSteps = health.SuggestedNextSteps
                    .Concat(new[] { "Use explicit targetInstanceId/targetPipeName for all debug control tools; do not automate business UI through debugger expressions." })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            },
            diagnostics,
            healthResult.IsPartial || statusResult.IsPartial || breakpointResult.IsPartial);
    }

    public async Task<WorkspaceQueryResult<CSharpDebugScenarioPlan>> PlanCSharpDebugScenario(
        string? problemText = null,
        string? scenarioName = null,
        string? breakpointFilePath = null,
        int? breakpointLine = null,
        string[]? artifactPaths = null,
        bool stopDebuggingAtEnd = false,
        int maxBreakpoints = 100,
        int maxFrames = 50,
        int maxSourceSnippets = 6,
        int contextLines = 3,
        int maxCharsPerSnippet = 8000,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (breakpointLine is < 1)
        {
            return Failure<CSharpDebugScenarioPlan>("BreakpointLine must be positive when provided.");
        }

        var debugResult = await PrepareDebugSession(
                maxBreakpoints,
                maxFrames,
                maxSourceSnippets,
                contextLines,
                maxCharsPerSnippet,
                includeGeneratedCode,
                targetPipeName,
                targetInstanceId,
                targetSolutionPath,
                cancellationToken)
            .ConfigureAwait(false);
        var current = debugResult.Items.FirstOrDefault() ?? new DebugSessionPreparationPlan
        {
            Status = "DebugSessionUnavailable",
            SuggestedNextSteps = new[] { "Open a C# solution in Visual Studio and retry plan_csharp_debug_scenario with an explicit target." },
        };
        var artifacts = NormalizePatterns(artifactPaths);
        var steps = CreateDebugScenarioSteps(
            current,
            breakpointFilePath,
            breakpointLine,
            artifacts,
            stopDebuggingAtEnd);

        return Success(
            new CSharpDebugScenarioPlan
            {
                Status = current.Status,
                ScenarioName = string.IsNullOrWhiteSpace(scenarioName) ? "debug-scenario" : scenarioName!,
                ProblemText = problemText ?? string.Empty,
                CurrentSession = current,
                Steps = steps,
                RecommendedNextActions = CreateDebugScenarioRecommendedActions(current, steps),
                SuggestedNextSteps = current.SuggestedNextSteps
                    .Concat(new[]
                    {
                        "Execute debug scenario steps one at a time; every mutating debug control call must use an explicit target.",
                        "Collect call stack, variables, source snippets, and artifacts before continuing or stopping the debuggee.",
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            },
            debugResult.Diagnostics,
            debugResult.IsPartial);
    }

    public Task<WorkspaceQueryResult<CSharpRepoWorkflowAnalysis>> AnalyzeCSharpRepoWorkflow(
        string? rootDirectory = null,
        string? preferredName = null,
        int maxDepth = 4,
        int maxSolutions = 8,
        int maxProjects = 80,
        CancellationToken cancellationToken = default)
    {
        if (maxDepth is < 0 or > 12)
        {
            return Task.FromResult(Failure<CSharpRepoWorkflowAnalysis>("MaxDepth must be between 0 and 12."));
        }

        if (maxSolutions is < 1 or > 50)
        {
            return Task.FromResult(Failure<CSharpRepoWorkflowAnalysis>("MaxSolutions must be between 1 and 50."));
        }

        if (maxProjects is < 1 or > 500)
        {
            return Task.FromResult(Failure<CSharpRepoWorkflowAnalysis>("MaxProjects must be between 1 and 500."));
        }

        var diagnostics = new List<string>();
        var root = ResolveWorkflowRootDirectory(rootDirectory, diagnostics);
        if (root is null)
        {
            return Task.FromResult(Success(
                new CSharpRepoWorkflowAnalysis
                {
                    Status = "RootNotFound",
                    SuggestedNextSteps = new[] { "Pass rootDirectory that contains a C# solution or project." },
                },
                diagnostics,
                isPartial: true));
        }

        var solutions = FindSolutionCandidates(root, preferredName, maxDepth, includeSlnx: true, maxSolutions, diagnostics, cancellationToken).ToArray();
        var selectedSolution = solutions.FirstOrDefault();
        var projects = EnumerateCSharpProjectFiles(root, maxDepth, maxProjects, diagnostics, cancellationToken).ToArray();
        var testProjects = projects.Where(IsLikelyTestProjectPath).ToArray();
        var buildCommands = CreateRepoWorkflowBuildCommands(selectedSolution, projects);
        var testCommands = CreateRepoWorkflowTestCommands(testProjects, selectedSolution);
        var noisyPaths = CreateDefaultNoisyPathPatterns();

        return Task.FromResult(Success(
            new CSharpRepoWorkflowAnalysis
            {
                Status = selectedSolution is null && projects.Length == 0 ? "NoCSharpWorkspaceFound" : "Ready",
                RootDirectory = root.FullName,
                SolutionCandidates = solutions,
                SelectedSolutionPath = selectedSolution?.FilePath ?? string.Empty,
                ProjectFiles = projects,
                TestProjectFiles = testProjects,
                BuildCommands = buildCommands,
                TestCommands = testCommands,
                NoisyPathPatterns = noisyPaths,
                ToolRoutingRules = CreateDefaultAgentToolRoutingRules(),
                ArtifactHints = CreateDefaultArtifactHints(),
                RecommendedNextActions = CreateRepoWorkflowRecommendedActions(selectedSolution, testProjects),
                SuggestedNextSteps = CreateRepoWorkflowSuggestedNextSteps(selectedSolution, testProjects),
            },
            diagnostics,
            diagnostics.Any(diagnostic => diagnostic.Contains("Truncated", StringComparison.OrdinalIgnoreCase))));
    }

    public async Task<WorkspaceQueryResult<CSharpAgentInstructionsDraft>> GenerateCSharpAgentInstructions(
        string? rootDirectory = null,
        string? preferredName = null,
        string? suggestedFileName = null,
        int maxDepth = 4,
        int maxSolutions = 8,
        int maxProjects = 80,
        CancellationToken cancellationToken = default)
    {
        var analysisResult = await AnalyzeCSharpRepoWorkflow(
                rootDirectory,
                preferredName,
                maxDepth,
                maxSolutions,
                maxProjects,
                cancellationToken)
            .ConfigureAwait(false);
        var analysis = analysisResult.Items.FirstOrDefault() ?? new CSharpRepoWorkflowAnalysis
        {
            Status = "AnalysisUnavailable",
        };
        var content = BuildAgentInstructionsMarkdown(analysis);

        return Success(
            new CSharpAgentInstructionsDraft
            {
                Status = analysis.Status,
                SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "AGENTS.md" : suggestedFileName!.Trim(),
                Format = "markdown",
                Content = content,
                RepoWorkflow = analysis,
                Sections = new[]
                {
                    "Project entry",
                    "Build and test",
                    "Noise and scope",
                    "Tool routing",
                    "Artifacts and reports",
                    "Agent work packets",
                },
                SuggestedNextSteps = new[]
                {
                    "Review this draft before copying it into AGENTS.md, Copilot instructions, or a project skill.",
                    "Keep project-specific constraints short and route C# semantic work through the Visual Studio C# Dev Workflow.",
                },
            },
            analysisResult.Diagnostics,
            analysisResult.IsPartial);
    }

    public Task<WorkspaceQueryResult<CSharpAgentWorkSplitPlan>> SplitCSharpAgentWork(
        string objective,
        string[]? changedFiles = null,
        string[]? scopeSymbols = null,
        string[]? evidenceResources = null,
        string[]? areaPaths = null,
        int maxPackets = 4,
        int maxFilesPerPacket = 8,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objective))
        {
            return Task.FromResult(Failure<CSharpAgentWorkSplitPlan>("Objective is required."));
        }

        if (maxPackets is < 1 or > 12)
        {
            return Task.FromResult(Failure<CSharpAgentWorkSplitPlan>("MaxPackets must be between 1 and 12."));
        }

        if (maxFilesPerPacket is < 1 or > 50)
        {
            return Task.FromResult(Failure<CSharpAgentWorkSplitPlan>("MaxFilesPerPacket must be between 1 and 50."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var files = NormalizePatterns(changedFiles)
            .Concat(NormalizePatterns(areaPaths))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var symbols = NormalizePatterns(scopeSymbols);
        var resources = NormalizePatterns(evidenceResources);
        var packets = CreateAgentWorkPackets(objective.Trim(), files, symbols, resources, maxPackets, maxFilesPerPacket);

        return Task.FromResult(Success(
            new CSharpAgentWorkSplitPlan
            {
                Status = packets.Length == 0 ? "NoScope" : "Ready",
                Objective = objective.Trim(),
                Packets = packets,
                ExpectedFindingSchema = GetAgentFindingSchema(),
                MergeInstructions = new[]
                {
                    "Each child agent must stay inside its packet scope and report packetId.",
                    "The main agent should call merge_csharp_agent_findings with expectedPacketIds before editing shared files.",
                    "If packets disagree, prefer file/symbol scoped evidence over whole-solution assumptions.",
                },
                RecommendedNextActions = packets
                    .Take(5)
                    .Select(packet => new RecommendedNextAction
                    {
                        Kind = WorkflowActionKind.NarrowScope,
                        EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                        Confidence = "medium",
                        Reason = packet.Objective,
                        SuggestedTool = packet.AllowedTools.FirstOrDefault() ?? "get_csharp_task_context",
                    })
                    .ToArray(),
                SuggestedNextSteps = new[]
                {
                    "Send each packet to a separate agent only when the scopes do not overlap heavily.",
                    "Do not let child agents run broad solution diagnostics unless the packet explicitly allows it.",
                },
            },
            Array.Empty<string>(),
            isPartial: false));
    }

    public Task<WorkspaceQueryResult<CSharpAgentFindingMergeResult>> MergeCSharpAgentFindings(
        string? objective = null,
        string[]? expectedPacketIds = null,
        CSharpAgentFinding[]? findings = null,
        string[]? findingSummaries = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expected = NormalizePatterns(expectedPacketIds);
        var normalizedFindings = NormalizeAgentFindings(findings, findingSummaries);
        var completedIds = normalizedFindings
            .Where(finding => !string.IsNullOrWhiteSpace(finding.PacketId))
            .Select(finding => finding.PacketId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missing = expected.Length == 0
            ? Array.Empty<string>()
            : expected.Except(completedIds, StringComparer.OrdinalIgnoreCase).ToArray();
        var status = missing.Length > 0 ? "MissingPackets" : normalizedFindings.Length == 0 ? "NoFindings" : "Ready";

        return Task.FromResult(Success(
            new CSharpAgentFindingMergeResult
            {
                Status = status,
                Objective = objective?.Trim() ?? string.Empty,
                ExpectedPacketCount = expected.Length,
                CompletedPacketCount = completedIds.Length,
                MissingPacketIds = missing,
                MergedSummary = CreateMergedFindingSummary(objective, normalizedFindings, missing),
                Findings = normalizedFindings.SelectMany(finding => finding.Findings).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Risks = normalizedFindings.SelectMany(finding => finding.Risks).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                FollowUpWork = normalizedFindings.SelectMany(finding => finding.RecommendedNextActions).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ExpectedOutputSchema = GetAgentFindingSchema(),
            },
            missing.Length > 0 ? new[] { "AgentFindingsMissingPackets: " + string.Join(", ", missing) } : Array.Empty<string>(),
            missing.Length > 0));
    }

    private static CSharpDebugScenarioStep[] CreateDebugScenarioSteps(
        DebugSessionPreparationPlan current,
        string? breakpointFilePath,
        int? breakpointLine,
        string[] artifactPaths,
        bool stopDebuggingAtEnd)
    {
        var steps = new List<CSharpDebugScenarioStep>();
        var order = 1;
        if (!string.IsNullOrWhiteSpace(breakpointFilePath) && breakpointLine is not null)
        {
            steps.Add(CreateDebugScenarioStep(
                order++,
                "configure",
                "set_debug_breakpoint",
                "Set the expected source breakpoint before starting or continuing the debuggee.",
                $"filePath={breakpointFilePath}; line={breakpointLine}; explicit target required",
                isMutating: true,
                requiresExplicitTarget: true,
                stopIf: "Stop if Visual Studio target is ambiguous, file path is not in the loaded solution, or breakpoint binding fails.",
                expectedEvidence: "DebugControlResult plus a subsequent prepare_debug_session breakpoint entry."));
        }

        if (current.DebuggerStatus is null || !current.DebuggerStatus.IsDebugging)
        {
            steps.Add(CreateDebugScenarioStep(
                order++,
                "start",
                "start_debugging",
                "Start the selected Visual Studio debug target.",
                "explicit target; timeoutMilliseconds tuned for application startup",
                isMutating: true,
                requiresExplicitTarget: true,
                stopIf: "Stop if startup project is wrong, debugging fails to start, or target selection is ambiguous.",
                expectedEvidence: "DebuggerStatus.IsDebugging=true from prepare_debug_session."));
        }
        else if (current.DebuggerStatus.IsPaused)
        {
            steps.Add(CreateDebugScenarioStep(
                order++,
                "collect",
                "prepare_debug_session",
                "Debugger is already paused; collect stack and source context before any continue/step action.",
                "maxFrames, maxSourceSnippets, explicit target",
                isMutating: false,
                requiresExplicitTarget: true,
                stopIf: "Stop if call stack/source snippets are empty or truncated for the frame under investigation.",
                expectedEvidence: "CallStack, SourceSnippets, and optional stack-frame variables."));
        }
        else
        {
            steps.Add(CreateDebugScenarioStep(
                order++,
                "wait",
                "break_debugging",
                "Debugger is running; break only when a manual pause is acceptable, otherwise wait for breakpoint/exception.",
                "explicit target; timeoutMilliseconds",
                isMutating: true,
                requiresExplicitTarget: true,
                stopIf: "Stop if pausing the debuggee would invalidate the reproduction.",
                expectedEvidence: "DebuggerStatus.IsPaused=true from prepare_debug_session."));
        }

        steps.Add(CreateDebugScenarioStep(
            order++,
            "wait",
            "prepare_debug_session",
            "Poll the debug state after start/continue/break and collect debugger evidence once paused.",
            "maxBreakpoints, maxFrames, maxSourceSnippets, explicit target",
            isMutating: false,
            requiresExplicitTarget: true,
            stopIf: "Stop if debugger never reaches paused state or source snippets are truncated.",
            expectedEvidence: "DebuggerStatus, Breakpoints, CallStack, SourceSnippets."));

        if (artifactPaths.Length > 0)
        {
            steps.Add(CreateDebugScenarioStep(
                order++,
                "collect",
                "collect_artifact_evidence",
                "Collect logs, reports, traces, JSON, or text outputs created by the debug scenario.",
                "artifactPaths=" + string.Join(", ", artifactPaths.Take(5)),
                isMutating: false,
                requiresExplicitTarget: false,
                stopIf: "Stop if expected artifacts are missing, stale, or too truncated for diagnosis.",
                expectedEvidence: "ArtifactEvidenceReport with error/warning lines and compact summaries."));
        }

        if (stopDebuggingAtEnd)
        {
            steps.Add(CreateDebugScenarioStep(
                order++,
                "cleanup",
                "stop_debugging",
                "Stop the debuggee after evidence has been collected.",
                "explicit target; timeoutMilliseconds",
                isMutating: true,
                requiresExplicitTarget: true,
                stopIf: "Stop if another user-owned debug session is active or evidence has not been collected yet.",
                expectedEvidence: "DebuggerStatus.IsDebugging=false from a final prepare_debug_session."));
        }

        return steps.ToArray();
    }

    private static CSharpDebugScenarioStep CreateDebugScenarioStep(
        int order,
        string phase,
        string toolName,
        string purpose,
        string argumentsSummary,
        bool isMutating,
        bool requiresExplicitTarget,
        string stopIf,
        string expectedEvidence)
    {
        return new CSharpDebugScenarioStep
        {
            Order = order,
            Phase = phase,
            ToolName = toolName,
            Purpose = purpose,
            ArgumentsSummary = argumentsSummary,
            IsMutating = isMutating,
            RequiresExplicitTarget = requiresExplicitTarget,
            StopIf = stopIf,
            ExpectedEvidence = expectedEvidence,
        };
    }

    private static RecommendedNextAction[] CreateDebugScenarioRecommendedActions(
        DebugSessionPreparationPlan current,
        IEnumerable<CSharpDebugScenarioStep> steps)
    {
        var stepActions = steps
            .Take(5)
            .Select(step => new RecommendedNextAction
            {
                Kind = step.ToolName switch
                {
                    "set_debug_breakpoint" => WorkflowActionKind.StartDebugging,
                    "start_debugging" => WorkflowActionKind.StartDebugging,
                    "break_debugging" => WorkflowActionKind.CollectDebugContext,
                    "prepare_debug_session" => WorkflowActionKind.CollectDebugContext,
                    "collect_artifact_evidence" => WorkflowActionKind.InspectArtifact,
                    "stop_debugging" => WorkflowActionKind.CleanDebugSession,
                    _ => WorkflowActionKind.Unknown,
                },
                EvidenceLevel = step.IsMutating ? WorkflowEvidenceLevel.Heuristic : WorkflowEvidenceLevel.Inference,
                Reason = step.Purpose,
                Confidence = step.IsMutating ? "medium" : "high",
                SuggestedTool = step.ToolName,
            });

        return current.RecommendedNextActions
            .Concat(stepActions)
            .GroupBy(action => string.Join("|", action.Kind.ToString(), action.SuggestedTool, action.Reason), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(12)
            .ToArray();
    }

    public async Task<WorkspaceQueryResult<CSharpWorkflowPerformanceSnapshot>> GetCSharpWorkflowPerformanceSnapshot(
        string? solutionPath = null,
        string? benchmarkProfile = null,
        int expectedToolCount = 83,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (expectedToolCount is < 1 or > 500)
        {
            return Failure<CSharpWorkflowPerformanceSnapshot>("ExpectedToolCount must be between 1 and 500.");
        }

        var diagnostics = new List<string>();
        var cacheStatistics = _queryCache.GetStatistics();
        var bridgeTelemetry = _bridgeTelemetryRecorder?.GetSummary() ?? default;
        var instancesResult = await ListVisualStudioInstances(includeStale: true, cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(instancesResult.Diagnostics);
        var activeInstances = instancesResult.Items
            .Where(instance => instance.IsAlive && !instance.IsStale)
            .ToArray();
        var staleInstances = instancesResult.Items
            .Where(instance => instance.IsStale || !instance.IsAlive)
            .ToArray();
        var resolvedSolution = FirstNonEmpty(solutionPath, targetSolutionPath, activeInstances.FirstOrDefault()?.SolutionPath);
        var profile = string.IsNullOrWhiteSpace(benchmarkProfile) ? "workflow" : benchmarkProfile.Trim();
        var bridgeStatus = activeInstances.Length > 0
            ? "Ready"
            : staleInstances.Length > 0 ? "OnlyStaleInstances" : "NoBridgeInstances";

        return Success(
            new CSharpWorkflowPerformanceSnapshot
            {
                Status = "Ready",
                BridgeStatus = bridgeStatus,
                TargetSolutionPath = resolvedSolution,
                ActiveInstanceCount = activeInstances.Length,
                StaleInstanceCount = staleInstances.Length,
                ToolCount = expectedToolCount,
                ExpectedToolCount = expectedToolCount,
                ActiveInstanceIds = activeInstances.Select(instance => instance.InstanceId).Where(id => !string.IsNullOrWhiteSpace(id)).Take(8).ToArray(),
                StaleInstanceIds = staleInstances.Select(instance => instance.InstanceId).Where(id => !string.IsNullOrWhiteSpace(id)).Take(8).ToArray(),
                RecommendedProfiles = new[] { "tool-schema", "workflow", "large", "agentic-resources" },
                SuggestedEnvironmentVariables = CreateWorkflowBenchmarkEnvironmentVariables(profile, resolvedSolution, activeInstances.FirstOrDefault()?.InstanceId),
                BudgetHints = CreateWorkflowPerformanceBudgetHints(),
                TelemetrySignals = CreateWorkflowTelemetrySignals(),
                CacheHitCount = cacheStatistics.HitCount,
                CacheMissCount = cacheStatistics.MissCount,
                CacheSingleFlightJoinCount = cacheStatistics.SingleFlightJoinCount,
                CacheEntryCount = cacheStatistics.EntryCount,
                CacheInFlightCount = cacheStatistics.InFlightCount,
                BridgeCallCount = bridgeTelemetry.TotalCallCount,
                BridgeFailedCallCount = bridgeTelemetry.FailedCallCount,
                BridgeRecentAverageElapsedMilliseconds = bridgeTelemetry.RecentAverageElapsedMilliseconds,
                BridgeRecentMaxElapsedMilliseconds = bridgeTelemetry.RecentMaxElapsedMilliseconds,
                BridgeRecentAverageExecutionMilliseconds = bridgeTelemetry.RecentAverageExecutionMilliseconds,
                SuggestedCommands = new[]
                {
                    new VerificationCommand
                    {
                        Command = "$env:CODE_NAVIGATOR_TEST_PROFILE='tool-schema'; dotnet run --project .\\artifacts\\mcp-stdio-e2e\\McpStdioE2e.csproj -c Release --no-restore",
                        Scope = "tool-schema",
                        Confidence = "high",
                        Reasons = new[] { "Verifies MCP tool count and required workflow tools." },
                    },
                    new VerificationCommand
                    {
                        Command = $"$env:CODE_NAVIGATOR_BENCHMARK_PROFILE='{profile}'; dotnet run --project .\\artifacts\\mcp-benchmark\\McpBenchmark.csproj -c Release --no-restore -- --iterations 1",
                        Scope = "benchmark",
                        Confidence = "medium",
                        Reasons = new[] { "Runs the configured workflow benchmark profile." },
                    },
                },
                SuggestedNextSteps = string.IsNullOrWhiteSpace(resolvedSolution)
                    ? new[] { "Pass solutionPath or targetSolutionPath before running large/workflow benchmark profiles.", "If BridgeStatus is NoBridgeInstances, call prepare_csharp_workspace before semantic benchmark profiles." }
                    : new[] { $"Use solution path {resolvedSolution} for workflow or large benchmark profile environment variables.", $"BridgeStatus={bridgeStatus}; prefer targetInstanceId when multiple Visual Studio instances are active." },
            },
            diagnostics,
            instancesResult.IsPartial);
    }

    private static string[] CreateWorkflowBenchmarkEnvironmentVariables(string profile, string solutionPath, string? instanceId)
    {
        var variables = new List<string>
        {
            "$env:CODE_NAVIGATOR_TEST_PROFILE='tool-schema'",
            $"$env:CODE_NAVIGATOR_BENCHMARK_PROFILE='{profile}'",
        };

        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            variables.Add($"$env:CODE_NAVIGATOR_TEST_SOLUTION='{solutionPath}'");
            variables.Add($"$env:CODE_NAVIGATOR_BENCHMARK_SOLUTION='{solutionPath}'");
        }

        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            variables.Add($"$env:CODE_NAVIGATOR_TEST_TARGET_INSTANCE_ID='{instanceId}'");
            variables.Add($"$env:CODE_NAVIGATOR_BENCHMARK_TARGET_INSTANCE_ID='{instanceId}'");
        }

        return variables.ToArray();
    }

    private static CSharpWorkflowBudgetHint[] CreateWorkflowPerformanceBudgetHints()
    {
        return new[]
        {
            new CSharpWorkflowBudgetHint
            {
                Name = "ReturnedCharacters",
                WarningThreshold = 18000,
                RecommendedLimit = 24000,
                Reason = "Keep task-level evidence compact enough for routine agent context.",
            },
            new CSharpWorkflowBudgetHint
            {
                Name = "ElapsedMilliseconds",
                WarningThreshold = 15000,
                RecommendedLimit = 30000,
                Reason = "Prefer scoped diagnostics, source context, and cached bridge reads before whole-solution scans.",
            },
            new CSharpWorkflowBudgetHint
            {
                Name = "ResourceLinks",
                WarningThreshold = 8,
                RecommendedLimit = 12,
                Reason = "Large evidence should move to MCP resources instead of the direct tool result.",
            },
        };
    }

    private static CSharpWorkflowTelemetrySignal[] CreateWorkflowTelemetrySignals()
    {
        return new[]
        {
            new CSharpWorkflowTelemetrySignal
            {
                Name = "WorkflowTelemetry.ReturnedCharacterCount",
                Source = "EvidencePacket.Telemetry",
                Interpretation = "Shows direct result size before following resource links.",
                RecommendedAction = "If it approaches the recommended limit, narrow file/symbol/project scope or read resources on demand.",
            },
            new CSharpWorkflowTelemetrySignal
            {
                Name = "WorkflowTelemetry.ResourceLinkCount",
                Source = "EvidencePacket.Telemetry",
                Interpretation = "Shows how much evidence was offloaded into MCP resources.",
                RecommendedAction = "Prefer reading the specific resource URI needed for the next edit or diagnosis.",
            },
            new CSharpWorkflowTelemetrySignal
            {
                Name = "ServerCacheHit",
                Source = "WorkspaceQueryResult.Diagnostics",
                Interpretation = "Means the server reused a short-lived read-only query result.",
                RecommendedAction = "Treat it as normal acceleration; rerun with a changed scope when a fresh Roslyn read is required.",
            },
            new CSharpWorkflowTelemetrySignal
            {
                Name = "IsPartial",
                Source = "WorkspaceQueryResult.IsPartial and diagnostics",
                Interpretation = "Means budget, truncation, timeout, or bridge limits affected the evidence.",
                RecommendedAction = "Inspect PartialReasons or diagnostics, then retry with narrower scope before editing.",
            },
        };
    }

    private static DirectoryInfo? ResolveWorkflowRootDirectory(string? rootDirectory, List<string> diagnostics)
    {
        var candidate = string.IsNullOrWhiteSpace(rootDirectory)
            ? Environment.CurrentDirectory
            : rootDirectory.Trim();
        try
        {
            var fullPath = Path.GetFullPath(candidate);
            if (!Directory.Exists(fullPath))
            {
                diagnostics.Add($"RepoWorkflowRootNotFound: {fullPath}");
                return null;
            }

            return new DirectoryInfo(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            diagnostics.Add($"RepoWorkflowRootInvalid: {candidate}: {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<string> EnumerateCSharpProjectFiles(
        DirectoryInfo rootDirectory,
        int maxDepth,
        int maxProjects,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var projects = new List<string>();
        var stack = new Stack<(DirectoryInfo Directory, int Depth)>();
        stack.Push((rootDirectory, 0));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = stack.Pop();
            FileInfo[] files;
            try
            {
                files = directory.GetFiles("*.csproj");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
            {
                diagnostics.Add($"ProjectSearchSkippedDirectory: {directory.FullName}: {ex.Message}");
                continue;
            }

            foreach (var file in files.OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase))
            {
                projects.Add(file.FullName);
                if (projects.Count >= maxProjects)
                {
                    diagnostics.Add($"ProjectFilesTruncated: returning {maxProjects} project file(s).");
                    return projects;
                }
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            DirectoryInfo[] children;
            try
            {
                children = directory.GetDirectories();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
            {
                diagnostics.Add($"ProjectSearchSkippedDirectory: {directory.FullName}: {ex.Message}");
                continue;
            }

            foreach (var child in children.OrderByDescending(child => child.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldSkipSolutionSearchDirectory(child.Name))
                {
                    stack.Push((child, depth + 1));
                }
            }
        }

        return projects.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsLikelyTestProjectPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var normalized = NormalizePathForMatching(path);
        return fileName.Contains("Test", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(".tests/", StringComparison.OrdinalIgnoreCase);
    }

    private static VerificationCommand[] CreateRepoWorkflowBuildCommands(CSharpSolutionCandidate? selectedSolution, string[] projects)
    {
        var commands = new List<VerificationCommand>();
        if (selectedSolution is not null)
        {
            commands.Add(new VerificationCommand
            {
                Command = $"dotnet build {QuotePowerShellPath(selectedSolution.FilePath)} -c Release -v:minimal --no-restore",
                Scope = "solution-build",
                Confidence = "high",
                Reasons = new[] { "Builds the selected solution without restore noise." },
            });
        }
        else
        {
            commands.AddRange(projects.Take(3).Select(project => new VerificationCommand
            {
                Command = $"dotnet build {QuotePowerShellPath(project)} -c Release -v:minimal --no-restore",
                Scope = "project-build",
                Confidence = "medium",
                Reasons = new[] { "No solution was selected; build a discovered project instead." },
            }));
        }

        return commands.ToArray();
    }

    private static VerificationCommand[] CreateRepoWorkflowTestCommands(string[] testProjects, CSharpSolutionCandidate? selectedSolution)
    {
        if (testProjects.Length > 0)
        {
            return testProjects.Take(5).Select(project => new VerificationCommand
            {
                Command = $"dotnet test {QuotePowerShellPath(project)} -c Release --no-build -v:minimal --nologo",
                Scope = "test-project",
                Confidence = "high",
                Reasons = new[] { "Runs a discovered C# test project after build." },
            }).ToArray();
        }

        if (selectedSolution is not null)
        {
            return new[]
            {
                new VerificationCommand
                {
                    Command = $"dotnet test {QuotePowerShellPath(selectedSolution.FilePath)} -c Release --no-build -v:minimal --nologo",
                    Scope = "solution-test",
                    Confidence = "low",
                    Reasons = new[] { "No test project was identified; solution-level test may be broader or unsupported." },
                },
            };
        }

        return Array.Empty<VerificationCommand>();
    }

    private static string QuotePowerShellPath(string path)
    {
        return "\"" + path.Replace("\"", "`\"", StringComparison.Ordinal) + "\"";
    }

    private static string[] CreateDefaultNoisyPathPatterns()
    {
        return new[]
        {
            "**/bin/**",
            "**/obj/**",
            "**/.vs/**",
            "**/packages/**",
            "**/generated/**",
            "**/vendor/**",
            "**/ACADPlugins/**",
            "**/TZData_src/**",
        };
    }

    private static string[] CreateDefaultAgentToolRoutingRules()
    {
        return new[]
        {
            "Start C# work with prepare_csharp_workspace or get_csharp_task_context when a solution is available.",
            "Prefer scoped diagnostics with changedFiles/includePathPatterns before whole-solution diagnostics.",
            "Use get_csharp_source_context or get_csharp_symbol_source before reading whole files.",
            "Use investigate_csharp_build_failure for build logs and plan_csharp_verification before broad build/test commands.",
            "Use collect_artifact_evidence or wait_for_artifact_evidence for reports, traces, logs, JSON, XML, and TRX files.",
            "Use preview/apply mutation tools only after reviewing blockers, truncation, conflicts, and explicit target selection.",
        };
    }

    private static string[] CreateDefaultArtifactHints()
    {
        return new[]
        {
            "TestResults/**/*.trx",
            "artifacts/**/*.log",
            "artifacts/**/*.json",
            "logs/**/*.log",
            "**/*.trace",
            "**/*.sarif",
        };
    }

    private static RecommendedNextAction[] CreateRepoWorkflowRecommendedActions(CSharpSolutionCandidate? selectedSolution, string[] testProjects)
    {
        var actions = new List<RecommendedNextAction>
        {
            new RecommendedNextAction
            {
                Kind = selectedSolution is null ? WorkflowActionKind.OpenVisualStudio : WorkflowActionKind.SelectTarget,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = selectedSolution is null ? "medium" : "high",
                Reason = selectedSolution is null
                    ? "No solution candidate was selected."
                    : "A primary solution candidate is available for Visual Studio C# Dev Workflow.",
                SuggestedTool = "prepare_csharp_workspace",
                TargetFilePath = selectedSolution?.FilePath ?? string.Empty,
            },
        };

        if (testProjects.Length > 0)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RunTests,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Confidence = "medium",
                Reason = "Test project files were discovered.",
                SuggestedTool = "plan_csharp_verification",
                TargetFilePath = testProjects[0],
            });
        }

        return actions.ToArray();
    }

    private static string[] CreateRepoWorkflowSuggestedNextSteps(CSharpSolutionCandidate? selectedSolution, string[] testProjects)
    {
        var steps = new List<string>();
        steps.Add(selectedSolution is null
            ? "Choose a solution or pass preferredName/rootDirectory before asking child agents to inspect C# symbols."
            : $"Use selected solution {selectedSolution.FilePath} for Visual Studio C# Dev Workflow setup.");
        steps.Add(testProjects.Length == 0
            ? "No obvious test project was found; ask plan_csharp_verification for affected build/test commands after edits."
            : "Prefer affected test projects before solution-level tests.");
        steps.Add("Keep noisy paths excluded unless doing a full audit.");
        return steps.ToArray();
    }

    private static string BuildAgentInstructionsMarkdown(CSharpRepoWorkflowAnalysis analysis)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# C# Agent Instructions Draft");
        builder.AppendLine();
        builder.AppendLine("## Project Entry");
        builder.AppendLine("- Root: " + EmptyFallback(analysis.RootDirectory, "(unknown)"));
        builder.AppendLine("- Selected solution: " + EmptyFallback(analysis.SelectedSolutionPath, "(not selected)"));
        if (analysis.SolutionCandidates.Length > 0)
        {
            builder.AppendLine("- Solution candidates:");
            foreach (var solution in analysis.SolutionCandidates.Take(8))
            {
                builder.AppendLine("  - " + solution.RelativePath);
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Build And Test");
        AppendCommands(builder, "Build", analysis.BuildCommands);
        AppendCommands(builder, "Test", analysis.TestCommands);
        builder.AppendLine();
        builder.AppendLine("## Noise And Scope");
        foreach (var pattern in analysis.NoisyPathPatterns)
        {
            builder.AppendLine("- Exclude by default: " + pattern);
        }

        builder.AppendLine();
        builder.AppendLine("## Tool Routing");
        builder.AppendLine("- Use Visual Studio C# Dev Workflow as the default C# semantic context layer when a solution is available.");
        foreach (var rule in analysis.ToolRoutingRules)
        {
            builder.AppendLine("- " + rule);
        }

        builder.AppendLine();
        builder.AppendLine("## Artifacts And Reports");
        foreach (var hint in analysis.ArtifactHints)
        {
            builder.AppendLine("- " + hint);
        }

        builder.AppendLine();
        builder.AppendLine("## Agent Work Packets");
        builder.AppendLine("- Child agents must receive packetId, objective, scopeFiles/scopeSymbols, allowedTools, budget, and expected output schema.");
        builder.AppendLine("- Child agents should not broaden scope without reporting the reason.");
        builder.AppendLine("- Main agent merges with merge_csharp_agent_findings before editing shared files.");
        return builder.ToString();
    }

    private static void AppendCommands(StringBuilder builder, string label, IEnumerable<VerificationCommand> commands)
    {
        var commandArray = commands.ToArray();
        if (commandArray.Length == 0)
        {
            builder.AppendLine("- " + label + ": (not detected)");
            return;
        }

        foreach (var command in commandArray.Take(8))
        {
            builder.AppendLine("- " + label + ": `" + command.Command + "`");
        }
    }

    private static string EmptyFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static CSharpAgentWorkPacket[] CreateAgentWorkPackets(
        string objective,
        string[] files,
        string[] symbols,
        string[] resources,
        int maxPackets,
        int maxFilesPerPacket)
    {
        var packets = new List<CSharpAgentWorkPacket>();
        if (files.Length > 0)
        {
            var fileGroups = files
                .GroupBy(GetAgentWorkFileGroupKey, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var group in fileGroups)
            {
                if (packets.Count >= maxPackets)
                {
                    break;
                }

                packets.Add(CreateAgentWorkPacket(
                    "packet-" + (packets.Count + 1).ToString("00"),
                    role: "scoped-file-investigation",
                    objective,
                    group.Take(maxFilesPerPacket).ToArray(),
                    symbols,
                    resources));
            }
        }

        if (packets.Count == 0 && symbols.Length > 0)
        {
            foreach (var symbolGroup in symbols.Chunk(Math.Max(1, Math.Min(symbols.Length, 8))))
            {
                if (packets.Count >= maxPackets)
                {
                    break;
                }

                packets.Add(CreateAgentWorkPacket(
                    "packet-" + (packets.Count + 1).ToString("00"),
                    role: "scoped-symbol-investigation",
                    objective,
                    Array.Empty<string>(),
                    symbolGroup,
                    resources));
            }
        }

        if (packets.Count == 0)
        {
            var templates = new[]
            {
                ("repo-context", new[] { "prepare_csharp_workspace", "analyze_csharp_repo_workflow", "get_csharp_workflow_performance_snapshot" }),
                ("diagnostics-and-impact", new[] { "get_csharp_task_context", "prepare_csharp_change_review", "analyze_csharp_symbol_impact" }),
                ("verification-and-artifacts", new[] { "plan_csharp_verification", "plan_csharp_regression_scope", "collect_artifact_evidence", "wait_for_artifact_evidence" }),
            };
            foreach (var template in templates.Take(maxPackets))
            {
                packets.Add(CreateAgentWorkPacket(
                    "packet-" + (packets.Count + 1).ToString("00"),
                    template.Item1,
                    objective,
                    Array.Empty<string>(),
                    symbols,
                    resources,
                    template.Item2));
            }
        }

        return packets.ToArray();
    }

    private static string GetAgentWorkFileGroupKey(string filePath)
    {
        var normalized = NormalizePathForMatching(filePath);
        var parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
        {
            return string.Join("/", parts.Take(3));
        }

        if (parts.Length >= 2)
        {
            return string.Join("/", parts.Take(2));
        }

        return normalized;
    }

    private static CSharpAgentWorkPacket CreateAgentWorkPacket(
        string packetId,
        string role,
        string objective,
        string[] files,
        string[] symbols,
        string[] resources,
        string[]? allowedTools = null)
    {
        var tools = allowedTools ?? new[]
        {
            "get_csharp_task_context",
            "get_csharp_source_context",
            "batch_get_csharp_source_contexts",
            "prepare_csharp_change_review",
            "plan_csharp_verification",
        };
        return new CSharpAgentWorkPacket
        {
            PacketId = packetId,
            Role = role,
            Objective = objective,
            ScopeFiles = files,
            ScopeSymbols = symbols,
            EvidenceResources = resources,
            AllowedTools = tools,
            Budget = new WorkflowBudget
            {
                MaxReturnedChars = 16000,
                MaxElapsedMilliseconds = 30000,
                MaxProjects = 8,
                MaxDiagnostics = 20,
                MaxReferences = 50,
                AllowWholeSolution = false,
            },
            SuggestedCommands = new[]
            {
                new VerificationCommand
                {
                    Command = "plan_csharp_verification with packet scope before broad build/test commands",
                    Scope = "packet-verification-plan",
                    Confidence = "medium",
                    Reasons = new[] { "Child agents should return verification recommendations instead of running broad commands by default." },
                },
            },
            ExpectedOutputSchema = GetAgentFindingSchema(),
            StopConditions = new[]
            {
                "Stop if required file/symbol scope is missing or ambiguous.",
                "Stop if evidence is partial/truncated and a narrower retry is needed.",
                "Stop before mutating source; return findings to the main agent.",
            },
        };
    }

    private static CSharpAgentFinding[] NormalizeAgentFindings(CSharpAgentFinding[]? findings, string[]? findingSummaries)
    {
        var normalized = (findings ?? Array.Empty<CSharpAgentFinding>())
            .Where(finding => finding is not null)
            .ToList();
        var summaries = NormalizePatterns(findingSummaries);
        for (var index = 0; index < summaries.Length; index++)
        {
            normalized.Add(new CSharpAgentFinding
            {
                PacketId = "summary-" + (index + 1).ToString("00"),
                Status = "SummaryOnly",
                Summary = summaries[index],
                Findings = new[] { summaries[index] },
            });
        }

        return normalized.ToArray();
    }

    private static string CreateMergedFindingSummary(string? objective, CSharpAgentFinding[] findings, string[] missingPacketIds)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(objective))
        {
            builder.Append("Objective: ").Append(objective.Trim()).Append(". ");
        }

        builder.Append("Merged ").Append(findings.Length).Append(" finding packet(s).");
        if (missingPacketIds.Length > 0)
        {
            builder.Append(" Missing packet(s): ").Append(string.Join(", ", missingPacketIds)).Append('.');
        }

        var summaries = findings
            .Select(finding => finding.Summary)
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .Take(5)
            .ToArray();
        if (summaries.Length > 0)
        {
            builder.Append(" Summaries: ").Append(string.Join(" | ", summaries));
        }

        return builder.ToString();
    }

    private static string GetAgentFindingSchema()
    {
        return """
{
  "packetId": "packet-01",
  "status": "Ready|Blocked|Partial",
  "summary": "short conclusion",
  "files": ["path/to/file.cs"],
  "symbols": ["TypeOrMember"],
  "evidenceResources": ["mcp://..."],
  "findings": ["fact or inference with source"],
  "risks": ["remaining risk or blocker"],
  "recommendedNextActions": ["next scoped action"]
}
""";
    }

    private static CodeDiagnostic ToCodeDiagnostic(PrimaryDiagnostic diagnostic)
    {
        return new CodeDiagnostic
        {
            Id = diagnostic.Id,
            Message = diagnostic.Message,
            Severity = diagnostic.Severity,
            ProjectName = diagnostic.ProjectName,
            Span = diagnostic.Span,
            RelevanceScore = diagnostic.RelevanceScore,
            ScopeReasons = diagnostic.Reasons,
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static IEnumerable<string> FindArtifactCandidates(
        string[] artifactPaths,
        string[] rootDirectories,
        string[] searchPatterns,
        string[] excludePathPatterns,
        int maxArtifacts,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in artifactPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path) && !IsExcludedArtifactPath(path, excludePathPatterns) && seen.Add(Path.GetFullPath(path)))
            {
                yield return Path.GetFullPath(path);
            }
            else if (!File.Exists(path))
            {
                diagnostics.Add($"ArtifactNotFound: {path}");
            }
        }

        var patterns = searchPatterns.Length == 0
            ? new[] { "*.log", "*.txt", "*.json", "*.trace", "*.trx", "*.xml", "*.md" }
            : searchPatterns;
        foreach (var root in rootDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                diagnostics.Add($"ArtifactRootNotFound: {root}");
                continue;
            }

            foreach (var pattern in patterns)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    diagnostics.Add($"ArtifactSearchFailed: {root} pattern={pattern}: {ex.Message}");
                    continue;
                }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsExcludedArtifactPath(file, excludePathPatterns) || !seen.Add(Path.GetFullPath(file)))
                    {
                        continue;
                    }

                    if (seen.Count > maxArtifacts)
                    {
                        diagnostics.Add($"ArtifactCandidatesTruncated: maxArtifacts={maxArtifacts}");
                        yield break;
                    }

                    yield return Path.GetFullPath(file);
                }
            }
        }
    }

    private static bool IsExcludedArtifactPath(string filePath, string[] excludePathPatterns)
    {
        return excludePathPatterns.Any(pattern => MatchesPathPattern(filePath, pattern));
    }

    private static ArtifactEvidenceItem? ReadArtifactEvidence(
        string filePath,
        string[] includeTextPatterns,
        int maxLines,
        int maxChars,
        List<string> diagnostics)
    {
        try
        {
            var info = new FileInfo(filePath);
            var text = File.ReadAllText(filePath);
            var truncated = text.Length > maxChars;
            if (truncated)
            {
                text = text[^maxChars..];
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var interesting = lines
                .Where(line => includeTextPatterns.Length == 0 || includeTextPatterns.Any(pattern => line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0))
                .Take(maxLines)
                .ToArray();
            var errorLines = lines
                .Where(line => line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(maxLines)
                .ToArray();
            var warningLines = lines
                .Where(line => line.IndexOf("warn", StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(maxLines)
                .ToArray();
            return new ArtifactEvidenceItem
            {
                FilePath = info.FullName,
                Kind = InferArtifactKind(info.Extension),
                Length = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                Summary = string.Join(Environment.NewLine, interesting.Length == 0 ? lines.Take(Math.Min(maxLines, lines.Length)) : interesting),
                MatchedPatterns = includeTextPatterns,
                WarningLines = warningLines,
                ErrorLines = errorLines,
                IsTruncated = truncated,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            diagnostics.Add($"ArtifactReadFailed: {filePath}: {ex.Message}");
            return null;
        }
    }

    private static ArtifactEvidenceKind InferArtifactKind(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".log" => ArtifactEvidenceKind.Log,
            ".trace" => ArtifactEvidenceKind.Trace,
            ".json" => ArtifactEvidenceKind.Json,
            ".txt" or ".md" => ArtifactEvidenceKind.Text,
            ".trx" or ".xml" => ArtifactEvidenceKind.Report,
            _ => ArtifactEvidenceKind.Unknown,
        };
    }

    private static WorkspaceQueryResult<T> Failure<T>(string diagnostic)
    {
        return new WorkspaceQueryResult<T>
        {
            Items = Array.Empty<T>(),
            Diagnostics = new[] { diagnostic },
            IsPartial = true,
        };
    }

    private static WorkspaceQueryResult<T> Success<T>(
        T item,
        IEnumerable<string> diagnostics,
        bool isPartial)
    {
        return new WorkspaceQueryResult<T>
        {
            Items = new[] { item },
            Diagnostics = diagnostics.ToArray(),
            IsPartial = isPartial,
        };
    }

    private static string CreateCacheKey<TRequest>(string operationName, TRequest request)
    {
        return operationName + ":" + JsonSerializer.Serialize(request, CacheKeyJsonOptions);
    }

    private static WorkspaceQueryResult<T> WithCacheDiagnostic<T>(
        WorkspaceQueryResult<T> result,
        string toolName)
    {
        return new WorkspaceQueryResult<T>
        {
            Items = result.Items,
            Diagnostics = result.Diagnostics.Concat(new[] { $"ServerCacheHit: {toolName} reused a short-lived read-only result." }).ToArray(),
            IsPartial = result.IsPartial,
        };
    }
}
