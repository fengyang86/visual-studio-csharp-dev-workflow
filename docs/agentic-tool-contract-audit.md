# Agentic MCP Tool Contract Audit

状态：R0 基线审计与 R12 Agent 工作包契约已更新。  
日期：2026-06-26。  
目标：冻结当前 Visual Studio C# Dev Workflow 对外工具契约，作为 Agentic V2 重构期间的兼容基线。

## 基线

- 当前对外工具数：82。旧工具保持兼容；Agentic 任务级入口、artifact wait、debug scenario plan、性能预算快照和多 Agent 工作包工具已纳入 schema 快照。
- 当前产品入口：`visual-studio-csharp-dev-workflow@personal`。
- 当前 MCP server id：`visual_studio_csharp_navigator`。
- 当前策略：旧工具短期保持兼容；新增 Agentic 任务级入口作为 opt-in，验收后再更新 skill 默认路由。
- 当前验证命令：

```powershell
$env:CODE_NAVIGATOR_TEST_PROFILE='tool-schema'
$env:CODE_NAVIGATOR_SERVER_EXE=(Resolve-Path .\src\VisualStudio.CSharpNavigator.Server\bin\Release\net8.0\VisualStudio.CSharpNavigator.Server.exe).Path
dotnet run --project .\artifacts\mcp-stdio-e2e\McpStdioE2e.csproj -c Release --no-restore
```

## 能力层索引

| 能力层 | 工具 | 契约要点 |
| --- | --- | --- |
| Workspace Preparation | `list_visual_studio_instances`, `find_csharp_solutions`, `prepare_csharp_workspace`, `open_csharp_solution_in_visual_studio`, `wait_for_visual_studio_bridge` | 打开 VS 是副作用；其余只读。多实例不得猜。`.sln` / `.slnx` 都是有效 solution。 |
| Workspace Health | `get_csharp_workspace_status`, `check_visual_studio_csharp_navigator_health` | 只读；可返回诊断预览；版本不匹配必须显式诊断。 |
| Code Intelligence | `search_csharp_symbols`, definitions/references/implementations/overrides, symbol/source context, call graph, impact, inheritance, project graph, generated docs, temp markers, related tests | 只读；所有大结果必须支持上限和 partial；启发式必须说明 reason。 |
| Build / Diagnostics | `analyze_csharp_build_errors`, `get_csharp_diagnostics`, `get_visual_studio_error_list`, `get_visual_studio_output_window`, `investigate_csharp_build_failure` | build log 优先于 VS UI 和 Roslyn diagnostics；known noise 过滤必须可解释。 |
| Review / Verification | `start_csharp_investigation`, `get_csharp_task_context`, `plan_csharp_verification`, `audit_csharp_area`, `review_csharp_change`, `plan_csharp_regression_scope`, `prepare_csharp_edit_task`, `prepare_csharp_change_review`, `prepare_csharp_verification_run` | 默认只读；Agentic 入口返回 EvidencePacket、resource links、next actions、partial 和残余风险。 |
| Runtime Debug Workflow | `prepare_debug_session`, `plan_csharp_debug_scenario`, `investigate_csharp_runtime_exception` | 默认只读；runtime exception 入口只聚合调试状态、调用栈、源码片段和 artifact evidence，不启动/停止/单步调试；debug scenario plan 只生成 configure/start/wait/collect/cleanup 步骤。 |
| Mutation / Refactoring | rename preview/apply, cleanup preview/apply, code fix/fix all list/preview/apply, refactoring plan | preview 只读；apply 有副作用，必须显式 target；Code Fix / Fix All apply 都必须基于 preview session replay。 |
| Debug Context | debugger status, call stack, stack variables, threads, breakpoints | 默认只读；表达式求值因可显式允许副作用，标记非只读。 |
| Debug Control | start/continue/break/stop/step, breakpoint mutation | 有副作用；必须显式 target。 |
| Artifact / Performance | `collect_artifact_evidence`, `wait_for_artifact_evidence`, `get_csharp_workflow_performance_snapshot` | 只读；大 artifact 只返回摘要；wait 工具只轮询本地 artifact，不启动应用；性能快照不运行 benchmark。 |
| Agent Collaboration | `analyze_csharp_repo_workflow`, `generate_csharp_agent_instructions`, `split_csharp_agent_work`, `merge_csharp_agent_findings` | 只读；生成 AGENTS/Copilot instructions 草稿但不写文件；work packet 限定文件、符号、工具和预算；merge 必须暴露缺失 packet。 |

## Agentic V2 增量契约

新增任务级入口必须遵守：

