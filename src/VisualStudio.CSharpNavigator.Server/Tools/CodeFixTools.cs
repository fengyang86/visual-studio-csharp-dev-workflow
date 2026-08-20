using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Agentic;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class CodeFixTools
{
    private readonly IVisualStudioWorkspaceBridge _workspaceBridge;
    private readonly MutationSessionStore _mutationSessionStore;

    public CodeFixTools(IVisualStudioWorkspaceBridge workspaceBridge)
        : this(workspaceBridge, new MutationSessionStore())
    {
    }

    public CodeFixTools(IVisualStudioWorkspaceBridge workspaceBridge, MutationSessionStore mutationSessionStore)
    {
        _workspaceBridge = workspaceBridge;
        _mutationSessionStore = mutationSessionStore;
    }

    [McpServerTool(Name = "list_csharp_code_fixes", ReadOnly = true, Idempotent = true)]
    [Description("List provider-backed C# CodeAction candidates from scoped Roslyn diagnostics, with explicit fallback when providers are unavailable.")]
    public Task<WorkspaceQueryResult<CSharpCodeFixCandidate>> ListCSharpCodeFixes(
        [Description("Code fix scope: Document, Project, ChangedFiles, or Solution.")]
        CSharpMutationScopeKind scopeKind = CSharpMutationScopeKind.Document,
        [Description("Absolute source file path for document scope.")]
        string? filePath = null,
        [Description("Visual Studio project name for project scope.")]
        string? projectName = null,
        [Description("Changed file paths for changed-files scope.")]
        string[]? changedFiles = null,
        [Description("Optional file, directory, or wildcard patterns to include. Use this to focus code fix candidates on touched folders.")]
        string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude. Use this to suppress known noisy generated, vendor, or legacy folders.")]
        string[]? excludePathPatterns = null,
        [Description("Optional diagnostic id filter, such as IDE0005 or CS8618.")]
        string? diagnosticId = null,
        [Description("Optional minimum severity filter: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Maximum candidates to return.")]
        int maxResults = 100,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateList(scopeKind, filePath, projectName, changedFiles, maxResults);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpCodeFixCandidate>(validation));
        }

        return _workspaceBridge.ListCodeFixesAsync(
            new CSharpCodeFixListRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                ScopeKind = scopeKind,
                FilePath = filePath,
                ProjectName = projectName,
                ChangedFiles = Normalize(changedFiles),
                IncludePathPatterns = Normalize(includePathPatterns),
                ExcludePathPatterns = Normalize(excludePathPatterns),
                DiagnosticId = diagnosticId,
                MinimumSeverity = minimumSeverity,
                NoiseProfile = noiseProfile,
                MaxResults = maxResults,
            },
            cancellationToken);
    }

    [McpServerTool(Name = "preview_csharp_code_fix", ReadOnly = true, Idempotent = true)]
    [Description("Preview a single provider-backed C# CodeAction through the unified mutation model without applying source changes.")]
    public Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewCSharpCodeFix(
        string diagnosticId,
        CSharpMutationScopeKind scopeKind = CSharpMutationScopeKind.Document,
        string? filePath = null,
        string? projectName = null,
        string? fixTitle = null,
        string? providerName = null,
        string? equivalenceKey = null,
        string? candidateStableKey = null,
        int maxTextChanges = 1000,
        int maxSnippetLength = 200,
        bool allowSolutionScope = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateMutation(diagnosticId, scopeKind, filePath, projectName, maxTextChanges, maxSnippetLength, allowSolutionScope);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpCodeFixPreview>(validation));
        }

        return PreviewCodeFixAndRecordSessionAsync(
            CreateRequest(diagnosticId, scopeKind, filePath, projectName, fixTitle, providerName, equivalenceKey, candidateStableKey, previewSessionId: null, expectedWorkspaceVersion: null, maxTextChanges, maxSnippetLength, allowSolutionScope, targetPipeName, targetInstanceId, targetSolutionPath),
            cancellationToken);
    }

    [McpServerTool(Name = "apply_csharp_code_fix", ReadOnly = false, Idempotent = false)]
    [Description("Apply a single C# code fix after preview-session replay safety checks. Requires an explicit target and previewSessionId from preview_csharp_code_fix.")]
    public Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyCSharpCodeFix(
        string diagnosticId,
        CSharpMutationScopeKind scopeKind = CSharpMutationScopeKind.Document,
        string? filePath = null,
        string? projectName = null,
        string? fixTitle = null,
        string? providerName = null,
        string? equivalenceKey = null,
        string? candidateStableKey = null,
        string? previewSessionId = null,
        string? expectedWorkspaceVersion = null,
        int maxTextChanges = 1000,
        int maxSnippetLength = 200,
        bool allowSolutionScope = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var targetValidation = ValidateExplicitTarget(targetPipeName, targetInstanceId, targetSolutionPath, "apply_csharp_code_fix");
        if (targetValidation is not null)
        {
            return Task.FromResult(Failure<CSharpCodeFixApplyResult>(targetValidation));
        }

        var validation = ValidateMutation(diagnosticId, scopeKind, filePath, projectName, maxTextChanges, maxSnippetLength, allowSolutionScope);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpCodeFixApplyResult>(validation));
        }

        var request = CreateRequest(diagnosticId, scopeKind, filePath, projectName, fixTitle, providerName, equivalenceKey, candidateStableKey, previewSessionId, expectedWorkspaceVersion, maxTextChanges, maxSnippetLength, allowSolutionScope, targetPipeName, targetInstanceId, targetSolutionPath);
        return ApplyCodeFixWithSessionValidationAsync(request, cancellationToken);
    }

    [McpServerTool(Name = "preview_csharp_fix_all", ReadOnly = true, Idempotent = true)]
    [Description("Preview provider-backed Fix All for a diagnostic id through the unified mutation model. Solution scope requires allowSolutionScope=true.")]
    public Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewCSharpFixAll(
        string diagnosticId,
        CSharpMutationScopeKind scopeKind = CSharpMutationScopeKind.Project,
        string? filePath = null,
        string? projectName = null,
        string? fixTitle = null,
        string? providerName = null,
        string? equivalenceKey = null,
        string? candidateStableKey = null,
        int maxTextChanges = 1000,
        int maxSnippetLength = 200,
        bool allowSolutionScope = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateMutation(diagnosticId, scopeKind, filePath, projectName, maxTextChanges, maxSnippetLength, allowSolutionScope);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpCodeFixPreview>(validation));
        }

        return PreviewFixAllAndRecordSessionAsync(
            CreateRequest(diagnosticId, scopeKind, filePath, projectName, fixTitle, providerName, equivalenceKey, candidateStableKey, previewSessionId: null, expectedWorkspaceVersion: null, maxTextChanges, maxSnippetLength, allowSolutionScope, targetPipeName, targetInstanceId, targetSolutionPath),
            cancellationToken);
    }

    [McpServerTool(Name = "apply_csharp_fix_all", ReadOnly = false, Idempotent = false)]
    [Description("Apply provider-backed Fix All after preview-session replay safety checks. Requires an explicit target and previewSessionId from preview_csharp_fix_all.")]
    public Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyCSharpFixAll(
        string diagnosticId,
        CSharpMutationScopeKind scopeKind = CSharpMutationScopeKind.Project,
        string? filePath = null,
        string? projectName = null,
        string? fixTitle = null,
        string? providerName = null,
        string? equivalenceKey = null,
        string? candidateStableKey = null,
        string? previewSessionId = null,
        string? expectedWorkspaceVersion = null,
        int maxTextChanges = 1000,
        int maxSnippetLength = 200,
        bool allowSolutionScope = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var targetValidation = ValidateExplicitTarget(targetPipeName, targetInstanceId, targetSolutionPath, "apply_csharp_fix_all");
        if (targetValidation is not null)
        {
            return Task.FromResult(Failure<CSharpCodeFixApplyResult>(targetValidation));
        }

        var validation = ValidateMutation(diagnosticId, scopeKind, filePath, projectName, maxTextChanges, maxSnippetLength, allowSolutionScope);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpCodeFixApplyResult>(validation));
        }

        var request = CreateRequest(diagnosticId, scopeKind, filePath, projectName, fixTitle, providerName, equivalenceKey, candidateStableKey, previewSessionId, expectedWorkspaceVersion, maxTextChanges, maxSnippetLength, allowSolutionScope, targetPipeName, targetInstanceId, targetSolutionPath);
        return ApplyFixAllWithSessionValidationAsync(request, cancellationToken);
    }

    private async Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewCodeFixAndRecordSessionAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workspaceBridge.PreviewCodeFixAsync(request, cancellationToken).ConfigureAwait(false);
        foreach (var preview in result.Items)
        {
            _mutationSessionStore.Record(preview.MutationPreview);
        }

        return result;
    }

    private async Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyCodeFixWithSessionValidationAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PreviewSessionId))
        {
            return Failure<CSharpCodeFixApplyResult>(
                "MutationSessionRequired: apply_csharp_code_fix requires previewSessionId from a recent preview_csharp_code_fix result.");
        }

        if (!_mutationSessionStore.TryGet(request.PreviewSessionId!, out var record) || record is null)
        {
            return Failure<CSharpCodeFixApplyResult>(
                "MutationSessionNotFound: preview session was not found or has expired. Re-run preview_csharp_code_fix.");
        }

        PopulateCodeFixRequestFromSession(request, record);
        var previewResult = await _workspaceBridge.PreviewCodeFixAsync(request, cancellationToken).ConfigureAwait(false);
        var preview = previewResult.Items.FirstOrDefault();
        if (preview is null)
        {
            return new WorkspaceQueryResult<CSharpCodeFixApplyResult>
            {
                Diagnostics = previewResult.Diagnostics,
                IsPartial = true,
            };
        }

        var validation = _mutationSessionStore.Validate(request, preview.MutationPreview);
        if (!validation.IsAllowed)
        {
            var diagnostics = previewResult.Diagnostics.Concat(new[] { validation.Blocker! }).ToArray();
            return new WorkspaceQueryResult<CSharpCodeFixApplyResult>
            {
                Items = new[]
                {
                    new CSharpCodeFixApplyResult
                    {
                        Applied = false,
                        Preview = preview,
                        ApplyFailure = validation.Blocker!,
                        MutationResult = new WorkspaceMutationApplyResult
                        {
                            Applied = false,
                            SessionId = preview.MutationPreview.SessionId,
                            WorkspaceVersion = preview.MutationPreview.WorkspaceVersion,
                            Preview = preview.MutationPreview,
                            ApplyFailure = validation.Blocker!,
                        },
                    },
                },
                Diagnostics = diagnostics,
                IsPartial = true,
            };
        }

        return await _workspaceBridge.ApplyCodeFixAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewFixAllAndRecordSessionAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workspaceBridge.PreviewFixAllAsync(request, cancellationToken).ConfigureAwait(false);
        foreach (var preview in result.Items)
        {
            _mutationSessionStore.Record(preview.MutationPreview);
        }

        return result;
    }

    private async Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyFixAllWithSessionValidationAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PreviewSessionId))
        {
            return Failure<CSharpCodeFixApplyResult>(
                "MutationSessionRequired: apply_csharp_fix_all requires previewSessionId from a recent preview_csharp_fix_all result.");
        }

        if (!_mutationSessionStore.TryGet(request.PreviewSessionId!, out var record) || record is null)
        {
            return Failure<CSharpCodeFixApplyResult>(
                "MutationSessionNotFound: preview session was not found or has expired. Re-run preview_csharp_fix_all.");
        }

        PopulateCodeFixRequestFromSession(request, record);
        var previewResult = await _workspaceBridge.PreviewFixAllAsync(request, cancellationToken).ConfigureAwait(false);
        var preview = previewResult.Items.FirstOrDefault();
        if (preview is null)
        {
            return new WorkspaceQueryResult<CSharpCodeFixApplyResult>
            {
                Diagnostics = previewResult.Diagnostics,
                IsPartial = true,
            };
        }

        var validation = _mutationSessionStore.Validate(request, preview.MutationPreview);
        if (!validation.IsAllowed)
        {
            var diagnostics = previewResult.Diagnostics.Concat(new[] { validation.Blocker! }).ToArray();
            return new WorkspaceQueryResult<CSharpCodeFixApplyResult>
            {
                Items = new[]
                {
                    new CSharpCodeFixApplyResult
                    {
                        Applied = false,
                        Preview = preview,
                        ApplyFailure = validation.Blocker!,
                        MutationResult = new WorkspaceMutationApplyResult
                        {
                            Applied = false,
                            SessionId = preview.MutationPreview.SessionId,
                            WorkspaceVersion = preview.MutationPreview.WorkspaceVersion,
                            Preview = preview.MutationPreview,
                            ApplyFailure = validation.Blocker!,
                        },
                    },
                },
                Diagnostics = diagnostics,
                IsPartial = true,
            };
        }

        return await _workspaceBridge.ApplyFixAllAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static void PopulateCodeFixRequestFromSession(
        CSharpCodeFixRequest request,
        MutationSessionRecord record)
    {
        request.ExpectedWorkspaceVersion ??= record.WorkspaceVersion;
        request.CandidateStableKey ??= record.CandidateIdentity.StableKey;
        request.ProviderName ??= record.CandidateIdentity.ProviderName;
        request.EquivalenceKey ??= record.CandidateIdentity.EquivalenceKey;
        request.FixTitle ??= record.CandidateIdentity.Title;
    }

    private static CSharpCodeFixRequest CreateRequest(
        string diagnosticId,
        CSharpMutationScopeKind scopeKind,
        string? filePath,
        string? projectName,
        string? fixTitle,
        string? providerName,
        string? equivalenceKey,
        string? candidateStableKey,
        string? previewSessionId,
        string? expectedWorkspaceVersion,
        int maxTextChanges,
        int maxSnippetLength,
        bool allowSolutionScope,
        string? targetPipeName,
        string? targetInstanceId,
        string? targetSolutionPath)
    {
        return new CSharpCodeFixRequest
        {
            Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
            ScopeKind = scopeKind,
            FilePath = filePath,
            ProjectName = projectName,
            DiagnosticId = diagnosticId,
            FixTitle = fixTitle,
            ProviderName = providerName,
            EquivalenceKey = equivalenceKey,
            CandidateStableKey = candidateStableKey,
            PreviewSessionId = previewSessionId,
            ExpectedWorkspaceVersion = expectedWorkspaceVersion,
            MaxTextChanges = maxTextChanges,
            MaxSnippetLength = maxSnippetLength,
            AllowSolutionScope = allowSolutionScope,
        };
    }

    private static string? ValidateList(
        CSharpMutationScopeKind scopeKind,
        string? filePath,
        string? projectName,
        string[]? changedFiles,
        int maxResults)
    {
        if (maxResults is < 1 or > 500)
        {
            return "maxResults must be between 1 and 500.";
        }

        return scopeKind switch
        {
            CSharpMutationScopeKind.Document when string.IsNullOrWhiteSpace(filePath) => "filePath is required for document code fix scope.",
            CSharpMutationScopeKind.Project when string.IsNullOrWhiteSpace(projectName) => "projectName is required for project code fix scope.",
            CSharpMutationScopeKind.ChangedFiles when Normalize(changedFiles).Length == 0 => "changedFiles is required for changed-files code fix scope.",
            CSharpMutationScopeKind.Unknown => "scopeKind is required.",
            _ => null,
        };
    }

    private static string? ValidateMutation(
        string diagnosticId,
        CSharpMutationScopeKind scopeKind,
        string? filePath,
        string? projectName,
        int maxTextChanges,
        int maxSnippetLength,
        bool allowSolutionScope)
    {
        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            return "diagnosticId is required.";
        }

        if (maxTextChanges is < 1 or > 10000)
        {
            return "maxTextChanges must be between 1 and 10000.";
        }

        if (maxSnippetLength is < 0 or > 2000)
        {
            return "maxSnippetLength must be between 0 and 2000.";
        }

        if (scopeKind == CSharpMutationScopeKind.Solution && !allowSolutionScope)
        {
            return "SolutionScopeBlocked: solution-wide code fix requires allowSolutionScope=true.";
        }

        return scopeKind switch
        {
            CSharpMutationScopeKind.Document when string.IsNullOrWhiteSpace(filePath) => "filePath is required for document code fix scope.",
            CSharpMutationScopeKind.Project when string.IsNullOrWhiteSpace(projectName) => "projectName is required for project code fix scope.",
            CSharpMutationScopeKind.ChangedFiles => "ChangedFiles scope is only supported by list_csharp_code_fixes in this stage.",
            CSharpMutationScopeKind.Unknown => "scopeKind is required.",
            _ => null,
        };
    }

    private static string? ValidateExplicitTarget(string? targetPipeName, string? targetInstanceId, string? targetSolutionPath, string toolName)
    {
        return string.IsNullOrWhiteSpace(targetPipeName)
            && string.IsNullOrWhiteSpace(targetInstanceId)
            && string.IsNullOrWhiteSpace(targetSolutionPath)
                ? $"{toolName} requires an explicit targetPipeName, targetInstanceId, or targetSolutionPath."
                : null;
    }

    private static VisualStudioBridgeTarget CreateTarget(string? pipeName, string? instanceId, string? solutionPath)
    {
        return new VisualStudioBridgeTarget
        {
            PipeName = string.IsNullOrWhiteSpace(pipeName) ? string.Empty : pipeName,
            InstanceId = string.IsNullOrWhiteSpace(instanceId) ? string.Empty : instanceId,
            SolutionPath = string.IsNullOrWhiteSpace(solutionPath) ? string.Empty : solutionPath,
        };
    }

    private static string[] Normalize(string[]? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
    }

    private static WorkspaceQueryResult<T> Failure<T>(string diagnostic)
    {
        return new WorkspaceQueryResult<T>
        {
            Diagnostics = new[] { diagnostic },
            IsPartial = true,
        };
    }
}
