[CmdletBinding()]
param(
    [string]$ProjectRoot,

    [string]$PluginPath = (Join-Path $HOME "plugins\visual-studio-csharp-dev-workflow"),

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$Build,

    [switch]$NoRestore,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$PluginName = "visual-studio-csharp-dev-workflow"
$PrimarySkillName = "visual-studio-csharp-dev-workflow"
$ServerExeName = "VisualStudio.CSharpNavigator.Server.exe"
$VsixName = "VisualStudio.CSharpNavigator.Vsix.vsix"

function Write-Section {
    param([string]$Message)
    Write-Host ""
    Write-Host "== $Message =="
}

function Write-Detail {
    param([string]$Message)
    Write-Host "  $Message"
}

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return (Resolve-Path -LiteralPath $Path).ProviderPath
}

function Get-ShortHash {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.Substring(0, 12)
}

function Assert-UnderDirectory {
    param(
        [string]$ChildPath,
        [string]$ParentPath
    )

    $child = [System.IO.Path]::GetFullPath($ChildPath).TrimEnd('\')
    $parent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\')

    $isSameDirectory = $child.Equals($parent, [System.StringComparison]::OrdinalIgnoreCase)
    $isNestedDirectory = $child.StartsWith($parent + "\", [System.StringComparison]::OrdinalIgnoreCase)

    if (-not $isSameDirectory -and -not $isNestedDirectory) {
        throw "Refusing to operate outside expected directory. Child='$child'; Parent='$parent'"
    }
}

function Clear-DirectoryContent {
    param([string]$Directory)

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        New-Item -ItemType Directory -Path $Directory | Out-Null
        return
    }

    Get-ChildItem -LiteralPath $Directory -Force | Remove-Item -Recurse -Force
}

function Copy-DirectoryContent {
    param(
        [string]$SourceDirectory,
        [string]$DestinationDirectory
    )

    if (-not (Test-Path -LiteralPath $DestinationDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $DestinationDirectory | Out-Null
    }

    Get-ChildItem -LiteralPath $SourceDirectory -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $DestinationDirectory -Recurse -Force
    }
}

function Get-ServerProcesses {
    param([string]$ResolvedRuntimePath)

    $normalizedRuntimePath = $ResolvedRuntimePath.TrimEnd('\') + '\'
    return @(Get-CimInstance Win32_Process -Filter "Name='$ServerExeName'" -ErrorAction SilentlyContinue |
        Where-Object {
            $commandLine = $_.CommandLine
            -not [string]::IsNullOrWhiteSpace($commandLine) -and
            $commandLine.IndexOf($normalizedRuntimePath, [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
}

function Write-PluginManifest {
    param([string]$Path)

    $pluginVersion = "0.1.0+codex." + ([DateTime]::UtcNow.ToString("yyyyMMddHHmmss"))
    $manifest = [ordered]@{
        name = $PluginName
        version = $pluginVersion
        description = "Local Visual Studio/Roslyn C# development workflow assistance for Codex."
        author = [ordered]@{
            name = "Local Visual Studio Tools"
        }
        skills = "./skills/"
        interface = [ordered]@{
            displayName = "Visual Studio C# Dev Workflow"
            shortDescription = "Use Visual Studio, Roslyn, build triage, diagnostics, and debugger context from Codex."
            longDescription = "Adds the local visual_studio_csharp_navigator MCP server and workflow-first guidance for selecting Visual Studio instances, investigating C# issues, triaging build output, planning focused verification, reviewing changes, analyzing symbols, and using debugger context safely."
            developerName = "Local Visual Studio Tools"
            category = "Productivity"
            capabilities = @("MCP", "C# Development Workflow", "Build Triage", "Semantic C# Review", "Debugger Context")
            defaultPrompt = @(
                "Investigate the current C# build or diagnostics issue",
                "Plan focused verification for my C# changes",
                "Inspect Visual Studio debugger context"
            )
        }
        mcpServers = "./.mcp.json"
    }

    $json = $manifest | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Write-McpConfig {
    param(
        [string]$Path,
        [string]$ServerExe
    )

    $config = [ordered]@{
        mcpServers = [ordered]@{
            visual_studio_csharp_navigator = [ordered]@{
                command = $ServerExe
                args = @()
                startup_timeout_sec = 120
                env = [ordered]@{
                    VisualStudioBridge__ConnectTimeoutMilliseconds = "5000"
                    VisualStudioBridge__DiscoveryStaleAfterSeconds = "120"
                }
            }
        }
    }

    $json = $config | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Write-DefaultSkill {
    param([string]$Path)

    $content = @'
---
name: visual-studio-csharp-dev-workflow
description: "Use when Codex should use the local visual_studio_csharp_navigator MCP tools for Visual Studio C# development workflows: opening or selecting a Visual Studio C# solution, investigating build failures or diagnostics, using changed files and scoped evidence, navigating symbols/references/call graphs/impact, planning focused verification, reviewing C# changes, inspecting debugger context, or using explicit debugger controls."
---

# Visual Studio C# Dev Workflow

## Core Rules

- When the user asks to analyze, navigate, review, explain, build, verify, or debug local C# code and the current repository contains a Visual Studio solution file (`.sln` or `.slnx`), prefer this workflow and the `visual_studio_csharp_navigator` MCP tools over plain text search alone.
- If there is no active Visual Studio bridge, proactively perform the setup flow: locate the solution, launch Visual Studio, wait for discovery, then call MCP tools. Do not first ask the user to manually open Visual Studio.
- Start with `list_visual_studio_instances` to discover active VSIX bridge instances.
- For first-use checks, build failures, or suspected VSIX/MCP setup problems, prefer `check_visual_studio_csharp_navigator_health` before running deeper navigation calls.
- If more than one Visual Studio instance is active, pass `targetInstanceId`, `targetPipeName`, or `targetSolutionPath`; do not guess.
- Prefer `targetInstanceId` for repeated work in one session.
- Preserve tool diagnostics and `isPartial`; do not convert them to empty success.
- Debug Control tools change Visual Studio/debuggee state and require an explicit target.

## Plugin Identity

- The Codex plugin and skill are `visual-studio-csharp-dev-workflow`.
- Do not look for or invoke an old `visual-studio-csharp-navigator` skill; that compatibility entry point has been intentionally removed.
- The MCP server id remains `visual_studio_csharp_navigator` as an internal tool namespace compatibility detail.
- User-facing language should describe this as Visual Studio C# Dev Workflow, not a code-navigation-only plugin.

## Default Workflow

For C# development tasks, use this order unless the user asks for a narrower action:

1. Establish target: call `prepare_csharp_workspace` from the repository root or a user-provided solution directory, then use its selected solution or selected instance.
2. Check readiness: `check_visual_studio_csharp_navigator_health`.
3. Route through the task-level entry point first: edit/change tasks use `prepare_csharp_edit_task`, review tasks use `prepare_csharp_change_review`, verification tasks use `prepare_csharp_verification_run`, and runtime/debug exception triage uses `investigate_csharp_runtime_exception`.
4. Use `get_csharp_task_context` only when no task-specific entry fits, or when you need a lower-level mixed context package with custom symbol/file/project scope.
5. Inspect only what the task package identifies as primary: EvidencePacket resources, source snippets, definitions, references, callers/callees, impact, diagnostics, build issues, debugger context, and recommended next actions.
6. Run selected shell build/test commands explicitly when verification is needed, then feed failures back into `investigate_csharp_build_failure`, `analyze_csharp_build_errors`, or `start_csharp_investigation`.

Treat whole-solution diagnostics as background. Current-task evidence from changed files, build triage, requested file/project/symbol, and scoped diagnostics wins.

## Agentic Task Entry Points

- Prefer `prepare_csharp_edit_task` for feature work, bug fixes, API changes, or any task likely to edit C# files. It gathers workspace readiness, active/open documents, changed files, symbols, diagnostics, source snippets, impact hints, related tests, resource links, and recommended next actions.
- Prefer `prepare_csharp_change_review` for reviewing local changes, risky diffs, public API impact, temporary markers, missing tests, or before applying broad mutation tools. It is a review-oriented evidence package, not a build/test runner.
- Prefer `prepare_csharp_verification_run` before choosing verification commands. It returns smoke/focused/broad verification tiers and evidence links; run only the selected shell commands explicitly.
- Prefer `investigate_csharp_runtime_exception` when the user reports a crash, thrown exception, debugger break, failing debug scenario, call stack, or runtime artifact. It is read-only and should gather debugger/source/artifact context before using Debug Control tools.
- Preserve each task package's `EvidencePacket`, `ResourceLinks`, `RecommendedNextActions`, `IsPartial`, diagnostics, and residual risks. If the package reports partial evidence, narrow the scope or read the linked resource before broadening to whole-solution tools.

## Source Context And Editing

- Prefer `get_csharp_symbol_source` before editing or explaining a known symbol. Pass a `symbolKey` from `search_csharp_symbols`, `find_csharp_definitions`, investigation output, or related semantic tools; start with small `contextLines`, `maxChars`, and `maxSnippets`.
- Prefer the task-level entry points for non-trivial C# tasks. Use `get_csharp_task_context` as a lower-level fallback or custom context compressor when you need health, build triage, scoped diagnostics, primary files/symbols, impact, related tests, next actions, and bounded source snippets in one result.
- Use `batch_get_csharp_symbol_sources` when several known symbols need declaration snippets in one MCP call.
- Prefer `get_csharp_source_context` for build errors, diagnostics, debugger frames, or user-provided file positions. It returns the enclosing member/type/lambda source with bounded surrounding context.
- Use `batch_get_csharp_source_contexts` when several build diagnostics, debugger frames, or file positions need enclosing source snippets in one MCP call.
- Use these source-context tools before broad file reads. They are intended to replace many routine `Get-Content`/`rg` follow-up reads with one compact Roslyn-backed source slice.
- Read the full file only when the edit requires file-level structure, usings, neighboring members, project conventions, or when the source-context result is partial/truncated.
- Preserve `FocusSpan`, `SnippetSpan`, `IsTextTruncated`, `Reasons`, diagnostics, and `isPartial` when summarizing evidence. If a snippet is truncated, either increase limits narrowly or read the specific file range around the returned span.

## Visual Studio Document Context

- Use `get_visual_studio_active_document_context` when the user refers to the file, cursor, selection, or current tab in Visual Studio.
- Use `get_visual_studio_open_documents` to understand the small set of files the user is actively working with before broad search.
- Use `open_csharp_source_location` only when navigating Visual Studio to a source location is useful for the user or the next debugging step. It changes VS UI focus, requires an explicit target, and does not modify files.

## Rename Refactoring

- Prefer `preview_csharp_rename` for C# symbol rename requests involving properties, fields, types, methods, parameters, locals, and similar Roslyn symbols. It shows the real Roslyn rename diff without applying workspace changes.
- Use `apply_csharp_rename` only after the user explicitly approves applying the rename. It mutates the target Visual Studio workspace through Roslyn and requires `targetPipeName`, `targetInstanceId`, or `targetSolutionPath`.
- Before applying, report the preview's affected document count, total text changes, conflicts, truncation, generated-code omissions, and unsupported document changes. Do not apply when the preview is truncated or contains conflicts unless the user explicitly accepts that risk.
- `apply_csharp_rename` is not a replacement for arbitrary file edits. For non-symbol edits, continue using normal file editing and focused verification.
- Default safety blocks are intentional: conflicts, truncated previews, generated document changes, and unsupported added/removed document changes stop the apply path unless the corresponding `allow*` flag is deliberately set.

## Cleanup And Mutation Safety

- Use `preview_csharp_cleanup` for Roslyn-backed formatting, using cleanup, and simplification. Start with document or changed-files scope; use project scope only when the task clearly needs it.
- Use `apply_csharp_cleanup` only after explicit user approval. It mutates the target Visual Studio workspace and requires `targetPipeName`, `targetInstanceId`, or `targetSolutionPath`.
- Treat `WorkspaceMutationPreview` and `WorkspaceMutationApplyResult` as the common safety contract for source-writing tools. Preserve blockers, conflicts, affected document counts, text change counts, generated-code omissions, unsupported document changes, partial state, and truncation diagnostics in your summary.
- Do not apply cleanup when preview output is truncated, generated documents are omitted, unsupported document changes are present, or the selected workspace target is ambiguous unless the user explicitly accepts the specific risk with the matching allow flag.
- Do not use cleanup as a broad refactor. If the user asks for behavior changes, inspect the affected symbols and edit normally, then use focused verification.

## Code Fix And Fix All

- Use `list_csharp_code_fixes` to inspect diagnostics-driven code-fix candidates in a scoped file, project, changed-files, or diagnostic-id slice.
- `preview_csharp_code_fix`, `apply_csharp_code_fix`, `preview_csharp_fix_all`, and `apply_csharp_fix_all` use provider-backed Roslyn CodeAction / FixAllProvider preview and preview-session replay apply when the target VSIX supports it.
- Before applying, report affected documents, text changes, blockers, workspace version, candidate identity, generated-code omissions, unsupported document changes, conflicts, and truncation.
- Do not apply when preview is truncated, blockers are present, workspace version changed, candidate identity mismatches, or target selection is ambiguous unless the user explicitly accepts the specific risk.
- Solution-scope Fix All must remain explicit and deliberately allowed; never run broad cleanup/fix-all style actions by default.

## Complex Refactoring Plan

- Use `preview_csharp_refactoring_plan` for high-risk requests such as extract method, change signature, move type, broad cleanup, Code Fix, Fix All, or mixed refactors.
- Treat Rename and Cleanup recommendations from the plan as routes to `preview_csharp_rename` / `apply_csharp_rename` or `preview_csharp_cleanup` / `apply_csharp_cleanup`.
- Treat Extract Method, Change Signature, Move Type, Code Fix provider execution, and Fix All provider execution as plan-only unless a later tool version returns a real mutation preview without blockers.
- For plan-only refactors, perform normal file edits with bounded source context and focused verification; do not use Visual Studio UI automation or textual search-and-replace to simulate semantic refactoring.

## Workflow Enhancement Packs

- Prefer `investigate_csharp_build_failure` when the user reports a build failure and you have build output, a build log, or an open Visual Studio Build pane. It combines build triage, scoped diagnostics, Error List, Build pane context, source snippets, and next actions.
- Use `collect_artifact_evidence` for application-generated reports, traces, logs, JSON, XML/TRX, or text artifacts. It is read-only and should be preferred over trying to drive business UI through Visual Studio automation.
- Use `wait_for_artifact_evidence` when a runner/debug scenario is expected to create a report, trace, log, JSON, XML/TRX, or text artifact after a short delay. It only waits for local files and does not start the application.
- Use `plan_csharp_regression_scope` after meaningful C# edits or review work. It combines verification planning and semantic review into smoke, focused, and broad command tiers.
- Use `prepare_debug_session` before mutating debugger state. It gathers health, current debugger status, breakpoints, call stack, source snippets, and recommended next actions; start/continue/step/stop still require explicit Debug Control tools and explicit targets.
- Use `batch_evaluate_debug_expressions` only in break mode when several independent values from the same frame are needed. Keep `allowSideEffects=false` unless the user explicitly authorizes debugger-side effects; the tool caps requests at 20 expressions and preserves per-expression errors.
- Use `plan_csharp_debug_scenario` before multi-step debugging. It creates explicit configure/start/wait/collect/cleanup steps without changing Visual Studio state.
- Use `get_csharp_workflow_performance_snapshot` when assessing tool availability, workflow benchmark commands, large-solution performance setup, context budgets, bridge state, and telemetry hints. It does not run benchmarks.
- Use `analyze_csharp_repo_workflow` and `generate_csharp_agent_instructions` to draft repository onboarding instructions with solution selection, build/test commands, noisy paths, artifact hints, and tool routing. These tools do not write AGENTS.md or Copilot instruction files.
- Use `split_csharp_agent_work` to create bounded child-agent packets with file/symbol scope, allowed tools, evidence resources, and budgets; use `merge_csharp_agent_findings` to detect missing packets and merge child-agent findings before editing shared files.
- Treat these packs as compressed evidence and routing tools. They do not replace explicit shell build/test execution, real source edits, or user approval for mutating operations.

## Automatic C# Solution Setup

When C# semantic information is needed but no active Visual Studio bridge is available:

1. Locate the solution:
   - Prefer `prepare_csharp_workspace(rootDirectory=...)` because it discovers `.sln` and `.slnx`, checks active bridges, ranks candidates, and returns machine-readable next steps.
   - Use `find_csharp_solutions` only when you need the candidate list without bridge readiness.
   - If `prepare_csharp_workspace.status` is `NoSolutionFound`, do not launch Visual Studio; use normal file reading/search and report that no solution was found.
   - If status is `AmbiguousSolution`, use `preferredName` or a user-provided solution path fragment to retry. Ask the user only if still ambiguous.
2. Check existing bridges:
   - If `prepare_csharp_workspace.status` is `Ready`, keep `selectedInstance.instanceId` as `targetInstanceId` and continue.
   - If status is `AmbiguousBridge`, choose by `targetInstanceId` or `targetPipeName`; do not guess.
3. Launch Visual Studio:
   - If status is `VisualStudioLaunchRequired`, call `open_csharp_solution_in_visual_studio(solutionPath=selectedSolution.filePath, waitForBridge=true)`.
   - This tool may launch Visual Studio, so treat it as a state-changing action. It does not close, restart, or kill existing Visual Studio processes.
   - If `open_csharp_solution_in_visual_studio` cannot locate `devenv.exe`, pass an explicit `devenvPath` or fall back to normal shell discovery.
4. Wait for discovery:
   - If Visual Studio was already launched by another step, call `wait_for_visual_studio_bridge(solutionPath=...)`.
   - When the wait report is `Ready`, use `selectedInstance.instanceId` for subsequent MCP tools.
5. Diagnose VSIX:
   - If Visual Studio is open with the target solution but `wait_for_visual_studio_bridge` times out, treat VSIX installation/loading as the next likely issue.
   - Do not install or repair the VSIX unless the user explicitly authorizes that action.

## Install / Repair Flow

- Use install/repair only when the user explicitly asks to install, update, or repair the Visual Studio extension or plugin runtime.
- First check whether the MCP server is loaded and whether any active bridge exists:
  1. If MCP tools are unavailable, check that Codex loaded `visual-studio-csharp-dev-workflow@personal`.
  2. If MCP tools are available but no bridge exists, launch/open the target solution in Visual Studio and wait for discovery.
  3. If Visual Studio is open with the solution but discovery is absent, diagnose VSIX installation/loading.
  4. If health reports `VersionMismatch` or `BridgeProtocolMismatch`, request explicit permission to install the packaged VSIX and restart Visual Studio.
- To update the personal Codex plugin runtime from this repository after explicit user authorization, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Update-VisualStudioCSharpNavigator.ps1" -NoRestore -StopRunningMcpServers
```

- To also install/update the Visual Studio extension after explicit user authorization, add `-InstallVsix`. If the user allows closing Visual Studio, also pass `-CloseRunningVisualStudio`; use `-ForceCloseRunningVisualStudio` only when the user explicitly allows force closing.
- When installing or repairing from an already installed personal plugin, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$HOME\plugins\visual-studio-csharp-dev-workflow\scripts\Install-VisualStudioCSharpNavigator.ps1"
```

- After plugin runtime updates, tell the user to restart Codex. After VSIX updates, tell the user to restart Visual Studio or reopen the target solution.
- Never reinstall the removed `visual-studio-csharp-navigator` plugin or recreate its compatibility skill.

## Diagnostics And Build Failures

- For broad C# investigation requests, prefer `start_csharp_investigation` before manually chaining health, build triage, diagnostics, symbol search, references, callers/callees, and related tests.
- Pass `problemText`, `buildOutput`, `buildLogFilePath`, `changedFiles`, `symbolQuery`, and path filters when available. Treat the returned package as the starting context, not as a final proof.
- On large solutions or high-frequency symbols, explicitly tune `maxRelatedItems` for `start_csharp_investigation` and `maxResults` for direct reference/implementation/override tools. Use small caps first, then broaden only when needed.
- After changing C# files or triaging a build failure, call `plan_csharp_verification` to get a focused verification plan before choosing broad commands. Pass `changedFiles`, `buildOutput` or `buildLogFilePath`, `symbolQuery`, `filePath`, `projectName`, and the same include/exclude path filters used for diagnostics.
- Treat `plan_csharp_verification.RecommendedCommands` as recommended commands only. The tool does not run builds or tests, and agents must still execute the selected commands explicitly when verification is needed.
- Prefer planner output in this order: reliable `single-test` commands, affected test-project commands, affected production project build commands, then solution-level fallback commands.
- When build output is available, call `analyze_csharp_build_errors` before reading or summarizing the full log. Pass `buildLogFilePath` for compact log files, `changedFiles` from git diff when possible, and `excludePathPatterns` for known noisy legacy/generated/vendor folders.
- `start_csharp_investigation` and `plan_csharp_verification` automatically read the Visual Studio Output Window `Build` pane for build triage when both `buildOutput` and `buildLogFilePath` are omitted. Treat `VisualStudioBuildOutputUsed: ...` as supporting evidence because Output Window text can be stale; explicit build output or a log file is stronger and prevents this fallback.
- Visual Studio Build pane project prefixes such as `1>` or `2>` are normalized before build triage, so source paths and `changedFiles` matching should not include those prefixes.
- Build triage preserves full source ranges when build output uses `(startLine,startColumn,endLine,endColumn)`, which is common for analyzer diagnostics.
- Build triage also handles generic `error : ...` / `warning : ...` lines without a diagnostic code by assigning stable synthetic ids `BUILDERROR` / `BUILDWARNING`.
- Treat the returned `BuildTriageReport.Issues` order as the primary investigation order. Start from non-cascade errors in changed files, then use symbol/search/definition/reference tools on those source locations.
- Read each build issue's `Kind` and `RankReasons`. `Restore`, `TargetFramework`, and `GeneratedOutput` issues usually need package source/version, SDK/TFM compatibility, or generator/build-target investigation before chasing downstream C# compiler errors.
- `analyze_csharp_build_errors` is a log triage tool. It does not run builds and does not require Visual Studio to be open.
- `get_csharp_diagnostics` is for Roslyn compiler/analyzer diagnostics from the loaded Visual Studio workspace. It is not a replacement for `dotnet build`, MSBuild, or RTK build-log extraction.
- `get_csharp_diagnostics` ranks returned items by `RelevanceScore` before truncation. Read `ScopeReasons` to explain why an item is relevant, for example `changed file`, `requested file`, `include pattern: ...`, `project scope`, or `known noise path`.
- `noiseProfile` controls built-in known-noise handling for diagnostics. Use the default `Auto` for normal current-task work: unscoped diagnostics suppress known-noise paths such as `ACADPlugins`, `TZData_src`, `obj`, `bin`, `generated`, `vendor`, or `packages`; focused diagnostics only mark them as `known noise path` instead of hiding them. Use `Filter` to always suppress known-noise paths, `Penalize` to always return but lower-rank them, and `Off` for full audits where no built-in penalty or filter should apply.
- When `noiseProfile=Auto` or `noiseProfile=Filter` suppresses diagnostics, read and report `DiagnosticsFilteredByNoiseProfile: ...` and `DiagnosticsAutoNoiseFilter: ...` diagnostics so users know known-noise items were intentionally hidden.
- Always pass `changedFiles` when the current task has known edited files. This lets diagnostics in the current change outrank pre-existing large-solution noise.
- When the user asks about C# build failures and no build output is available, first use `start_csharp_investigation` or `plan_csharp_verification` to harvest the VS Build Output fallback if Visual Studio is open. If that output is missing, stale, or inconclusive, run a compact build command and feed the captured output into `analyze_csharp_build_errors`.
- Use `check_visual_studio_csharp_navigator_health(includeDiagnosticsPreview=true, ...)` when you need a quick readiness check plus a small scoped diagnostics preview.
- Read `get_csharp_workspace_status` before build/test planning when Visual Studio context matters. Use `activeConfigurationName`, `activePlatformName`, `startupProjects`, and `projects[].targetFrameworks` to avoid guessing the active VS context. `projects` is a compressed summary and may be partial; check `isProjectListPartial`.
- In health results, inspect `expectedBridgeProtocolVersion`, each instance's `bridgeProtocolVersion`, `extensionAssemblyVersion`, and `extensionFileVersion`. If health returns `VersionMismatch` or `BridgeProtocolMismatch`, install the matching packaged VSIX and restart Visual Studio before trusting semantic/debug results.
- Use `get_visual_studio_error_list` as a fast read-only snapshot of the current Visual Studio Error List UI when you need VS-side context. Treat it as supporting evidence only: build output and scoped Roslyn diagnostics remain the primary proof for compiler/analyzer failures.
- Use `get_visual_studio_output_window(paneName="Build")` when you need to inspect the raw Build pane yourself. It returns only the tail of the requested Output Window pane; the investigation and verification planner tools already use this pane automatically as a fallback.
- `ServerCacheHit: ...` diagnostics mean the MCP server reused a bounded, short-lived read-only result for performance. Treat it as normal metadata, not as a VSIX failure; rerun with a different target/scope or after a few seconds if you need a fresh Roslyn read.
- Large solutions may contain existing unrelated diagnostics. Do not treat whole-solution diagnostics as proof that the current change failed.
- Prefer scoped diagnostics in this order:
  1. `filePath` for a single file when the build error names one.
  2. `includePathPatterns` for changed files, touched folders, or the feature area under investigation.
  3. `projectName` for touched projects.
  4. Whole-solution diagnostics only as a background scan.
- Use `excludePathPatterns` to suppress known noisy folders such as generated, vendored, legacy, or host-specific plugin projects. Example: exclude `src\ACADPlugins`, `TZData_src`, `**\obj\**`, or other known unrelated folders.
- Prefer explicit `excludePathPatterns` for project-specific noise. Keep `noiseProfile=Auto` as the fast default for broad diagnostics in known noisy solutions, and switch to `noiseProfile=Off` when auditing every diagnostic matters more than precision.
- If whole-solution diagnostics and scoped diagnostics disagree, report both clearly: scoped diagnostics are the primary signal for the current task; whole-solution diagnostics may be pre-existing noise.
- After `search_csharp_symbols`, prefer passing the returned `symbolKey` to references/callers/callees/impact tools. Avoid line/column lookup when a symbol key is already available.
- When the user only provides a symbol name and asks for references, prefer `find_csharp_references_by_symbol_search` with `containingType`, `projectName`, or `kind` filters when available. If it reports `AmbiguousSymbolSearch`, show the candidates and ask for or infer a narrower filter from project context before retrying.

- If the MCP server is unavailable, check that Codex loaded this plugin and that `.mcp.json` points to `runtimes\mcp-server\VisualStudio.CSharpNavigator.Server.exe`.
- If the MCP server is available but no active bridge is listed, distinguish Visual Studio not open, no C# solution loaded, VSIX not installed/loaded, and stale discovery records.
- Do not silently install or update the VSIX. Installing a VSIX changes the user's Visual Studio environment.
- For repository maintainers updating this tool, prefer the unified update script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Update-VisualStudioCSharpNavigator.ps1" -StagingOnly -NoRestore
```

- To update the personal Codex plugin and install the VSIX after explicit user authorization, run the same script without `-StagingOnly` and add `-InstallVsix`. If the plugin runtime is in use, the script will fail safely instead of overwriting active MCP files.
- When the user explicitly asks to install or repair the Visual Studio extension, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$HOME\plugins\visual-studio-csharp-dev-workflow\scripts\Install-VisualStudioCSharpNavigator.ps1"
```

- After VSIX installation, ask the user to restart or open Visual Studio with the target C# solution, then retry `list_visual_studio_instances`.
'@

    [System.IO.File]::WriteAllText($Path, $content + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

$ProjectRoot = Resolve-FullPath $ProjectRoot
$PluginPath = [System.IO.Path]::GetFullPath($PluginPath)
$pluginParent = Split-Path -Parent $PluginPath
if (-not (Test-Path -LiteralPath $pluginParent -PathType Container)) {
    New-Item -ItemType Directory -Path $pluginParent | Out-Null
}
if (-not (Test-Path -LiteralPath $PluginPath -PathType Container)) {
    New-Item -ItemType Directory -Path $PluginPath | Out-Null
}
$PluginPath = Resolve-FullPath $PluginPath

$solutionFile = Join-Path $ProjectRoot "VisualStudio.CSharpNavigatorMcp.sln"
$publishScript = Join-Path $ProjectRoot "scripts\Publish-McpServerRuntime.ps1"
$installScript = Join-Path $ProjectRoot "scripts\Install-VisualStudioCSharpNavigator.ps1"
$sourceRuntime = Join-Path $ProjectRoot "artifacts\mcp-server-runtime"
$sourceVsix = Join-Path $ProjectRoot "src\VisualStudio.CSharpNavigator.Vsix\bin\$Configuration\net472\$VsixName"

$pluginRuntime = Join-Path $PluginPath "runtimes\mcp-server"
$pluginVsixDir = Join-Path $PluginPath "vsix"
$pluginScriptsDir = Join-Path $PluginPath "scripts"
$pluginManifestDir = Join-Path $PluginPath ".codex-plugin"
$pluginSkillsRoot = Join-Path $PluginPath "skills"
$primarySkillDir = Join-Path $pluginSkillsRoot $PrimarySkillName
$pluginServerExe = Join-Path $pluginRuntime $ServerExeName
$pluginVsix = Join-Path $pluginVsixDir $VsixName

if (-not (Test-Path -LiteralPath $solutionFile -PathType Leaf)) {
    throw "Solution file not found: $solutionFile"
}

Write-Section "Inputs"
Write-Detail "ProjectRoot: $ProjectRoot"
Write-Detail "PluginPath: $PluginPath"
Write-Detail "Configuration: $Configuration"

if ($Build) {
    Write-Section "Build"
    $buildArgs = @("build", $solutionFile, "-c", $Configuration, "-v:minimal")
    if ($NoRestore) {
        $buildArgs += "--no-restore"
    }

    Write-Detail "dotnet $($buildArgs -join ' ')"
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }

    Write-Section "Publish MCP runtime"
    $publishArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $publishScript, "-Configuration", "Release", "-OutputPath", $sourceRuntime, "-Force")
    if ($NoRestore) {
        $publishArgs += "-NoRestore"
    }

    Write-Detail "powershell $($publishArgs -join ' ')"
    & powershell @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Publish-McpServerRuntime.ps1 failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $sourceRuntime $ServerExeName) -PathType Leaf)) {
    throw "MCP runtime was not found. Run scripts\Publish-McpServerRuntime.ps1 or pass -Build."
}
if (-not (Test-Path -LiteralPath $sourceVsix -PathType Leaf)) {
    throw "VSIX package was not found: $sourceVsix. Build the VSIX or pass -Build."
}
if (-not (Test-Path -LiteralPath $installScript -PathType Leaf)) {
    throw "Install script was not found: $installScript"
}

$runningFromPluginRuntime = @(Get-ServerProcesses $pluginRuntime)
if ($runningFromPluginRuntime.Count -gt 0 -and -not $Force) {
    $processIds = ($runningFromPluginRuntime | ForEach-Object { $_.ProcessId }) -join ", "
    throw "Refusing to overwrite plugin runtime while MCP server is running from it (processId: $processIds). Stop Codex or pass -Force after confirming it is safe."
}

Write-Section "Create plugin structure"
foreach ($directory in @($pluginManifestDir, $pluginSkillsRoot, $pluginRuntime, $pluginVsixDir, $pluginScriptsDir)) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }
}

Assert-UnderDirectory -ChildPath $pluginRuntime -ParentPath $PluginPath
Assert-UnderDirectory -ChildPath $pluginVsixDir -ParentPath $PluginPath
Assert-UnderDirectory -ChildPath $pluginScriptsDir -ParentPath $PluginPath
Assert-UnderDirectory -ChildPath $pluginSkillsRoot -ParentPath $PluginPath

Write-Section "Copy runtime"
Clear-DirectoryContent $pluginRuntime
Copy-DirectoryContent -SourceDirectory $sourceRuntime -DestinationDirectory $pluginRuntime
if (-not (Test-Path -LiteralPath $pluginServerExe -PathType Leaf)) {
    throw "Plugin runtime copy did not produce server executable: $pluginServerExe"
}

Write-Section "Copy VSIX and scripts"
Clear-DirectoryContent $pluginVsixDir
Copy-Item -LiteralPath $sourceVsix -Destination $pluginVsix -Force
Copy-Item -LiteralPath $installScript -Destination (Join-Path $pluginScriptsDir "Install-VisualStudioCSharpNavigator.ps1") -Force

Write-Section "Write plugin config"
Write-PluginManifest -Path (Join-Path $pluginManifestDir "plugin.json")
Write-McpConfig -Path (Join-Path $PluginPath ".mcp.json") -ServerExe $pluginServerExe
Clear-DirectoryContent $pluginSkillsRoot
New-Item -ItemType Directory -Path $primarySkillDir -Force | Out-Null
Write-DefaultSkill -Path (Join-Path $primarySkillDir "SKILL.md")

Write-Section "Packaged plugin"
Write-Detail "Plugin: $PluginPath"
Write-Detail "MCP server: $pluginServerExe"
Write-Detail "MCP server hash: $(Get-ShortHash $pluginServerExe)"
Write-Detail "VSIX: $pluginVsix"
Write-Detail "VSIX hash: $(Get-ShortHash $pluginVsix)"
Write-Detail "Installer: $(Join-Path $pluginScriptsDir 'Install-VisualStudioCSharpNavigator.ps1')"