- 返回 `EvidencePacket` 或可映射到 `EvidencePacket` 的任务结果。
- 证据等级明确区分 Fact、Inference、Heuristic、Assumption。
- 只读任务入口不得修改文件、VS 调试状态或 VS UI。
- 所有大结果先返回摘要，逐步迁移到 MCP Resources；当前已支持 context/source/review/verification/debug-evidence/build-failure/build-log/diagnostics/impact/artifact 资源模板。
- Build failure 会话保持旧字段兼容，同时在 `BuildFailureSession` 中返回 root-cause/cascade、issue bindings、project bindings、scoped diagnostics、source snippets 和 related tests。issue/project/test 绑定是事实与推断混合证据，失败时应记录诊断而不是返回空成功。
- Mutation preview/apply 统一携带 `SessionId`、`WorkspaceVersion` 和 `MutationCandidateIdentity`。Code Fix / Fix All 当前已能透传 provider/equivalence/stable key；`list_csharp_code_fixes` 已返回 provider-backed 候选，`preview_csharp_code_fix` 已返回 provider-backed CodeAction diff，`apply_csharp_code_fix` 已要求 preview session replay、workspace version 和 candidate identity 校验；`preview_csharp_fix_all` / `apply_csharp_fix_all` 已通过 Roslyn FixAllProvider 接入同一 session replay 管线。
- `get_csharp_workflow_performance_snapshot` 返回 bridge 状态、目标 solution、工具计数预期、benchmark/profile 环境变量、上下文预算提示和 telemetry 信号说明。
- `split_csharp_agent_work` 输出的子 Agent 包必须包含 `packetId`、`objective`、`scopeFiles`、`scopeSymbols`、`allowedTools`、`evidenceResources`、`budget` 和固定 finding schema；子 Agent 不应自行扩大到全仓库。
- `merge_csharp_agent_findings` 必须按 `expectedPacketIds` 检测缺失包，并把缺失状态反映为 partial。
- 无 VS bridge、目标歧义、workspace 未加载时不能返回空成功，必须给出 safety blocker 或 next action。

## 热点文件

- `src/VisualStudio.CSharpNavigator.Protocol/CodeNavigationContracts.cs`
- `src/VisualStudio.CSharpNavigator.Server/Tools/CodeNavigationTools.cs`
- `src/VisualStudio.CSharpNavigator.Server/Tools/*Tools.cs`
- `src/VisualStudio.CSharpNavigator.Vsix/Workspace/VisualStudioWorkspaceQueryService*.cs`
- `tests/VisualStudio.CSharpNavigator.Server.Tests/CodeNavigationToolsTests.cs`
- `artifacts/mcp-stdio-e2e/Program.cs`
- `artifacts/mcp-benchmark/Program.cs`

## 最新验证

- `dotnet build .\VisualStudio.CSharpNavigatorMcp.sln -c Release -v:minimal --no-restore`：通过，0 warning / 0 error。
- `dotnet test .\tests\VisualStudio.CSharpNavigator.Server.Tests\VisualStudio.CSharpNavigator.Server.Tests.csproj -c Release --no-build -v:minimal --nologo`：通过，250/250。
- `CODE_NAVIGATOR_TEST_PROFILE=tool-schema` + Release server exe：通过，`TOOL_SCHEMA total=82`，`RESOURCES templates=15`，并刷新 `docs/agentic-tool-schema.snapshot.json`。
- `CODE_NAVIGATOR_TEST_PROFILE=agentic-resources` + Release server exe：通过，context/review/verification/debug-evidence/build-failure/build-log resource 均可列出并读取。
- `dotnet build .\artifacts\mcp-benchmark\McpBenchmark.csproj -c Release -v:minimal --no-restore`：通过，0 warning / 0 error。
- 触碰 C# 文件 `BareLF=0`；`git diff --check` 无 whitespace error，仅有既有 LF/CRLF warning。

## 当前缺口

- R0 的机器生成 schema 快照已落盘到 `docs/agentic-tool-schema.snapshot.json`。
- R4 MCP Resources 首版已实现；context/source/review/verification/debug-evidence/build-failure/build-log/diagnostics/impact/artifact 模板已接入，并补充主要 JSON resource 模板。
- R6 Build Failure Workflow 已完成 root-cause issue 到项目、enclosing symbol、相关测试的有限绑定，并新增推荐验证命令；更完整的分类和影响图资源仍可继续扩展。
- R7 CodeAction provider-backed discovery、单项 preview/apply replay、Fix All preview/apply replay 和 `MutationSessionStore` 校验已实现；真实 VS dogfood 已通过，覆盖 `CS0103` 单项 CodeAction preview/apply 和 `CS0219` 项目级 Fix All preview/apply。后续仍可继续扩展更多 provider 样本。
- Skill 默认路由仍需在下一次打包/更新时同步到最新 82 工具说明；当前源码模板已准备更新。
