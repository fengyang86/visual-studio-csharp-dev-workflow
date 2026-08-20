using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Vsix.Workspace;

namespace VisualStudio.CSharpNavigator.Vsix.Bridge;

internal sealed class VisualStudioBridgeServer : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly BridgeInstanceRegistry _registry;
    private readonly VisualStudioWorkspaceQueryService _queryService;
    private readonly VisualStudioDebugContextService _debugContextService;
    private readonly VisualStudioDebugControlService _debugControlService;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _acceptLoopTask;
    private bool _disposed;

    public VisualStudioBridgeServer(
        BridgeInstanceRegistry registry,
        VisualStudioWorkspaceQueryService queryService,
        VisualStudioDebugContextService debugContextService,
        VisualStudioDebugControlService debugControlService)
    {
        _registry = registry;
        _queryService = queryService;
        _debugContextService = debugContextService;
        _debugControlService = debugControlService;
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (_acceptLoopTask is not null)
        {
            return;
        }

        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_disposeCts.Token));
        BridgeLog.Info($"Visual Studio bridge server started on pipe '{_registry.PipeName}'.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                var connectedPipe = pipe;
                pipe = null;
                _ = Task.Run(
                    () => HandleClientAndDisposeAsync(connectedPipe, cancellationToken),
                    CancellationToken.None).ContinueWith(
                    task => BridgeLog.Error("Visual Studio bridge client handler failed.", task.Exception!),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                BridgeLog.Error("Visual Studio bridge accept loop failed.", ex);
                await DelayAfterAcceptFailureAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        return new NamedPipeServerStream(
            _registry.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private static async Task DelayAfterAcceptFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    private async Task HandleClientAndDisposeAsync(
        NamedPipeServerStream pipe,
        CancellationToken serverCancellationToken)
    {
        using (pipe)
        {
            await HandleClientAsync(pipe, serverCancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken serverCancellationToken)
    {
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(serverCancellationToken);
        requestCts.CancelAfter(RequestTimeout);

        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = true,
        };

        string responseJson;
        var requestStopwatch = Stopwatch.StartNew();
        try
        {
            var requestJson = await ReadLineWithTimeoutAsync(reader, requestCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                responseJson = ErrorJson("MalformedRequest", "The bridge request was empty.");
            }
            else
            {
                responseJson = await DispatchAsync(requestJson!, requestCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!serverCancellationToken.IsCancellationRequested)
        {
            responseJson = ErrorJson("RequestTimedOut", $"The bridge request exceeded {RequestTimeout.TotalSeconds:n0} seconds.");
        }
        catch (OperationCanceledException)
        {
            responseJson = ErrorJson("RequestCanceled", "The bridge request was canceled.");
        }
        catch (IOException ex)
        {
            BridgeLog.Warning("Visual Studio bridge client disconnected before a response could be written: " + ex.Message);
            return;
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Visual Studio bridge request failed.", ex);
            responseJson = ErrorJson("RoslynQueryFailed", ex.Message);
        }

        requestStopwatch.Stop();
        responseJson = AddExecutionTiming(responseJson, requestStopwatch.ElapsedMilliseconds);
        await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var readTask = reader.ReadLineAsync();
        var completedTask = await Task.WhenAny(readTask, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken))
            .ConfigureAwait(false);
        if (!ReferenceEquals(completedTask, readTask))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await readTask.ConfigureAwait(false);
    }

    private async Task<string> DispatchAsync(
        string requestJson,
        CancellationToken cancellationToken)
    {
        PipeBridgeRequestEnvelope? request;
        try
        {
            request = JsonSerializer.Deserialize<PipeBridgeRequestEnvelope>(
                requestJson,
                PipeBridgeProtocol.JsonOptions);
        }
        catch (JsonException ex)
        {
            return ErrorJson("MalformedRequest", "The bridge request was not valid JSON: " + ex.Message);
        }

        if (request is null)
        {
            return ErrorJson("MalformedRequest", "The bridge request could not be parsed.");
        }

        if (!string.Equals(request.ProtocolVersion, PipeBridgeProtocol.Version, StringComparison.Ordinal))
        {
            return ErrorJson("UnsupportedProtocolVersion", $"Protocol version '{request.ProtocolVersion}' is not supported.");
        }

        try
        {
            return request.Method switch
            {
                "GetWorkspaceStatus" => SuccessJson(await _queryService.GetWorkspaceStatusResultAsync(
                    _registry.InstanceId,
                    _registry.ProcessId,
                    cancellationToken).ConfigureAwait(false)),
                "SearchSymbols" => SuccessJson(await _queryService.SearchSymbolsAsync(
                    DeserializePayload<SymbolSearchRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "FindReferences" => SuccessJson(await _queryService.FindReferencesAsync(
                    DeserializePayload<SymbolReferenceRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "FindDefinitions" => SuccessJson(await _queryService.FindDefinitionsAsync(
                    DeserializePayload<SymbolReferenceRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "FindImplementations" => SuccessJson(await _queryService.FindImplementationsAsync(
                    DeserializePayload<SymbolReferenceRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "FindOverrides" => SuccessJson(await _queryService.FindOverridesAsync(
                    DeserializePayload<SymbolReferenceRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "DescribeSymbol" => SuccessJson(await _queryService.DescribeSymbolAsync(
                    DeserializePayload<SymbolDescriptionRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetSymbolSource" => SuccessJson(await _queryService.GetSymbolSourceAsync(
                    DeserializePayload<SourceContextRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetSourceContext" => SuccessJson(await _queryService.GetSourceContextAsync(
                    DeserializePayload<SourceContextRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ListDocumentSymbols" => SuccessJson(await _queryService.ListDocumentSymbolsAsync(
                    DeserializePayload<DocumentSymbolsRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetOpenDocuments" => SuccessJson(await _queryService.GetOpenDocumentsAsync(
                    DeserializePayload<VisualStudioDocumentsRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetActiveDocumentContext" => SuccessJson(await _queryService.GetActiveDocumentContextAsync(
                    DeserializePayload<VisualStudioDocumentsRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "OpenSourceLocation" => SuccessJson(await _queryService.OpenSourceLocationAsync(
                    DeserializePayload<SourceNavigationRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetDiagnostics" => SuccessJson(await _queryService.GetDiagnosticsAsync(
                    DeserializePayload<DiagnosticsRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetErrorList" => SuccessJson(await _queryService.GetErrorListAsync(
                    DeserializePayload<ErrorListRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetOutputWindow" => SuccessJson(await _queryService.GetOutputWindowAsync(
                    DeserializePayload<OutputWindowRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "FindCallers" => SuccessJson(await _queryService.FindCallersAsync(
                    DeserializePayload<CallGraphRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "FindCallees" => SuccessJson(await _queryService.FindCalleesAsync(
                    DeserializePayload<CallGraphRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "AnalyzeSymbolImpact" => SuccessJson(await _queryService.AnalyzeSymbolImpactAsync(
                    DeserializePayload<SymbolImpactRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "FindDerivedTypes" => SuccessJson(await _queryService.FindDerivedTypesAsync(
                    DeserializePayload<DerivedTypesRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetInheritanceChain" => SuccessJson(await _queryService.GetInheritanceChainAsync(
                    DeserializePayload<InheritanceChainRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetProjectGraph" => SuccessJson(await _queryService.GetProjectGraphAsync(
                    DeserializePayload<ProjectGraphRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetDebuggerStatus" => SuccessJson(await _debugContextService.GetDebuggerStatusAsync(
                    DeserializePayload<DebuggerStatusRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetDebugCallStack" => SuccessJson(await _debugContextService.GetDebugCallStackAsync(
                    DeserializePayload<DebugCallStackRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetDebugStackFrameVariables" => SuccessJson(await _debugContextService.GetStackFrameVariablesAsync(
                    DeserializePayload<DebugStackFrameVariablesRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "EvaluateDebugExpression" => SuccessJson(await _debugContextService.EvaluateExpressionAsync(
                    DeserializePayload<DebugExpressionRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ListDebugThreads" => SuccessJson(await _debugContextService.ListDebugThreadsAsync(
                    DeserializePayload<DebugThreadsRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ListDebugBreakpoints" => SuccessJson(await _debugContextService.ListBreakpointsAsync(
                    DeserializePayload<DebugBreakpointsRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "StartDebugging" => SuccessJson(await _debugControlService.StartDebuggingAsync(
                    DeserializePayload<DebugControlRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ContinueDebugging" => SuccessJson(await _debugControlService.ContinueDebuggingAsync(
                    DeserializePayload<DebugControlRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "BreakDebugging" => SuccessJson(await _debugControlService.BreakDebuggingAsync(
                    DeserializePayload<DebugControlRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "StopDebugging" => SuccessJson(await _debugControlService.StopDebuggingAsync(
                    DeserializePayload<DebugControlRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "StepOver" => SuccessJson(await _debugControlService.StepOverAsync(
                    DeserializePayload<DebugControlRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "StepInto" => SuccessJson(await _debugControlService.StepIntoAsync(
                    DeserializePayload<DebugControlRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "StepOut" => SuccessJson(await _debugControlService.StepOutAsync(
                    DeserializePayload<DebugControlRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "SetDebugBreakpoint" => SuccessJson(await _debugControlService.SetDebugBreakpointAsync(
                    DeserializePayload<DebugBreakpointMutationRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "RemoveDebugBreakpoint" => SuccessJson(await _debugControlService.RemoveDebugBreakpointAsync(
                    DeserializePayload<DebugBreakpointMutationRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "EnableDebugBreakpoint" => SuccessJson(await _debugControlService.EnableDebugBreakpointAsync(
                    DeserializePayload<DebugBreakpointMutationRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ListGeneratedDocuments" => SuccessJson(await _queryService.ListGeneratedDocumentsAsync(
                    DeserializePayload<GeneratedDocumentsRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "FindTemporaryMarkers" => SuccessJson(await _queryService.FindTemporaryMarkersAsync(
                    DeserializePayload<TemporaryMarkersRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "GetEnclosingContext" => SuccessJson(await _queryService.GetEnclosingContextAsync(
                    DeserializePayload<EnclosingContextRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "FindRelatedTests" => SuccessJson(await _queryService.FindRelatedTestsAsync(
                    DeserializePayload<RelatedTestsRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "PreviewRename" => SuccessJson(await _queryService.PreviewRenameAsync(
                    DeserializePayload<RenamePreviewRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ApplyRename" => SuccessJson(await _queryService.ApplyRenameAsync(
                    DeserializePayload<RenameApplyRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "PreviewCleanup" => SuccessJson(await _queryService.PreviewCleanupAsync(
                    DeserializePayload<CSharpCleanupRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ApplyCleanup" => SuccessJson(await _queryService.ApplyCleanupAsync(
                    DeserializePayload<CSharpCleanupApplyRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ListCodeFixes" => SuccessJson(await _queryService.ListCodeFixesAsync(
                    DeserializePayload<CSharpCodeFixListRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "PreviewCodeFix" => SuccessJson(await _queryService.PreviewCodeFixAsync(
                    DeserializePayload<CSharpCodeFixRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ApplyCodeFix" => SuccessJson(await _queryService.ApplyCodeFixAsync(
                    DeserializePayload<CSharpCodeFixRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "PreviewFixAll" => SuccessJson(await _queryService.PreviewFixAllAsync(
                    DeserializePayload<CSharpCodeFixRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "ApplyFixAll" => SuccessJson(await _queryService.ApplyFixAllAsync(
                    DeserializePayload<CSharpCodeFixRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                "PreviewRefactoringPlan" => SuccessJson(await _queryService.PreviewRefactoringPlanAsync(
                    DeserializePayload<CSharpRefactoringPlanRequest>(request),
                    cancellationToken).ConfigureAwait(false)),
                _ => ErrorJson("UnsupportedMethod", $"Bridge method '{request.Method}' is not supported."),
            };
        }
        catch (JsonException ex)
        {
            return ErrorJson("MalformedRequest", "The bridge payload was not valid for method '" + request.Method + "': " + ex.Message);
        }
    }

    private static TPayload DeserializePayload<TPayload>(PipeBridgeRequestEnvelope request)
        where TPayload : new()
    {
        if (request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new TPayload();
        }

        var payload = request.Payload.Deserialize<TPayload>(PipeBridgeProtocol.JsonOptions);
        return payload ?? new TPayload();
    }

    private static string SuccessJson<TItem>(WorkspaceQueryResult<TItem> result)
    {
        var response = new PipeBridgeResponseEnvelope<TItem>
        {
            Result = result,
        };

        return JsonSerializer.Serialize(response, PipeBridgeProtocol.JsonOptions);
    }

    private static string AddExecutionTiming(string responseJson, long elapsedMilliseconds)
    {
        using var document = JsonDocument.Parse(responseJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "bridgeExecutionMilliseconds", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteNumber("bridgeExecutionMilliseconds", elapsedMilliseconds);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ErrorJson(string code, string message)
    {
        var response = new PipeBridgeErrorResponseEnvelope
        {
            Error = new PipeBridgeError
            {
                Code = code,
                Message = message,
            },
        };

        return JsonSerializer.Serialize(response, PipeBridgeProtocol.JsonOptions);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(VisualStudioBridgeServer));
        }
    }
}
