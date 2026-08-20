using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Vsix.Bridge;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed partial class VisualStudioWorkspaceQueryService
{
    public async Task<WorkspaceQueryResult<RenamePreview>> PreviewRenameAsync(
        RenamePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRenamePreviewRequest(request);
        if (validation is not null)
        {
            return Failure<RenamePreview>(validation);
        }

        var symbolResult = await ResolveRequestedSymbolAsync(ToSymbolReferenceRequest(request), cancellationToken)
            .ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<RenamePreview>();
        }

        var solution = symbolResult.Solution!;
        var symbol = symbolResult.Symbol!;
        var descriptor = CreateDescriptor(symbol, solution, cancellationToken);
        if (descriptor?.Span is null)
        {
            return Failure<RenamePreview>("SymbolNotInSource: rename preview requires a source symbol in the active solution.");
        }

        var targetIsGenerated = IsGeneratedPath(descriptor.Span.FilePath);
        if (targetIsGenerated && !request.IncludeGeneratedCode)
        {
            return Failure<RenamePreview>("The requested symbol is in generated code. Retry with IncludeGeneratedCode=true.");
        }

        var diagnostics = new List<string>
        {
            "Rename preview uses Roslyn Renamer.RenameSymbolAsync and does not apply changes to VisualStudioWorkspace or files.",
        };

        if (string.Equals(symbol.Name, request.NewName, StringComparison.Ordinal))
        {
            diagnostics.Add("NewName is identical to the current symbol name; preview contains no text changes.");
            return Success(
                new[]
                {
                    CreateEmptyRenamePreview(symbol, descriptor, request, targetIsGenerated),
                },
                diagnostics);
        }

        Solution renamedSolution;
        try
        {
            renamedSolution = await Renamer.RenameSymbolAsync(
                    solution,
                    symbol,
                    new SymbolRenameOptions
                    {
                        RenameOverloads = request.RenameOverloads,
                        RenameInStrings = request.RenameInStrings,
                        RenameInComments = request.RenameInComments,
                        RenameFile = request.RenameFile,
                    },
                    request.NewName,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("Roslyn rename preview failed.", ex);
            return Failure<RenamePreview>("RoslynRenameFailed: " + ex.Message);
        }

        var preview = await CreateRenamePreviewAsync(
                solution,
                renamedSolution,
                symbol,
                descriptor,
                request,
                targetIsGenerated,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        return Success(
            new[] { preview },
            diagnostics,
            preview.IsTruncated || preview.OmittedGeneratedDocumentCount > 0 || preview.UnsupportedDocumentChangeCount > 0);
    }

    public async Task<WorkspaceQueryResult<RenameApplyResult>> ApplyRenameAsync(
        RenameApplyRequest request,
        CancellationToken cancellationToken)
    {
        var previewRequest = ToRenamePreviewRequest(request);
        var previewValidation = ValidateRenamePreviewRequest(previewRequest);
        if (previewValidation is not null)
        {
            return Failure<RenameApplyResult>(previewValidation);
        }

        var symbolResult = await ResolveRequestedSymbolAsync(ToSymbolReferenceRequest(request), cancellationToken)
            .ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<RenameApplyResult>();
        }

        var solution = symbolResult.Solution!;
        var symbol = symbolResult.Symbol!;
        var descriptor = CreateDescriptor(symbol, solution, cancellationToken);
        if (descriptor?.Span is null)
        {
            return Failure<RenameApplyResult>("SymbolNotInSource: rename apply requires a source symbol in the active solution.");
        }

        var targetIsGenerated = IsGeneratedPath(descriptor.Span.FilePath);
        if (targetIsGenerated && !request.IncludeGeneratedCode)
        {
            return Failure<RenameApplyResult>("The requested symbol is in generated code. Retry with IncludeGeneratedCode=true.");
        }

        var diagnostics = new List<string>
        {
            "Rename apply uses Roslyn Renamer.RenameSymbolAsync and VisualStudioWorkspace.TryApplyChanges.",
        };

        if (string.Equals(symbol.Name, request.NewName, StringComparison.Ordinal))
        {
            diagnostics.Add("NewName is identical to the current symbol name; no changes were applied.");
            return Success(
                new[]
                {
                    new RenameApplyResult
                    {
                        Applied = false,
                        Preview = CreateEmptyRenamePreview(symbol, descriptor, previewRequest, targetIsGenerated),
                        ApplyFailure = "NoChanges: NewName is identical to the current symbol name.",
                        MutationResult = CreateMutationApplyResult(
                            false,
                            CreateEmptyRenamePreview(symbol, descriptor, previewRequest, targetIsGenerated).MutationPreview,
                            "NoChanges: NewName is identical to the current symbol name."),
                    },
                },
                diagnostics);
        }

        Solution renamedSolution;
        try
        {
            renamedSolution = await Renamer.RenameSymbolAsync(
                    solution,
                    symbol,
                    new SymbolRenameOptions
                    {
                        RenameOverloads = request.RenameOverloads,
                        RenameInStrings = request.RenameInStrings,
                        RenameInComments = request.RenameInComments,
                        RenameFile = request.RenameFile,
                    },
                    request.NewName,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("Roslyn rename apply failed before applying changes.", ex);
            return Failure<RenameApplyResult>("RoslynRenameFailed: " + ex.Message);
        }

        var preview = await CreateRenamePreviewAsync(
                solution,
                renamedSolution,
                symbol,
                descriptor,
                previewRequest,
                targetIsGenerated,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        var applyBlocker = GetRenameApplyBlocker(preview, request);
        if (applyBlocker is not null)
        {
            diagnostics.Add(applyBlocker);
            return Success(
                new[]
                {
                    new RenameApplyResult
                    {
                        Applied = false,
                        Preview = preview,
                        ApplyFailure = applyBlocker,
                        MutationResult = CreateMutationApplyResult(false, preview.MutationPreview, applyBlocker),
                    },
                },
                diagnostics,
                isPartial: true);
        }

        WorkspaceLookupResult workspaceResult;
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            workspaceResult = await TryGetWorkspaceAsync(cancellationToken).ConfigureAwait(true);
            if (workspaceResult.Workspace is null)
            {
                return Failure<RenameApplyResult>(
                    workspaceResult.Diagnostic ?? "WorkspaceUnavailable: VisualStudioWorkspace is not available.");
            }

            if (!workspaceResult.Workspace.TryApplyChanges(renamedSolution))
            {
                return Success(
                    new[]
                    {
                        new RenameApplyResult
                        {
                            Applied = false,
                            Preview = preview,
                            ApplyFailure = "TryApplyChangesFailed: VisualStudioWorkspace rejected the renamed solution.",
                            MutationResult = CreateMutationApplyResult(
                                false,
                                preview.MutationPreview,
                                "TryApplyChangesFailed: VisualStudioWorkspace rejected the renamed solution."),
                        },
                    },
                    diagnostics,
                    isPartial: true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("Roslyn rename apply failed while applying changes.", ex);
            return Failure<RenameApplyResult>("TryApplyChangesFailed: " + ex.Message);
        }

        diagnostics.Add("Rename apply completed through VisualStudioWorkspace.TryApplyChanges.");
        return Success(
            new[]
            {
                new RenameApplyResult
                {
                    Applied = true,
                    Preview = preview,
                    MutationResult = CreateMutationApplyResult(true, preview.MutationPreview, string.Empty),
                },
            },
            diagnostics,
            preview.IsTruncated || preview.OmittedGeneratedDocumentCount > 0 || preview.UnsupportedDocumentChangeCount > 0);
    }

    public async Task<WorkspaceQueryResult<CSharpCleanupPreview>> PreviewCleanupAsync(
        CSharpCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCleanupRequest(request);
        if (validation is not null)
        {
            return Failure<CSharpCleanupPreview>(validation);
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<CSharpCleanupPreview>();
        }

        var solution = solutionResult.Solution!;
        var diagnostics = new List<string>
        {
            "Cleanup preview uses Roslyn Formatter/Simplifier and syntax-tree using cleanup; it does not apply changes to VisualStudioWorkspace or files.",
        };

        var documents = await ResolveCleanupDocumentsAsync(solution, request, diagnostics, cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0)
        {
            return Failure<CSharpCleanupPreview>("CleanupScopeEmpty: no C# documents matched the requested cleanup scope.");
        }

        Solution cleanedSolution;
        try
        {
            cleanedSolution = await CreateCleanupSolutionAsync(solution, documents, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("C# cleanup preview failed.", ex);
            return Failure<CSharpCleanupPreview>("CleanupPreviewFailed: " + ex.Message);
        }

        var mutationPreview = await CreateWorkspaceMutationPreviewAsync(
                WorkspaceMutationKind.Cleanup,
                "csharp_cleanup",
                solution,
                cleanedSolution,
                request.MaxTextChanges,
                request.MaxSnippetLength,
                request.IncludeGeneratedCode,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        AddDefaultMutationBlockers(mutationPreview);
        var preview = new CSharpCleanupPreview
        {
            ScopeKind = request.ScopeKind,
            ScopeValues = GetCleanupScopeValues(request).ToArray(),
            Operations = NormalizeCleanupOperations(request.Operations).ToArray(),
            MutationPreview = mutationPreview,
        };

        return Success(
            new[] { preview },
            diagnostics,
            mutationPreview.IsTruncated || mutationPreview.OmittedGeneratedDocumentCount > 0 || mutationPreview.UnsupportedDocumentChangeCount > 0);
    }

    public async Task<WorkspaceQueryResult<CSharpCleanupApplyResult>> ApplyCleanupAsync(
        CSharpCleanupApplyRequest request,
        CancellationToken cancellationToken)
    {
        var previewResult = await PreviewCleanupAsync(request, cancellationToken).ConfigureAwait(false);
        if (previewResult.Items.Count == 0)
        {
            return new WorkspaceQueryResult<CSharpCleanupApplyResult>
            {
                Diagnostics = previewResult.Diagnostics,
                IsPartial = true,
            };
        }

        var preview = previewResult.Items[0];
        var diagnostics = new List<string>(previewResult.Diagnostics)
        {
            "Cleanup apply uses VisualStudioWorkspace.TryApplyChanges after preview safety checks.",
        };

        var blocker = GetCleanupApplyBlocker(preview.MutationPreview, request);
        if (blocker is not null)
        {
            diagnostics.Add(blocker.Message);
            return Success(
                new[]
                {
                    new CSharpCleanupApplyResult
                    {
                        Applied = false,
                        Preview = preview,
                        ApplyFailure = blocker.Message,
                        MutationResult = CreateMutationApplyResult(false, preview.MutationPreview, blocker.Message),
                    },
                },
                diagnostics,
                isPartial: true);
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<CSharpCleanupApplyResult>();
        }

        var documents = await ResolveCleanupDocumentsAsync(solutionResult.Solution!, request, diagnostics, cancellationToken).ConfigureAwait(false);
        var cleanedSolution = await CreateCleanupSolutionAsync(solutionResult.Solution!, documents, request, cancellationToken).ConfigureAwait(false);

        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var workspaceResult = await TryGetWorkspaceAsync(cancellationToken).ConfigureAwait(true);
            if (workspaceResult.Workspace is null)
            {
                return Failure<CSharpCleanupApplyResult>(
                    workspaceResult.Diagnostic ?? "WorkspaceUnavailable: VisualStudioWorkspace is not available.");
            }

            if (!workspaceResult.Workspace.TryApplyChanges(cleanedSolution))
            {
                const string failure = "TryApplyChangesFailed: VisualStudioWorkspace rejected the cleanup solution.";
                return Success(
                    new[]
                    {
                        new CSharpCleanupApplyResult
                        {
                            Applied = false,
                            Preview = preview,
                            ApplyFailure = failure,
                            MutationResult = CreateMutationApplyResult(false, preview.MutationPreview, failure),
                        },
                    },
                    diagnostics,
                    isPartial: true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("C# cleanup apply failed while applying changes.", ex);
            return Failure<CSharpCleanupApplyResult>("TryApplyChangesFailed: " + ex.Message);
        }

        diagnostics.Add("Cleanup apply completed through VisualStudioWorkspace.TryApplyChanges.");
        return Success(
            new[]
            {
                new CSharpCleanupApplyResult
                {
                    Applied = true,
                    Preview = preview,
                    MutationResult = CreateMutationApplyResult(true, preview.MutationPreview, string.Empty),
                },
            },
            diagnostics,
            preview.MutationPreview.IsTruncated
                || preview.MutationPreview.OmittedGeneratedDocumentCount > 0
                || preview.MutationPreview.UnsupportedDocumentChangeCount > 0);
    }

    public async Task<WorkspaceQueryResult<CSharpCodeFixCandidate>> ListCodeFixesAsync(
        CSharpCodeFixListRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCodeFixListRequest(request);
        if (validation is not null)
        {
            return Failure<CSharpCodeFixCandidate>(validation);
        }

        var diagnostics = new List<string>
        {
            "Code fix listing uses Roslyn diagnostics plus Visual Studio MEF CodeFixProvider discovery.",
        };
        var diagnosticContextResult = await CollectCodeFixDiagnosticContextsAsync(request, diagnostics, cancellationToken)
            .ConfigureAwait(false);
        if (diagnosticContextResult.Failure is not null)
        {
            return diagnosticContextResult.Failure.As<CSharpCodeFixCandidate>();
        }

        var componentModelResult = await TryGetComponentModelAsync(cancellationToken).ConfigureAwait(false);
        if (componentModelResult.ComponentModel is null)
        {
            diagnostics.Add(componentModelResult.Diagnostic ?? "CodeFixProviderDiscoveryUnavailable: SComponentModel is not available.");
            return CreateDiagnosticOnlyCodeFixCandidates(request, diagnosticContextResult.Contexts, diagnostics, diagnosticContextResult.IsPartial);
        }

        var providerExports = GetCodeFixProviderExports(componentModelResult.ComponentModel, diagnostics)
            .Where(IsCSharpCodeFixProviderExport)
            .ToArray();
        if (providerExports.Length == 0)
        {
            diagnostics.Add("CodeFixProviderDiscoveryUnavailable: no C# CodeFixProvider exports were found in the Visual Studio MEF container.");
            return CreateDiagnosticOnlyCodeFixCandidates(request, diagnosticContextResult.Contexts, diagnostics, diagnosticContextResult.IsPartial);
        }

        var candidates = new List<CSharpCodeFixCandidate>();
        var noiseRequest = CreateCodeFixDiagnosticsRequest(request);
        foreach (var context in diagnosticContextResult.Contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var export in providerExports)
            {
                if (!CanFixDiagnostic(export.Value, context.Diagnostic.Id))
                {
                    continue;
                }

                var actions = await CollectCodeActionsAsync(
                        export.Value,
                        context.Document,
                        context.Diagnostic,
                        diagnostics,
                        cancellationToken)
                    .ConfigureAwait(false);
                foreach (var action in actions)
                {
                    var providerName = GetProviderName(export);
                    var title = action.Title ?? string.Empty;
                    var equivalenceKey = action.EquivalenceKey ?? string.Empty;
                    candidates.Add(new CSharpCodeFixCandidate
                    {
                        DiagnosticId = context.Diagnostic.Id,
                        Title = title,
                        FilePath = context.Span?.FilePath ?? context.Document.FilePath ?? string.Empty,
                        ProjectName = context.Document.Project.Name,
                        Span = context.Span,
                        Severity = MapDiagnosticSeverity(context.Diagnostic.Severity).ToString(),
                        Message = context.Diagnostic.GetMessage(),
                        SupportsPreview = true,
                        SupportsApply = true,
                        SupportsFixAll = export.Value.GetFixAllProvider() is not null,
                        ProviderName = providerName,
                        EquivalenceKey = equivalenceKey,
                        StableKey = CreateMutationCandidateStableKey(
                            context.Diagnostic.Id,
                            providerName,
                            equivalenceKey,
                            title,
                            request.ScopeKind.ToString(),
                            context.Span?.FilePath ?? context.Document.FilePath ?? context.Document.Name),
                        Reasons = CreateCodeFixCandidateReasons(context, noiseRequest),
                    });
                }
            }

            if (candidates.Count >= request.MaxResults)
            {
                break;
            }
        }

        var distinctCandidates = candidates
            .GroupBy(candidate => candidate.StableKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(request.MaxResults)
            .ToArray();
        if (candidates.Count > distinctCandidates.Length)
        {
            diagnostics.Add($"Code fix candidates were truncated at MaxResults={request.MaxResults}.");
        }

        if (distinctCandidates.Length == 0)
        {
            diagnostics.Add("No provider-backed CodeAction candidates were registered for the scoped diagnostics.");
        }

        return new WorkspaceQueryResult<CSharpCodeFixCandidate>
        {
            Items = distinctCandidates,
            Diagnostics = diagnostics,
            IsPartial = diagnosticContextResult.IsPartial || candidates.Count > distinctCandidates.Length,
        };
    }

    public async Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewCodeFixAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCodeFixRequest(request, WorkspaceMutationKind.CodeFix);
        if (validation is not null)
        {
            return Failure<CSharpCodeFixPreview>(validation);
        }

        var diagnostics = new List<string>
        {
            "Code fix preview uses Visual Studio MEF CodeFixProvider discovery and Roslyn CodeAction.GetOperationsAsync.",
            "Code fix preview does not apply changes to VisualStudioWorkspace or files.",
        };
        CodeFixPreviewReplay replay;
        try
        {
            replay = await CreateCodeFixPreviewReplayAsync(request, "preview_csharp_code_fix", diagnostics, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("C# code fix preview failed.", ex);
            return Failure<CSharpCodeFixPreview>("CodeFixPreviewFailed: " + ex.Message);
        }

        if (replay.Failure is not null)
        {
            return replay.Failure.As<CSharpCodeFixPreview>();
        }

        return Success(
            new[]
            {
                new CSharpCodeFixPreview
                {
                    DiagnosticId = request.DiagnosticId,
                    FixTitle = replay.ActionTitle,
                    ScopeKind = request.ScopeKind,
                    MutationPreview = replay.Preview,
                },
            },
            diagnostics,
            replay.Preview.IsTruncated || replay.Preview.OmittedGeneratedDocumentCount > 0 || replay.Preview.UnsupportedDocumentChangeCount > 0);
    }

    public async Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyCodeFixAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCodeFixRequest(request, WorkspaceMutationKind.CodeFix);
        if (validation is not null)
        {
            return Failure<CSharpCodeFixApplyResult>(validation);
        }

        if (string.IsNullOrWhiteSpace(request.PreviewSessionId))
        {
            return Failure<CSharpCodeFixApplyResult>(
                "MutationSessionRequired: apply_csharp_code_fix requires PreviewSessionId from a recent preview_csharp_code_fix result.");
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedWorkspaceVersion))
        {
            return Failure<CSharpCodeFixApplyResult>(
                "ExpectedWorkspaceVersionRequired: apply_csharp_code_fix requires ExpectedWorkspaceVersion from preview_csharp_code_fix.");
        }

        var diagnostics = new List<string>
        {
            "Code fix apply uses provider-backed CodeAction preview replay and VisualStudioWorkspace.TryApplyChanges.",
        };

        CodeFixPreviewReplay replay;
        try
        {
            replay = await CreateCodeFixPreviewReplayAsync(request, "apply_csharp_code_fix", diagnostics, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("C# code fix apply failed before applying changes.", ex);
            return Failure<CSharpCodeFixApplyResult>("CodeFixApplyFailed: " + ex.Message);
        }

        if (replay.Failure is not null)
        {
            return replay.Failure.As<CSharpCodeFixApplyResult>();
        }

        if (!string.Equals(request.PreviewSessionId, replay.Preview.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            const string failure = "MutationSessionMismatch: current CodeAction replay produced a different preview session id.";
            diagnostics.Add(failure);
            return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
        }

        if (!string.Equals(request.ExpectedWorkspaceVersion, replay.Preview.WorkspaceVersion, StringComparison.Ordinal))
        {
            const string failure = "WorkspaceVersionChanged: current workspace version differs from the preview session.";
            diagnostics.Add(failure);
            return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
        }

        if (replay.Preview.Blockers.Count > 0)
        {
            const string failure = "MutationPreviewBlocked: current CodeAction preview has safety blockers.";
            diagnostics.Add(failure);
            return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
        }

        var changedSolution = replay.ChangedSolution;
        if (changedSolution is null)
        {
            const string failure = "CodeFixApplyFailed: replay did not produce a changed solution.";
            diagnostics.Add(failure);
            return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
        }

        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var workspaceResult = await TryGetWorkspaceAsync(cancellationToken).ConfigureAwait(true);
            if (workspaceResult.Workspace is null)
            {
                return Failure<CSharpCodeFixApplyResult>(
                    workspaceResult.Diagnostic ?? "WorkspaceUnavailable: VisualStudioWorkspace is not available.");
            }

            if (!workspaceResult.Workspace.TryApplyChanges(changedSolution))
            {
                const string failure = "TryApplyChangesFailed: VisualStudioWorkspace rejected the code fix solution.";
                diagnostics.Add(failure);
                return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("C# code fix apply failed while applying changes.", ex);
            return Failure<CSharpCodeFixApplyResult>("TryApplyChangesFailed: " + ex.Message);
        }

        diagnostics.Add("Code fix apply completed through VisualStudioWorkspace.TryApplyChanges.");
        return Success(
            new[]
            {
                new CSharpCodeFixApplyResult
                {
                    Applied = true,
                    Preview = new CSharpCodeFixPreview
                    {
                        DiagnosticId = request.DiagnosticId,
                        FixTitle = replay.ActionTitle,
                        ScopeKind = request.ScopeKind,
                        MutationPreview = replay.Preview,
                    },
                    MutationResult = CreateMutationApplyResult(true, replay.Preview, string.Empty),
                },
            },
            diagnostics);
    }

    public async Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewFixAllAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCodeFixRequest(request, WorkspaceMutationKind.FixAll);
        if (validation is not null)
        {
            return Failure<CSharpCodeFixPreview>(validation);
        }

        var diagnostics = new List<string>
        {
            "Fix All preview uses Visual Studio MEF CodeFixProvider discovery, Roslyn FixAllProvider, and CodeAction.GetOperationsAsync.",
            "Fix All preview does not apply changes to VisualStudioWorkspace or files.",
        };
        CodeFixPreviewReplay replay;
        try
        {
            replay = await CreateFixAllPreviewReplayAsync(request, "preview_csharp_fix_all", diagnostics, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("C# Fix All preview failed.", ex);
            return Failure<CSharpCodeFixPreview>("FixAllPreviewFailed: " + ex.Message);
        }

        if (replay.Failure is not null)
        {
            return replay.Failure.As<CSharpCodeFixPreview>();
        }

        return Success(
            new[]
            {
                new CSharpCodeFixPreview
                {
                    DiagnosticId = request.DiagnosticId,
                    FixTitle = replay.ActionTitle,
                    ScopeKind = request.ScopeKind,
                    MutationPreview = replay.Preview,
                },
            },
            diagnostics,
            replay.Preview.IsTruncated || replay.Preview.OmittedGeneratedDocumentCount > 0 || replay.Preview.UnsupportedDocumentChangeCount > 0);
    }

    public async Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyFixAllAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCodeFixRequest(request, WorkspaceMutationKind.FixAll);
        if (validation is not null)
        {
            return Failure<CSharpCodeFixApplyResult>(validation);
        }

        if (string.IsNullOrWhiteSpace(request.PreviewSessionId))
        {
            return Failure<CSharpCodeFixApplyResult>(
                "MutationSessionRequired: apply_csharp_fix_all requires PreviewSessionId from a recent preview_csharp_fix_all result.");
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedWorkspaceVersion))
        {
            return Failure<CSharpCodeFixApplyResult>(
                "ExpectedWorkspaceVersionRequired: apply_csharp_fix_all requires ExpectedWorkspaceVersion from preview_csharp_fix_all.");
        }

        var diagnostics = new List<string>
        {
            "Fix All apply uses provider-backed FixAllProvider preview replay and VisualStudioWorkspace.TryApplyChanges.",
        };

        CodeFixPreviewReplay replay;
        try
        {
            replay = await CreateFixAllPreviewReplayAsync(request, "apply_csharp_fix_all", diagnostics, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("C# Fix All apply failed before applying changes.", ex);
            return Failure<CSharpCodeFixApplyResult>("FixAllApplyFailed: " + ex.Message);
        }

        if (replay.Failure is not null)
        {
            return replay.Failure.As<CSharpCodeFixApplyResult>();
        }

        if (!string.Equals(request.PreviewSessionId, replay.Preview.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            const string failure = "MutationSessionMismatch: current Fix All replay produced a different preview session id.";
            diagnostics.Add(failure);
            return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
        }

        if (!string.Equals(request.ExpectedWorkspaceVersion, replay.Preview.WorkspaceVersion, StringComparison.Ordinal))
        {
            const string failure = "WorkspaceVersionChanged: current workspace version differs from the preview session.";
            diagnostics.Add(failure);
            return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
        }

        if (replay.Preview.Blockers.Count > 0)
        {
            const string failure = "MutationPreviewBlocked: current Fix All preview has safety blockers.";
            diagnostics.Add(failure);
            return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
        }

        var changedSolution = replay.ChangedSolution;
        if (changedSolution is null)
        {
            const string failure = "FixAllApplyFailed: replay did not produce a changed solution.";
            diagnostics.Add(failure);
            return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
        }

        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var workspaceResult = await TryGetWorkspaceAsync(cancellationToken).ConfigureAwait(true);
            if (workspaceResult.Workspace is null)
            {
                return Failure<CSharpCodeFixApplyResult>(
                    workspaceResult.Diagnostic ?? "WorkspaceUnavailable: VisualStudioWorkspace is not available.");
            }

            if (!workspaceResult.Workspace.TryApplyChanges(changedSolution))
            {
                const string failure = "TryApplyChangesFailed: VisualStudioWorkspace rejected the Fix All solution.";
                diagnostics.Add(failure);
                return CreateCodeFixApplyBlockedResult(request, replay.Preview, replay.ActionTitle, failure, diagnostics);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("C# Fix All apply failed while applying changes.", ex);
            return Failure<CSharpCodeFixApplyResult>("TryApplyChangesFailed: " + ex.Message);
        }

        diagnostics.Add("Fix All apply completed through VisualStudioWorkspace.TryApplyChanges.");
        return Success(
            new[]
            {
                new CSharpCodeFixApplyResult
                {
                    Applied = true,
                    Preview = new CSharpCodeFixPreview
                    {
                        DiagnosticId = request.DiagnosticId,
                        FixTitle = replay.ActionTitle,
                        ScopeKind = request.ScopeKind,
                        MutationPreview = replay.Preview,
                    },
                    MutationResult = CreateMutationApplyResult(true, replay.Preview, string.Empty),
                },
            },
            diagnostics);
    }

    public async Task<WorkspaceQueryResult<CSharpRefactoringPlan>> PreviewRefactoringPlanAsync(
        CSharpRefactoringPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxRelatedItems is < 0 or > 500)
        {
            return Failure<CSharpRefactoringPlan>("MaxRelatedItems must be between 0 and 500.");
        }

        SymbolDescriptor? targetSymbol = null;
        if (!string.IsNullOrWhiteSpace(request.SymbolKey) || request.Position is not null)
        {
            var symbolResult = await ResolveRequestedSymbolAsync(
                    new SymbolReferenceRequest
                    {
                        SymbolKey = string.IsNullOrWhiteSpace(request.SymbolKey) ? null : new SymbolKey(request.SymbolKey!),
                        Position = request.Position,
                        IncludeGeneratedCode = request.IncludeGeneratedCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (symbolResult.Symbol is not null && symbolResult.Solution is not null)
            {
                targetSymbol = CreateDescriptor(symbolResult.Symbol, symbolResult.Solution, cancellationToken);
            }
        }

        var plan = CreateRefactoringPlan(request, targetSymbol);
        return Success(new[] { plan }, Array.Empty<string>(), plan.Blockers.Count > 0);
    }

    private static string? ValidateCleanupRequest(CSharpCleanupRequest request)
    {
        if (request.MaxTextChanges is < 1 or > 10000)
        {
            return "MaxTextChanges must be between 1 and 10000.";
        }

        if (request.MaxSnippetLength is < 0 or > 2000)
        {
            return "MaxSnippetLength must be between 0 and 2000.";
        }

        if (request.ScopeKind == CSharpMutationScopeKind.Solution && !request.AllowSolutionScope)
        {
            return "SolutionScopeBlocked: solution-wide cleanup requires AllowSolutionScope=true.";
        }

        return request.ScopeKind switch
        {
            CSharpMutationScopeKind.Document when string.IsNullOrWhiteSpace(request.FilePath) => "FilePath is required for document cleanup scope.",
            CSharpMutationScopeKind.Project when string.IsNullOrWhiteSpace(request.ProjectName) => "ProjectName is required for project cleanup scope.",
            CSharpMutationScopeKind.ChangedFiles when request.ChangedFiles.Count == 0 => "ChangedFiles is required for changed-files cleanup scope.",
            CSharpMutationScopeKind.Unknown => "ScopeKind is required.",
            _ => null,
        };
    }

    private static string? ValidateCodeFixListRequest(CSharpCodeFixListRequest request)
    {
        if (request.MaxResults is < 1 or > 500)
        {
            return "MaxResults must be between 1 and 500.";
        }

        return request.ScopeKind switch
        {
            CSharpMutationScopeKind.Document when string.IsNullOrWhiteSpace(request.FilePath) => "FilePath is required for document code fix scope.",
            CSharpMutationScopeKind.Project when string.IsNullOrWhiteSpace(request.ProjectName) => "ProjectName is required for project code fix scope.",
            CSharpMutationScopeKind.ChangedFiles when request.ChangedFiles.Count == 0 => "ChangedFiles is required for changed-files code fix scope.",
            CSharpMutationScopeKind.Unknown => "ScopeKind is required.",
            _ => null,
        };
    }

    private static string? ValidateCodeFixRequest(CSharpCodeFixRequest request, WorkspaceMutationKind kind)
    {
        if (string.IsNullOrWhiteSpace(request.DiagnosticId))
        {
            return "DiagnosticId is required.";
        }

        if (request.MaxTextChanges is < 1 or > 10000)
        {
            return "MaxTextChanges must be between 1 and 10000.";
        }

        if (request.MaxSnippetLength is < 0 or > 2000)
        {
            return "MaxSnippetLength must be between 0 and 2000.";
        }

        if (request.ScopeKind == CSharpMutationScopeKind.Solution && !request.AllowSolutionScope)
        {
            return "SolutionScopeBlocked: solution-wide code fix requires AllowSolutionScope=true.";
        }

        if ((kind == WorkspaceMutationKind.CodeFix || kind == WorkspaceMutationKind.FixAll)
            && request.ScopeKind == CSharpMutationScopeKind.ChangedFiles)
        {
            return "ChangedFiles scope is only supported by list_csharp_code_fixes in this stage.";
        }

        return request.ScopeKind switch
        {
            CSharpMutationScopeKind.Document when string.IsNullOrWhiteSpace(request.FilePath) => "FilePath is required for document code fix scope.",
            CSharpMutationScopeKind.Project when string.IsNullOrWhiteSpace(request.ProjectName) => "ProjectName is required for project code fix scope.",
            CSharpMutationScopeKind.Unknown => "ScopeKind is required.",
            _ => null,
        };
    }

    private WorkspaceQueryResult<CSharpCodeFixCandidate> CreateDiagnosticOnlyCodeFixCandidates(
        CSharpCodeFixListRequest request,
        IReadOnlyList<CodeFixDiagnosticContext> diagnosticContexts,
        List<string> diagnostics,
        bool isPartial)
    {
        var candidates = diagnosticContexts
            .Select(context => new CSharpCodeFixCandidate
            {
                DiagnosticId = context.Diagnostic.Id,
                Title = $"Plan code fix for {context.Diagnostic.Id}",
                FilePath = context.Span?.FilePath ?? context.Document.FilePath ?? string.Empty,
                ProjectName = context.Document.Project.Name,
                Span = context.Span,
                Severity = MapDiagnosticSeverity(context.Diagnostic.Severity).ToString(),
                Message = context.Diagnostic.GetMessage(),
                SupportsPreview = false,
                SupportsApply = false,
                SupportsFixAll = false,
                ProviderName = "RoslynCodeFixProviderUnavailable",
                EquivalenceKey = context.Diagnostic.Id,
                StableKey = CreateMutationCandidateStableKey(
                    context.Diagnostic.Id,
                    "RoslynCodeFixProviderUnavailable",
                    context.Diagnostic.Id,
                    $"Plan code fix for {context.Diagnostic.Id}",
                    request.ScopeKind.ToString(),
                    context.Span?.FilePath ?? context.Document.FilePath ?? context.Document.Name),
                Reasons = new[]
                {
                    "Diagnostic was returned by scoped Roslyn diagnostics.",
                    "Code fix provider discovery was unavailable; use this candidate as planning evidence.",
                },
            })
            .Take(request.MaxResults)
            .ToArray();

        return new WorkspaceQueryResult<CSharpCodeFixCandidate>
        {
            Items = candidates,
            Diagnostics = diagnostics,
            IsPartial = isPartial || diagnosticContexts.Count > candidates.Length,
        };
    }

    private async Task<CodeFixDiagnosticCollectionResult> CollectCodeFixDiagnosticContextsAsync(
        CSharpCodeFixListRequest request,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return CodeFixDiagnosticCollectionResult.FromFailure(solutionResult.Failure);
        }

        var solution = solutionResult.Solution!;
        var documents = await ResolveCodeFixDocumentsAsync(solution, request, diagnostics, cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0)
        {
            return CodeFixDiagnosticCollectionResult.FromFailure("CodeFixScopeEmpty: no C# documents matched the requested code fix scope.");
        }

        var contexts = new List<CodeFixDiagnosticContext>();
        var isPartial = false;
        var filteredByNoiseProfileCount = 0;
        var noiseRequest = CreateCodeFixDiagnosticsRequest(request);
        foreach (var projectGroup in documents.GroupBy(document => document.Project.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectDocuments = projectGroup.ToArray();
            var project = projectDocuments[0].Project;
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                diagnostics.Add($"CodeFixCompilationUnavailable: project '{project.Name}' has no compilation.");
                isPartial = true;
                continue;
            }

            var documentTrees = new List<(Document Document, SyntaxTree? Tree)>();
            foreach (var document in projectDocuments)
            {
                documentTrees.Add((document, await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false)));
            }

            var projectDiagnostics = await GetProjectDiagnosticsAsync(project, compilation, diagnostics, cancellationToken).ConfigureAwait(false);
            foreach (var diagnostic in projectDiagnostics)
            {
                if (!MatchesCodeFixDiagnostic(request, diagnostic))
                {
                    continue;
                }

                var matchingDocument = FindDiagnosticDocument(documentTrees, diagnostic);
                if (matchingDocument is null)
                {
                    continue;
                }

                var span = CreateSpan(diagnostic.Location);
                if (span is not null && IsCodeFixDiagnosticPathExcluded(span.FilePath, request.ExcludePathPatterns))
                {
                    continue;
                }

                if (span is not null && !IsCodeFixDiagnosticPathIncluded(span.FilePath, request.IncludePathPatterns))
                {
                    continue;
                }

                if (span is not null
                    && DiagnosticsNoisePolicy.ShouldFilterKnownNoisePath(noiseRequest, span.FilePath))
                {
                    filteredByNoiseProfileCount++;
                    continue;
                }

                contexts.Add(new CodeFixDiagnosticContext(matchingDocument, diagnostic, span));
                if (contexts.Count >= request.MaxResults)
                {
                    isPartial = true;
                    break;
                }
            }

            if (contexts.Count >= request.MaxResults)
            {
                break;
            }
        }

        if (filteredByNoiseProfileCount > 0)
        {
            diagnostics.Add($"CodeFixDiagnosticsFilteredByNoiseProfile: {filteredByNoiseProfileCount} known-noise diagnostic(s) were suppressed by noiseProfile={request.NoiseProfile}.");
        }

        if (DiagnosticsNoisePolicy.ShouldAutoFilterKnownNoise(noiseRequest))
        {
            diagnostics.Add(DiagnosticsNoisePolicy.AutoNoiseFilterDiagnostic);
        }

        return CodeFixDiagnosticCollectionResult.Success(contexts, isPartial);
    }

    private async Task<CodeActionSelectionResult> FindRequestedCodeActionAsync(
        CSharpCodeFixRequest request,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var listRequest = new CSharpCodeFixListRequest
        {
            ScopeKind = request.ScopeKind,
            FilePath = request.FilePath,
            ProjectName = request.ProjectName,
            DiagnosticId = request.DiagnosticId,
            MinimumSeverity = null,
            NoiseProfile = CodeDiagnosticNoiseProfile.Off,
            MaxResults = 500,
        };
        var diagnosticContextResult = await CollectCodeFixDiagnosticContextsAsync(listRequest, diagnostics, cancellationToken)
            .ConfigureAwait(false);
        if (diagnosticContextResult.Failure is not null)
        {
            return CodeActionSelectionResult.FromFailure(diagnosticContextResult.Failure);
        }

        var componentModelResult = await TryGetComponentModelAsync(cancellationToken).ConfigureAwait(false);
        if (componentModelResult.ComponentModel is null)
        {
            return CodeActionSelectionResult.FromFailure(componentModelResult.Diagnostic ?? "CodeFixProviderDiscoveryUnavailable: SComponentModel is not available.");
        }

        var providerExports = GetCodeFixProviderExports(componentModelResult.ComponentModel, diagnostics)
            .Where(IsCSharpCodeFixProviderExport)
            .ToArray();
        foreach (var context in diagnosticContextResult.Contexts)
        {
            foreach (var export in providerExports)
            {
                if (!CanFixDiagnostic(export.Value, context.Diagnostic.Id))
                {
                    continue;
                }

                var actions = await CollectCodeActionsAsync(
                        export.Value,
                        context.Document,
                        context.Diagnostic,
                        diagnostics,
                        cancellationToken)
                    .ConfigureAwait(false);
                foreach (var action in actions)
                {
                    var providerName = GetProviderName(export);
                    if (!MatchesRequestedCodeAction(request, action, providerName, context))
                    {
                        continue;
                    }

                    return CodeActionSelectionResult.Success(context.Document, context.Diagnostic, action, export.Value, providerName);
                }
            }
        }

        return CodeActionSelectionResult.FromFailure("CodeFixCandidateNotFound: no provider-backed CodeAction matched the requested diagnostic/title/provider/equivalence/stable key.");
    }

    private async Task<CodeFixPreviewReplay> CreateCodeFixPreviewReplayAsync(
        CSharpCodeFixRequest request,
        string operationName,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var selection = await FindRequestedCodeActionAsync(request, diagnostics, cancellationToken).ConfigureAwait(false);
        if (selection.Failure is not null)
        {
            return CodeFixPreviewReplay.FromFailure(selection.Failure);
        }

        var operations = await selection.Action!.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        var applyChangesOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyChangesOperation is null)
        {
            return CodeFixPreviewReplay.FromFailure("CodeFixPreviewUnsupportedOperation: selected CodeAction did not return an ApplyChangesOperation.");
        }

        var changedSolution = applyChangesOperation.ChangedSolution;
        var preview = await CreateWorkspaceMutationPreviewAsync(
                WorkspaceMutationKind.CodeFix,
                operationName,
                selection.Document!.Project.Solution,
                changedSolution,
                request.MaxTextChanges,
                request.MaxSnippetLength,
                includeGeneratedCode: false,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        preview.WorkspaceVersion = CreateWorkspaceVersion(selection.Document!.Project.Solution);
        preview.CandidateIdentity = CreateCodeFixCandidateIdentity(request, selection.Action!, selection.ProviderName!, selection.Document!, selection.Diagnostic!);
        preview.SessionId = CreateMutationSessionId(WorkspaceMutationKind.CodeFix, request.DiagnosticId, preview.CandidateIdentity.StableKey);
        AddDefaultMutationBlockers(preview);

        return CodeFixPreviewReplay.Success(
            preview,
            changedSolution,
            selection.Action!.Title ?? request.FixTitle ?? string.Empty);
    }

    private async Task<CodeFixPreviewReplay> CreateFixAllPreviewReplayAsync(
        CSharpCodeFixRequest request,
        string operationName,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var selection = await FindRequestedCodeActionAsync(request, diagnostics, cancellationToken).ConfigureAwait(false);
        if (selection.Failure is not null)
        {
            return CodeFixPreviewReplay.FromFailure(selection.Failure);
        }

        var provider = selection.Provider!;
        var fixAllProvider = provider.GetFixAllProvider();
        if (fixAllProvider is null)
        {
            return CodeFixPreviewReplay.FromFailure("FixAllProviderUnavailable: selected CodeFixProvider does not provide Fix All support.");
        }

        var fixAllScope = ToRoslynFixAllScope(request.ScopeKind);
        if (fixAllScope is null)
        {
            return CodeFixPreviewReplay.FromFailure("FixAllScopeUnsupported: Fix All supports Document, Project, or explicitly allowed Solution scope.");
        }

        var supportedScopes = fixAllProvider.GetSupportedFixAllScopes();
        if (!supportedScopes.Contains(fixAllScope.Value))
        {
            return CodeFixPreviewReplay.FromFailure($"FixAllScopeUnsupported: provider '{selection.ProviderName}' does not support {fixAllScope.Value} scope.");
        }

        var supportedDiagnosticIds = fixAllProvider.GetSupportedFixAllDiagnosticIds(provider);
        if (!supportedDiagnosticIds.Any(id => string.Equals(id, selection.Diagnostic!.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return CodeFixPreviewReplay.FromFailure($"FixAllDiagnosticUnsupported: provider '{selection.ProviderName}' does not support Fix All for diagnostic '{selection.Diagnostic!.Id}'.");
        }

        var equivalenceKey = selection.Action!.EquivalenceKey ?? request.EquivalenceKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(equivalenceKey))
        {
            return CodeFixPreviewReplay.FromFailure("FixAllEquivalenceKeyRequired: selected CodeAction has no EquivalenceKey, so Roslyn cannot safely compute Fix All.");
        }

        var diagnosticProvider = new ScopedFixAllDiagnosticProvider(request.DiagnosticId, diagnostics);
        var fixAllContext = new FixAllContext(
            selection.Document!,
            provider,
            fixAllScope.Value,
            equivalenceKey,
            new[] { selection.Diagnostic!.Id },
            diagnosticProvider,
            cancellationToken);

        var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext).ConfigureAwait(false);
        if (fixAllAction is null)
        {
            return CodeFixPreviewReplay.FromFailure("FixAllActionUnavailable: selected FixAllProvider returned no CodeAction.");
        }

        var operations = await fixAllAction.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        var applyChangesOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyChangesOperation is null)
        {
            return CodeFixPreviewReplay.FromFailure("FixAllPreviewUnsupportedOperation: selected Fix All CodeAction did not return an ApplyChangesOperation.");
        }

        var changedSolution = applyChangesOperation.ChangedSolution;
        var preview = await CreateWorkspaceMutationPreviewAsync(
                WorkspaceMutationKind.FixAll,
                operationName,
                selection.Document!.Project.Solution,
                changedSolution,
                request.MaxTextChanges,
                request.MaxSnippetLength,
                includeGeneratedCode: false,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        preview.WorkspaceVersion = CreateWorkspaceVersion(selection.Document!.Project.Solution);
        preview.CandidateIdentity = CreateCodeFixCandidateIdentity(request, selection.Action!, selection.ProviderName!, selection.Document!, selection.Diagnostic!);
        preview.SessionId = CreateMutationSessionId(WorkspaceMutationKind.FixAll, request.DiagnosticId, preview.CandidateIdentity.StableKey);
        AddDefaultMutationBlockers(preview);

        return CodeFixPreviewReplay.Success(
            preview,
            changedSolution,
            fixAllAction.Title ?? selection.Action!.Title ?? request.FixTitle ?? string.Empty);
    }

    private static IEnumerable<CSharpCleanupOperation> NormalizeCleanupOperations(IReadOnlyList<CSharpCleanupOperation> operations)
    {
        return operations.Count == 0
            ? new[] { CSharpCleanupOperation.Format, CSharpCleanupOperation.OrganizeUsings, CSharpCleanupOperation.Simplify }
            : operations.Where(operation => Enum.IsDefined(typeof(CSharpCleanupOperation), operation)).Distinct();
    }

    private static IEnumerable<string> GetCleanupScopeValues(CSharpCleanupRequest request)
    {
        return request.ScopeKind switch
        {
            CSharpMutationScopeKind.Document => new[] { request.FilePath ?? string.Empty },
            CSharpMutationScopeKind.Project => new[] { request.ProjectName ?? string.Empty },
            CSharpMutationScopeKind.ChangedFiles => request.ChangedFiles,
            CSharpMutationScopeKind.Solution => new[] { "solution" },
            _ => Array.Empty<string>(),
        };
    }

    private static async Task<IReadOnlyList<CodeAction>> CollectCodeActionsAsync(
        CodeFixProvider provider,
        Document document,
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var actions = new List<CodeAction>();
        try
        {
            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) => AddFlattenedCodeActions(actions, action),
                cancellationToken);
            await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add($"CodeFixProviderFailed: {provider.GetType().FullName}: {ex.Message}");
        }

        return actions;
    }

    private static void AddFlattenedCodeActions(List<CodeAction> actions, CodeAction action)
    {
        if (action.NestedActions.Length > 0)
        {
            foreach (var nestedAction in action.NestedActions)
            {
                AddFlattenedCodeActions(actions, nestedAction);
            }

            return;
        }

        actions.Add(action);
    }

    private static bool MatchesCodeFixDiagnostic(
        CSharpCodeFixListRequest request,
        Microsoft.CodeAnalysis.Diagnostic diagnostic)
    {
        if (!string.IsNullOrWhiteSpace(request.DiagnosticId)
            && !string.Equals(diagnostic.Id, request.DiagnosticId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchesMinimumSeverity(diagnostic.Severity, request.MinimumSeverity);
    }

    private static Document? FindDiagnosticDocument(
        IReadOnlyList<(Document Document, SyntaxTree? Tree)> documentTrees,
        Microsoft.CodeAnalysis.Diagnostic diagnostic)
    {
        var sourceTree = diagnostic.Location.SourceTree;
        if (sourceTree is null)
        {
            return null;
        }

        foreach (var (document, tree) in documentTrees)
        {
            if (ReferenceEquals(sourceTree, tree))
            {
                return document;
            }
        }

        return null;
    }

    private static IEnumerable<Lazy<CodeFixProvider, IDictionary<string, object>>> GetCodeFixProviderExports(
        IComponentModel componentModel,
        List<string> diagnostics)
    {
        try
        {
            return componentModel.DefaultExportProvider
                .GetExports<CodeFixProvider, IDictionary<string, object>>()
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add("CodeFixProviderDiscoveryFailed: " + ex.Message);
            return Array.Empty<Lazy<CodeFixProvider, IDictionary<string, object>>>();
        }
    }

    private static bool IsCSharpCodeFixProviderExport(Lazy<CodeFixProvider, IDictionary<string, object>> export)
    {
        return MetadataContains(export.Metadata, "Languages", LanguageNames.CSharp);
    }

    private static string GetProviderName(Lazy<CodeFixProvider, IDictionary<string, object>> export)
    {
        var name = GetMetadataString(export.Metadata, "Name");
        return string.IsNullOrWhiteSpace(name)
            ? export.Value.GetType().Name
            : name;
    }

    private static bool MetadataContains(IDictionary<string, object> metadata, string key, string value)
    {
        if (!metadata.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is string text)
        {
            return string.Equals(text, value, StringComparison.OrdinalIgnoreCase);
        }

        if (raw is IEnumerable<string> strings)
        {
            return strings.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }

        if (raw is System.Collections.IEnumerable values)
        {
            foreach (var item in values)
            {
                if (item is not null && string.Equals(item.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetMetadataString(IDictionary<string, object> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && value is not null
            ? value.ToString() ?? string.Empty
            : string.Empty;
    }

    private static bool CanFixDiagnostic(CodeFixProvider provider, string diagnosticId)
    {
        return provider.FixableDiagnosticIds.Any(id => string.Equals(id, diagnosticId, StringComparison.OrdinalIgnoreCase));
    }

    private static FixAllScope? ToRoslynFixAllScope(CSharpMutationScopeKind scopeKind)
    {
        return scopeKind switch
        {
            CSharpMutationScopeKind.Document => FixAllScope.Document,
            CSharpMutationScopeKind.Project => FixAllScope.Project,
            CSharpMutationScopeKind.Solution => FixAllScope.Solution,
            _ => null,
        };
    }

    private static DiagnosticsRequest CreateCodeFixDiagnosticsRequest(CSharpCodeFixListRequest request)
    {
        return new DiagnosticsRequest
        {
            Target = request.Target,
            FilePath = request.ScopeKind == CSharpMutationScopeKind.Document ? request.FilePath : null,
            ProjectName = request.ScopeKind == CSharpMutationScopeKind.Project ? request.ProjectName : null,
            IncludePathPatterns = request.IncludePathPatterns.ToArray(),
            ExcludePathPatterns = request.ExcludePathPatterns.ToArray(),
            ChangedFiles = request.ScopeKind == CSharpMutationScopeKind.ChangedFiles
                ? request.ChangedFiles.ToArray()
                : Array.Empty<string>(),
            MinimumSeverity = request.MinimumSeverity,
            NoiseProfile = request.NoiseProfile,
            MaxResults = request.MaxResults,
        };
    }

    private static bool IsCodeFixDiagnosticPathExcluded(
        string filePath,
        IReadOnlyCollection<string> excludePathPatterns)
    {
        return excludePathPatterns.Count > 0
            && excludePathPatterns.Any(pattern => MatchesPathPattern(filePath, pattern));
    }

    private static bool IsCodeFixDiagnosticPathIncluded(
        string filePath,
        IReadOnlyCollection<string> includePathPatterns)
    {
        return includePathPatterns.Count == 0
            || includePathPatterns.Any(pattern => MatchesPathPattern(filePath, pattern));
    }

    private static string[] CreateCodeFixCandidateReasons(
        CodeFixDiagnosticContext context,
        DiagnosticsRequest noiseRequest)
    {
        var reasons = new List<string>
        {
            "Diagnostic was returned by scoped Roslyn diagnostics.",
            "CodeAction was registered by a Visual Studio MEF CodeFixProvider.",
            "Preview and apply are supported through the workspace mutation session pipeline.",
        };

        if (context.Span is not null
            && DiagnosticsNoisePolicy.ShouldMarkKnownNoisePath(noiseRequest, context.Span.FilePath))
        {
            reasons.Add("Diagnostic is in a known-noise path and was retained because the request scope/noiseProfile did not filter it.");
        }

        return reasons.ToArray();
    }

    private static bool MatchesRequestedCodeAction(
        CSharpCodeFixRequest request,
        CodeAction action,
        string providerName,
        CodeFixDiagnosticContext context)
    {
        var title = action.Title ?? string.Empty;
        var equivalenceKey = action.EquivalenceKey ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(request.ProviderName)
            && !string.Equals(providerName, request.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.FixTitle)
            && !string.Equals(title, request.FixTitle, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.EquivalenceKey)
            && !string.Equals(equivalenceKey, request.EquivalenceKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.CandidateStableKey))
        {
            var stableKey = CreateMutationCandidateStableKey(
                context.Diagnostic.Id,
                providerName,
                equivalenceKey,
                title,
                request.ScopeKind.ToString(),
                context.Span?.FilePath ?? context.Document.FilePath ?? context.Document.Name);
            return string.Equals(stableKey, request.CandidateStableKey, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static MutationCandidateIdentity CreateCodeFixCandidateIdentity(
        CSharpCodeFixRequest request,
        CodeAction action,
        string providerName,
        Document document,
        Microsoft.CodeAnalysis.Diagnostic diagnostic)
    {
        var title = action.Title ?? request.FixTitle ?? string.Empty;
        var equivalenceKey = action.EquivalenceKey ?? request.EquivalenceKey ?? string.Empty;
        var documentOrProject = CreateSpan(diagnostic.Location)?.FilePath ?? document.FilePath ?? document.Name;
        var stableKey = !string.IsNullOrWhiteSpace(request.CandidateStableKey)
            ? request.CandidateStableKey!
            : CreateMutationCandidateStableKey(diagnostic.Id, providerName, equivalenceKey, title, request.ScopeKind.ToString(), documentOrProject);
        return new MutationCandidateIdentity
        {
            DiagnosticId = diagnostic.Id,
            ProviderName = providerName,
            EquivalenceKey = equivalenceKey,
            Title = title,
            Scope = request.ScopeKind.ToString(),
            DocumentOrProject = documentOrProject,
            StableKey = stableKey,
        };
    }

    private static string CreateWorkspaceVersion(Solution solution)
    {
        return string.Join(
            "|",
            solution.FilePath ?? string.Empty,
            solution.Projects.Count().ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(
                ",",
                solution.Projects
                    .OrderBy(project => project.Id.Id)
                    .Select(project => project.Version.ToString())));
    }

    private static async Task<IReadOnlyList<Document>> ResolveCodeFixDocumentsAsync(
        Solution solution,
        CSharpCodeFixListRequest request,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var documents = new List<Document>();
        switch (request.ScopeKind)
        {
            case CSharpMutationScopeKind.Document:
                var document = await FindDocumentByPathAsync(solution, request.FilePath!, includeSourceGeneratedDocuments: false, cancellationToken)
                    .ConfigureAwait(false);
                if (document is not null)
                {
                    documents.Add(document);
                }

                break;
            case CSharpMutationScopeKind.Project:
                documents.AddRange(solution.Projects
                    .Where(project => string.Equals(project.Name, request.ProjectName, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(project => project.Documents));
                break;
            case CSharpMutationScopeKind.ChangedFiles:
                foreach (var changedFile in request.ChangedFiles.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var changedDocument = await FindDocumentByPathAsync(solution, changedFile, includeSourceGeneratedDocuments: false, cancellationToken)
                        .ConfigureAwait(false);
                    if (changedDocument is not null)
                    {
                        documents.Add(changedDocument);
                    }
                    else
                    {
                        diagnostics.Add("CodeFixChangedFileNotFound: " + changedFile);
                    }
                }

                break;
            case CSharpMutationScopeKind.Solution:
                documents.AddRange(solution.Projects
                    .Where(project => project.Language == LanguageNames.CSharp)
                    .SelectMany(project => project.Documents));
                break;
        }

        return documents
            .Where(document => document.Project.Language == LanguageNames.CSharp)
            .Where(document => !IsGeneratedDocument(document))
            .GroupBy(document => document.Id)
            .Select(group => group.First())
            .ToArray();
    }

    private sealed class CodeFixDiagnosticContext
    {
        public CodeFixDiagnosticContext(Document document, Microsoft.CodeAnalysis.Diagnostic diagnostic, SourceSpan? span)
        {
            Document = document;
            Diagnostic = diagnostic;
            Span = span;
        }

        public Document Document { get; }

        public Microsoft.CodeAnalysis.Diagnostic Diagnostic { get; }

        public SourceSpan? Span { get; }
    }

    private sealed class CodeFixDiagnosticCollectionResult
    {
        private CodeFixDiagnosticCollectionResult(
            IReadOnlyList<CodeFixDiagnosticContext> contexts,
            bool isPartial,
            QueryFailure? failure)
        {
            Contexts = contexts;
            IsPartial = isPartial;
            Failure = failure;
        }

        public IReadOnlyList<CodeFixDiagnosticContext> Contexts { get; }

        public bool IsPartial { get; }

        public QueryFailure? Failure { get; }

        public static CodeFixDiagnosticCollectionResult Success(
            IReadOnlyList<CodeFixDiagnosticContext> contexts,
            bool isPartial)
        {
            return new CodeFixDiagnosticCollectionResult(contexts, isPartial, null);
        }

        public static CodeFixDiagnosticCollectionResult FromFailure(QueryFailure failure)
        {
            return new CodeFixDiagnosticCollectionResult(Array.Empty<CodeFixDiagnosticContext>(), true, failure);
        }

        public static CodeFixDiagnosticCollectionResult FromFailure(string diagnostic)
        {
            return new CodeFixDiagnosticCollectionResult(
                Array.Empty<CodeFixDiagnosticContext>(),
                true,
                new QueryFailure(diagnostic));
        }
    }

    private sealed class CodeActionSelectionResult
    {
        private CodeActionSelectionResult(
            Document? document,
            Microsoft.CodeAnalysis.Diagnostic? diagnostic,
            CodeAction? action,
            CodeFixProvider? provider,
            string? providerName,
            QueryFailure? failure)
        {
            Document = document;
            Diagnostic = diagnostic;
            Action = action;
            Provider = provider;
            ProviderName = providerName;
            Failure = failure;
        }

        public Document? Document { get; }

        public Microsoft.CodeAnalysis.Diagnostic? Diagnostic { get; }

        public CodeAction? Action { get; }

        public CodeFixProvider? Provider { get; }

        public string? ProviderName { get; }

        public QueryFailure? Failure { get; }

        public static CodeActionSelectionResult Success(
            Document document,
            Microsoft.CodeAnalysis.Diagnostic diagnostic,
            CodeAction action,
            CodeFixProvider provider,
            string providerName)
        {
            return new CodeActionSelectionResult(document, diagnostic, action, provider, providerName, null);
        }

        public static CodeActionSelectionResult FromFailure(QueryFailure failure)
        {
            return new CodeActionSelectionResult(null, null, null, null, null, failure);
        }

        public static CodeActionSelectionResult FromFailure(string diagnostic)
        {
            return new CodeActionSelectionResult(null, null, null, null, null, new QueryFailure(diagnostic));
        }
    }

    private sealed class ScopedFixAllDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly string _diagnosticId;
        private readonly List<string> _diagnostics;

        public ScopedFixAllDiagnosticProvider(string diagnosticId, List<string> diagnostics)
        {
            _diagnosticId = diagnosticId;
            _diagnostics = diagnostics;
        }

        public override async Task<IEnumerable<Microsoft.CodeAnalysis.Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken)
        {
            return await GetDiagnosticsAsync(
                    document.Project,
                    document,
                    includeDocumentDiagnostics: true,
                    includeProjectDiagnostics: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<IEnumerable<Microsoft.CodeAnalysis.Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            return await GetDiagnosticsAsync(
                    project,
                    document: null,
                    includeDocumentDiagnostics: false,
                    includeProjectDiagnostics: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<IEnumerable<Microsoft.CodeAnalysis.Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            return await GetDiagnosticsAsync(
                    project,
                    document: null,
                    includeDocumentDiagnostics: true,
                    includeProjectDiagnostics: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<IEnumerable<Microsoft.CodeAnalysis.Diagnostic>> GetDiagnosticsAsync(
            Project project,
            Document? document,
            bool includeDocumentDiagnostics,
            bool includeProjectDiagnostics,
            CancellationToken cancellationToken)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                _diagnostics.Add($"FixAllCompilationUnavailable: project '{project.Name}' has no compilation.");
                return Array.Empty<Microsoft.CodeAnalysis.Diagnostic>();
            }

            var projectDiagnostics = await VisualStudioWorkspaceQueryService.GetProjectDiagnosticsAsync(
                    project,
                    compilation,
                    _diagnostics,
                    cancellationToken)
                .ConfigureAwait(false);
            var documentTree = document is null
                ? null
                : await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);

            return projectDiagnostics
                .Where(diagnostic => string.Equals(diagnostic.Id, _diagnosticId, StringComparison.OrdinalIgnoreCase))
                .Where(diagnostic =>
                {
                    if (diagnostic.Location.SourceTree is null)
                    {
                        return includeProjectDiagnostics;
                    }

                    if (!includeDocumentDiagnostics)
                    {
                        return false;
                    }

                    if (documentTree is not null && !ReferenceEquals(diagnostic.Location.SourceTree, documentTree))
                    {
                        return false;
                    }

                    var span = CreateSpan(diagnostic.Location);
                    return span is null || !IsGeneratedPath(span.FilePath);
                })
                .ToArray();
        }
    }

    private sealed class CodeFixPreviewReplay
    {
        private CodeFixPreviewReplay(
            WorkspaceMutationPreview preview,
            Solution? changedSolution,
            string actionTitle,
            QueryFailure? failure)
        {
            Preview = preview;
            ChangedSolution = changedSolution;
            ActionTitle = actionTitle;
            Failure = failure;
        }

        public WorkspaceMutationPreview Preview { get; }

        public Solution? ChangedSolution { get; }

        public string ActionTitle { get; }

        public QueryFailure? Failure { get; }

        public static CodeFixPreviewReplay Success(
            WorkspaceMutationPreview preview,
            Solution changedSolution,
            string actionTitle)
        {
            return new CodeFixPreviewReplay(preview, changedSolution, actionTitle, null);
        }

        public static CodeFixPreviewReplay FromFailure(QueryFailure failure)
        {
            return new CodeFixPreviewReplay(new WorkspaceMutationPreview(), null, string.Empty, failure);
        }

        public static CodeFixPreviewReplay FromFailure(string diagnostic)
        {
            return new CodeFixPreviewReplay(new WorkspaceMutationPreview(), null, string.Empty, new QueryFailure(diagnostic));
        }
    }

    private static async Task<IReadOnlyList<Document>> ResolveCleanupDocumentsAsync(
        Solution solution,
        CSharpCleanupRequest request,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var documents = new List<Document>();
        switch (request.ScopeKind)
        {
            case CSharpMutationScopeKind.Document:
                var document = await FindDocumentByPathAsync(solution, request.FilePath!, request.IncludeGeneratedCode, cancellationToken)
                    .ConfigureAwait(false);
                if (document is not null)
                {
                    documents.Add(document);
                }

                break;
            case CSharpMutationScopeKind.Project:
                documents.AddRange(solution.Projects
                    .Where(project => string.Equals(project.Name, request.ProjectName, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(project => project.Documents));
                break;
            case CSharpMutationScopeKind.ChangedFiles:
                foreach (var changedFile in request.ChangedFiles.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var changedDocument = await FindDocumentByPathAsync(solution, changedFile, request.IncludeGeneratedCode, cancellationToken)
                        .ConfigureAwait(false);
                    if (changedDocument is not null)
                    {
                        documents.Add(changedDocument);
                    }
                    else
                    {
                        diagnostics.Add("CleanupChangedFileNotFound: " + changedFile);
                    }
                }

                break;
            case CSharpMutationScopeKind.Solution:
                documents.AddRange(solution.Projects
                    .Where(project => project.Language == LanguageNames.CSharp)
                    .SelectMany(project => project.Documents));
                break;
        }

        return documents
            .Where(document => document.Project.Language == LanguageNames.CSharp)
            .Where(document => request.IncludeGeneratedCode || !IsGeneratedDocument(document))
            .GroupBy(document => document.Id)
            .Select(group => group.First())
            .ToArray();
    }

    private static async Task<Solution> CreateCleanupSolutionAsync(
        Solution solution,
        IReadOnlyList<Document> documents,
        CSharpCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var currentSolution = solution;
        var operations = NormalizeCleanupOperations(request.Operations).ToArray();
        foreach (var originalDocument in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = currentSolution.GetDocument(originalDocument.Id);
            if (document is null)
            {
                continue;
            }

            foreach (var operation in operations)
            {
                document = operation switch
                {
                    CSharpCleanupOperation.Format => await Formatter.FormatAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false),
                    CSharpCleanupOperation.Simplify => await Simplifier.ReduceAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false),
                    CSharpCleanupOperation.OrganizeUsings => await OrganizeDocumentUsingsAsync(document, cancellationToken).ConfigureAwait(false),
                    _ => document,
                };
            }

            currentSolution = document.Project.Solution;
        }

        return currentSolution;
    }

    private static async Task<Document> OrganizeDocumentUsingsAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return document;
        }

        var organizedUsings = compilationUnit.Usings
            .GroupBy(item => item.ToString(), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? 1 : 0)
            .ThenBy(item => item.Alias is null ? 1 : 0)
            .ThenBy(item => item.Name?.ToString(), StringComparer.Ordinal)
            .ToArray();
        var newRoot = compilationUnit.WithUsings(SyntaxFactory.List(organizedUsings));
        return document.WithSyntaxRoot(newRoot);
    }

    private static WorkspaceMutationApplyResult CreateMutationApplyResult(
        bool applied,
        WorkspaceMutationPreview preview,
        string failure)
    {
        return new WorkspaceMutationApplyResult
        {
            Applied = applied,
            SessionId = preview.SessionId,
            WorkspaceVersion = preview.WorkspaceVersion,
            Preview = preview,
            ApplyFailure = failure,
        };
    }

    private static WorkspaceMutationPreview CreateWorkspaceMutationPreviewFromRename(RenamePreview preview)
    {
        var mutationPreview = new WorkspaceMutationPreview
        {
            Kind = WorkspaceMutationKind.Rename,
            OperationName = "csharp_rename",
            HasConflicts = preview.HasConflicts,
            IsTruncated = preview.IsTruncated,
            AffectedDocumentCount = preview.AffectedDocumentCount,
            ReturnedDocumentCount = preview.ReturnedDocumentCount,
            OmittedGeneratedDocumentCount = preview.OmittedGeneratedDocumentCount,
            UnsupportedDocumentChangeCount = preview.UnsupportedDocumentChangeCount,
            TotalTextChangeCount = preview.TotalTextChangeCount,
            ReturnedTextChangeCount = preview.ReturnedTextChangeCount,
            Documents = preview.Documents.Select(ToWorkspaceMutationDocumentPreview).ToArray(),
            Conflicts = preview.Conflicts.Select(ToWorkspaceMutationConflict).ToArray(),
        };
        AddDefaultMutationBlockers(mutationPreview);
        return mutationPreview;
    }

    private static WorkspaceMutationDocumentPreview ToWorkspaceMutationDocumentPreview(RenameDocumentPreview document)
    {
        return new WorkspaceMutationDocumentPreview
        {
            ProjectName = document.ProjectName,
            OldDocumentName = document.OldDocumentName,
            NewDocumentName = document.NewDocumentName,
            OldFilePath = document.OldFilePath,
            NewFilePath = document.NewFilePath,
            IsGenerated = document.IsGenerated,
            IsDocumentRename = document.IsDocumentRename,
            TextChangeCount = document.TextChangeCount,
            ReturnedTextChangeCount = document.ReturnedTextChangeCount,
            TextChanges = document.TextChanges.Select(change => new WorkspaceMutationTextChange
            {
                OldSpan = change.OldSpan,
                NewSpan = change.NewSpan,
                OldText = change.OldText,
                NewText = change.NewText,
                IsSnippetTruncated = change.IsSnippetTruncated,
            }).ToArray(),
        };
    }

    private static WorkspaceMutationConflict ToWorkspaceMutationConflict(RenameConflict conflict)
    {
        return new WorkspaceMutationConflict
        {
            ProjectName = conflict.ProjectName,
            FilePath = conflict.FilePath,
            Description = conflict.Description,
            Span = conflict.Span,
        };
    }

    private static void AddDefaultMutationBlockers(WorkspaceMutationPreview preview)
    {
        var blockers = new List<WorkspaceMutationBlocker>();
        if (preview.HasConflicts)
        {
            blockers.Add(CreateBlocker(WorkspaceMutationBlockerKind.Conflict, "Conflicts", "Mutation has conflicts and default apply should be blocked."));
        }

        if (preview.IsTruncated)
        {
            blockers.Add(CreateBlocker(WorkspaceMutationBlockerKind.TruncatedPreview, "TruncatedPreview", "Mutation preview was truncated and default apply should be blocked."));
        }

        if (preview.OmittedGeneratedDocumentCount > 0)
        {
            blockers.Add(CreateBlocker(WorkspaceMutationBlockerKind.GeneratedDocumentChanges, "GeneratedDocumentChanges", "Generated document changes were omitted and default apply should be blocked."));
        }

        if (preview.UnsupportedDocumentChangeCount > 0)
        {
            blockers.Add(CreateBlocker(WorkspaceMutationBlockerKind.UnsupportedDocumentChanges, "UnsupportedDocumentChanges", "Mutation produced added/removed document changes not represented as text diffs."));
        }

        preview.Blockers = blockers.ToArray();
    }

    private static WorkspaceMutationBlocker CreateBlocker(
        WorkspaceMutationBlockerKind kind,
        string code,
        string message)
    {
        return new WorkspaceMutationBlocker
        {
            Kind = kind,
            Code = code,
            Message = message,
        };
    }

    private static WorkspaceMutationBlocker? GetCleanupApplyBlocker(
        WorkspaceMutationPreview preview,
        CSharpCleanupApplyRequest request)
    {
        if (preview.IsTruncated && !request.AllowTruncatedPreview)
        {
            return CreateBlocker(WorkspaceMutationBlockerKind.TruncatedPreview, "CleanupApplyBlocked", "CleanupApplyBlocked: cleanup preview was truncated. Increase MaxTextChanges or set AllowTruncatedPreview=true.");
        }

        if (preview.OmittedGeneratedDocumentCount > 0 && !request.AllowGeneratedDocumentChanges)
        {
            return CreateBlocker(WorkspaceMutationBlockerKind.GeneratedDocumentChanges, "CleanupApplyBlocked", "CleanupApplyBlocked: generated document changes were omitted. Retry with IncludeGeneratedCode=true and AllowGeneratedDocumentChanges=true if intentional.");
        }

        if (preview.UnsupportedDocumentChangeCount > 0 && !request.AllowUnsupportedDocumentChanges)
        {
            return CreateBlocker(WorkspaceMutationBlockerKind.UnsupportedDocumentChanges, "CleanupApplyBlocked", "CleanupApplyBlocked: cleanup produced added/removed document changes that are not represented as text diffs.");
        }

        return null;
    }

    private static async Task<WorkspaceMutationPreview> CreateWorkspaceMutationPreviewAsync(
        WorkspaceMutationKind kind,
        string operationName,
        Solution oldSolution,
        Solution newSolution,
        int maxTextChanges,
        int maxSnippetLength,
        bool includeGeneratedCode,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var changedDocumentIds = new HashSet<DocumentId>();
        var unsupportedDocumentChanges = 0;
        foreach (var projectChanges in newSolution.GetChanges(oldSolution).GetProjectChanges())
        {
            foreach (var documentId in projectChanges.GetChangedDocuments())
            {
                changedDocumentIds.Add(documentId);
            }

            unsupportedDocumentChanges += projectChanges.GetAddedDocuments().Count();
            unsupportedDocumentChanges += projectChanges.GetRemovedDocuments().Count();
        }

        var documents = new List<WorkspaceMutationDocumentPreview>();
        var affectedDocumentCount = 0;
        var omittedGeneratedDocumentCount = 0;
        var totalTextChangeCount = 0;
        var returnedTextChangeCount = 0;
        var textChangesTruncated = false;

        foreach (var documentId in changedDocumentIds
                     .OrderBy(id => newSolution.GetDocument(id)?.Project.Name ?? oldSolution.GetDocument(id)?.Project.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(id => newSolution.GetDocument(id)?.FilePath ?? oldSolution.GetDocument(id)?.FilePath ?? newSolution.GetDocument(id)?.Name ?? oldSolution.GetDocument(id)?.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldDocument = oldSolution.GetDocument(documentId);
            var newDocument = newSolution.GetDocument(documentId);
            if (oldDocument is null || newDocument is null)
            {
                unsupportedDocumentChanges++;
                continue;
            }

            var oldText = await oldDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var newText = await newDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var textChanges = newText.GetTextChanges(oldText).OrderBy(change => change.Span.Start).ToArray();
            if (textChanges.Length == 0)
            {
                continue;
            }

            affectedDocumentCount++;
            totalTextChangeCount += textChanges.Length;

            var oldFilePath = oldDocument.FilePath ?? oldDocument.Name;
            var newFilePath = newDocument.FilePath ?? newDocument.Name;
            var isGenerated = IsGeneratedDocument(oldDocument) || IsGeneratedDocument(newDocument);
            if (isGenerated && !includeGeneratedCode)
            {
                omittedGeneratedDocumentCount++;
                continue;
            }

            var returnedChanges = new List<WorkspaceMutationTextChange>();
            var runningDelta = 0;
            foreach (var change in textChanges)
            {
                var newChangeText = change.NewText ?? string.Empty;
                var newStart = change.Span.Start + runningDelta;
                var newSpan = new TextSpan(newStart, newChangeText.Length);
                runningDelta += newChangeText.Length - change.Span.Length;

                if (returnedTextChangeCount >= maxTextChanges)
                {
                    textChangesTruncated = true;
                    continue;
                }

                returnedChanges.Add(new WorkspaceMutationTextChange
                {
                    OldSpan = CreateSpan(oldFilePath, oldText, change.Span),
                    NewSpan = CreateSpan(newFilePath, newText, newSpan),
                    OldText = TruncateSnippet(oldText.ToString(change.Span), maxSnippetLength, out var oldTruncated),
                    NewText = TruncateSnippet(newChangeText, maxSnippetLength, out var newTruncated),
                    IsSnippetTruncated = oldTruncated || newTruncated,
                });
                returnedTextChangeCount++;
            }

            documents.Add(new WorkspaceMutationDocumentPreview
            {
                ProjectName = newDocument.Project.Name,
                OldDocumentName = oldDocument.Name,
                NewDocumentName = newDocument.Name,
                OldFilePath = oldFilePath,
                NewFilePath = newFilePath,
                IsGenerated = isGenerated,
                IsDocumentRename = !string.Equals(oldDocument.Name, newDocument.Name, StringComparison.Ordinal)
                    || !string.Equals(oldDocument.FilePath, newDocument.FilePath, StringComparison.OrdinalIgnoreCase),
                TextChangeCount = textChanges.Length,
                ReturnedTextChangeCount = returnedChanges.Count,
                TextChanges = returnedChanges.ToArray(),
            });
        }

        if (unsupportedDocumentChanges > 0)
        {
            diagnostics.Add($"{operationName} saw {unsupportedDocumentChanges} added/removed document changes that are not represented as text diffs.");
        }

        if (omittedGeneratedDocumentCount > 0)
        {
            diagnostics.Add($"Generated document changes were omitted for {omittedGeneratedDocumentCount} document(s). Retry with IncludeGeneratedCode=true to include them.");
        }

        if (textChangesTruncated)
        {
            diagnostics.Add($"{operationName} text changes were truncated at MaxTextChanges={maxTextChanges}.");
        }

        var preview = new WorkspaceMutationPreview
        {
            Kind = kind,
            OperationName = operationName,
            HasConflicts = false,
            IsTruncated = textChangesTruncated,
            AffectedDocumentCount = affectedDocumentCount,
            ReturnedDocumentCount = documents.Count,
            OmittedGeneratedDocumentCount = omittedGeneratedDocumentCount,
            UnsupportedDocumentChangeCount = unsupportedDocumentChanges,
            TotalTextChangeCount = totalTextChangeCount,
            ReturnedTextChangeCount = returnedTextChangeCount,
            Documents = documents.ToArray(),
            Conflicts = Array.Empty<WorkspaceMutationConflict>(),
        };
        AddDefaultMutationBlockers(preview);
        return preview;
    }

    private static WorkspaceQueryResult<CSharpCodeFixPreview> CreateUnsupportedCodeFixPreview(
        CSharpCodeFixRequest request,
        WorkspaceMutationKind kind,
        string operationName)
    {
        var preview = CreateUnsupportedCodeFixMutationPreview(request, kind, operationName);
        return Success(
            new[]
            {
                new CSharpCodeFixPreview
                {
                    DiagnosticId = request.DiagnosticId,
                    FixTitle = request.FixTitle ?? string.Empty,
                    ScopeKind = request.ScopeKind,
                    MutationPreview = preview,
                },
            },
            new[] { "Provider-backed Roslyn CodeAction preview is not enabled yet; this tool returns a blocker instead of writing source." },
            isPartial: true);
    }

    private static WorkspaceQueryResult<CSharpCodeFixApplyResult> CreateUnsupportedCodeFixApplyResult(
        CSharpCodeFixRequest request,
        WorkspaceMutationKind kind,
        string operationName)
    {
        var preview = CreateUnsupportedCodeFixMutationPreview(request, kind, operationName);
        const string failure = "UnsupportedOperation: provider-backed Roslyn CodeAction apply is not enabled yet.";
        return Success(
            new[]
            {
                new CSharpCodeFixApplyResult
                {
                    Applied = false,
                    Preview = new CSharpCodeFixPreview
                    {
                        DiagnosticId = request.DiagnosticId,
                        FixTitle = request.FixTitle ?? string.Empty,
                        ScopeKind = request.ScopeKind,
                        MutationPreview = preview,
                    },
                    ApplyFailure = failure,
                    MutationResult = CreateMutationApplyResult(false, preview, failure),
                },
            },
            new[] { failure },
            isPartial: true);
    }

    private static WorkspaceQueryResult<CSharpCodeFixApplyResult> CreateCodeFixApplyBlockedResult(
        CSharpCodeFixRequest request,
        WorkspaceMutationPreview preview,
        string actionTitle,
        string failure,
        IReadOnlyList<string> diagnostics)
    {
        return Success(
            new[]
            {
                new CSharpCodeFixApplyResult
                {
                    Applied = false,
                    Preview = new CSharpCodeFixPreview
                    {
                        DiagnosticId = request.DiagnosticId,
                        FixTitle = actionTitle,
                        ScopeKind = request.ScopeKind,
                        MutationPreview = preview,
                    },
                    ApplyFailure = failure,
                    MutationResult = CreateMutationApplyResult(false, preview, failure),
                },
            },
            diagnostics,
            isPartial: true);
    }

    private static WorkspaceMutationPreview CreateUnsupportedCodeFixMutationPreview(
        CSharpCodeFixRequest request,
        WorkspaceMutationKind kind,
        string operationName)
    {
        var candidate = CreateCodeFixCandidateIdentity(request);
        return new WorkspaceMutationPreview
        {
            SessionId = CreateMutationSessionId(kind, request),
            WorkspaceVersion = string.Empty,
            CandidateIdentity = candidate,
            Kind = kind,
            OperationName = operationName,
            Blockers = new[]
            {
                CreateBlocker(
                    WorkspaceMutationBlockerKind.ProviderUnavailable,
                    "CodeFixProviderExecutionUnavailable",
                    "Provider-backed Roslyn CodeAction execution is not enabled in this build; the mutation candidate identity is returned for session tracking only."),
            },
        };
    }

    private static MutationCandidateIdentity CreateCodeFixCandidateIdentity(CSharpCodeFixRequest request)
    {
        var title = request.FixTitle ?? string.Empty;
        var providerName = request.ProviderName ?? string.Empty;
        var equivalenceKey = request.EquivalenceKey ?? string.Empty;
        var scope = request.ScopeKind.ToString();
        var documentOrProject = request.FilePath ?? request.ProjectName ?? string.Empty;
        var stableKey = !string.IsNullOrWhiteSpace(request.CandidateStableKey)
            ? request.CandidateStableKey!
            : CreateMutationCandidateStableKey(request.DiagnosticId, providerName, equivalenceKey, title, scope, documentOrProject);
        return new MutationCandidateIdentity
        {
            DiagnosticId = request.DiagnosticId,
            ProviderName = providerName,
            EquivalenceKey = equivalenceKey,
            Title = title,
            Scope = scope,
            DocumentOrProject = documentOrProject,
            StableKey = stableKey,
        };
    }

    private static string CreateMutationSessionId(WorkspaceMutationKind kind, CSharpCodeFixRequest request)
    {
        return CreateMutationSessionId(kind, request.DiagnosticId, CreateCodeFixCandidateIdentity(request).StableKey);
    }

    private static string CreateMutationSessionId(WorkspaceMutationKind kind, string diagnosticId, string stableKey)
    {
        return string.Join(
            ":",
            "mutation",
            kind.ToString().ToLowerInvariant(),
            diagnosticId,
            unchecked((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(stableKey)).ToString("X8"));
    }

    private static string CreateMutationCandidateStableKey(
        string diagnosticId,
        string providerName,
        string equivalenceKey,
        string title,
        string scope,
        string documentOrProject)
    {
        return string.Join(
            "|",
            diagnosticId,
            providerName,
            equivalenceKey,
            title,
            scope,
            documentOrProject);
    }

    private static CSharpRefactoringPlan CreateRefactoringPlan(
        CSharpRefactoringPlanRequest request,
        SymbolDescriptor? targetSymbol)
    {
        var blockers = new List<WorkspaceMutationBlocker>();
        var recommendedTools = new List<string>();
        var requiredInputs = new List<string>();
        var safetyChecks = new List<string>
        {
            "Use preview before apply for every source mutation.",
            "Apply requires explicit Visual Studio target.",
            "Block truncated preview, generated omissions, unsupported document changes, conflicts, and workspace apply rejection by default.",
        };

        var isSupportedForPreview = false;
        var isSupportedForApply = false;
        var mutationKind = WorkspaceMutationKind.Refactoring;
        var summary = request.Kind switch
        {
            CSharpRefactoringPlanKind.Rename => "Use Roslyn symbol rename preview/apply for this refactoring.",
            CSharpRefactoringPlanKind.Cleanup => "Use C# cleanup preview/apply for format, organize usings, and simplify.",
            CSharpRefactoringPlanKind.CodeFix => "Use diagnostic-driven code fix listing plus provider-backed CodeAction or Fix All preview/apply through mutation session replay validation.",
            CSharpRefactoringPlanKind.ExtractMethod => "Extract Method requires provider-specific selection parameters and is plan-only in this build.",
            CSharpRefactoringPlanKind.ChangeSignature => "Change Signature requires provider-specific parameter ordering and call-site update modeling; it is plan-only in this build.",
            CSharpRefactoringPlanKind.MoveType => "Move Type requires file/project destination modeling and namespace policy; it is plan-only in this build.",
            _ => "Unknown refactoring kind; provide a supported kind.",
        };

        switch (request.Kind)
        {
            case CSharpRefactoringPlanKind.Rename:
                mutationKind = WorkspaceMutationKind.Rename;
                isSupportedForPreview = true;
                isSupportedForApply = true;
                requiredInputs.Add("SymbolKey or source position");
                requiredInputs.Add("NewName");
                recommendedTools.Add("preview_csharp_rename");
                recommendedTools.Add("apply_csharp_rename");
                break;
            case CSharpRefactoringPlanKind.Cleanup:
                mutationKind = WorkspaceMutationKind.Cleanup;
                isSupportedForPreview = true;
                isSupportedForApply = true;
                requiredInputs.Add("Document, project, changed-files, or explicitly allowed solution scope");
                recommendedTools.Add("preview_csharp_cleanup");
                recommendedTools.Add("apply_csharp_cleanup");
                break;
            case CSharpRefactoringPlanKind.CodeFix:
                mutationKind = WorkspaceMutationKind.CodeFix;
                isSupportedForPreview = true;
                isSupportedForApply = true;
                requiredInputs.Add("DiagnosticId and document/project scope; changed-files is supported for candidate listing only");
                recommendedTools.Add("list_csharp_code_fixes");
                recommendedTools.Add("preview_csharp_code_fix");
                recommendedTools.Add("apply_csharp_code_fix");
                recommendedTools.Add("preview_csharp_fix_all");
                recommendedTools.Add("apply_csharp_fix_all");
                break;
            case CSharpRefactoringPlanKind.ExtractMethod:
            case CSharpRefactoringPlanKind.ChangeSignature:
            case CSharpRefactoringPlanKind.MoveType:
                blockers.Add(CreateBlocker(WorkspaceMutationBlockerKind.UnsupportedOperation, "ComplexRefactoringPlanOnly", "This refactoring is plan-only until provider-specific preview/apply support is implemented."));
                recommendedTools.Add("get_csharp_source_context");
                recommendedTools.Add("analyze_csharp_symbol_impact");
                recommendedTools.Add("plan_csharp_verification");
                break;
            default:
                blockers.Add(CreateBlocker(WorkspaceMutationBlockerKind.InvalidScope, "UnknownRefactoringKind", "Provide a supported CSharpRefactoringPlanKind."));
                break;
        }

        return new CSharpRefactoringPlan
        {
            Kind = request.Kind,
            Summary = summary,
            IsSupportedForPreview = isSupportedForPreview,
            IsSupportedForApply = isSupportedForApply,
            MutationKind = mutationKind,
            TargetSymbol = targetSymbol,
            TargetSpan = targetSymbol?.Span,
            RequiredInputs = requiredInputs.ToArray(),
            SafetyChecks = safetyChecks.ToArray(),
            RecommendedTools = recommendedTools.ToArray(),
            Blockers = blockers.ToArray(),
        };
    }

    private static string? ValidateRenamePreviewRequest(RenamePreviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            return "NewName is required.";
        }

        if (!SyntaxFacts.IsValidIdentifier(request.NewName))
        {
            return "InvalidIdentifier: NewName must be a valid C# identifier.";
        }

        if (request.MaxTextChanges is < 1 or > 10000)
        {
            return "MaxTextChanges must be between 1 and 10000.";
        }

        if (request.MaxSnippetLength is < 0 or > 2000)
        {
            return "MaxSnippetLength must be between 0 and 2000.";
        }

        return null;
    }

    private static string? GetRenameApplyBlocker(RenamePreview preview, RenameApplyRequest request)
    {
        if (preview.HasConflicts && !request.AllowConflicts)
        {
            return "RenameApplyBlocked: Roslyn reported rename conflicts. Retry only if AllowConflicts=true is intentional.";
        }

        if (preview.IsTruncated && !request.AllowTruncatedPreview)
        {
            return "RenameApplyBlocked: rename preview was truncated. Increase MaxTextChanges or set AllowTruncatedPreview=true.";
        }

        if (preview.OmittedGeneratedDocumentCount > 0 && !request.AllowGeneratedDocumentChanges)
        {
            return "RenameApplyBlocked: generated document changes were omitted. Retry with IncludeGeneratedCode=true and AllowGeneratedDocumentChanges=true if intentional.";
        }

        if (preview.UnsupportedDocumentChangeCount > 0 && !request.AllowUnsupportedDocumentChanges)
        {
            return "RenameApplyBlocked: rename produced added/removed document changes that are not represented as text diffs.";
        }

        return null;
    }

    private static RenamePreview CreateEmptyRenamePreview(
        ISymbol symbol,
        SymbolDescriptor descriptor,
        RenamePreviewRequest request,
        bool targetIsGenerated)
    {
        var preview = new RenamePreview
        {
            Symbol = descriptor,
            NewName = request.NewName,
            RenameOverloads = request.RenameOverloads,
            RenameInStrings = request.RenameInStrings,
            RenameInComments = request.RenameInComments,
            RenameFile = request.RenameFile,
            TargetIsGenerated = targetIsGenerated,
            TargetIsMetadata = !symbol.Locations.Any(location => location.IsInSource),
            TargetIsPartial = IsPartialSymbol(symbol),
            HasConflicts = false,
            IsTruncated = false,
            AffectedDocumentCount = 0,
            ReturnedDocumentCount = 0,
            OmittedGeneratedDocumentCount = 0,
            UnsupportedDocumentChangeCount = 0,
            TotalTextChangeCount = 0,
            ReturnedTextChangeCount = 0,
            Documents = Array.Empty<RenameDocumentPreview>(),
            Conflicts = Array.Empty<RenameConflict>(),
        };
        preview.MutationPreview = CreateWorkspaceMutationPreviewFromRename(preview);
        return preview;
    }

    private static async Task<RenamePreview> CreateRenamePreviewAsync(
        Solution oldSolution,
        Solution newSolution,
        ISymbol symbol,
        SymbolDescriptor descriptor,
        RenamePreviewRequest request,
        bool targetIsGenerated,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var changedDocumentIds = new HashSet<DocumentId>();
        var unsupportedDocumentChanges = 0;
        foreach (var projectChanges in newSolution.GetChanges(oldSolution).GetProjectChanges())
        {
            foreach (var documentId in projectChanges.GetChangedDocuments())
            {
                changedDocumentIds.Add(documentId);
            }

            unsupportedDocumentChanges += projectChanges.GetAddedDocuments().Count();
            unsupportedDocumentChanges += projectChanges.GetRemovedDocuments().Count();
        }

        if (unsupportedDocumentChanges > 0)
        {
            diagnostics.Add($"Rename preview saw {unsupportedDocumentChanges} added/removed document changes that are not represented as text diffs.");
        }

        var documents = new List<RenameDocumentPreview>();
        var conflicts = new List<RenameConflict>();
        var affectedDocumentCount = 0;
        var omittedGeneratedDocumentCount = 0;
        var totalTextChangeCount = 0;
        var returnedTextChangeCount = 0;
        var textChangesTruncated = false;

        foreach (var documentId in changedDocumentIds
                     .OrderBy(id => newSolution.GetDocument(id)?.Project.Name ?? oldSolution.GetDocument(id)?.Project.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(id => newSolution.GetDocument(id)?.FilePath ?? oldSolution.GetDocument(id)?.FilePath ?? newSolution.GetDocument(id)?.Name ?? oldSolution.GetDocument(id)?.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldDocument = oldSolution.GetDocument(documentId);
            var newDocument = newSolution.GetDocument(documentId);
            if (oldDocument is null || newDocument is null)
            {
                unsupportedDocumentChanges++;
                continue;
            }

            var oldText = await oldDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var newText = await newDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var textChanges = newText.GetTextChanges(oldText)
                .OrderBy(change => change.Span.Start)
                .ToArray();
            var isDocumentRename = !string.Equals(oldDocument.Name, newDocument.Name, StringComparison.Ordinal)
                || !string.Equals(oldDocument.FilePath, newDocument.FilePath, StringComparison.OrdinalIgnoreCase);

            if (textChanges.Length == 0 && !isDocumentRename)
            {
                continue;
            }

            affectedDocumentCount++;
            totalTextChangeCount += textChanges.Length;

            var oldFilePath = oldDocument.FilePath ?? oldDocument.Name;
            var newFilePath = newDocument.FilePath ?? newDocument.Name;
            var isGenerated = IsGeneratedDocument(oldDocument) || IsGeneratedDocument(newDocument);
            if (isGenerated && !request.IncludeGeneratedCode)
            {
                omittedGeneratedDocumentCount++;
                continue;
            }

            var returnedChanges = new List<RenameTextChange>();
            var runningDelta = 0;
            foreach (var change in textChanges)
            {
                var newChangeText = change.NewText ?? string.Empty;
                var newStart = change.Span.Start + runningDelta;
                var newSpan = new TextSpan(newStart, newChangeText.Length);
                runningDelta += newChangeText.Length - change.Span.Length;

                if (returnedTextChangeCount >= request.MaxTextChanges)
                {
                    textChangesTruncated = true;
                    continue;
                }

                returnedChanges.Add(new RenameTextChange
                {
                    OldSpan = CreateSpan(oldFilePath, oldText, change.Span),
                    NewSpan = CreateSpan(newFilePath, newText, newSpan),
                    OldText = TruncateSnippet(oldText.ToString(change.Span), request.MaxSnippetLength, out var oldTruncated),
                    NewText = TruncateSnippet(newChangeText, request.MaxSnippetLength, out var newTruncated),
                    IsSnippetTruncated = oldTruncated || newTruncated,
                });
                returnedTextChangeCount++;
            }

            conflicts.AddRange(await CollectRenameConflictsAsync(newDocument, newText, cancellationToken).ConfigureAwait(false));

            documents.Add(new RenameDocumentPreview
            {
                ProjectName = newDocument.Project.Name,
                OldDocumentName = oldDocument.Name,
                NewDocumentName = newDocument.Name,
                OldFilePath = oldFilePath,
                NewFilePath = newFilePath,
                IsGenerated = isGenerated,
                IsDocumentRename = isDocumentRename,
                TextChangeCount = textChanges.Length,
                ReturnedTextChangeCount = returnedChanges.Count,
                TextChanges = returnedChanges.ToArray(),
            });
        }

        if (omittedGeneratedDocumentCount > 0)
        {
            diagnostics.Add($"Generated document changes were omitted for {omittedGeneratedDocumentCount} document(s). Retry with IncludeGeneratedCode=true to include them.");
        }

        if (textChangesTruncated)
        {
            diagnostics.Add($"Rename preview text changes were truncated at MaxTextChanges={request.MaxTextChanges}.");
        }

        if (conflicts.Count > 0)
        {
            diagnostics.Add("Roslyn reported rename conflict annotations; inspect Conflicts before applying any manual edits.");
        }

        var preview = new RenamePreview
        {
            Symbol = descriptor,
            NewName = request.NewName,
            RenameOverloads = request.RenameOverloads,
            RenameInStrings = request.RenameInStrings,
            RenameInComments = request.RenameInComments,
            RenameFile = request.RenameFile,
            TargetIsGenerated = targetIsGenerated,
            TargetIsMetadata = !symbol.Locations.Any(location => location.IsInSource),
            TargetIsPartial = IsPartialSymbol(symbol),
            HasConflicts = conflicts.Count > 0,
            IsTruncated = textChangesTruncated,
            AffectedDocumentCount = affectedDocumentCount,
            ReturnedDocumentCount = documents.Count,
            OmittedGeneratedDocumentCount = omittedGeneratedDocumentCount,
            UnsupportedDocumentChangeCount = unsupportedDocumentChanges,
            TotalTextChangeCount = totalTextChangeCount,
            ReturnedTextChangeCount = returnedTextChangeCount,
            Documents = documents.ToArray(),
            Conflicts = conflicts.ToArray(),
        };
        preview.MutationPreview = CreateWorkspaceMutationPreviewFromRename(preview);
        return preview;
    }

    private static async Task<IReadOnlyList<RenameConflict>> CollectRenameConflictsAsync(
        Document document,
        SourceText sourceText,
        CancellationToken cancellationToken)
    {
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null)
        {
            return Array.Empty<RenameConflict>();
        }

        var filePath = document.FilePath ?? document.Name;
        var conflicts = new List<RenameConflict>();
        foreach (var annotatedNodeOrToken in syntaxRoot.GetAnnotatedNodesAndTokens(ConflictAnnotation.Kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var annotation in annotatedNodeOrToken.GetAnnotations(ConflictAnnotation.Kind))
            {
                conflicts.Add(new RenameConflict
                {
                    ProjectName = document.Project.Name,
                    FilePath = filePath,
                    Description = ConflictAnnotation.GetDescription(annotation) ?? string.Empty,
                    Span = CreateSpan(filePath, sourceText, annotatedNodeOrToken.Span),
                });
            }
        }

        return conflicts;
    }
}
