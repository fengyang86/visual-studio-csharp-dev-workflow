using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Vsix.Bridge;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

// VS-side build orchestration and activity journal. Both run inside the VSIX
// process: builds go through EnvDTE SolutionBuild (fire-and-forget start, the
// poll reads build state), and the activity ring buffer subscribes to EnvDTE
// events at initialization so the AI learns about human-triggered builds,
// debug sessions, and solution switches even when it was not the one driving.
internal sealed class VisualStudioBuildActivityService
{
    private const int ActivityBufferCapacity = 200;

    private readonly AsyncPackage _package;
    private readonly object _activityGate = new();
    private readonly Queue<VisualStudioActivityEvent> _activityBuffer = new();
    private bool _eventsSubscribed;

    public VisualStudioBuildActivityService(AsyncPackage package)
    {
        _package = package;
    }

    public async Task SubscribeEventsAsync(CancellationToken cancellationToken)
    {
        if (_eventsSubscribed)
        {
            return;
        }

        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dteObject = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dteObject is not EnvDTE.DTE dte)
            {
                BridgeLog.Warning("VisualStudioBuildActivityService: EnvDTE service unavailable for event subscription.");
                return;
            }

            var events = dte.Events;
            events.BuildEvents.OnBuildBegin += (EnvDTE.vsBuildScope scope, EnvDTE.vsBuildAction action) =>
                RecordActivity("build", "BuildBegin", $"A build started (scope={scope}, action={action}).");
            events.BuildEvents.OnBuildDone += (EnvDTE.vsBuildScope scope, EnvDTE.vsBuildAction action) =>
                RecordActivity("build", "BuildDone", $"A build finished (scope={scope}, action={action}).");
            events.BuildEvents.OnBuildProjConfigDone += (string project, string config, string platform, string solutionConfig, bool success) =>
                RecordActivity("build", "ProjectConfigDone", $"Project '{project}' finished building ({config}|{platform}, success={success}).");
            events.SolutionEvents.Opened += () =>
                RecordActivity("solution", "Opened", "A solution was opened in Visual Studio.");
            events.SolutionEvents.AfterClosing += () =>
                RecordActivity("solution", "AfterClosing", "A solution was closed in Visual Studio.");
            events.SolutionEvents.BeforeClosing += () =>
                RecordActivity("solution", "BeforeClosing", "A solution is about to close in Visual Studio.");
            events.DebuggerEvents.OnEnterBreakMode += (EnvDTE.dbgEventReason reason, ref EnvDTE.dbgExecutionAction executionAction) =>
                RecordActivity("debug", "EnterBreakMode", $"Debugger entered break mode ({reason}).");
            events.DebuggerEvents.OnEnterDesignMode += (EnvDTE.dbgEventReason reason) =>
                RecordActivity("debug", "EnterDesignMode", $"Debugger entered design mode ({reason}).");
            events.DebuggerEvents.OnEnterRunMode += (EnvDTE.dbgEventReason reason) =>
                RecordActivity("debug", "EnterRunMode", $"Debugger entered run mode ({reason}).");
            events.DocumentEvents.DocumentOpened += (EnvDTE.Document document) =>
                RecordActivity("document", "DocumentOpened", $"Document '{document?.FullName}' was opened.");
            events.DocumentEvents.DocumentSaved += (EnvDTE.Document document) =>
                RecordActivity("document", "DocumentSaved", $"Document '{document?.FullName}' was saved.");

