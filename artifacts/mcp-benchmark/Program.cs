using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var iterations = ReadIntArg(args, "--iterations", 3);
var showServerLog = args.Any(arg => string.Equals(arg, "--server-log", StringComparison.OrdinalIgnoreCase));
if (iterations <= 0)
{
    throw new ArgumentOutOfRangeException(nameof(iterations), "iterations must be greater than zero.");
}

var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var configuredServerExe = Environment.GetEnvironmentVariable("CODE_NAVIGATOR_SERVER_EXE");
#if DEBUG
const string defaultServerConfiguration = "Debug";
#else
const string defaultServerConfiguration = "Release";
#endif
var serverExe = string.IsNullOrWhiteSpace(configuredServerExe)
    ? Path.Combine(
        workspaceRoot,
        "src",
        "VisualStudio.CSharpNavigator.Server",
        "bin",
        defaultServerConfiguration,
        "net8.0",
        "VisualStudio.CSharpNavigator.Server.exe")
    : Path.GetFullPath(configuredServerExe);

if (!File.Exists(serverExe))
{
    throw new FileNotFoundException("Build or publish the MCP server before running the benchmark.", serverExe);
}

var profile = BenchmarkProfile.Load(workspaceRoot);
var solutionPath = profile.SolutionPath;

var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "VisualStudio.CSharpNavigator MCP Benchmark",
    Command = serverExe,
    WorkingDirectory = workspaceRoot,
    EnvironmentVariables = new Dictionary<string, string?>
    {
        ["VisualStudioBridge__SolutionPath"] = solutionPath,
        ["VisualStudioBridge__ConnectTimeoutMilliseconds"] =
            Environment.GetEnvironmentVariable("VisualStudioBridge__ConnectTimeoutMilliseconds") ?? "5000",
        ["VisualStudioBridge__DiscoveryStaleAfterSeconds"] =
            Environment.GetEnvironmentVariable("VisualStudioBridge__DiscoveryStaleAfterSeconds") ?? "120",
    },
    StandardErrorLines = line =>
    {
        if (showServerLog)
        {
            Console.Error.WriteLine("[server] " + line);
        }
    },
});

await using var client = await McpClient.CreateAsync(transport);

var tools = await client.ListToolsAsync();
var toolNames = tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
RequireTools(toolNames);

Console.WriteLine("BENCHMARK profile={0} solutionPath={1} iterations={2}", profile.Name, solutionPath, iterations);
Console.WriteLine("PROFILE name={0} symbol={1}", profile.Name, profile.SymbolQuery);
Console.WriteLine("TOOLS " + string.Join(",", toolNames));

if (profile.IsLargeSolutionProfile)
{
    await RunLargeSolutionBenchmarkAsync(client, profile, iterations);
    return;
}

if (profile.IsWorkflowProfile)
{
    await RunWorkflowBenchmarkAsync(client, profile, iterations);
    return;
}

if (!profile.UsesRichCSharpNavigatorCases)
{
    await RunGenericBenchmarkAsync(client, profile, iterations);
    return;
}

var setPropsKey = await FindSymbolKeyAsync(client, profile, "SetProps", "SetProps", "SampleWorkspace.Core.LcObject");
var lcLineStartKey = await FindSymbolKeyAsync(client, profile, "LcLine.Start", "Start", "SampleWorkspace.Core.Elements.LcLine");
var lcElementKey = await FindSymbolKeyAsync(client, profile, "LcElement", "LcElement", string.Empty);
var lcLineKey = await FindSymbolKeyAsync(client, profile, "LcLine", "LcLine", string.Empty);
var iElement3dKey = await FindSymbolKeyAsync(client, profile, "IElement3d", "IElement3d", string.Empty);
var cloneKey = await FindSymbolKeyAsync(client, profile, "LcElement.Clone", "Clone", "SampleWorkspace.Core.LcElement");
var lcLinePath = profile.DocumentPath;

