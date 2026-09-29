# Codex

Codex is the primary supported distribution. Use the generated release package
and run `Install-CodexPlugin.ps1`; it installs the personal plugin, MCP runtime,
and workflow skill. The VSIX is included but requires explicit `-InstallVsix`
approval.

For a direct MCP-only setup, generate a TOML snippet:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\New-McpClientConfiguration.ps1 -ClientHost Codex -ServerExe <absolute-server-exe-path> -OutputPath .\codex-mcp.toml
```

The packaged plugin remains preferable because it carries the C# workflow skill
that routes agents through task-level tools before low-level navigation tools.

## 按能力路由

优先调用 `get_csharp_workflow_capabilities(taskCategory=...)`，再从返回的
`EntryPoints` 中选择符合当前任务的入口。`AvailableTools` 只列当前服务器
程序集按 MCP 特性实际注册的工具；数量由反射计算，不维护固定工具数量。
该规则对应当前 `WithToolsFromAssembly` 注册方式，不探测其他进程或已安装副本。

常用分类为 `edit`、`review`、`verification`、`build`、`diagnostics`、
`navigation`、`debug`、`mutation` 和 `operation-status`。其他分类包括
`discovery`、`workspace`、`artifacts`、`performance`、`repository`、
`collaboration` 和 `other`；省略参数或使用 `all` 返回全部。分类忽略大小写，
未知分类返回诊断；合法分类没有已注册工具时返回空分类结果和说明，不虚构入口。

能力发现始终仅在服务器本地执行，不连接 VSIX、不运行 Roslyn 查询或性能快照，
不启动 Visual Studio，也不安装组件。返回的 `Safety` 明确标记 VSIX 未核验、
版本安全未确认；服务器工具存在不证明目标桥接支持新能力。实际桥接调用仍须
明确目标，按需检查健康状态并处理版本不匹配或不支持的诊断。

旧版没有能力发现工具时，技能依据客户端实际工具清单路由：
编辑任务从 `prepare_csharp_edit_task` 降级到已有的 `get_csharp_task_context`
或 `start_csharp_investigation`；审查、验证、构建和调试任务采用各自已有入口。
仅调用实际存在的候选，不用性能快照代替轻量发现，也不自动安装升级。

## 修改结果确认

修改调用超时、取消或中断后不得直接重试。保留已有回执和明确的原目标；
只有当前服务器已注册 `get_csharp_operation_status` 时，能力发现才返回此入口。
`OperationStatusRequiresExplicitTarget` 与 `OperationStatusSupportsRecentListing`
描述该服务器入口的约束和查询方式，不证明目标 VSIX 支持查询。

查询始终须携带明确的原 `targetInstanceId`、`targetPipeName` 或
`targetSolutionPath`。有回执时凭原 `requestId` 精确查询；
`includeResponse=true` 仅允许用于指定 `requestId` 的精确查询。
MCP 取消后未收到回执时，可省略 `requestId`，按原目标列最近记录
（默认 10 项、最多 20 项），保持 `includeResponse=false`，只供核对。
禁止自动选择记录重放，也不得把最近一条记录直接当成被中断调用的结果。

未知、未找到、处理中或查询失败均不构成自动重放依据。旧版没有查询入口、
目标不明确或最近记录无法确认结果时，停止自动修改，人工核对状态与已有证据。

## 诊断范围

诊断基线工具可用时，在修改前调用 `capture_csharp_diagnostic_baseline`，
修改后在相同 Visual Studio 目标和范围下调用
`compare_csharp_diagnostics_to_baseline`。旧版缺少基线工具时保留前后限定范围
的诊断证据并说明限制。业务噪声路径来自当前仓库配置或用户明确指定，
不再把其他项目的业务目录硬编码到技能示例中。