            _eventsSubscribed = true;
            RecordActivity("system", "EventsSubscribed", "Activity journal event subscriptions were registered.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Warning("VisualStudioBuildActivityService: event subscription failed: " + ex.Message);
        }
    }

    private void RecordActivity(string category, string kind, string summary)
    {
        lock (_activityGate)
        {
            _activityBuffer.Enqueue(new VisualStudioActivityEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Category = category,
                Kind = kind,
                Summary = summary,
            });
            while (_activityBuffer.Count > ActivityBufferCapacity)
            {
                _activityBuffer.Dequeue();
            }
        }
    }

    public async Task<WorkspaceQueryResult<VisualStudioActivityResult>> GetActivityAsync(
        VisualStudioActivityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxEvents is < 1 or > 200)
        {
            return new WorkspaceQueryResult<VisualStudioActivityResult>
            {
                Diagnostics = new[] { "MaxEvents must be between 1 and 200." },
                IsPartial = true,
                Succeeded = false,
            };
        }

        await SubscribeEventsAsync(cancellationToken).ConfigureAwait(true);

        lock (_activityGate)
        {
            var events = _activityBuffer
                .Reverse<VisualStudioActivityEvent>();
            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                events = events.Where(e => string.Equals(e.Category, request.Category, StringComparison.OrdinalIgnoreCase));
            }

            var items = events.Take(request.MaxEvents).ToList();
            return new WorkspaceQueryResult<VisualStudioActivityResult>
            {
                Items = new[] { new VisualStudioActivityResult { Events = items } },
                Diagnostics = items.Count == 0
                    ? new[] { $"ActivityBufferEmpty: no {request.Category ?? "recent"} events recorded yet; the journal starts when Visual Studio runs with this extension." }
                    : Array.Empty<string>(),
                IsPartial = items.Count == 0,
            };
        }
    }

    public async Task<WorkspaceQueryResult<VisualStudioBuildResult>> StartBuildAsync(
        VisualStudioBuildRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dteObject = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dteObject is not EnvDTE.DTE dte)
            {
                return new WorkspaceQueryResult<VisualStudioBuildResult>
                {
                    Diagnostics = new[] { "BuildUnavailable: EnvDTE service is not available." },
                    IsPartial = true,
                    Succeeded = false,
                };
            }

            var solutionBuild = dte.Solution?.SolutionBuild;
            if (solutionBuild is null)
            {
                return new WorkspaceQueryResult<VisualStudioBuildResult>
                {
                    Diagnostics = new[] { "BuildUnavailable: no solution is loaded in Visual Studio." },
                    IsPartial = true,
                    Succeeded = false,
                };
            }

            if (solutionBuild.BuildState == EnvDTE.vsBuildState.vsBuildStateInProgress)
            {
                return new WorkspaceQueryResult<VisualStudioBuildResult>
                {
                    Items = new[] { new VisualStudioBuildResult { BuildSubmitted = false } },
                    Diagnostics = new[] { "BuildInProgress: a build is already running; poll get_visual_studio_build_status for completion." },
                    IsPartial = true,
                };
            }

            // Fire-and-forget: WaitForBuildToFinish=false so the bridge returns
            // immediately; the 60s bridge timeout cannot accommodate full builds.
            if (!string.IsNullOrWhiteSpace(request.ProjectName))
            {
                var allProjects = EnumerateAllDteProjects(dte);
                var project = allProjects.FirstOrDefault(p => MatchesDteProjectName(p, request.ProjectName));
                if (project is null)
                {
                    var availableNames = string.Join(", ", allProjects.Select(p => GetDteProjectDisplayName(p)).Take(10));
                    return new WorkspaceQueryResult<VisualStudioBuildResult>
                    {
                        Diagnostics = new[]
                        {
                            $"ProjectNotFound: no project matching '{request.ProjectName}' in the loaded solution.",
                            $"Available projects (first 10): {availableNames}",
                        },
                        IsPartial = true,
                        Succeeded = false,
                    };
                }

                // BuildProject's first parameter is the SOLUTION CONFIGURATION
                // name (e.g. "Debug"), not the build action; the active
                // configuration is what the user would build with.
                var activeConfigName = solutionBuild.ActiveConfiguration?.Name ?? "Debug";
                if (request.Rebuild)
                {
                    solutionBuild.Clean(true);
                }

                var solutionBuild2 = (EnvDTE80.SolutionBuild2)solutionBuild;
                solutionBuild2.BuildProject(activeConfigName, project.FullName, false);
            }
            else
            {
                if (request.Rebuild)
                {
                    solutionBuild.Clean(true);
                }

                solutionBuild.Build(false);
            }

            RecordActivity("build", request.Rebuild ? "RebuildStarted" : "BuildStarted",
                string.IsNullOrWhiteSpace(request.ProjectName)
                    ? "Solution build was started (fire-and-forget)."
                    : $"Project '{request.ProjectName}' build was started (fire-and-forget).");

            return new WorkspaceQueryResult<VisualStudioBuildResult>
            {
                Items = new[] { new VisualStudioBuildResult { BuildSubmitted = true } },
                Diagnostics = new[] { "Build submitted; poll get_visual_studio_build_status for completion and error counts." },
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WorkspaceQueryResult<VisualStudioBuildResult>
            {
                Diagnostics = new[] { "BuildStartFailed: " + ex.Message },
                IsPartial = true,
                Succeeded = false,
            };
        }
    }

    public async Task<WorkspaceQueryResult<VisualStudioBuildStatus>> GetBuildStatusAsync(
        VisualStudioBuildStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dteObject = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dteObject is not EnvDTE.DTE dte)
            {
                return new WorkspaceQueryResult<VisualStudioBuildStatus>
                {
                    Diagnostics = new[] { "BuildUnavailable: EnvDTE service is not available." },
                    IsPartial = true,
                    Succeeded = false,
                };
            }

            var solutionBuild = dte.Solution?.SolutionBuild;
            if (solutionBuild is null)
            {
                return new WorkspaceQueryResult<VisualStudioBuildStatus>
                {
                    Diagnostics = new[] { "BuildUnavailable: no solution is loaded in Visual Studio." },
                    IsPartial = true,
                    Succeeded = false,
                };
            }

            var buildState = solutionBuild.BuildState switch
            {
                EnvDTE.vsBuildState.vsBuildStateInProgress => "InProgress",
                EnvDTE.vsBuildState.vsBuildStateDone => "Done",
                EnvDTE.vsBuildState.vsBuildStateNotStarted => "NotStarted",
                _ => "Unknown",
            };

            var startupProjectNames = new List<string>();
            try
            {
                if (solutionBuild.StartupProjects is object[] startupArray)
                {
                    foreach (var name in startupArray.OfType<string>())
                    {
                        startupProjectNames.Add(name);
                    }
                }
            }
            catch
            {
                // StartupProjects can throw when no startup project is set.
            }

            var activeConfiguration = solutionBuild.ActiveConfiguration?.Name ?? string.Empty;

            return new WorkspaceQueryResult<VisualStudioBuildStatus>
            {
                Items = new[]
                {
                    new VisualStudioBuildStatus
                    {
                        BuildState = buildState,
                        IsBuildInProgress = solutionBuild.BuildState == EnvDTE.vsBuildState.vsBuildStateInProgress,
                        LastBuildErrorCount = 0,
                        LastBuildWarningCount = 0,
                        StartupProjectName = string.Join("; ", startupProjectNames),
                        ActiveConfigurationName = activeConfiguration,
                    },
                },
                Diagnostics = new[] { "Use get_visual_studio_error_list for actual error and warning details after a build." },
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WorkspaceQueryResult<VisualStudioBuildStatus>
            {
                Diagnostics = new[] { "BuildStatusFailed: " + ex.Message },
                IsPartial = true,
                Succeeded = false,
            };
        }
    }

    // DTE nests projects inside solution-folder pseudo-projects; the top-level
    // Projects collection only returns the outermost items. Walk the hierarchy
    // to find real (non-folder) projects at every depth.
    private static List<EnvDTE.Project> EnumerateAllDteProjects(EnvDTE.DTE dte)
    {
        var results = new List<EnvDTE.Project>();
        try
        {
            foreach (var item in dte.Solution?.Projects?.Cast<EnvDTE.Project>() ?? Enumerable.Empty<EnvDTE.Project>())
            {
                CollectProjects(item, results);
            }
        }
        catch
        {
            // If enumeration fails (e.g., unloaded project throws), return what we have.
        }

        return results;
    }

    private static void CollectProjects(EnvDTE.Project project, List<EnvDTE.Project> results)
    {
        if (project is null)
        {
            return;
        }

        // Solution folders report Kind as the VS project-folder GUID and have no
        // FullName; real projects have a .csproj/.vbproj path.
        var isSolutionFolder = string.IsNullOrEmpty(project.FullName);
        if (!isSolutionFolder)
        {
            results.Add(project);
        }

        // Solution folders nest sub-projects as ProjectItems with SubProject.
        try
        {
            if (project.ProjectItems is not null)
            {
                foreach (EnvDTE.ProjectItem item in project.ProjectItems)
                {
                    if (item?.SubProject is not null)
                    {
                        CollectProjects(item.SubProject, results);
                    }
                }
            }
        }
        catch
        {
            // Some project types throw on ProjectItems; skip.
        }
    }

    // Match by the most user-friendly forms: the project file stem (QdElectricBase
    // from QdElectricBase.csproj), the DTE Name (which can be the full relative
    // path for nested projects), and the FullName.
    private static bool MatchesDteProjectName(EnvDTE.Project project, string requestedName)
    {
        if (string.IsNullOrWhiteSpace(project.FullName))
        {
            return false;
        }

        var fileStem = System.IO.Path.GetFileNameWithoutExtension(project.FullName);
        if (string.Equals(fileStem, requestedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(project.Name, requestedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // DTE Name for nested projects is "Folder\Project.csproj" - match the
        // trailing segment too so "QdElectricBase" finds "QdElectricBase\QdElectricBase.csproj".
        if (project.Name is not null
            && (project.Name.EndsWith("\\" + requestedName, StringComparison.OrdinalIgnoreCase)
                || project.Name.EndsWith("\\" + requestedName + ".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static string GetDteProjectDisplayName(EnvDTE.Project project)
    {
        return string.IsNullOrWhiteSpace(project.FullName)
            ? project.Name ?? "(unnamed)"
            : System.IO.Path.GetFileNameWithoutExtension(project.FullName);
    }
}