var cases = new List<BenchmarkCase>
{
    new(
        "RenamePreview_LcLine_Start",
        () => CallAsync(client, "preview_csharp_rename", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = lcLineStartKey,
            ["newName"] = "StartPreview",
            ["renameOverloads"] = false,
            ["renameInStrings"] = false,
            ["renameInComments"] = false,
            ["renameFile"] = false,
            ["maxTextChanges"] = 20,
            ["maxSnippetLength"] = 80,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var item = payload.RootElement.GetProperty("items")[0];
            Require(item.GetProperty("totalTextChangeCount").GetInt32() >= 100, "LcLine.Start rename preview should return Roslyn text changes.");
            Require(item.GetProperty("returnedTextChangeCount").GetInt32() <= 20, "preview_csharp_rename must respect MaxTextChanges.");
            Require(item.GetProperty("documents").GetArrayLength() >= 1, "preview_csharp_rename should return affected documents.");
            Require(DiagnosticsContain(payload, "Renamer.RenameSymbolAsync"), "rename preview must disclose that Roslyn rename API was used without applying edits.");
        }),
    new(
        "工作区状态",
        () => CallAsync(client, "get_csharp_workspace_status", profile.WithTarget(new Dictionary<string, object?>())),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() == 1, "工作区状态应当只匹配一个 VS 实例。");
            var item = items[0];
            Require(item.GetProperty("isSolutionLoaded").GetBoolean(), "目标 VS 实例必须已加载解决方案。");
            Require(item.GetProperty("projectCount").GetInt32() >= 30, "项目数量低于预期。");
            Require(item.GetProperty("documentCount").GetInt32() >= 3000, "文档数量低于预期。");
            RequireNotPartial(payload);
        }),
    new(
        "常用符号搜索_SetProps",
        () => CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
        {
            ["queryText"] = "SetProps",
            ["maxResults"] = 10,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var item = FindSymbol(payload.RootElement.GetProperty("items"), "SetProps", "SampleWorkspace.Core.LcObject");
            Require(item.GetProperty("span").GetProperty("startLine").GetInt32() == 199, "SetProps 定义行不符合当前样本预期。");
            RequireNotPartial(payload);
        }),
    new(
        "定义跳转_SetProps",
        () => CallAsync(client, "find_csharp_definitions", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = setPropsKey,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            Require(payload.RootElement.GetProperty("items").GetArrayLength() == 1, "SetProps 定义数量应为 1。");
            RequireNotPartial(payload);
        }),
    new(
        "高频引用_SetProps",
        () => CallAsync(client, "find_csharp_references", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = setPropsKey,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() >= 600, "SetProps 引用数量低于预期。");
            Require(AllRolesAre(items, "Invocation"), "SetProps 引用角色应全部为 Invocation。");
            RequireNotPartial(payload);
        }),
    new(
        "读写角色_LcLine.Start",
        () => CallAsync(client, "find_csharp_references", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = lcLineStartKey,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var roleCounts = CountRoles(payload.RootElement.GetProperty("items"));
            Require(roleCounts.GetValueOrDefault("Read") >= 150, "LcLine.Start Read 数量低于预期。");
            Require(roleCounts.GetValueOrDefault("Write") >= 10, "LcLine.Start Write 数量低于预期。");
            Require(roleCounts.GetValueOrDefault("Unknown") == 0, "LcLine.Start 不应出现 Unknown 角色。");
            RequireNotPartial(payload);
        }),
    new(
        "实现查找_IElement3d",
        () => CallAsync(client, "find_csharp_implementations", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = iElement3dKey,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() >= 50, "IElement3d 实现数量低于预期。");
            Require(AllRolesAre(items, "Implementation"), "IElement3d 结果角色应全部为 Implementation。");
            RequireNotPartial(payload);
        }),
    new(
        "重写查找_LcElement.Clone",
        () => CallAsync(client, "find_csharp_overrides", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = cloneKey,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() >= 120, "LcElement.Clone override 数量低于预期。");
            Require(AllRolesAre(items, "Override"), "LcElement.Clone 结果角色应全部为 Override。");
            RequireNotPartial(payload);
        }),
    new(
        "符号描述_SetProps",
        () => CallAsync(client, "describe_csharp_symbol", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = setPropsKey,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var item = payload.RootElement.GetProperty("items")[0];
            Require(item.GetProperty("symbol").GetProperty("name").GetString() == "SetProps", "DescribeSymbol 应返回 SetProps。");
            Require(item.GetProperty("displayString").GetString()?.Contains("SetProps", StringComparison.Ordinal) == true, "DescribeSymbol 应包含 C# display string。");
            RequireNotPartial(payload);
        }),
    new(
        "文档符号_LcLine",
        () => CallAsync(client, "list_csharp_document_symbols", profile.WithTarget(new Dictionary<string, object?>
        {
            ["filePath"] = lcLinePath,
            ["maxResults"] = 200,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() >= 20, "LcLine 文档符号数量低于预期。");
            Require(ContainsDocumentSymbol(items, "LcLine"), "LcLine 文档符号应包含 LcLine 类型。");
            RequireNotPartial(payload);
        }),
    new(
        "诊断_LcLine",
        () => CallAsync(client, "get_csharp_diagnostics", profile.WithTarget(new Dictionary<string, object?>
        {
            ["filePath"] = lcLinePath,
            ["minimumSeverity"] = "Warning",
            ["maxResults"] = 20,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            Require(payload.RootElement.GetProperty("items").GetArrayLength() <= 20, "Diagnostics 应遵守 MaxResults。");
        }),
    new(
        "调用方_SetProps",
        () => CallAsync(client, "find_csharp_callers", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = setPropsKey,
            ["maxDepth"] = 1,
            ["maxResults"] = 25,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() >= 1, "SetProps 应有调用方。");
            Require(AllCallGraphEdgesHaveSourceTargetAndSpan(items), "调用方边必须包含 source、target 和 span。");
        }),
    new(
        "被调用方_SetProps",
        () => CallAsync(client, "find_csharp_callees", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = setPropsKey,
            ["maxDepth"] = 1,
            ["maxResults"] = 25,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() >= 1, "SetProps 应有被调用方。");
            Require(AllCallGraphEdgesHaveSourceTargetAndSpan(items), "被调用方边必须包含 source、target 和 span。");
            RequireNotPartial(payload);
        }),
    new(
        "影响面聚合_SetProps",
        () => CallAsync(client, "analyze_csharp_symbol_impact", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = setPropsKey,
            ["maxResults"] = 2000,
            ["maxProjects"] = 10,
            ["maxFiles"] = 10,
            ["maxContainingTypes"] = 10,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() == 1, "Impact analysis did not return exactly one summary item: " + payload.RootElement.ToString());
            var item = items[0];
            Require(item.GetProperty("totalReferences").GetInt32() >= 600, "SetProps 影响面引用总数低于预期。");
            Require(item.GetProperty("distinctProjectCount").GetInt32() >= 1, "SetProps 影响面项目数低于预期。");
            Require(item.GetProperty("files").GetArrayLength() >= 1, "SetProps 影响面应返回至少一个文件分组。");
        }),
    new(
        "派生类型_LcElement",
        () => CallAsync(client, "find_csharp_derived_types", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = lcElementKey,
            ["transitive"] = true,
            ["maxResults"] = 50,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() >= 10, "LcElement 派生类型数量低于预期。");
            Require(AllDerivedTypesHaveSource(items), "派生类型必须包含 source span。");
        }),
    new(
        "继承链_LcLine",
        () => CallAsync(client, "get_csharp_inheritance_chain", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = lcLineKey,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var item = payload.RootElement.GetProperty("items")[0];
            Require(item.GetProperty("baseTypes").GetArrayLength() >= 1, "LcLine 应至少有一个 base type。");
            Require(item.GetProperty("symbol").GetProperty("name").GetString() == "LcLine", "继承链目标应是 LcLine。");
            RequireNotPartial(payload);
        }),
    new(
        "相关测试_LcLine",
        () => CallAsync(client, "find_csharp_related_tests", profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = lcLineKey,
            ["maxResults"] = 20,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var items = payload.RootElement.GetProperty("items");
            Require(items.GetArrayLength() <= 20, "find_csharp_related_tests 必须遵守 MaxResults。");
            Require(payload.RootElement.GetProperty("isPartial").GetBoolean() == false, "相关测试不应在未截断时返回 partial。");
            Require(DiagnosticsContain(payload, "Related test discovery"), "相关测试必须说明引用证据和启发式证据的边界。");
            Require(AllRelatedTestsHaveReasonAndSpan(items), "相关测试结果必须包含匹配依据和源码位置。");
        }),
    new(
        "项目图_SampleWorkspace.Core",
        () => CallAsync(client, "get_csharp_project_graph", profile.WithTarget(new Dictionary<string, object?>
        {
            ["projectName"] = "SampleWorkspace.Core",
            ["maxProjects"] = 50,
            ["maxMetadataReferencesPerProject"] = 500,
            ["includeMetadataReferences"] = true,
        })),
        payload =>
        {
            var graph = payload.RootElement.GetProperty("items")[0];
            Require(graph.GetProperty("nodes").GetArrayLength() == 1, "按项目名查询项目图应返回一个节点。");
            Require(graph.GetProperty("nodes")[0].GetProperty("projectName").GetString() == "SampleWorkspace.Core", "项目图节点应为 SampleWorkspace.Core。");
            if (payload.RootElement.GetProperty("isPartial").GetBoolean())
            {
                Require(
                    DiagnosticsContain(payload, "TargetProjectIncluded=false")
                    || DiagnosticsContain(payload, "metadata references"),
                    "项目图 partial 必须说明是外部项目边或 metadata references 截断。");
            }
        }),
    new(
        "调试状态",
        () => CallAsync(client, "get_debugger_status", profile.WithTarget(new Dictionary<string, object?>())),
        payload =>
        {
            var item = payload.RootElement.GetProperty("items")[0];
            Require(!string.IsNullOrWhiteSpace(item.GetProperty("state").GetString()), "调试状态必须包含 state。");
        }),
    new(
        "调试调用栈_诊断模型",
        () => CallAsync(client, "get_debug_call_stack", profile.WithTarget(new Dictionary<string, object?>
        {
            ["maxFrames"] = 5,
        })),
        payload =>
        {
            var isPartial = payload.RootElement.GetProperty("isPartial").GetBoolean();
            var count = payload.RootElement.GetProperty("items").GetArrayLength();
            Require(count > 0 || isPartial, "未暂停时应返回明确诊断；暂停时应返回真实调用栈。");
        }),
    new(
        "调试线程_诊断模型",
        () => CallAsync(client, "list_debug_threads", profile.WithTarget(new Dictionary<string, object?>())),
        payload =>
        {
            var isPartial = payload.RootElement.GetProperty("isPartial").GetBoolean();
            var count = payload.RootElement.GetProperty("items").GetArrayLength();
            Require(count > 0 || isPartial, "未调试时应返回明确诊断；调试时应返回真实线程。");
        }),
    new(
        "调试变量_诊断模型",
        () => CallAsync(client, "get_debug_stack_frame_variables", profile.WithTarget(new Dictionary<string, object?>
        {
            ["maxChildren"] = 5,
            ["maxStringLength"] = 200,
        })),
        payload =>
        {
            var isPartial = payload.RootElement.GetProperty("isPartial").GetBoolean();
            var count = payload.RootElement.GetProperty("items").GetArrayLength();
            Require(count > 0 || isPartial, "未暂停时应返回明确诊断；暂停时应返回真实变量。");
        }),
    new(
        "调试表达式_默认副作用保护",
        () => CallAsync(client, "evaluate_debug_expression", profile.WithTarget(new Dictionary<string, object?>
        {
            ["expression"] = "this",
            ["maxChildren"] = 5,
            ["maxStringLength"] = 200,
            ["allowSideEffects"] = false,
        })),
        payload =>
        {
            Require(payload.RootElement.GetProperty("isPartial").GetBoolean(), "默认不允许副作用时应拒绝表达式求值。");
            Require(DiagnosticsContain(payload, "SideEffectsNotAllowed"), "表达式求值必须说明副作用保护原因。");
        }),
    new(
        "断点只读列表",
        () => CallAsync(client, "list_debug_breakpoints", profile.WithTarget(new Dictionary<string, object?>
        {
            ["maxResults"] = 100,
        })),
        payload =>
        {
            Require(payload.RootElement.GetProperty("items").GetArrayLength() <= 100, "断点列表应遵守 MaxResults。");
        }),
    new(
        "生成文档列表",
        () => CallAsync(client, "list_csharp_generated_documents", profile.WithTarget(new Dictionary<string, object?>
        {
            ["projectName"] = "SampleWorkspace.LocalSolution",
            ["maxResults"] = 50,
            ["includeSourceGeneratedDocuments"] = true,
        })),
        payload =>
        {
            Require(payload.RootElement.GetProperty("items").GetArrayLength() >= 1, "SampleWorkspace.LocalSolution 应有 Designer/generated 文档样本。");
        }),
    new(
        "临时标记扫描",
        () => CallAsync(client, "find_csharp_temporary_markers", profile.WithTarget(new Dictionary<string, object?>
        {
            ["projectName"] = "SampleWorkspace.Core",
            ["maxResults"] = 20,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            Require(payload.RootElement.GetProperty("items").GetArrayLength() <= 20, "临时标记扫描应遵守 MaxResults。");
            Require(DiagnosticsContain(payload, "audit aid"), "临时标记扫描必须声明它是审查辅助。");
        }),
    new(
        "位置上下文_LcLine",
        () => CallAsync(client, "get_csharp_enclosing_context", profile.WithTarget(new Dictionary<string, object?>
        {
            ["filePath"] = lcLinePath,
            ["line"] = 15,
            ["column"] = 20,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            var item = payload.RootElement.GetProperty("items")[0];
            Require(item.GetProperty("type").GetString()?.Contains("LcLine", StringComparison.Ordinal) == true, "位置上下文应识别 LcLine 类型。");
            RequireNotPartial(payload);
        }),
    new(
        "生成代码过滤_默认排除",
        () => CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
        {
            ["queryText"] = "toolStripSeparator2",
            ["maxResults"] = 10,
            ["includeGeneratedCode"] = false,
        })),
        payload =>
        {
            Require(payload.RootElement.GetProperty("items").GetArrayLength() == 0, "默认不应返回 Designer 生成代码符号。");
            RequireNotPartial(payload);
        }),
    new(
        "生成代码过滤_显式包含",
        () => CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
        {
            ["queryText"] = "toolStripSeparator2",
            ["maxResults"] = 10,
            ["includeGeneratedCode"] = true,
        })),
        payload =>
        {
            Require(payload.RootElement.GetProperty("items").GetArrayLength() >= 1, "显式包含时应返回 Designer 生成代码符号。");
            Require(payload.RootElement.GetProperty("isPartial").GetBoolean() == false, "工具结果不应为 partial。");
            Require(
                DiagnosticsContain(payload, "IncludeGeneratedCode=true"),
                "显式包含生成代码时应返回对应 diagnostics 提示。");
        }),
};

