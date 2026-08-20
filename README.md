# Visual Studio C# Dev Workflow

A local MCP workflow for AI-assisted C# development in Visual Studio. It combines a host-neutral .NET MCP server with a Visual Studio VSIX bridge backed by Roslyn and the Visual Studio debugger.

## What it provides

- Workspace preparation for `.sln` and `.slnx` solutions.
- Compact source context, symbol navigation, references, call graphs, and impact analysis.
- Scoped diagnostics and build-failure triage designed to suppress unrelated solution noise.
- Change review, related-test discovery, and focused verification planning.
- Preview-first rename, cleanup, Code Fix, and Fix All workflows.
- Debugger context and explicit debug controls.
- MCP resources for deferred evidence, reducing repeated output and model context usage.

The current MCP schema exposes 82 tools and 15 resource templates.

## Architecture

```text
Codex / Claude Code / DeepSeekHarness
                |
          MCP stdio server
                |
        named-pipe bridge
                |
       Visual Studio VSIX
                |
 Roslyn / Debugger / Output / Error List
```

The VSIX is the only component that accesses Visual Studio in-process APIs. Client integrations share the same MCP runtime and do not duplicate C# analysis logic.

## Client support

| Client | Status |
| --- | --- |
| Codex | Supported through the packaged plugin and workflow skill |
| Claude Code | MCP configuration is provided; clean-environment smoke testing is pending |
| DeepSeekHarness | Experimental MCP configuration; version-specific smoke testing is required |

See [`integrations/`](integrations/README.md) for configuration templates.

## Build and test

Requirements:

- Windows
- Visual Studio 2022 or later with C# development tools
- .NET 8 SDK

```powershell
dotnet restore .\VisualStudio.CSharpNavigatorMcp.sln
dotnet build .\VisualStudio.CSharpNavigatorMcp.sln -c Release --no-restore
dotnet test .\tests\VisualStudio.CSharpNavigator.Server.Tests\VisualStudio.CSharpNavigator.Server.Tests.csproj -c Release --no-build --no-restore
```

Publish the host-neutral MCP runtime:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-McpServerRuntime.ps1 -Configuration Release
```

Generate a client configuration snippet without overwriting user settings:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\New-McpClientConfiguration.ps1 -ClientHost ClaudeCode -ServerExe <absolute-server-exe-path> -OutputPath .\mcp.json
```

## Safety model

Read-only task-level tools are preferred. Source mutations use preview/apply sessions with candidate identity, workspace version checks, and blockers. Debug controls and Visual Studio startup are explicit side effects. VSIX installation is never performed silently.

## Documentation

- [Agentic workflow architecture](docs/agentic-csharp-dev-workflow-architecture.md)
- [Multi-host distribution architecture](docs/github-multi-host-architecture.md)
- [Client integrations](integrations/README.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)

## License

Licensed under the [Apache License 2.0](LICENSE).
