using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class WorkflowCapabilityTools
{
    private const string OperationStatusToolName = "get_csharp_operation_status";
    private static readonly Lazy<RegisteredTool[]> RegisteredTools = new(DiscoverRegisteredTools);

    internal static int RegisteredToolCount => RegisteredTools.Value.Length;

    private static readonly TaskRoute[] Routes =
    {
        new("discovery", "Lightweight capability discovery.",
            new[] { "get_csharp_workflow_capabilities" }, new[] { typeof(WorkflowCapabilityTools) }),
        new("workspace", "Select the workspace, inspect instances and health; opening a solution is a separate state-changing action.",
            new[] { "prepare_csharp_workspace", "list_visual_studio_instances", "find_csharp_solutions" },
            new[] { typeof(WorkspacePreparationTools), typeof(WorkspaceHealthTools) }),
        new("edit", "Edit-task context and investigation entry points; none of them modify source code.",
            new[] { "prepare_csharp_edit_task", "get_csharp_task_context", "start_csharp_investigation" }),
        new("review", "Change review and area audits.",
            new[] { "prepare_csharp_change_review", "review_csharp_change", "audit_csharp_area" }),
        new("verification", "Produce verification plans; building and testing are executed only by execute_csharp_verification.",
            new[] { "prepare_csharp_verification_run", "plan_csharp_regression_scope", "plan_csharp_verification" },
            additionalTools: new[] { "find_csharp_related_tests", "execute_csharp_verification" }),
        new("build", "Build-failure investigation and existing-log triage; does not start builds itself.",
            new[] { "investigate_csharp_build_failure", "analyze_csharp_build_errors", "start_csharp_investigation" }),
        new("diagnostics", "Scoped diagnostics, live diagnostics from Visual Studio's background analysis, diagnostic baselines, and baseline diffs.",
            new[] { "get_csharp_diagnostics", "get_csharp_live_diagnostics" },
            additionalTools: new[]
            {
                "capture_csharp_diagnostic_baseline", "compare_csharp_diagnostics_to_baseline",
                "get_visual_studio_error_list", "get_visual_studio_output_window",
            }),
        new("navigation", "Symbols, source context, call graphs, and document context; open_csharp_source_location changes UI focus.",
            new[] { "search_csharp_symbols", "get_csharp_source_context", "get_csharp_symbol_source" },
            new[] { typeof(CodeIntelligenceTools), typeof(VisualStudioDocumentTools) }),
        new("debug", "Runtime investigation and debugger context; debug controls and expression evaluation require explicit targets and risk acknowledgment.",
            new[] { "investigate_csharp_runtime_exception", "prepare_debug_session", "get_debugger_status" },
            new[] { typeof(DebugContextTools), typeof(DebugControlTools) },
            new[] { "plan_csharp_debug_scenario" }),
        new("mutation", "Preview-first, explicitly-authorized rename, cleanup, code fixes, and plain text edits.",
            new[] { "preview_csharp_refactoring_plan", "preview_csharp_rename", "preview_csharp_cleanup", "preview_csharp_text_edits", "list_csharp_code_fixes" },
            new[] { typeof(RefactoringTools), typeof(CodeCleanupTools), typeof(CodeFixTools), typeof(TextEditTools) }),
        new("operation-status", "Reconcile outcomes for the explicit original target by receipt requestId; without a receipt, only list recent records to cross-check.",
            new[] { OperationStatusToolName }),
        new("artifacts", "Read or wait for local artifacts; never starts the application under test.",
            new[] { "collect_artifact_evidence", "wait_for_artifact_evidence" }),
        new("performance", "On-demand performance, telemetry, and budget inspection; not a prerequisite for lightweight discovery.",
            new[] { "get_csharp_workflow_performance_snapshot" }),
        new("repository", "Repository workflow analysis and agent instruction drafts.",
            new[] { "analyze_csharp_repo_workflow", "generate_csharp_agent_instructions" }),
        new("collaboration", "Produce scoped sub-agent packets and merge their findings.",
            new[] { "split_csharp_agent_work", "merge_csharp_agent_findings" }),
    };

    [McpServerTool(Name = "get_csharp_workflow_capabilities", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Discover the tools actually registered by this server, grouped by task category, with preferred entry points. Call this first when unsure which tool fits a task. Local assembly reflection only: it does not connect to the VSIX, run Roslyn or performance snapshots, start Visual Studio, or install anything, and it does not prove target bridge support or version safety.")]
    public WorkspaceQueryResult<CSharpWorkflowCapabilities> GetCSharpWorkflowCapabilities(
        [Description("Optional task category, case-insensitive; null or 'all' returns everything. Supported: discovery, workspace, edit, review, verification, build, diagnostics, navigation, debug, mutation, operation-status, artifacts, performance, repository, collaboration, other.")]
        string? taskCategory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var category = string.IsNullOrWhiteSpace(taskCategory) ? "all" : taskCategory.Trim().ToLowerInvariant();
        var supportedCategories = new[] { "all" }.Concat(Routes.Select(route => route.Category)).Append("other").ToArray();
        if (!supportedCategories.Contains(category, StringComparer.Ordinal))
        {
            return new WorkspaceQueryResult<CSharpWorkflowCapabilities>
            {
                IsPartial = true,
                Diagnostics = new[] { $"UnknownWorkflowTaskCategory: unsupported task category; supported values are {string.Join(", ", supportedCategories)}." },
            };
        }

        var registered = RegisteredTools.Value;
        var allCapabilities = Routes.Select(route => CreateCapability(route, registered))
            .Where(capability => capability.AvailableTools.Length > 0)
            .ToList();
        var classifiedNames = allCapabilities.SelectMany(capability => capability.AvailableTools)
            .ToHashSet(StringComparer.Ordinal);
        var unclassifiedNames = registered.Select(tool => tool.Name)
            .Where(name => !classifiedNames.Contains(name))
            .ToArray();
        if (unclassifiedNames.Length > 0)
        {
            allCapabilities.Add(new CSharpWorkflowTaskCapability
            {
                TaskCategory = "other",
                Description = "Registered tools without a configured task route yet; check their parameters and safety requirements before use.",
                AvailableTools = unclassifiedNames,
            });
        }

        var selected = allCapabilities.Where(capability => category == "all" || capability.TaskCategory == category).ToArray();
        var operationStatusAvailable = registered.Any(tool => tool.Name == OperationStatusToolName);
        cancellationToken.ThrowIfCancellationRequested();
        return new WorkspaceQueryResult<CSharpWorkflowCapabilities>
        {
            Items = new[]
            {
                new CSharpWorkflowCapabilities
                {
                    DiscoveryScope = "ServerAssemblyRegistration",
                    IsLocalOnly = true,
                    ServerAssemblyVersion = typeof(WorkflowCapabilityTools).Assembly.GetName().Version?.ToString() ?? string.Empty,
                    TaskCategory = category,
                    RegisteredToolCount = WorkflowCapabilityTools.RegisteredToolCount,
                    ReturnedToolCount = selected.SelectMany(capability => capability.AvailableTools).Distinct(StringComparer.Ordinal).Count(),
                    SupportedTaskCategories = supportedCategories,
                    Capabilities = selected,
                    Safety = CreateSafety(operationStatusAvailable),
                },
            },
            Diagnostics = selected.Length == 0
                ? new[] { "NoRegisteredToolsForTaskCategory: this server registers no tools for that category; do not infer target VSIX capabilities from it." }
                : Array.Empty<string>(),
        };
    }

    private static CSharpWorkflowTaskCapability CreateCapability(TaskRoute route, RegisteredTool[] registered)
    {
        var names = registered.Where(tool =>
                route.Groups.Contains(tool.Group)
                || route.EntryPoints.Contains(tool.Name, StringComparer.Ordinal)
                || route.AdditionalTools.Contains(tool.Name, StringComparer.Ordinal))
            .Select(tool => tool.Name)
            .ToArray();
        return new CSharpWorkflowTaskCapability
        {
            TaskCategory = route.Category,
            Description = route.Description,
            EntryPoints = route.EntryPoints.Where(name => names.Contains(name, StringComparer.Ordinal)).ToArray(),
            AvailableTools = names,
        };
    }

    private static CSharpWorkflowCapabilitySafety CreateSafety(bool operationStatusAvailable) => new()
    {
        VsixVerificationStatus = "NotChecked",
        VersionSafetyStatus = "NotVerified",
        BridgeCapabilitiesVerified = false,
        AutomaticMutationRetryAllowed = false,
        OperationStatusTool = operationStatusAvailable ? OperationStatusToolName : string.Empty,
        OperationStatusRequiresExplicitTarget = operationStatusAvailable,
        OperationStatusSupportsRecentListing = operationStatusAvailable,
        Notes = new[]
        {
            "The capability list only proves this MCP server registered the tools; it has not discovered or connected to any VSIX and has not verified protocol, extension versions, target workspace, or bridge method support.",
            "Before calling bridge tools, check health against an explicit target on demand. The server assembly version and the read-only/idempotent annotations do not prove version safety or new-capability support on the target VSIX.",
            "When an older server lacks capability discovery or the preferred entry points, use only the lower-level entry points that actually exist in the client tool list; do not substitute the performance snapshot for default lightweight discovery.",
            "A timed-out or cancelled mutation call must not be retried blindly: the operation may still have executed. Keep the explicit original target and any receipt, reconcile by the original requestId.",
            operationStatusAvailable
                ? "The operation-status entry is registered, but target VSIX support is still unverified. Without a receipt you may omit requestId and list recent records for the explicit original target (default 10, max 20) for cross-checking only - never auto-pick a record to replay. includeResponse is allowed only for an exact requestId query. Unknown, not-found, in-progress, failed, or unsupported outcomes must never trigger an automatic replay of a mutation."
                : "This server registers no operation-status entry. Stop automatic mutations, cross-check the original target state and existing evidence manually, do not guess the outcome, and do not auto-replay.",
        },
    };

    private static RegisteredTool[] DiscoverRegisteredTools()
    {
        // 与 Program.cs 的 WithToolsFromAssembly 保持同一程序集和特性筛选范围，不实例化工具或桥接。
        var tools = new List<RegisteredTool>();
        foreach (var type in typeof(WorkflowCapabilityTools).Assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not { } attribute)
                {
                    continue;
                }

                // 现有工具均显式命名；以后若省略名称，由 SDK 生成名称，避免自行模拟其命名规则。
                var name = attribute.Name ?? (method.IsStatic
                    ? McpServerTool.Create(method)
                    : McpServerTool.Create(method, _ =>
                        throw new InvalidOperationException("能力发现不得实例化或调用业务工具。"))).ProtocolTool.Name;
                tools.Add(new RegisteredTool(name, type));
            }
        }

        return tools.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
    }

    private sealed record RegisteredTool(string Name, Type Group);

    private sealed class TaskRoute
    {
        public TaskRoute(string category, string description, string[] entryPoints, Type[]? groups = null, string[]? additionalTools = null)
        {
            Category = category;
            Description = description;
            EntryPoints = entryPoints;
            Groups = groups ?? Array.Empty<Type>();
            AdditionalTools = additionalTools ?? Array.Empty<string>();
        }

        public string Category { get; }

        public string Description { get; }

        public string[] EntryPoints { get; }

        public Type[] Groups { get; }

        public string[] AdditionalTools { get; }
    }
}