foreach (var testCase in cases)
{
    var result = await RunBenchmarkCaseAsync(testCase, iterations);
    Console.WriteLine(
        "CASE profile={0} name={1} ok={2}/{3} avgMs={4:F1} p95Ms={5:F1} maxMs={6:F1}",
        profile.Name,
        result.Name,
        result.SuccessCount,
        iterations,
        result.Average.TotalMilliseconds,
        result.P95.TotalMilliseconds,
        result.Max.TotalMilliseconds);
}

Console.WriteLine("RESULT profile={0} passed cases={1} iterations={2}", profile.Name, cases.Count, iterations);

static async Task RunGenericBenchmarkAsync(McpClient client, BenchmarkProfile profile, int iterations)
{
    var symbolKey = await FindProfileSymbolKeyAsync(client, profile);
    var cases = new List<BenchmarkCase>
    {
        new(
            "Generic_WorkspaceStatus",
            () => CallAsync(client, "get_csharp_workspace_status", profile.WithTarget(new Dictionary<string, object?>())),
            payload =>
            {
                var items = payload.RootElement.GetProperty("items");
                Require(items.GetArrayLength() == 1, "Generic workspace status should return exactly one selected Visual Studio instance.");
                Require(items[0].GetProperty("isSolutionLoaded").GetBoolean(), "Generic target solution must be loaded in Visual Studio.");
            }),
        new(
            "Generic_SearchSymbol",
            () => CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
            {
                ["queryText"] = profile.SymbolQuery,
                ["maxResults"] = 20,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                _ = FindSymbol(
                    payload.RootElement.GetProperty("items"),
                    profile.ExpectedSymbolName,
                    profile.ExpectedContainingType);
            }),
        new(
            "Generic_FindDefinitions",
            () => CallAsync(client, "find_csharp_definitions", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolKey"] = symbolKey,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                Require(payload.RootElement.GetProperty("items").GetArrayLength() >= 1, "Generic symbol should have at least one source definition.");
            }),
        new(
            "Generic_FindReferences",
            () => CallAsync(client, "find_csharp_references", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolKey"] = symbolKey,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                Require(payload.RootElement.GetProperty("items").GetArrayLength() >= 1, "Generic symbol should have at least one reference.");
            }),
        new(
            "Generic_RecursiveCallers",
            () => CallAsync(client, "find_csharp_callers", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolKey"] = symbolKey,
                ["maxDepth"] = 2,
                ["maxResults"] = 20,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                var items = payload.RootElement.GetProperty("items");
                Require(items.GetArrayLength() >= 1, "Generic recursive callers should return at least one edge.");
                Require(MaxCallGraphDepth(items) >= 2, "Generic recursive callers should include a depth=2 edge.");
            }),
        new(
            "Generic_RecursiveImpact",
            () => CallAsync(client, "analyze_csharp_symbol_impact", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolKey"] = symbolKey,
                ["maxDepth"] = 2,
                ["maxResults"] = 20,
                ["maxProjects"] = 10,
                ["maxFiles"] = 10,
                ["maxContainingTypes"] = 10,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                var item = payload.RootElement.GetProperty("items")[0];
                Require(item.GetProperty("maxDepth").GetInt32() == 2, "Generic recursive impact should report MaxDepth=2.");
                Require(item.GetProperty("transitiveReferenceCount").GetInt32() >= 1, "Generic recursive impact should include transitive references.");
            }),
        new(
            "Generic_ProjectGraph",
            () => CallAsync(client, "get_csharp_project_graph", profile.WithTarget(new Dictionary<string, object?>
            {
                ["projectName"] = profile.ProjectName,
                ["maxProjects"] = 20,
                ["maxMetadataReferencesPerProject"] = 10,
                ["includeMetadataReferences"] = true,
            })),
            payload =>
            {
                var graph = payload.RootElement.GetProperty("items")[0];
                Require(graph.GetProperty("nodes").GetArrayLength() >= 1, "Generic project graph should include the configured project.");
            }),
        new(
            "Generic_DebuggerStatus",
            () => CallAsync(client, "get_debugger_status", profile.WithTarget(new Dictionary<string, object?>())),
            payload =>
            {
                var item = payload.RootElement.GetProperty("items")[0];
                Require(!string.IsNullOrWhiteSpace(item.GetProperty("state").GetString()), "Debugger status must include state.");
            }),
    };

    foreach (var testCase in cases)
    {
        var result = await RunBenchmarkCaseAsync(testCase, iterations);
        Console.WriteLine(
            "CASE profile={0} name={1} ok={2}/{3} avgMs={4:F1} p95Ms={5:F1} maxMs={6:F1}",
            profile.Name,
            result.Name,
            result.SuccessCount,
            iterations,
            result.Average.TotalMilliseconds,
            result.P95.TotalMilliseconds,
            result.Max.TotalMilliseconds);
    }

    Console.WriteLine("RESULT profile={0} passed cases={1} iterations={2}", profile.Name, cases.Count, iterations);
}

