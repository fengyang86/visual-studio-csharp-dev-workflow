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
