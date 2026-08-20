using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed partial class VisualStudioWorkspaceQueryService
{
    private static VisualStudioErrorListItem[] ReadErrorListItems(
        EnvDTE.DTE dte,
        int maxResults,
        ICollection<string> diagnostics)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (dte is not EnvDTE80.DTE2 dte2)
        {
            diagnostics.Add("ErrorListUnavailable: EnvDTE80.DTE2 service is not available.");
            return Array.Empty<VisualStudioErrorListItem>();
        }

        EnvDTE80.ErrorItems errorItems;
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            errorItems = dte2.ToolWindows.ErrorList.ErrorItems;
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            diagnostics.Add("ErrorListUnavailable: Visual Studio Error List tool window is not available: " + ex.Message);
            return Array.Empty<VisualStudioErrorListItem>();
        }

        var items = new List<VisualStudioErrorListItem>();
        int count;
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            count = errorItems.Count;
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            diagnostics.Add("ErrorListReadFailed: could not read ErrorItems.Count: " + ex.Message);
            return Array.Empty<VisualStudioErrorListItem>();
        }

        for (var index = 1; index <= count && items.Count < maxResults; index++)
        {
            try
            {
                var item = errorItems.Item(index);
                var severity = item.ErrorLevel.ToString();
                var description = item.Description ?? string.Empty;
                var filePath = item.FileName ?? string.Empty;
                var line = item.Line;
                var column = item.Column;
                var projectName = item.Project?.ToString() ?? string.Empty;

                items.Add(new VisualStudioErrorListItem
                {
                    Severity = severity,
                    Description = description,
                    FilePath = filePath,
                    Line = line,
                    Column = column,
                    ProjectName = projectName,
                });
            }
            catch (Exception ex) when (IsRecoverableException(ex))
            {
                diagnostics.Add($"ErrorListItemReadFailed: failed to read ErrorItems[{index}]: {ex.Message}");
            }
        }

        if (count > maxResults)
        {
            diagnostics.Add($"ErrorListTruncated: returned {items.Count} of {count} Error List item(s).");
        }

        return items.ToArray();
    }

    private static VisualStudioDocumentSnapshot[] ReadOpenDocuments(
        EnvDTE.DTE dte,
        int maxResults,
        bool includeSelection,
        ICollection<string> diagnostics)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var activeFullName = dte.ActiveDocument?.FullName ?? string.Empty;
        var documents = dte.Documents;
        var items = new List<VisualStudioDocumentSnapshot>();
        int count;
        try
        {
            count = documents.Count;
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            diagnostics.Add("OpenDocumentsReadFailed: could not read Documents.Count: " + ex.Message);
            return Array.Empty<VisualStudioDocumentSnapshot>();
        }

        for (var index = 1; index <= count && items.Count < maxResults; index++)
        {
            try
            {
                var document = documents.Item(index);
                items.Add(CreateDocumentSnapshot(
                    dte,
                    document,
                    string.Equals(document.FullName ?? string.Empty, activeFullName, StringComparison.OrdinalIgnoreCase),
                    includeSelection,
                    diagnostics));
            }
            catch (Exception ex) when (IsRecoverableException(ex))
            {
                diagnostics.Add($"OpenDocumentReadFailed: failed to read Documents[{index}]: {ex.Message}");
            }
        }

        if (count > maxResults)
        {
            diagnostics.Add($"OpenDocumentsTruncated: returned {items.Count} of {count} open document(s).");
        }

        return items.ToArray();
    }

    private static VisualStudioDocumentSnapshot CreateDocumentSnapshot(
        EnvDTE.DTE dte,
        EnvDTE.Document document,
        bool isActive,
        bool includeSelection,
        ICollection<string> diagnostics)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var snapshot = new VisualStudioDocumentSnapshot
        {
            FilePath = document.FullName ?? string.Empty,
            Name = document.Name ?? string.Empty,
            ProjectName = TryGetDocumentProjectName(document),
            IsActive = isActive,
            IsDirty = !document.Saved,
        };

        if (!includeSelection)
        {
            return snapshot;
        }

        try
        {
            if ((isActive ? dte.ActiveDocument?.Selection : document.Selection) is EnvDTE.TextSelection selection)
            {
                snapshot.SelectionStartLine = selection.TopPoint.Line;
                snapshot.SelectionStartColumn = selection.TopPoint.LineCharOffset;
                snapshot.SelectionEndLine = selection.BottomPoint.Line;
                snapshot.SelectionEndColumn = selection.BottomPoint.LineCharOffset;
            }
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            diagnostics.Add($"DocumentSelectionReadFailed: failed to read selection for '{snapshot.Name}': {ex.Message}");
        }

        return snapshot;
    }

    private static string TryGetDocumentProjectName(EnvDTE.Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var projectItem = document.ProjectItem;
            return projectItem?.ContainingProject?.Name ?? string.Empty;
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return string.Empty;
        }
    }

    private static VisualStudioOutputWindowSnapshot? ReadOutputWindowSnapshot(
        EnvDTE.DTE dte,
        string requestedPaneName,
        int maxCharacters,
        ICollection<string> diagnostics)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (dte is not EnvDTE80.DTE2 dte2)
        {
            diagnostics.Add("OutputWindowUnavailable: EnvDTE80.DTE2 service is not available.");
            return null;
        }

        EnvDTE.OutputWindowPanes panes;
        try
        {
            panes = dte2.ToolWindows.OutputWindow.OutputWindowPanes;
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            diagnostics.Add("OutputWindowUnavailable: Visual Studio Output Window tool window is not available: " + ex.Message);
            return null;
        }

        var availablePaneNames = new List<string>();
        EnvDTE.OutputWindowPane? selectedPane = null;
        EnvDTE.OutputWindowPane? aliasMatchedPane = null;
        int count;
        try
        {
            count = panes.Count;
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            diagnostics.Add("OutputWindowReadFailed: could not read OutputWindowPanes.Count: " + ex.Message);
            return null;
        }

        for (var index = 1; index <= count; index++)
        {
            try
            {
                var pane = panes.Item(index);
                var paneName = pane.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(paneName))
                {
                    availablePaneNames.Add(paneName);
                }

                if (selectedPane is null
                    && string.Equals(paneName, requestedPaneName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedPane = pane;
                }

                if (aliasMatchedPane is null && IsOutputPaneAlias(requestedPaneName, paneName))
                {
                    aliasMatchedPane = pane;
                }
            }
            catch (Exception ex) when (IsRecoverableException(ex))
            {
                diagnostics.Add($"OutputWindowPaneReadFailed: failed to read OutputWindowPanes[{index}]: {ex.Message}");
            }
        }

        if (selectedPane is null && aliasMatchedPane is not null)
        {
            selectedPane = aliasMatchedPane;
            diagnostics.Add($"OutputPaneAliasMatched: requested pane '{requestedPaneName}', using pane '{selectedPane.Name}'.");
        }

        if (selectedPane is null)
        {
            var available = availablePaneNames.Count == 0
                ? "<none>"
                : string.Join(", ", availablePaneNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            diagnostics.Add($"OutputPaneNotFound: pane '{requestedPaneName}' was not found. Available panes: {available}.");
            return null;
        }

        string text;
        try
        {
            selectedPane.Activate();
            var document = selectedPane.TextDocument;
            var start = document.StartPoint.CreateEditPoint();
            text = start.GetText(document.EndPoint) ?? string.Empty;
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            diagnostics.Add($"OutputWindowTextReadFailed: failed to read pane '{selectedPane.Name}': {ex.Message}");
            return null;
        }

        var totalCharacters = text.Length;
        var isTruncated = totalCharacters > maxCharacters;
        var returnedText = isTruncated
            ? text.Substring(totalCharacters - maxCharacters, maxCharacters)
            : text;
        if (isTruncated)
        {
            diagnostics.Add($"OutputWindowTruncated: returned last {returnedText.Length} of {totalCharacters} character(s) from pane '{selectedPane.Name}'.");
        }

        return new VisualStudioOutputWindowSnapshot
        {
            PaneName = selectedPane.Name ?? requestedPaneName,
            Text = returnedText,
            TotalCharacters = totalCharacters,
            ReturnedCharacters = returnedText.Length,
            IsTruncated = isTruncated,
        };
    }

    private static bool IsOutputPaneAlias(string requestedPaneName, string candidatePaneName)
    {
        if (!string.Equals(requestedPaneName, "Build", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(candidatePaneName, "生成", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetOpenDocumentsAsync(
        VisualStudioDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 500)
        {
            return Failure<VisualStudioDocumentSnapshot>("MaxResults must be between 1 and 500.");
        }

        var diagnostics = new List<string>();
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dteObject = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dteObject is not EnvDTE.DTE dte)
            {
                return Failure<VisualStudioDocumentSnapshot>("OpenDocumentsUnavailable: EnvDTE service is not available.");
            }

            var items = ReadOpenDocuments(dte, request.MaxResults, request.IncludeSelection, diagnostics);
            return Success(items, diagnostics, diagnostics.Count > 0 || items.Length >= request.MaxResults);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return Failure<VisualStudioDocumentSnapshot>("OpenDocumentsReadFailed: " + ex.Message);
        }
    }

    public async Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetActiveDocumentContextAsync(
        VisualStudioDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dteObject = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dteObject is not EnvDTE.DTE dte)
            {
                return Failure<VisualStudioDocumentSnapshot>("ActiveDocumentUnavailable: EnvDTE service is not available.");
            }

            var activeDocument = dte.ActiveDocument;
            if (activeDocument is null)
            {
                return Failure<VisualStudioDocumentSnapshot>("ActiveDocumentUnavailable: no active Visual Studio document.");
            }

            return Success(new[] { CreateDocumentSnapshot(dte, activeDocument, isActive: true, request.IncludeSelection, diagnostics) }, diagnostics, diagnostics.Count > 0);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return Failure<VisualStudioDocumentSnapshot>("ActiveDocumentReadFailed: " + ex.Message);
        }
    }

    public async Task<WorkspaceQueryResult<SourceNavigationResult>> OpenSourceLocationAsync(
        SourceNavigationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return Failure<SourceNavigationResult>("FilePath is required.");
        }

        if (request.Line <= 0 || request.Column <= 0)
        {
            return Failure<SourceNavigationResult>("Line and column must be one-based positive integers.");
        }

        var diagnostics = new List<string>();
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dteObject = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dteObject is not EnvDTE.DTE dte)
            {
                return Failure<SourceNavigationResult>("SourceNavigationUnavailable: EnvDTE service is not available.");
            }

            var normalizedPath = NormalizePath(request.FilePath);
            if (!File.Exists(normalizedPath))
            {
                return Failure<SourceNavigationResult>("FileNotFound: source file does not exist.");
            }

            var window = dte.ItemOperations.OpenFile(normalizedPath);
            if (request.Activate)
            {
                window.Activate();
            }

            if (dte.ActiveDocument?.Selection is EnvDTE.TextSelection selection)
            {
                selection.MoveToLineAndOffset(request.Line, request.Column, Extend: false);
            }
            else
            {
                diagnostics.Add("SourceNavigationSelectionUnavailable: the active document has no text selection.");
            }

            return Success(
                new[]
                {
                    new SourceNavigationResult
                    {
                        FilePath = normalizedPath,
                        Line = request.Line,
                        Column = request.Column,
                        Opened = true,
                        Activated = request.Activate,
                        SuggestedNextSteps = new[]
                        {
                            "Use get_visual_studio_active_document_context to verify the active document and caret position.",
                        },
                    },
                },
                diagnostics,
                diagnostics.Count > 0);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return Failure<SourceNavigationResult>("SourceNavigationFailed: " + ex.Message);
        }
    }


    public async Task<WorkspaceQueryResult<VisualStudioErrorListItem>> GetErrorListAsync(
        ErrorListRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 500)
        {
            return Failure<VisualStudioErrorListItem>("MaxResults must be between 1 and 500.");
        }

        var diagnostics = new List<string>();
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dteObject = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dteObject is not EnvDTE.DTE dte)
            {
                return Failure<VisualStudioErrorListItem>("ErrorListUnavailable: EnvDTE service is not available.");
            }

            var items = ReadErrorListItems(dte, request.MaxResults, diagnostics);
            return Success(items, diagnostics, diagnostics.Count > 0);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return Failure<VisualStudioErrorListItem>("ErrorListReadFailed: " + ex.Message);
        }
    }

    public async Task<WorkspaceQueryResult<VisualStudioOutputWindowSnapshot>> GetOutputWindowAsync(
        OutputWindowRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PaneName))
        {
            return Failure<VisualStudioOutputWindowSnapshot>("PaneName is required.");
        }

        if (request.MaxCharacters is < 1 or > 200000)
        {
            return Failure<VisualStudioOutputWindowSnapshot>("MaxCharacters must be between 1 and 200000.");
        }

        var diagnostics = new List<string>();
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dteObject = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dteObject is not EnvDTE.DTE dte)
            {
                return Failure<VisualStudioOutputWindowSnapshot>("OutputWindowUnavailable: EnvDTE service is not available.");
            }

            var snapshot = ReadOutputWindowSnapshot(dte, request.PaneName, request.MaxCharacters, diagnostics);
            if (snapshot is null)
            {
                return new WorkspaceQueryResult<VisualStudioOutputWindowSnapshot>
                {
                    Items = Array.Empty<VisualStudioOutputWindowSnapshot>(),
                    Diagnostics = diagnostics,
                    IsPartial = true,
                };
            }

            return Success(new[] { snapshot }, diagnostics, diagnostics.Count > 0 || snapshot.IsTruncated);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return Failure<VisualStudioOutputWindowSnapshot>("OutputWindowReadFailed: " + ex.Message);
        }
    }

}