static async Task RunLargeSolutionBenchmarkAsync(McpClient client, BenchmarkProfile profile, int iterations)
{
    var symbolKey = await FindProfileSymbolKeyAsync(client, profile);
    var changedFiles = CreateWorkflowChangedFiles(profile);
    var buildOutput = CreateWorkflowBuildOutput(profile);
    var cases = new List<BenchmarkCase>
    {
        new(
            "Large_WorkspaceStatus",
            () => CallAsync(client, "get_csharp_workspace_status", profile.WithTarget(new Dictionary<string, object?>())),
            payload =>
            {
                var items = payload.RootElement.GetProperty("items");
                Require(items.GetArrayLength() == 1, "Large profile workspace status should select exactly one Visual Studio instance.");
                Require(items[0].GetProperty("isSolutionLoaded").GetBoolean(), "Large profile target solution must be loaded in Visual Studio.");
            }),
        new(
            "Large_SearchSymbol",
            () => CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
            {
                ["queryText"] = profile.SymbolQuery,
                ["maxResults"] = 50,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                _ = FindSymbol(
                    payload.RootElement.GetProperty("items"),
                    profile.ExpectedSymbolName,
                    profile.ExpectedContainingType);
            }),
        new(
            "Large_FindDefinitions",
            () => CallAsync(client, "find_csharp_definitions", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolKey"] = symbolKey,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                Require(payload.RootElement.GetProperty("items").GetArrayLength() >= 1, "Large profile symbol should have at least one source definition.");
            }),
        new(
            "Large_FindReferences_Capped",
            () => CallAsync(client, "find_csharp_references", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolKey"] = symbolKey,
                ["maxResults"] = 100,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                var count = payload.RootElement.GetProperty("items").GetArrayLength();
                Require(count >= 1, "Large profile symbol should have at least one reference.");
                Require(count <= 100, "Large profile references must respect MaxResults.");
            }),
        new(
            "Large_Callers_Capped",
            () => CallAsync(client, "find_csharp_callers", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolKey"] = symbolKey,
                ["maxDepth"] = 2,
                ["maxResults"] = 100,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                Require(payload.RootElement.GetProperty("items").GetArrayLength() <= 100, "Large profile callers must respect MaxResults.");
            }),
        new(
            "Large_Callees_Capped",
            () => CallAsync(client, "find_csharp_callees", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolKey"] = symbolKey,
                ["maxDepth"] = 2,
                ["maxResults"] = 100,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                Require(payload.RootElement.GetProperty("items").GetArrayLength() <= 100, "Large profile callees must respect MaxResults.");
            }),
        new(
            "Large_Impact_Capped",
            () => CallAsync(client, "analyze_csharp_symbol_impact", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolKey"] = symbolKey,
                ["maxDepth"] = 2,
                ["maxResults"] = 100,
                ["maxProjects"] = 10,
                ["maxFiles"] = 10,
                ["maxContainingTypes"] = 10,
                ["includeGeneratedCode"] = false,
            })),
            payload =>
            {
                var item = payload.RootElement.GetProperty("items")[0];
                Require(item.GetProperty("maxDepth").GetInt32() == 2, "Large profile impact should report MaxDepth=2.");
            }),
        new(
            "Large_Diagnostics_FilterNoise",
            () => CallAsync(client, "get_csharp_diagnostics", profile.WithTarget(new Dictionary<string, object?>
            {
                ["filePath"] = string.IsNullOrWhiteSpace(profile.DocumentPath) ? null : profile.DocumentPath,
                ["minimumSeverity"] = "Warning",
                ["maxResults"] = 20,
                ["includeGeneratedCode"] = false,
                ["noiseProfile"] = "Filter",
            })),
            payload =>
            {
                Require(payload.RootElement.GetProperty("items").GetArrayLength() <= 20, "Large profile diagnostics must respect MaxResults.");
            }),
        new(
            "Large_ProjectGraph_Capped",
            () => CallAsync(client, "get_csharp_project_graph", profile.WithTarget(new Dictionary<string, object?>
            {
                ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
                ["maxProjects"] = 50,
                ["maxMetadataReferencesPerProject"] = 20,
                ["includeMetadataReferences"] = true,
            })),
            payload =>
            {
                var graph = payload.RootElement.GetProperty("items")[0];
                Require(graph.GetProperty("nodes").GetArrayLength() >= 1, "Large profile project graph should include at least one project.");
            }),
        new(
            "Large_WorkflowInvestigation_Capped",
            () => CallAsync(client, "start_csharp_investigation", profile.WithTarget(new Dictionary<string, object?>
            {
                ["problemText"] = "Benchmark workflow investigation with capped related context.",
                ["symbolQuery"] = profile.SymbolQuery,
                ["changedFiles"] = changedFiles,
                ["buildOutput"] = buildOutput,
                ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
                ["maxSymbols"] = 10,
                ["maxDiagnostics"] = 20,
                ["maxRelatedItems"] = 5,
                ["includeVisualStudioBuildOutput"] = false,
                ["includeGeneratedCode"] = false,
            })),
            payload => AssertWorkflowInvestigationPayload(payload, maxRelatedItems: 5)),
        new(
            "Large_WorkflowVerification_Capped",
            () => CallAsync(client, "plan_csharp_verification", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolQuery"] = profile.SymbolQuery,
                ["changedFiles"] = changedFiles,
                ["buildOutput"] = buildOutput,
                ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
                ["maxDiagnostics"] = 20,
                ["maxRelatedTests"] = 5,
                ["includeVisualStudioBuildOutput"] = false,
                ["includeGeneratedCode"] = false,
            })),
            AssertWorkflowVerificationPayload),
        new(
            "Large_WorkflowReview_Capped",
            () => CallAsync(client, "review_csharp_change", profile.WithTarget(new Dictionary<string, object?>
            {
                ["problemText"] = "Benchmark read-only workflow review with capped related context.",
                ["symbolQuery"] = profile.SymbolQuery,
                ["changedFiles"] = changedFiles,
                ["buildOutput"] = buildOutput,
                ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
                ["maxDiagnostics"] = 20,
                ["maxRelatedItems"] = 5,
                ["includeGeneratedCode"] = false,
            })),
            AssertWorkflowReviewPayload),
    };

    foreach (var testCase in cases)
    {
        var result = await RunBenchmarkCaseAsync(testCase, iterations);
        Console.WriteLine(
            "CASE profile={0} name={1} ok={2}/{3} avgMs={4:F1} p95Ms={5:F1} maxMs={6:F1}",
            profile.Name,
            result.Name,
            result.SuccessCount,
            iterations,
            result.Average.TotalMilliseconds,
            result.P95.TotalMilliseconds,
            result.Max.TotalMilliseconds);
    }

    Console.WriteLine("RESULT profile={0} passed cases={1} iterations={2}", profile.Name, cases.Count, iterations);
}

