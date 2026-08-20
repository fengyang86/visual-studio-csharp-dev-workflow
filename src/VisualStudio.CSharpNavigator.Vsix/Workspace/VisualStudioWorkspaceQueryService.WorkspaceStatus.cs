using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Shell;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed partial class VisualStudioWorkspaceQueryService
{
    private async Task<WorkspaceStatus> GetWorkspaceStatusAsync(
        string instanceId,
        int processId,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var status = new WorkspaceStatus
        {
            InstanceId = instanceId,
            ProcessId = processId,
        };

        var workspaceResult = await TryGetWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        if (workspaceResult.Workspace is null)
        {
            diagnostics.Add(workspaceResult.Diagnostic ?? "WorkspaceUnavailable: VisualStudioWorkspace is not available.");
            return status;
        }

        var solution = workspaceResult.Workspace.CurrentSolution;
        status.SolutionPath = solution.FilePath ?? string.Empty;
        status.SolutionName = string.IsNullOrWhiteSpace(solution.FilePath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(solution.FilePath);
        status.ProjectCount = solution.Projects.Count();
        status.DocumentCount = solution.Projects.Sum(project => project.DocumentIds.Count);
        status.IsSolutionLoaded = !string.IsNullOrWhiteSpace(solution.FilePath) || status.ProjectCount > 0;
        await PopulateVisualStudioStatusAsync(status, diagnostics, cancellationToken).ConfigureAwait(false);
        PopulateProjectStatus(status, solution, diagnostics);

        if (!status.IsSolutionLoaded)
        {
            diagnostics.Add("NoSolutionLoaded: no solution is loaded in this Visual Studio instance.");
        }

        return status;
    }

    private async Task PopulateVisualStudioStatusAsync(
        WorkspaceStatus status,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dteObject = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dteObject is not EnvDTE.DTE dte)
            {
                diagnostics.Add("WorkspaceStatusDteUnavailable: EnvDTE service is not available; active configuration and startup projects were not populated.");
                return;
            }

            var activeConfiguration = dte.Solution?.SolutionBuild?.ActiveConfiguration;
            status.ActiveConfigurationName = ReadDynamicString(() => activeConfiguration?.Name);
            status.ActivePlatformName = ReadSolutionPlatformName(activeConfiguration);
            status.StartupProjects = ReadStartupProjects(dte.Solution?.SolutionBuild?.StartupProjects);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            diagnostics.Add($"WorkspaceStatusDteReadFailed: {ex.Message}");
        }
    }

    private static void PopulateProjectStatus(
        WorkspaceStatus status,
        Solution solution,
        ICollection<string> diagnostics)
    {
        var projects = solution.Projects
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(MaxWorkspaceStatusProjects)
            .Select(project => new WorkspaceProjectStatus
            {
                ProjectName = project.Name,
                FilePath = project.FilePath ?? string.Empty,
                Language = project.Language,
                TargetFrameworks = ReadTargetFrameworks(project.FilePath, diagnostics),
            })
            .ToArray();

        status.Projects = projects;
        status.IsProjectListPartial = status.ProjectCount > projects.Length;
        if (status.IsProjectListPartial)
        {
            diagnostics.Add($"WorkspaceStatusProjectsTruncated: returned {projects.Length} of {status.ProjectCount} projects.");
        }
    }

    private static string[] ReadStartupProjects(object? startupProjects)
    {
        if (startupProjects is null)
        {
            return Array.Empty<string>();
        }

        if (startupProjects is string singleProject)
        {
            return string.IsNullOrWhiteSpace(singleProject)
                ? Array.Empty<string>()
                : new[] { singleProject };
        }

        if (startupProjects is Array array)
        {
            return array
                .Cast<object?>()
                .Select(item => item?.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var value = startupProjects.ToString();
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : new[] { value };
    }

}