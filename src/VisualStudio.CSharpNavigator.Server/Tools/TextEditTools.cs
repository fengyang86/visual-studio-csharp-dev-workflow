using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class TextEditTools
{
    private readonly IVisualStudioWorkspaceBridge _workspaceBridge;

    public TextEditTools(IVisualStudioWorkspaceBridge workspaceBridge)
    {
        _workspaceBridge = workspaceBridge;
    }

    [McpServerTool(Name = "preview_csharp_text_edits", ReadOnly = true, Idempotent = true)]
    [Description("Preview plain text edits for one C# document through the Visual Studio workspace: returns old/new snippets, text-change diffs, fingerprint, and safety blockers WITHOUT applying anything. Use this instead of editing files on disk when Visual Studio has the document open, so buffers never desync; then call apply_csharp_text_edits after user approval.")]
    public Task<WorkspaceQueryResult<CSharpTextEditPreview>> PreviewCSharpTextEdits(
        [Description("Absolute path of the target document in the active solution.")] string filePath,
        [Description("Edits as 1-based line/column ranges with an EXCLUSIVE end position; columns are character offsets (1 = first character, line length + 1 = end of line). Edits must not overlap.")] CSharpTextEdit[] edits,
        [Description("Maximum text changes included in the preview before it is marked truncated.")] int maxTextChanges = 200,
        [Description("Maximum characters per old/new snippet in the preview.")] int maxSnippetLength = 200,
        [Description("Allow targeting generated documents such as .Designer.cs and source-generated files.")] bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")] string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id. Preferred for repeated calls in one session.")] string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path. Fails with candidates when ambiguous.")] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateTextEditRequest(filePath, edits, maxTextChanges, maxSnippetLength);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpTextEditPreview>(validation));
        }

        return _workspaceBridge.PreviewTextEditAsync(
            new CSharpTextEditRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                FilePath = filePath,
                Edits = edits,
                MaxTextChanges = maxTextChanges,
                MaxSnippetLength = maxSnippetLength,
                IncludeGeneratedCode = includeGeneratedCode,
            },
            cancellationToken);
    }

    [McpServerTool(Name = "apply_csharp_text_edits", ReadOnly = false, Idempotent = false)]
    [Description("Apply previously reviewed plain text edits to one C# document through VisualStudioWorkspace.TryApplyChanges so open buffers update in place without reload prompts. Requires an explicit target and user approval; preview_csharp_text_edits first. Blocks on truncated previews and generated-document changes unless the matching allow flag is set.")]
    public Task<WorkspaceQueryResult<CSharpTextEditApplyResult>> ApplyCSharpTextEdits(
        [Description("Absolute path of the target document in the active solution.")] string filePath,
        [Description("Edits as 1-based line/column ranges with an EXCLUSIVE end position; columns are character offsets (1 = first character, line length + 1 = end of line). Edits must not overlap.")] CSharpTextEdit[] edits,
        [Description("Allow applying changes that touch generated documents; requires includeGeneratedCode=true.")] bool allowGeneratedDocumentChanges = false,
        [Description("Allow applying when the preview reported unsupported (non text-diff) document changes.")] bool allowUnsupportedDocumentChanges = false,
        [Description("Allow applying when the preview was truncated.")] bool allowTruncatedPreview = false,
        [Description("Maximum text changes included in the preview evidence.")] int maxTextChanges = 200,
        [Description("Maximum characters per old/new snippet in the preview evidence.")] int maxSnippetLength = 200,
        [Description("Allow targeting generated documents such as .Designer.cs and source-generated files.")] bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")] string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id. Preferred for repeated calls in one session.")] string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path. Fails with candidates when ambiguous.")] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var targetValidation = ValidateExplicitTarget(targetPipeName, targetInstanceId, targetSolutionPath, "apply_csharp_text_edits");
        if (targetValidation is not null)
        {
            return Task.FromResult(Failure<CSharpTextEditApplyResult>(targetValidation));
        }

        var validation = ValidateTextEditRequest(filePath, edits, maxTextChanges, maxSnippetLength);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpTextEditApplyResult>(validation));
        }

        return _workspaceBridge.ApplyTextEditAsync(
            new CSharpTextEditApplyRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                FilePath = filePath,
                Edits = edits,
                MaxTextChanges = maxTextChanges,
                MaxSnippetLength = maxSnippetLength,
                IncludeGeneratedCode = includeGeneratedCode,
                AllowGeneratedDocumentChanges = allowGeneratedDocumentChanges,
                AllowUnsupportedDocumentChanges = allowUnsupportedDocumentChanges,
                AllowTruncatedPreview = allowTruncatedPreview,
            },
            cancellationToken);
    }

    private static string? ValidateTextEditRequest(string filePath, CSharpTextEdit[] edits, int maxTextChanges, int maxSnippetLength)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "FilePathRequired: a target document path is required.";
        }

        if (edits.Length == 0)
        {
            return "EditsRequired: at least one text edit is required.";
        }

        if (edits.Length > 500)
        {
            return "TooManyEdits: at most 500 edits per request.";
        }

        foreach (var edit in edits)
        {
            if (edit.StartLine < 1
                || edit.StartColumn < 1
                || edit.EndLine < 1
                || edit.EndColumn < 1
                || edit.EndLine < edit.StartLine
                || (edit.EndLine == edit.StartLine && edit.EndColumn < edit.StartColumn))
            {
                return "TextEditInvalidRange: every edit needs 1-based lines/columns with an end position at or after the start position.";
            }
        }

        if (maxTextChanges is < 1 or > 1000)
        {
            return "MaxTextChanges must be between 1 and 1000.";
        }

        if (maxSnippetLength is < 20 or > 4000)
        {
            return "MaxSnippetLength must be between 20 and 4000.";
        }

        return null;
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

    private static WorkspaceQueryResult<T> Failure<T>(string diagnostic)
    {
        return new WorkspaceQueryResult<T>
        {
            Diagnostics = new[] { diagnostic },
            IsPartial = true,
            Succeeded = false,
        };
    }
}