static async Task RunWorkflowBenchmarkAsync(McpClient client, BenchmarkProfile profile, int iterations)
{
    var changedFiles = CreateWorkflowChangedFiles(profile);
    var buildOutput = CreateWorkflowBuildOutput(profile);
    var cases = new List<BenchmarkCase>
    {
        new(
            "Workflow_StartInvestigation",
            () => CallAsync(client, "start_csharp_investigation", profile.WithTarget(new Dictionary<string, object?>
            {
                ["problemText"] = "Workflow profile should keep current-task evidence ahead of background diagnostics.",
                ["symbolQuery"] = profile.SymbolQuery,
                ["changedFiles"] = changedFiles,
                ["buildOutput"] = buildOutput,
                ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
                ["maxSymbols"] = 10,
                ["maxDiagnostics"] = 20,
                ["maxRelatedItems"] = 5,
                ["includeVisualStudioBuildOutput"] = false,
                ["includeGeneratedCode"] = false,
            })),
            payload => AssertWorkflowInvestigationPayload(payload, maxRelatedItems: 5)),
        new(
            "Workflow_VerificationPlan",
            () => CallAsync(client, "plan_csharp_verification", profile.WithTarget(new Dictionary<string, object?>
            {
                ["symbolQuery"] = profile.SymbolQuery,
                ["changedFiles"] = changedFiles,
                ["buildOutput"] = buildOutput,
                ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
                ["maxDiagnostics"] = 20,
                ["maxRelatedTests"] = 5,
                ["includeVisualStudioBuildOutput"] = false,
                ["includeGeneratedCode"] = false,
            })),
            AssertWorkflowVerificationPayload),
        new(
            "Workflow_ReviewChange",
            () => CallAsync(client, "review_csharp_change", profile.WithTarget(new Dictionary<string, object?>
            {
                ["problemText"] = "Workflow profile should produce a compact read-only change review package.",
                ["symbolQuery"] = profile.SymbolQuery,
                ["changedFiles"] = changedFiles,
                ["buildOutput"] = buildOutput,
                ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
                ["maxDiagnostics"] = 20,
                ["maxRelatedItems"] = 5,
                ["includeGeneratedCode"] = false,
            })),
            AssertWorkflowReviewPayload),
    };

    foreach (var testCase in cases)
    {
        var result = await RunBenchmarkCaseAsync(testCase, iterations);
        Console.WriteLine(
            "CASE profile={0} name={1} ok={2}/{3} avgMs={4:F1} p95Ms={5:F1} maxMs={6:F1}",
            profile.Name,
            result.Name,
            result.SuccessCount,
            iterations,
            result.Average.TotalMilliseconds,
            result.P95.TotalMilliseconds,
            result.Max.TotalMilliseconds);
    }

    Console.WriteLine("RESULT profile={0} passed cases={1} iterations={2}", profile.Name, cases.Count, iterations);
}

static async Task<BenchmarkResult> RunBenchmarkCaseAsync(BenchmarkCase testCase, int iterations)
{
    var durations = new List<TimeSpan>(iterations);
    for (var i = 0; i < iterations; i++)
    {
        var stopwatch = Stopwatch.StartNew();
        var resultJson = await testCase.Call();
        stopwatch.Stop();

        using var payload = ParseToolPayload(resultJson);
        testCase.Assert(payload);
        durations.Add(stopwatch.Elapsed);
    }

    durations.Sort();
    return new BenchmarkResult(
        testCase.Name,
        durations.Count,
        TimeSpan.FromTicks((long)durations.Average(duration => duration.Ticks)),
        durations[(int)Math.Ceiling(durations.Count * 0.95) - 1],
        durations[^1]);
}

