using System.IO.Pipes;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Protocol;
using Microsoft.Extensions.Options;

namespace VisualStudio.CSharpNavigator.Server.Bridge;

public sealed class NamedPipeVisualStudioWorkspaceBridge : IVisualStudioWorkspaceBridge
{
    private const string ProtocolVersion = "1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly NamedPipeBridgeOptions _options;
    private readonly BridgeCallTelemetryRecorder _telemetryRecorder;

    public NamedPipeVisualStudioWorkspaceBridge(
        IOptions<NamedPipeBridgeOptions> options,
        BridgeCallTelemetryRecorder? telemetryRecorder = null)
    {
        _options = options.Value;
        _telemetryRecorder = telemetryRecorder ?? new BridgeCallTelemetryRecorder();
    }

    public Task<WorkspaceQueryResult<VisualStudioBridgeInstanceDescriptor>> ListVisualStudioInstancesAsync(
        VisualStudioInstancesRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var discovery = ReadDiscoveredInstances(request.IncludeStale);
        return Task.FromResult(new WorkspaceQueryResult<VisualStudioBridgeInstanceDescriptor>
        {
            Items = discovery.Instances,
            Diagnostics = discovery.Diagnostics,
            IsPartial = discovery.Diagnostics.Count > 0,
        });
    }

