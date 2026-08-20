# DeepSeekHarness (Experimental)

This integration uses the standard MCP stdio JSON shape. Replace the command
placeholder in `mcp.json.template` with the absolute path to the published MCP
server executable, then add that server entry through the MCP configuration
mechanism exposed by the installed DeepSeekHarness version.

Generate a ready-to-paste fragment:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\New-McpClientConfiguration.ps1 -ClientHost DeepSeekHarness -ServerExe <absolute-server-exe-path> -OutputPath .\deepseek-harness-mcp.json
```

Do not treat this as supported until a real smoke test succeeds: list MCP tools,
open a `.sln` or `.slnx` in Visual Studio, run `prepare_csharp_workspace`, and
run `get_csharp_workspace_status`. Record the Harness version and configuration
file path in the issue or release notes.