static async Task<string> CallAsync(
    McpClient client,
    string toolName,
    IReadOnlyDictionary<string, object?> arguments)
{
    var result = await client.CallToolAsync(toolName, arguments!);
    if (result.IsError == true)
    {
        throw new InvalidOperationException($"{toolName} returned MCP error: {Serialize(result)}");
    }

    return Serialize(result);
}

static async Task<string> FindSymbolKeyAsync(
    McpClient client,
    BenchmarkProfile profile,
    string queryText,
    string expectedName,
    string expectedContainingType)
{
    var search = await CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
    {
        ["queryText"] = queryText,
        ["maxResults"] = 50,
        ["includeGeneratedCode"] = false,
    }));

    using var payload = ParseToolPayload(search);
    var item = FindSymbol(payload.RootElement.GetProperty("items"), expectedName, expectedContainingType);
    return item.GetProperty("key").GetProperty("value").GetString()
        ?? throw new InvalidOperationException($"No symbol key for {queryText}.");
}

static async Task<string> FindProfileSymbolKeyAsync(McpClient client, BenchmarkProfile profile)
{
    var search = await CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
    {
        ["queryText"] = profile.SymbolQuery,
        ["maxResults"] = 50,
        ["includeGeneratedCode"] = false,
    }));

    using var payload = ParseToolPayload(search);
    var item = FindSymbol(
        payload.RootElement.GetProperty("items"),
        profile.ExpectedSymbolName,
        profile.ExpectedContainingType);
    return item.GetProperty("key").GetProperty("value").GetString()
        ?? throw new InvalidOperationException($"No symbol key for {profile.SymbolQuery}.");
}

static JsonElement FindSymbol(JsonElement items, string expectedName, string expectedContainingType)
{
    foreach (var item in items.EnumerateArray())
    {
        if (string.Equals(item.GetProperty("name").GetString(), expectedName, StringComparison.Ordinal)
            && string.Equals(item.GetProperty("containingType").GetString(), expectedContainingType, StringComparison.Ordinal))
        {
            return item.Clone();
        }
    }

    throw new InvalidOperationException($"Could not find symbol {expectedContainingType}.{expectedName}.");
}