    public Task<WorkspaceQueryResult<WorkspaceStatus>> GetWorkspaceStatusAsync(
        WorkspaceStatusRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<WorkspaceStatusRequest, WorkspaceStatus>(
            "GetWorkspaceStatus",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SymbolDescriptor>> SearchSymbolsAsync(
        SymbolSearchRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SymbolSearchRequest, SymbolDescriptor>(
            "SearchSymbols",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SymbolReference>> FindReferencesAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SymbolReferenceRequest, SymbolReference>(
            "FindReferences",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SymbolDescriptor>> FindDefinitionsAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SymbolReferenceRequest, SymbolDescriptor>(
            "FindDefinitions",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SymbolReference>> FindImplementationsAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SymbolReferenceRequest, SymbolReference>(
            "FindImplementations",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SymbolReference>> FindOverridesAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SymbolReferenceRequest, SymbolReference>(
            "FindOverrides",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SymbolDescription>> DescribeSymbolAsync(
        SymbolDescriptionRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SymbolDescriptionRequest, SymbolDescription>(
            "DescribeSymbol",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SourceContextSnippet>> GetSymbolSourceAsync(
        SourceContextRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SourceContextRequest, SourceContextSnippet>(
            "GetSymbolSource",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SourceContextSnippet>> GetSourceContextAsync(
        SourceContextRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SourceContextRequest, SourceContextSnippet>(
            "GetSourceContext",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DocumentSymbolNode>> ListDocumentSymbolsAsync(
        DocumentSymbolsRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DocumentSymbolsRequest, DocumentSymbolNode>(
            "ListDocumentSymbols",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetOpenDocumentsAsync(
        VisualStudioDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<VisualStudioDocumentsRequest, VisualStudioDocumentSnapshot>(
            "GetOpenDocuments",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetActiveDocumentContextAsync(
        VisualStudioDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<VisualStudioDocumentsRequest, VisualStudioDocumentSnapshot>(
            "GetActiveDocumentContext",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SourceNavigationResult>> OpenSourceLocationAsync(
        SourceNavigationRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SourceNavigationRequest, SourceNavigationResult>(
            "OpenSourceLocation",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CodeDiagnostic>> GetDiagnosticsAsync(
        DiagnosticsRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DiagnosticsRequest, CodeDiagnostic>(
            "GetDiagnostics",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<VisualStudioErrorListItem>> GetErrorListAsync(
        ErrorListRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<ErrorListRequest, VisualStudioErrorListItem>(
            "GetErrorList",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<VisualStudioOutputWindowSnapshot>> GetOutputWindowAsync(
        OutputWindowRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<OutputWindowRequest, VisualStudioOutputWindowSnapshot>(
            "GetOutputWindow",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CallGraphEdge>> FindCallersAsync(
        CallGraphRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CallGraphRequest, CallGraphEdge>(
            "FindCallers",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CallGraphEdge>> FindCalleesAsync(
        CallGraphRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CallGraphRequest, CallGraphEdge>(
            "FindCallees",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<SymbolImpactSummary>> AnalyzeSymbolImpactAsync(
        SymbolImpactRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<SymbolImpactRequest, SymbolImpactSummary>(
            "AnalyzeSymbolImpact",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DerivedTypeDescriptor>> FindDerivedTypesAsync(
        DerivedTypesRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DerivedTypesRequest, DerivedTypeDescriptor>(
            "FindDerivedTypes",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<InheritanceChain>> GetInheritanceChainAsync(
        InheritanceChainRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<InheritanceChainRequest, InheritanceChain>(
            "GetInheritanceChain",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<ProjectGraph>> GetProjectGraphAsync(
        ProjectGraphRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<ProjectGraphRequest, ProjectGraph>(
            "GetProjectGraph",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugSessionStatus>> GetDebuggerStatusAsync(
        DebuggerStatusRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebuggerStatusRequest, DebugSessionStatus>(
            "GetDebuggerStatus",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugStackFrameInfo>> GetDebugCallStackAsync(
        DebugCallStackRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugCallStackRequest, DebugStackFrameInfo>(
            "GetDebugCallStack",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugVariableInfo>> GetDebugStackFrameVariablesAsync(
        DebugStackFrameVariablesRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugStackFrameVariablesRequest, DebugVariableInfo>(
            "GetDebugStackFrameVariables",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugExpressionResult>> EvaluateDebugExpressionAsync(
        DebugExpressionRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugExpressionRequest, DebugExpressionResult>(
            "EvaluateDebugExpression",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugThreadInfo>> ListDebugThreadsAsync(
        DebugThreadsRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugThreadsRequest, DebugThreadInfo>(
            "ListDebugThreads",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugBreakpointInfo>> ListDebugBreakpointsAsync(
        DebugBreakpointsRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugBreakpointsRequest, DebugBreakpointInfo>(
            "ListDebugBreakpoints",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StartDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugControlRequest, DebugControlResult>(
            "StartDebugging",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> ContinueDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugControlRequest, DebugControlResult>(
            "ContinueDebugging",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> BreakDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugControlRequest, DebugControlResult>(
            "BreakDebugging",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StopDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugControlRequest, DebugControlResult>(
            "StopDebugging",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StepOverAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugControlRequest, DebugControlResult>(
            "StepOver",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StepIntoAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugControlRequest, DebugControlResult>(
            "StepInto",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StepOutAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugControlRequest, DebugControlResult>(
            "StepOut",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> SetDebugBreakpointAsync(
        DebugBreakpointMutationRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugBreakpointMutationRequest, DebugControlResult>(
            "SetDebugBreakpoint",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> RemoveDebugBreakpointAsync(
        DebugBreakpointMutationRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugBreakpointMutationRequest, DebugControlResult>(
            "RemoveDebugBreakpoint",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> EnableDebugBreakpointAsync(
        DebugBreakpointMutationRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<DebugBreakpointMutationRequest, DebugControlResult>(
            "EnableDebugBreakpoint",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<GeneratedDocumentDescriptor>> ListGeneratedDocumentsAsync(
        GeneratedDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<GeneratedDocumentsRequest, GeneratedDocumentDescriptor>(
            "ListGeneratedDocuments",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<TemporaryMarker>> FindTemporaryMarkersAsync(
        TemporaryMarkersRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<TemporaryMarkersRequest, TemporaryMarker>(
            "FindTemporaryMarkers",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<EnclosingContext>> GetEnclosingContextAsync(
        EnclosingContextRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<EnclosingContextRequest, EnclosingContext>(
            "GetEnclosingContext",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<RelatedTestDescriptor>> FindRelatedTestsAsync(
        RelatedTestsRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<RelatedTestsRequest, RelatedTestDescriptor>(
            "FindRelatedTests",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<RenamePreview>> PreviewRenameAsync(
        RenamePreviewRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<RenamePreviewRequest, RenamePreview>(
            "PreviewRename",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<RenameApplyResult>> ApplyRenameAsync(
        RenameApplyRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<RenameApplyRequest, RenameApplyResult>(
            "ApplyRename",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CSharpCleanupPreview>> PreviewCleanupAsync(
        CSharpCleanupRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CSharpCleanupRequest, CSharpCleanupPreview>(
            "PreviewCleanup",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CSharpCleanupApplyResult>> ApplyCleanupAsync(
        CSharpCleanupApplyRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CSharpCleanupApplyRequest, CSharpCleanupApplyResult>(
            "ApplyCleanup",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CSharpCodeFixCandidate>> ListCodeFixesAsync(
        CSharpCodeFixListRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CSharpCodeFixListRequest, CSharpCodeFixCandidate>(
            "ListCodeFixes",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewCodeFixAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CSharpCodeFixRequest, CSharpCodeFixPreview>(
            "PreviewCodeFix",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyCodeFixAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CSharpCodeFixRequest, CSharpCodeFixApplyResult>(
            "ApplyCodeFix",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewFixAllAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CSharpCodeFixRequest, CSharpCodeFixPreview>(
            "PreviewFixAll",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyFixAllAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CSharpCodeFixRequest, CSharpCodeFixApplyResult>(
            "ApplyFixAll",
            request,
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<CSharpRefactoringPlan>> PreviewRefactoringPlanAsync(
        CSharpRefactoringPlanRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<CSharpRefactoringPlanRequest, CSharpRefactoringPlan>(
            "PreviewRefactoringPlan",
            request,
            cancellationToken);
    }

    private async Task<WorkspaceQueryResult<TResponseItem>> SendAsync<TRequest, TResponseItem>(
        string method,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        var target = payload is IVisualStudioBridgeTargetedRequest targetedRequest
            ? targetedRequest.Target
            : null;
        var pipeResolution = ResolvePipeName<TResponseItem>(target);
        if (pipeResolution.Failure is not null)
        {
            return pipeResolution.Failure;
        }

        var pipeName = pipeResolution.PipeName;
        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;
        PipeBridgeResponse<TResponseItem>? response = null;

        var request = new PipeBridgeRequest<TRequest>
        {
            ProtocolVersion = ProtocolVersion,
            Method = method,
            Payload = payload,
        };

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(_options.ConnectTimeoutMilliseconds, cancellationToken)
                .ConfigureAwait(false);

            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            await writer.WriteLineAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);

            var responseJson = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return Failure<TResponseItem>("The Visual Studio bridge returned an empty response.");
            }

            response = JsonSerializer.Deserialize<PipeBridgeResponse<TResponseItem>>(
                responseJson,
                JsonOptions);
            if (response is null)
            {
                return Failure<TResponseItem>("The Visual Studio bridge returned an invalid response.");
            }

            if (!string.Equals(response.ProtocolVersion, ProtocolVersion, StringComparison.Ordinal))
            {
                return Failure<TResponseItem>(
                    $"Unsupported Visual Studio bridge protocol version '{response.ProtocolVersion}'.");
            }

            if (response.Error is not null)
            {
                return Failure<TResponseItem>(
                    $"{response.Error.Code}: {response.Error.Message}");
            }

            var result = response.Result ?? Failure<TResponseItem>(
                "The Visual Studio bridge response did not include a result.");
            succeeded = response.Error is null;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return Failure<TResponseItem>(
                $"Timed out connecting to Visual Studio bridge pipe '{pipeName}'. Is the selected VSIX instance loaded?");
        }
        catch (IOException ex)
        {
            return Failure<TResponseItem>(
                $"Could not communicate with Visual Studio bridge pipe '{pipeName}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure<TResponseItem>(
                $"Access denied to Visual Studio bridge pipe '{pipeName}': {ex.Message}");
        }
        catch (JsonException ex)
        {
            return Failure<TResponseItem>(
                $"The Visual Studio bridge returned invalid JSON: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            _telemetryRecorder.Record(method, stopwatch.ElapsedMilliseconds, response?.BridgeExecutionMilliseconds ?? 0, succeeded);
        }
    }

    private PipeResolution<T> ResolvePipeName<T>(VisualStudioBridgeTarget? target)
    {
        if (!string.IsNullOrWhiteSpace(target?.PipeName))
        {
            return ResolveExplicitPipeName<T>(target.PipeName);
        }

        DiscoveryReadResult? discovery = null;
        DiscoveryReadResult GetDiscovery()
        {
            discovery ??= ReadDiscoveredInstances(includeStale: false);
            return discovery.Value;
        }

        var targetInstanceId = target?.InstanceId;
        if (!string.IsNullOrWhiteSpace(targetInstanceId))
        {
            return ResolveInstanceId<T>(GetDiscovery(), targetInstanceId);
        }

        var targetSolutionPath = target?.SolutionPath;
        if (!string.IsNullOrWhiteSpace(targetSolutionPath))
        {
            return ResolveSolutionPath<T>(GetDiscovery(), targetSolutionPath);
        }

        if (!string.IsNullOrWhiteSpace(_options.PipeName))
        {
            return ResolveExplicitPipeName<T>(_options.PipeName);
        }

        if (!string.IsNullOrWhiteSpace(_options.InstanceId))
        {
            return ResolveInstanceId<T>(GetDiscovery(), _options.InstanceId);
        }

        if (!string.IsNullOrWhiteSpace(_options.SolutionPath))
        {
            return ResolveSolutionPath<T>(GetDiscovery(), _options.SolutionPath);
        }

        var finalDiscovery = GetDiscovery();
        if (finalDiscovery.Instances.Count > 0)
        {
            return ResolveSingleMatch<T>(
                finalDiscovery.Instances,
                "the configured discovery directory",
                "Use targetPipeName, targetInstanceId, or targetSolutionPath to select one Visual Studio instance.");
        }

        if (finalDiscovery.Diagnostics.Count > 0)
        {
            return PipeResolution<T>.FromFailure(string.Join(" ", finalDiscovery.Diagnostics));
        }

        return ResolveSingleMatch<T>(
            finalDiscovery.Instances,
            "the configured discovery directory",
            "Use targetPipeName, targetInstanceId, or targetSolutionPath to select one Visual Studio instance.");
    }

    private PipeResolution<T> ResolveExplicitPipeName<T>(string pipeName)
    {
        var discovery = ReadDiscoveredInstances(includeStale: true);
        var matches = discovery.Instances
            .Where(instance => string.Equals(instance.PipeName, pipeName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Any(instance => !instance.IsStale))
        {
            return PipeResolution<T>.Success(pipeName);
        }

        if (matches.Length == 0)
        {
            return PipeResolution<T>.Success(pipeName);
        }

        var diagnostics = string.Join(
            " ",
            matches.SelectMany(instance => instance.Diagnostics)
                .DefaultIfEmpty("The matching discovery record is stale."));
        return PipeResolution<T>.FromFailure(
            $"Visual Studio bridge pipe '{pipeName}' matches only stale discovery records. {diagnostics}");
    }

    private PipeResolution<T> ResolveInstanceId<T>(
        DiscoveryReadResult discovery,
        string instanceId)
    {
        var matches = discovery.Instances
            .Where(instance => string.Equals(
                instance.InstanceId,
                instanceId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length > 0)
        {
            return ResolveSingleMatch<T>(
                matches,
                $"instance id '{instanceId}'",
                "Use targetPipeName or targetSolutionPath to disambiguate duplicate instance records.");
        }

        if (discovery.Diagnostics.Count > 0)
        {
            return PipeResolution<T>.FromFailure(string.Join(" ", discovery.Diagnostics));
        }

        return ResolveSingleMatch<T>(
            matches,
            $"instance id '{instanceId}'",
            "Use targetPipeName or targetSolutionPath to select one Visual Studio instance.");
    }

    private PipeResolution<T> ResolveSolutionPath<T>(
        DiscoveryReadResult discovery,
        string solutionPath)
    {
        var expectedPath = NormalizePath(solutionPath);
        var matches = discovery.Instances
            .Where(instance => string.Equals(
                NormalizePath(instance.SolutionPath),
                expectedPath,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length > 0)
        {
            return ResolveSingleMatch<T>(
                matches,
                $"solution '{solutionPath}'",
                "Use targetInstanceId or targetPipeName to disambiguate multiple Visual Studio instances for the same solution path.");
        }

        if (discovery.Diagnostics.Count > 0)
        {
            return PipeResolution<T>.FromFailure(string.Join(" ", discovery.Diagnostics));
        }

        return ResolveSingleMatch<T>(
            matches,
            $"solution '{solutionPath}'",
            "Use targetInstanceId or targetPipeName to select one Visual Studio instance.");
    }

    private PipeResolution<T> ResolveSingleMatch<T>(
        IReadOnlyList<VisualStudioBridgeInstanceDescriptor> matches,
        string targetDescription,
        string multipleMatchHint)
    {
        if (matches.Count == 0)
        {
            return PipeResolution<T>.FromFailure(
                $"No active Visual Studio bridge instance matched {targetDescription}. Configure VisualStudioBridge:PipeName, VisualStudioBridge:InstanceId, or VisualStudioBridge:SolutionPath.");
        }

        if (matches.Count > 1)
        {
            var candidates = string.Join(
                "; ",
                matches.Select(instance =>
                    $"{instance.InstanceId} pid={instance.ProcessId} solution='{instance.SolutionPath}' pipe='{instance.PipeName}'"));

            return PipeResolution<T>.FromFailure(
                $"Multiple Visual Studio bridge instances matched {targetDescription}. {multipleMatchHint} Candidates: {candidates}");
        }

        var match = matches[0];
        if (string.IsNullOrWhiteSpace(match.PipeName))
        {
            return PipeResolution<T>.FromFailure(
                $"Visual Studio bridge instance '{match.InstanceId}' did not advertise a pipe name.");
        }

        return PipeResolution<T>.Success(match.PipeName);
    }

    private DiscoveryReadResult ReadDiscoveredInstances(bool includeStale)
    {
        if (string.IsNullOrWhiteSpace(_options.DiscoveryDirectory)
            || !Directory.Exists(_options.DiscoveryDirectory))
        {
            return DiscoveryReadResult.Success(Array.Empty<VisualStudioBridgeInstanceDescriptor>());
        }

        var staleCutoff = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(1, _options.DiscoveryStaleAfterSeconds));
        var instances = new List<VisualStudioBridgeInstanceDescriptor>();
        var diagnostics = new List<string>();
        foreach (var filePath in Directory.EnumerateFiles(_options.DiscoveryDirectory, "*.json"))
        {
            try
            {
                var json = ReadDiscoveryFileWithRetry(filePath);
                var instance = JsonSerializer.Deserialize<VisualStudioBridgeInstance>(json, JsonOptions);
                if (instance is null)
                {
                    diagnostics.Add($"Invalid Visual Studio bridge discovery file '{filePath}': file did not contain an instance record.");
                    continue;
                }

                var descriptor = DescribeInstance(instance, staleCutoff);
                if (!includeStale && descriptor.IsStale)
                {
                    continue;
                }

                instances.Add(descriptor);
            }
            catch (IOException ex)
            {
                diagnostics.Add($"Could not read Visual Studio bridge discovery file '{filePath}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                diagnostics.Add($"Access denied reading Visual Studio bridge discovery file '{filePath}': {ex.Message}");
            }
            catch (JsonException ex)
            {
                diagnostics.Add($"Invalid Visual Studio bridge discovery file '{filePath}': {ex.Message}");
            }
        }

        return DiscoveryReadResult.Success(instances, diagnostics);
    }

    private static string ReadDiscoveryFileWithRetry(string filePath)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(filePath);
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(25);
            }
        }
    }

    private static VisualStudioBridgeInstanceDescriptor DescribeInstance(
        VisualStudioBridgeInstance instance,
        DateTimeOffset staleCutoff)
    {
        var itemDiagnostics = new List<string>();
        var processLiveness = GetProcessLiveness(instance.ProcessId);
        if (!processLiveness.IsAlive)
        {
            itemDiagnostics.Add($"Process {instance.ProcessId} is not running.");
        }
        else if (!string.IsNullOrWhiteSpace(processLiveness.Diagnostic))
        {
            itemDiagnostics.Add(processLiveness.Diagnostic);
        }

        var heartbeatStale = instance.LastSeenUtc < staleCutoff;
        if (heartbeatStale)
        {
            itemDiagnostics.Add($"Heartbeat is stale. Last seen UTC: {instance.LastSeenUtc:O}.");
        }

        if (string.IsNullOrWhiteSpace(instance.PipeName))
        {
            itemDiagnostics.Add("Pipe name is empty.");
        }

        return new VisualStudioBridgeInstanceDescriptor
        {
            InstanceId = instance.InstanceId,
            ProcessId = instance.ProcessId,
            SolutionPath = instance.SolutionPath,
            SolutionName = instance.SolutionName,
            PipeName = instance.PipeName,
            BridgeProtocolVersion = instance.BridgeProtocolVersion,
            ExtensionAssemblyVersion = instance.ExtensionAssemblyVersion,
            ExtensionFileVersion = instance.ExtensionFileVersion,
            LastSeenUtc = instance.LastSeenUtc,
            IsAlive = processLiveness.IsAlive,
            IsStale = !processLiveness.IsAlive || heartbeatStale || string.IsNullOrWhiteSpace(instance.PipeName),
            Diagnostics = itemDiagnostics,
        };
    }

    private static ProcessLiveness GetProcessLiveness(int processId)
    {
        if (processId <= 0)
        {
            return ProcessLiveness.NotRunning;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.HasExited
                ? ProcessLiveness.NotRunning
                : ProcessLiveness.Running;
        }
        catch (ArgumentException)
        {
            return ProcessLiveness.NotRunning;
        }
        catch (InvalidOperationException)
        {
            return ProcessLiveness.NotRunning;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // A fresh bridge heartbeat is sufficient when Windows blocks process inspection.
            return ProcessLiveness.AssumeRunning($"Could not verify process {processId} because access was denied: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ProcessLiveness.AssumeRunning($"Could not verify process {processId} because access was denied: {ex.Message}");
        }
    }

    private readonly record struct ProcessLiveness(bool IsAlive, string? Diagnostic)
    {
        public static ProcessLiveness Running { get; } = new(true, null);

        public static ProcessLiveness NotRunning { get; } = new(false, null);

        public static ProcessLiveness AssumeRunning(string diagnostic) => new(true, diagnostic);
    }

    private static string NormalizePath(string path)
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

    private sealed class PipeBridgeRequest<TPayload>
    {
        public string ProtocolVersion { get; set; } = NamedPipeVisualStudioWorkspaceBridge.ProtocolVersion;

        public string Method { get; set; } = string.Empty;

        public TPayload? Payload { get; set; }
    }

    private sealed class PipeBridgeResponse<TItem>
    {
        public string ProtocolVersion { get; set; } = NamedPipeVisualStudioWorkspaceBridge.ProtocolVersion;

        public WorkspaceQueryResult<TItem>? Result { get; set; }

        public PipeBridgeError? Error { get; set; }

        public long BridgeExecutionMilliseconds { get; set; }
    }

    private sealed class PipeBridgeError
    {
        public string Code { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    private readonly struct PipeResolution<T>
    {
        private PipeResolution(string pipeName, WorkspaceQueryResult<T>? failure)
        {
            PipeName = pipeName;
            Failure = failure;
        }

        public string PipeName { get; }

        public WorkspaceQueryResult<T>? Failure { get; }

        public static PipeResolution<T> Success(string pipeName) => new(pipeName, null);

        public static PipeResolution<T> FromFailure(string diagnostic) => new(string.Empty, NamedPipeVisualStudioWorkspaceBridge.Failure<T>(diagnostic));
    }

    private readonly struct DiscoveryReadResult
    {
        private DiscoveryReadResult(
            IReadOnlyList<VisualStudioBridgeInstanceDescriptor> instances,
            IReadOnlyList<string> diagnostics)
        {
            Instances = instances;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<VisualStudioBridgeInstanceDescriptor> Instances { get; }

        public IReadOnlyList<string> Diagnostics { get; }

        public static DiscoveryReadResult Success(
            IReadOnlyList<VisualStudioBridgeInstanceDescriptor> instances,
            IReadOnlyList<string>? diagnostics = null)
        {
            return new DiscoveryReadResult(
                instances,
                diagnostics ?? Array.Empty<string>());
        }
    }
}
