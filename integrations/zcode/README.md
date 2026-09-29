# ZCode 本机接入

ZCode 使用与 Codex 相同的 MCP 运行时和 Visual Studio VSIX，通过用户级 MCP
配置和共享技能接入，不需要运行 Codex，也不要求安装一个独立的 ZCode 插件。
以下方式已在本机 ZCode 3.12.1 验证，尚未纳入通用分发安装器。

## 配置

用户配置文件为 `%USERPROFILE%\.zcode\cli\config.json`，服务器放在
`mcp.servers.visual_studio_csharp_navigator`。只合并本工具的配置，不覆盖其他服务器：

```json
{
  "mcp": {
    "servers": {
      "visual_studio_csharp_navigator": {
        "type": "stdio",
        "command": "<MCP 服务器的绝对 exe 路径>",
        "args": [],
        "env": {
          "VisualStudioBridge__ConnectTimeoutMilliseconds": "5000",
          "VisualStudioBridge__DiscoveryStaleAfterSeconds": "120"
        },
        "timeoutMs": 120000
      }
    }
  }
}
```

本机运行时位于 `%USERPROFILE%\plugins\visual-studio-csharp-dev-workflow\runtimes\mcp-server`。
写入 JSON 时应展开为实际绝对路径，并正确转义反斜杠；
用户 MCP 配置不会展开插件专用的变量占位符。
已有 `.zcode` MCP 配置时，不要只修改 `.agents/mcp.json`，该回退配置可能不会被读取。

## 技能

共享技能位于 `%USERPROFILE%\.agents\skills\visual-studio-csharp-dev-workflow\SKILL.md`。
更新时同步已打包的工作流规则，并保留宿主适配说明：ZCode 检查自身的
“设置 → MCP”，不应要求安装 Codex 插件。检查 `.zcode/skills` 是否存在同名高优先级副本。

## 验证

1. 用户配置可解析，服务器路径存在，其他服务器配置保持不变。
2. 新建任务或重启 ZCode，检查“设置 → MCP”和“设置 → Skills”。
3. 日志中本服务器的 `mcp.server.connected` 应包含实际工具数量；
   2026-09-16 本机验证为 90 个工具，不能把这个数量当作未来版本的固定要求。
4. 指定 Visual Studio 实例，调用工作区状态、健康检查与符号搜索。
   VSIX 必须与运行时配套，仅有 MCP 连接成功不代表桥接可用。

诊断依据：本机 ZCode 内置 `zcode-guide` 的 `diagnosing-mcp`、
`diagnosing-skills` 与 `zcode-configuration-guide` 文档。