static void RequireTools(string[] toolNames)
{
    var required = new[]
    {
        "find_csharp_definitions",
        "find_csharp_derived_types",
        "find_csharp_solutions",
        "find_csharp_callers",
        "find_csharp_callees",
        "find_csharp_implementations",
        "find_csharp_overrides",
        "find_csharp_references",
        "find_csharp_related_tests",
        "get_csharp_enclosing_context",
        "get_csharp_source_context",
        "get_csharp_symbol_source",
        "describe_csharp_symbol",
        "evaluate_debug_expression",
        "get_csharp_diagnostics",
        "get_csharp_inheritance_chain",
        "get_csharp_project_graph",
        "get_csharp_workspace_status",
        "get_visual_studio_error_list",
        "get_visual_studio_output_window",
        "open_csharp_solution_in_visual_studio",
        "get_debug_call_stack",
        "get_debug_stack_frame_variables",
        "get_debugger_status",
        "list_csharp_document_symbols",
        "list_csharp_generated_documents",
        "list_debug_breakpoints",
        "list_debug_threads",
        "list_visual_studio_instances",
        "start_debugging",
        "continue_debugging",
        "break_debugging",
        "stop_debugging",
        "collect_artifact_evidence",
        "step_over",
        "step_into",
        "step_out",
        "set_debug_breakpoint",
        "remove_debug_breakpoint",
        "enable_debug_breakpoint",
        "find_csharp_temporary_markers",
        "analyze_csharp_build_errors",
        "analyze_csharp_repo_workflow",
        "analyze_csharp_symbol_impact",
        "apply_csharp_cleanup",
        "apply_csharp_code_fix",
        "apply_csharp_fix_all",
        "apply_csharp_rename",
        "batch_get_csharp_source_contexts",
        "batch_get_csharp_symbol_sources",
        "generate_csharp_agent_instructions",
        "preview_csharp_rename",
        "search_csharp_symbols",
        "get_csharp_task_context",
        "get_csharp_workflow_performance_snapshot",
        "get_visual_studio_active_document_context",
        "get_visual_studio_open_documents",
        "investigate_csharp_runtime_exception",
        "merge_csharp_agent_findings",
        "open_csharp_source_location",
        "start_csharp_investigation",
        "investigate_csharp_build_failure",
        "plan_csharp_verification",
        "plan_csharp_regression_scope",
        "plan_csharp_debug_scenario",
        "prepare_csharp_change_review",
        "prepare_csharp_edit_task",
        "prepare_csharp_verification_run",
        "prepare_debug_session",
        "list_csharp_code_fixes",
        "preview_csharp_cleanup",
        "preview_csharp_code_fix",
        "preview_csharp_fix_all",
        "preview_csharp_refactoring_plan",
        "prepare_csharp_workspace",
        "split_csharp_agent_work",
        "audit_csharp_area",
        "review_csharp_change",
        "wait_for_artifact_evidence",
        "wait_for_visual_studio_bridge",
    };

    foreach (var name in required)
    {
        Require(toolNames.Contains(name, StringComparer.Ordinal), $"MCP server missing required tool: {name}");
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void RequireNotPartial(JsonDocument payload)
{
    Require(payload.RootElement.GetProperty("isPartial").GetBoolean() == false, "工具结果不应为 partial。");
    Require(payload.RootElement.GetProperty("diagnostics").GetArrayLength() == 0, "工具结果不应包含 diagnostics。");
}

static void AssertWorkflowInvestigationPayload(JsonDocument payload, int maxRelatedItems)
{
    var items = payload.RootElement.GetProperty("items");
    Require(items.GetArrayLength() == 1, "Workflow investigation should return exactly one report.");
    var item = items[0];
    Require(item.TryGetProperty("taskContext", out _), "Workflow investigation should include taskContext.");
    Require(item.TryGetProperty("primaryFiles", out var primaryFiles), "Workflow investigation should include primaryFiles.");
    Require(item.TryGetProperty("primaryDiagnostics", out var primaryDiagnostics), "Workflow investigation should include primaryDiagnostics.");
    Require(item.TryGetProperty("recommendedNextActions", out var nextActions), "Workflow investigation should include recommendedNextActions.");
    Require(primaryFiles.GetArrayLength() >= 1, "Workflow investigation should identify at least one primary file.");
    Require(primaryDiagnostics.GetArrayLength() >= 1, "Workflow investigation should identify at least one primary diagnostic.");
    Require(nextActions.GetArrayLength() >= 1, "Workflow investigation should recommend at least one next action.");
    Require(item.GetProperty("references").GetArrayLength() <= maxRelatedItems, "Workflow investigation references should respect maxRelatedItems.");
    Require(item.GetProperty("callers").GetArrayLength() <= maxRelatedItems, "Workflow investigation callers should respect maxRelatedItems.");
    Require(item.GetProperty("callees").GetArrayLength() <= maxRelatedItems, "Workflow investigation callees should respect maxRelatedItems.");
    Require(item.GetProperty("relatedTests").GetArrayLength() <= maxRelatedItems, "Workflow investigation related tests should respect maxRelatedItems.");
}

static void AssertWorkflowVerificationPayload(JsonDocument payload)
{
    var items = payload.RootElement.GetProperty("items");
    Require(items.GetArrayLength() == 1, "Workflow verification should return exactly one plan.");
    var item = items[0];
    Require(item.TryGetProperty("taskContext", out _), "Workflow verification should include taskContext.");
    Require(item.TryGetProperty("recommendedCommands", out var commands), "Workflow verification should include recommendedCommands.");
    Require(item.TryGetProperty("recommendedNextActions", out _), "Workflow verification should include recommendedNextActions.");
    Require(commands.GetArrayLength() >= 1, "Workflow verification should recommend at least one command.");
}

static void AssertWorkflowReviewPayload(JsonDocument payload)
{
    var items = payload.RootElement.GetProperty("items");
    Require(items.GetArrayLength() == 1, "Workflow review should return exactly one report.");
    var item = items[0];
    Require(item.TryGetProperty("taskContext", out _), "Workflow review should include taskContext.");
    Require(item.TryGetProperty("primaryFiles", out var primaryFiles), "Workflow review should include primaryFiles.");
    Require(item.TryGetProperty("candidateEditLocations", out _), "Workflow review should include candidateEditLocations.");
    Require(item.TryGetProperty("recommendedNextActions", out var nextActions), "Workflow review should include recommendedNextActions.");
    Require(primaryFiles.GetArrayLength() >= 1, "Workflow review should identify at least one primary file.");
    Require(nextActions.GetArrayLength() >= 1, "Workflow review should recommend at least one next action.");
}

static string[] CreateWorkflowChangedFiles(BenchmarkProfile profile)
{
    var configured = Environment.GetEnvironmentVariable("CODE_NAVIGATOR_BENCHMARK_CHANGED_FILES")
        ?? Environment.GetEnvironmentVariable("CODE_NAVIGATOR_TEST_CHANGED_FILES");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    return string.IsNullOrWhiteSpace(profile.DocumentPath)
        ? Array.Empty<string>()
        : new[] { profile.DocumentPath };
}

static string CreateWorkflowBuildOutput(BenchmarkProfile profile)
{
    var configured = Environment.GetEnvironmentVariable("CODE_NAVIGATOR_BENCHMARK_BUILD_OUTPUT")
        ?? Environment.GetEnvironmentVariable("CODE_NAVIGATOR_TEST_BUILD_OUTPUT");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    var path = string.IsNullOrWhiteSpace(profile.DocumentPath)
        ? Path.Combine(profile.SolutionPath, "WorkflowBenchmark.cs")
        : profile.DocumentPath;
    var project = string.IsNullOrWhiteSpace(profile.ProjectName)
        ? profile.SolutionPath
        : profile.ProjectName;

    return string.Format(
        "{0}(1,1): error CS0103: The name 'workflowBenchmarkMissingValue' does not exist in the current context [{1}]",
        path,
        project);
}

static bool DiagnosticsContain(JsonDocument payload, string text)
{
    foreach (var diagnostic in payload.RootElement.GetProperty("diagnostics").EnumerateArray())
    {
        if (diagnostic.GetString()?.Contains(text, StringComparison.Ordinal) == true)
        {
            return true;
        }
    }

    return false;
}

static bool AllRolesAre(JsonElement items, string expectedRole)
{
    foreach (var item in items.EnumerateArray())
    {
        if (!string.Equals(item.GetProperty("role").GetString(), expectedRole, StringComparison.Ordinal))
        {
            return false;
        }
    }

    return true;
}

static bool ContainsDocumentSymbol(JsonElement items, string symbolName)
{
    foreach (var item in items.EnumerateArray())
    {
        if (string.Equals(
            item.GetProperty("symbol").GetProperty("name").GetString(),
            symbolName,
            StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

static bool AllCallGraphEdgesHaveSourceTargetAndSpan(JsonElement items)
{
    foreach (var item in items.EnumerateArray())
    {
        if (string.IsNullOrWhiteSpace(item.GetProperty("source").GetProperty("name").GetString())
            || string.IsNullOrWhiteSpace(item.GetProperty("target").GetProperty("name").GetString())
            || string.IsNullOrWhiteSpace(item.GetProperty("span").GetProperty("filePath").GetString())
            || item.GetProperty("kind").GetString() == "Unknown")
        {
            return false;
        }
    }

    return true;
}

static int MaxCallGraphDepth(JsonElement items)
{
    var maxDepth = 0;
    foreach (var item in items.EnumerateArray())
    {
        maxDepth = Math.Max(maxDepth, item.GetProperty("depth").GetInt32());
    }

    return maxDepth;
}

static bool AllDerivedTypesHaveSource(JsonElement items)
{
    foreach (var item in items.EnumerateArray())
    {
        if (!item.GetProperty("symbol").TryGetProperty("span", out var span)
            || string.IsNullOrWhiteSpace(span.GetProperty("filePath").GetString()))
        {
            return false;
        }
    }

    return true;
}

static bool AllRelatedTestsHaveReasonAndSpan(JsonElement items)
{
    foreach (var item in items.EnumerateArray())
    {
        if (item.GetProperty("matchReasons").GetArrayLength() == 0
            || string.IsNullOrWhiteSpace(item.GetProperty("span").GetProperty("filePath").GetString()))
        {
            return false;
        }
    }

    return true;
}

static Dictionary<string, int> CountRoles(JsonElement items)
{
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in items.EnumerateArray())
    {
        var role = item.GetProperty("role").GetString() ?? "Unknown";
        counts[role] = counts.TryGetValue(role, out var count) ? count + 1 : 1;
    }

    return counts;
}

static JsonDocument ParseToolPayload(string resultJson)
{
    using var result = JsonDocument.Parse(resultJson);
    var content = result.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
        ?? throw new InvalidOperationException("Tool result did not contain text content.");

    return JsonDocument.Parse(content);
}

static string Serialize<T>(T value)
{
    return JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = false,
    });
}

static int ReadIntArg(string[] args, string name, int defaultValue)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[i + 1], out var value))
        {
            return value;
        }
    }

    return defaultValue;
}

