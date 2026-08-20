# Claude Code

Use `mcp.json.template` as the MCP server entry in the Claude Code project or
user configuration location used by your installed Claude Code version. Replace
the command placeholder with the absolute path to the published runtime.

Generate an equivalent file without editing a template manually:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\New-McpClientConfiguration.ps1 -ClientHost ClaudeCode -ServerExe <absolute-server-exe-path> -OutputPath .\claude-mcp.json
```

Restart Claude Code after changing its MCP configuration. Then open a C#
solution in Visual Studio and ask Claude Code to run `prepare_csharp_workspace`.
