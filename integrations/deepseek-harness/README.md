# DeepSeekHarness (Supported)

DeepSeekHarness does not use an `mcpServers` JSON configuration. It registers
stdio MCP servers as instances of the bundled `@deepseek-ai/dsh-mcp-client`
plugin through a Cordis patch layer, and both user patch files are hot-reloaded
by a running DSH session.

## Configuration locations

DSH resolves its home directory in this order: explicit configuration,
`DSH_HOME` environment variable, then `~/.dsh`. The DSHSharp desktop app keeps
its home under `%APPDATA%\DSHSharp\dsh-home`. Register the server in one of:

- `<DSH_HOME>\profiles\<profile>\cordis.patch.yml` — one profile only
- `<DSH_HOME>\cordis.patch.yml` — all profiles

If the target file currently contains only `[]`, replace the whole file with
the generated list. Editing the file while DSH is running triggers a
disconnect/reconnect without a restart.

## Install steps

1. Copy or publish the MCP runtime to a stable directory that no AI client
   rebuild will overwrite (for example
   `%LOCALAPPDATA%\VisualStudio.CSharpNavigatorMcp\mcp-server`), or reuse the
   packaged distribution installer `Install-DshMcpServer.ps1`.
2. Generate a ready-to-paste patch entry with the absolute server path:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\New-McpClientConfiguration.ps1 -ClientHost DeepSeekHarness -ServerExe <absolute-server-exe-path> -OutputPath .\visual-studio-csharp-navigator.cordis.patch.yml
```

3. Merge the generated `- insert:` entry into the chosen `cordis.patch.yml`.
   `serverName` must stay `visual_studio_csharp_navigator` (max 32 chars,
   `[A-Za-z0-9_-]`); tools surface to the model as
   `mcp__visual_studio_csharp_navigator__<toolName>`.
4. Install the VSIX in Visual Studio (explicit approval required) and open a
   C# `.sln` or `.slnx` before asking DSH to use the tools.

Notes: DSH scrubs credential-looking environment variables and all `DSH_*`
variables before spawning the server; startup inherits the MCP SDK's 60 second
default and there is no per-server startup timeout key yet.

## Verified smoke record

- Date: 2026-09-07
- DSH client: DSHSharp 0.2.2 (`0.2.2+c32c522857fad4bf36ac4046869944cf4e5d7e22`), Windows desktop build
- Managed runtime: `@deepseek-ai/dsh` 0.1.2-rc.1 with `@deepseek-ai/dsh-mcp-client` 0.1.2-rc.1 (MCP handshake identifies as `dsh-mcp-client 0.0.1`)
- Configuration file: `%APPDATA%\DSHSharp\dsh-home\cordis.patch.yml` (home-level, all profiles)
- MCP runtime: `%LOCALAPPDATA%\VisualStudio.CSharpNavigatorMcp\mcp-server\VisualStudio.CSharpNavigator.Server.exe`
- Verified end to end: DSH spawned the server on startup, `initialize` and `tools/list` completed through `dsh-mcp-client`; in a DSH session the model called `list_visual_studio_instances` (3 live bridges), `get_csharp_workspace_status` via `targetSolutionPath` (19 projects / 1475 documents), and `search_csharp_symbols` (38 real symbol hits with source spans).

Re-verification after a DSH upgrade should repeat the same steps and record the new versions here.
