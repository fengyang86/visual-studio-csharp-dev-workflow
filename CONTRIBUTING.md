# Contributing

感谢参与。这个项目连接 AI 客户端、MCP、Visual Studio 和 Roslyn；可复现证据比“大概可用”更重要。

## 开始前

- 不要提交 API key、个人 MCP 配置、`.cc-switch` / Codex++ 数据库、用户名路径或 Visual Studio discovery 文件。
- 不要提交 `bin/`、`obj/`、`artifacts/plugin-staging/` 或 `artifacts/distribution/` 生成物。
- C# 源码保持现有 CRLF 行尾。
- 公共 MCP tool、协议 DTO、VSIX bridge 和 mutation 流程的改动必须说明兼容性影响。

## 提交要求

1. 说明问题、预期行为和实际行为。
2. 对 bug 提供最小复现或受限日志，删除项目机密和个人路径。
3. 为行为变更加测试；对 VSIX/Visual Studio 行为说明可验证的环境与版本。
4. 至少运行相关 build/test；若不能运行，明确原因。
5. 不把启发式结论写成 Roslyn、构建日志或调试器事实。

## 多宿主改动

新的 MCP 客户端适配必须复用现有 server runtime，不得复制工具逻辑。请记录宿主版本、配置路径、配置片段和最小 smoke 结果；没有真实 smoke 的适配应标为实验性。
