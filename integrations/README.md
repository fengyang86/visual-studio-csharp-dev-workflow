# MCP Client Integrations

The core runtime is host-neutral: every supported client starts the same local
`VisualStudio.CSharpNavigator.Server.exe` process through MCP stdio. The VSIX
continues to provide the in-process Visual Studio and Roslyn bridge.

## Shared prerequisites

1. Install Visual Studio 2022 or later with C# development support.
2. Install the packaged VSIX. It is required for Roslyn, debugger, and Visual
   Studio UI evidence; a standalone MCP server cannot replace it.
3. Build or download the MCP runtime and use its full executable path in the
   selected client configuration.
4. Start Visual Studio with a C# `.sln` or `.slnx`, then ask the client to run
   `prepare_csharp_workspace`.

`scripts/New-McpClientConfiguration.ps1` creates a client-specific configuration
snippet without editing a user configuration file. This preserves existing
provider settings and avoids accidentally overwriting other MCP servers.

## Support levels

| Host | Status | Integration surface |
| --- | --- | --- |
| Codex | Supported | Packaged personal plugin, skill, and MCP configuration |
| DeepSeekHarness | Supported | Cordis patch integration (`cordis.patch.yml`), generated fragments, and the packaged `Install-DshMcpServer.ps1` installer |
| Claude Code | Configuration ready | Standard project or user MCP JSON configuration; E3 smoke pending |

DeepSeekHarness registers stdio MCP servers through a Cordis patch layer that
instantiates the bundled `@deepseek-ai/dsh-mcp-client` plugin; it does not
consume an `mcpServers` JSON file. See
[deepseek-harness/README.md](deepseek-harness/README.md) for patch locations,
hot-reload behavior, and the support gate that records the exact DSH version
and `cordis.patch.yml` path used by each verified smoke test.