sealed record BenchmarkProfile(
    string Name,
    string SolutionPath,
    string TargetPipeName,
    string TargetInstanceId,
    string SymbolQuery,
    string ExpectedSymbolName,
    string ExpectedContainingType,
    string ProjectName,
    string DocumentPath)
{
    public bool UsesRichCSharpNavigatorCases =>
        string.Equals(Name, "realproject", StringComparison.OrdinalIgnoreCase);

    public bool IsLargeSolutionProfile => string.Equals(Name, "large", StringComparison.OrdinalIgnoreCase);

    public bool IsWorkflowProfile => string.Equals(Name, "workflow", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object?> WithTarget(Dictionary<string, object?> arguments)
    {
        if (!string.IsNullOrWhiteSpace(TargetPipeName))
        {
            arguments["targetPipeName"] = TargetPipeName;
        }
        else if (!string.IsNullOrWhiteSpace(TargetInstanceId))
        {
            arguments["targetInstanceId"] = TargetInstanceId;
        }
        else if (!string.IsNullOrWhiteSpace(SolutionPath))
        {
            arguments["targetSolutionPath"] = SolutionPath;
        }

        return arguments;
    }

    public static BenchmarkProfile Load(string workspaceRoot)
    {
        var profileName = ReadSetting("CODE_NAVIGATOR_BENCHMARK_PROFILE")
            ?? ReadSetting("CODE_NAVIGATOR_TEST_PROFILE")
            ?? "generic";
        var targetPipeName = ReadSetting("CODE_NAVIGATOR_BENCHMARK_TARGET_PIPE_NAME")
            ?? ReadSetting("CODE_NAVIGATOR_TEST_TARGET_PIPE_NAME")
            ?? string.Empty;
        var targetInstanceId = ReadSetting("CODE_NAVIGATOR_BENCHMARK_TARGET_INSTANCE_ID")
            ?? ReadSetting("CODE_NAVIGATOR_TEST_TARGET_INSTANCE_ID")
            ?? string.Empty;

        if (string.Equals(profileName, "generic", StringComparison.OrdinalIgnoreCase))
        {
            var sampleSolution = Path.Combine(
                workspaceRoot,
                "artifacts",
                "sample-csharp-solution",
                "CodeNavigator.Sample.sln");

            return new BenchmarkProfile(
                "generic",
                ReadSetting("CODE_NAVIGATOR_TEST_SOLUTION") ?? sampleSolution,
                targetPipeName,
                targetInstanceId,
                ReadSetting("CODE_NAVIGATOR_TEST_SYMBOL") ?? "Calculator.Add",
                ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_NAME") ?? "Add",
                ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_CONTAINING_TYPE") ?? "CodeNavigator.Sample.Calculator",
                ReadSetting("CODE_NAVIGATOR_TEST_PROJECT") ?? "CodeNavigator.Sample",
                Path.Combine(
                    workspaceRoot,
                    "artifacts",
                    "sample-csharp-solution",
                    "src",
                    "CodeNavigator.Sample",
                    "Calculator.cs"));
        }

        if (string.Equals(profileName, "workflow", StringComparison.OrdinalIgnoreCase))
        {
            var sampleSolution = Path.Combine(
                workspaceRoot,
                "artifacts",
                "sample-csharp-solution",
                "CodeNavigator.Sample.sln");
            var sampleDocument = Path.Combine(
                workspaceRoot,
                "artifacts",
                "sample-csharp-solution",
                "src",
                "CodeNavigator.Sample",
                "Calculator.cs");

            return new BenchmarkProfile(
                "workflow",
                ReadSetting("CODE_NAVIGATOR_BENCHMARK_SOLUTION")
                    ?? ReadSetting("CODE_NAVIGATOR_TEST_SOLUTION")
                    ?? sampleSolution,
                targetPipeName,
                targetInstanceId,
                ReadSetting("CODE_NAVIGATOR_BENCHMARK_SYMBOL")
                    ?? ReadSetting("CODE_NAVIGATOR_TEST_SYMBOL")
                    ?? "Calculator.Add",
                ReadSetting("CODE_NAVIGATOR_BENCHMARK_EXPECTED_NAME")
                    ?? ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_NAME")
                    ?? "Add",
                ReadSetting("CODE_NAVIGATOR_BENCHMARK_EXPECTED_CONTAINING_TYPE")
                    ?? ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_CONTAINING_TYPE")
                    ?? "CodeNavigator.Sample.Calculator",
                ReadSetting("CODE_NAVIGATOR_BENCHMARK_PROJECT")
                    ?? ReadSetting("CODE_NAVIGATOR_TEST_PROJECT")
                    ?? "CodeNavigator.Sample",
                ReadSetting("CODE_NAVIGATOR_BENCHMARK_DOCUMENT")
                    ?? ReadSetting("CODE_NAVIGATOR_TEST_DOCUMENT")
                    ?? sampleDocument);
        }

        if (string.Equals(profileName, "large", StringComparison.OrdinalIgnoreCase))
        {
            return CreateLargeSolutionProfile(profileName, targetPipeName, targetInstanceId);
        }

        if (!string.Equals(profileName, "realproject", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported benchmark profile '{profileName}'. Use 'generic', 'workflow', 'realproject', or 'large'.");
        }

        return CreateLargeSolutionProfile("realproject", targetPipeName, targetInstanceId);
    }

    private static BenchmarkProfile CreateLargeSolutionProfile(
        string profileName,
        string targetPipeName,
        string targetInstanceId)
    {
        return new BenchmarkProfile(
            profileName,
            ReadSetting("CODE_NAVIGATOR_BENCHMARK_SOLUTION")
                ?? ReadSetting("CODE_NAVIGATOR_TEST_SOLUTION")
                ?? ReadSetting("VisualStudioBridge__SolutionPath")
                ?? @"D:\Samples\SampleWorkspace\SampleWorkspace.sln",
            targetPipeName,
            targetInstanceId,
            ReadSetting("CODE_NAVIGATOR_BENCHMARK_SYMBOL")
                ?? ReadSetting("CODE_NAVIGATOR_TEST_SYMBOL")
                ?? "SetProps",
            ReadSetting("CODE_NAVIGATOR_BENCHMARK_EXPECTED_NAME")
                ?? ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_NAME")
                ?? "SetProps",
            ReadSetting("CODE_NAVIGATOR_BENCHMARK_EXPECTED_CONTAINING_TYPE")
                ?? ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_CONTAINING_TYPE")
                ?? "SampleWorkspace.Core.LcObject",
            ReadSetting("CODE_NAVIGATOR_BENCHMARK_PROJECT")
                ?? ReadSetting("CODE_NAVIGATOR_TEST_PROJECT")
                ?? "SampleWorkspace.Core",
            ReadSetting("CODE_NAVIGATOR_BENCHMARK_DOCUMENT")
                ?? ReadSetting("CODE_NAVIGATOR_TEST_DOCUMENT")
                ?? @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\Elements\Basic\LcLine.cs");
    }

    private static string? ReadSetting(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

sealed record BenchmarkCase(
    string Name,
    Func<Task<string>> Call,
    Action<JsonDocument> Assert);

sealed record BenchmarkResult(
    string Name,
    int SuccessCount,
    TimeSpan Average,
    TimeSpan P95,
    TimeSpan Max);
