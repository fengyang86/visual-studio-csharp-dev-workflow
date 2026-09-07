[CmdletBinding()]
param(
    [string]$ProjectRoot,

    [string]$DistributionRoot,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "0.1.0",

    [switch]$NoRestore,

    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$PluginName = "visual-studio-csharp-dev-workflow"
$PackagedMcpServerCommand = "__VISUAL_STUDIO_CSHARP_NAVIGATOR_INSTALLER_REWRITES_THIS_COMMAND__"

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
    param(
        [string]$Directory,
        [string]$ExpectedParent
    )

    $fullDirectory = [System.IO.Path]::GetFullPath($Directory)
    $fullParent = [System.IO.Path]::GetFullPath($ExpectedParent)
    Assert-UnderDirectory -ChildPath $fullDirectory -ParentPath $fullParent

    if (-not (Test-Path -LiteralPath $fullDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $fullDirectory | Out-Null
        return
    }

    Get-ChildItem -LiteralPath $fullDirectory -Force | Remove-Item -Recurse -Force
}

function Write-Installer {
    param([string]$Path)

    $content = @'
[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $HOME "plugins"),
    [switch]$SkipCodexInstall,
    [switch]$InstallVsix,
    [switch]$UninstallVsixFirst,
    [switch]$ForcePluginRuntimeOverwrite,
    [switch]$StopRunningMcpServers,
    [switch]$ForceStopRunningMcpServers,
    [switch]$CloseRunningVisualStudio,
    [switch]$ForceCloseRunningVisualStudio,
    [int]$CloseVisualStudioTimeoutSeconds = 60,
    [string]$DevenvPath,
    [string]$RootSuffix,
    [string]$MarketplacePath = (Join-Path $HOME ".agents\plugins\marketplace.json")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$pluginName = "visual-studio-csharp-dev-workflow"
$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePlugin = Join-Path $packageRoot "plugins\$pluginName"
$targetPlugin = Join-Path $InstallRoot $pluginName
$marketplacePath = $MarketplacePath
$serverExe = Join-Path $targetPlugin "runtimes\mcp-server\VisualStudio.CSharpNavigator.Server.exe"

function Get-RunningPluginServerProcesses {
    param([string]$TargetPlugin)

    if (-not (Test-Path -LiteralPath $TargetPlugin -PathType Container)) {
        return @()
    }

    $targetRoot = [System.IO.Path]::GetFullPath($TargetPlugin).TrimEnd('\')
    $serverProcesses = New-Object System.Collections.ArrayList
    foreach ($process in Get-CimInstance Win32_Process -Filter "name = 'VisualStudio.CSharpNavigator.Server.exe'" -ErrorAction SilentlyContinue) {
        if ([string]::IsNullOrWhiteSpace($process.ExecutablePath)) {
            continue
        }

        $exe = [System.IO.Path]::GetFullPath($process.ExecutablePath)
        if ($exe.StartsWith($targetRoot + "\", [System.StringComparison]::OrdinalIgnoreCase)) {
            [void]$serverProcesses.Add($process)
        }
    }

    return @($serverProcesses)
}

function Stop-RunningPluginServerProcesses {
    param(
        [string]$TargetPlugin,
        [bool]$Force
    )

    $runningServers = @(Get-RunningPluginServerProcesses -TargetPlugin $TargetPlugin)
    if ($runningServers.Count -eq 0) {
        Write-Host "No running MCP server processes found under plugin path."
        return
    }

    $ids = @($runningServers | ForEach-Object { [int]$_.ProcessId })
    Write-Host "Stopping MCP server processId: $($ids -join ', ')"
    foreach ($id in $ids) {
        if ($Force) {
            Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        }
        else {
            Stop-Process -Id $id -ErrorAction SilentlyContinue
        }
    }

    foreach ($id in $ids) {
        try {
            Wait-Process -Id $id -Timeout 10 -ErrorAction Stop
        }
        catch {
            if (Get-Process -Id $id -ErrorAction SilentlyContinue) {
                throw "MCP server process did not exit within timeout: $id"
            }
        }
    }
}

if (-not (Test-Path -LiteralPath $sourcePlugin -PathType Container)) {
    throw "Package plugin source not found: $sourcePlugin"
}

if ($StopRunningMcpServers) {
    Stop-RunningPluginServerProcesses -TargetPlugin $targetPlugin -Force:$ForceStopRunningMcpServers
}

$runningServers = @(Get-RunningPluginServerProcesses -TargetPlugin $targetPlugin)
if ($runningServers.Count -gt 0 -and -not $ForcePluginRuntimeOverwrite) {
    $ids = ($runningServers | ForEach-Object { $_.ProcessId }) -join ", "
    throw "Refusing to overwrite plugin runtime while MCP server is running from it (processId: $ids). Restart Codex, then rerun this installer."
}

if (-not (Test-Path -LiteralPath $InstallRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
}

if (Test-Path -LiteralPath $targetPlugin -PathType Container) {
    Remove-Item -LiteralPath $targetPlugin -Recurse -Force
}
Copy-Item -LiteralPath $sourcePlugin -Destination $targetPlugin -Recurse -Force

if (-not (Test-Path -LiteralPath $serverExe -PathType Leaf)) {
    throw "MCP server executable not found after copy: $serverExe"
}

$mcpPath = Join-Path $targetPlugin ".mcp.json"
$mcp = Get-Content -Raw -Encoding UTF8 -LiteralPath $mcpPath | ConvertFrom-Json
$mcp.mcpServers.visual_studio_csharp_navigator.command = $serverExe
$mcpJson = $mcp | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($mcpPath, $mcpJson + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

$marketplaceDir = Split-Path -Parent $marketplacePath
if (-not (Test-Path -LiteralPath $marketplaceDir -PathType Container)) {
    New-Item -ItemType Directory -Path $marketplaceDir -Force | Out-Null
}

if (Test-Path -LiteralPath $marketplacePath -PathType Leaf) {
    $marketplace = Get-Content -Raw -Encoding UTF8 -LiteralPath $marketplacePath | ConvertFrom-Json
    if (-not $marketplace.plugins) {
        $marketplace | Add-Member -NotePropertyName plugins -NotePropertyValue @()
    }
    $remaining = @($marketplace.plugins | Where-Object { $_.name -ne $pluginName })
    $entry = [pscustomobject]@{
        name = $pluginName
        source = [pscustomobject]@{ source = "local"; path = "./plugins/$pluginName" }
        policy = [pscustomobject]@{ installation = "AVAILABLE"; authentication = "ON_INSTALL" }
        category = "Productivity"
    }
    $marketplace.plugins = @($remaining + $entry)
}
else {
    $marketplace = [pscustomobject]@{
        name = "personal"
        interface = [pscustomobject]@{ displayName = "Personal" }
        plugins = @([pscustomobject]@{
            name = $pluginName
            source = [pscustomobject]@{ source = "local"; path = "./plugins/$pluginName" }
            policy = [pscustomobject]@{ installation = "AVAILABLE"; authentication = "ON_INSTALL" }
            category = "Productivity"
        })
    }
}
$marketplace | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $marketplacePath -Encoding UTF8

Write-Host "Installed plugin source: $targetPlugin"
Write-Host "Updated marketplace: $marketplacePath"
Write-Host "MCP server: $serverExe"

if (-not $SkipCodexInstall) {
    $codex = Get-Command codex -ErrorAction SilentlyContinue
    if ($null -eq $codex) {
        Write-Host "Codex CLI was not found. After installing Codex, run: codex plugin add $pluginName@personal"
    }
    else {
        & codex plugin add "$pluginName@personal"
        if ($LASTEXITCODE -ne 0) {
            throw "codex plugin add failed with exit code $LASTEXITCODE."
        }
    }
}

if ($InstallVsix) {
    $installVsixScript = Join-Path $targetPlugin "scripts\Install-VisualStudioCSharpNavigator.ps1"
    $vsixPath = Join-Path $targetPlugin "vsix\VisualStudio.CSharpNavigator.Vsix.vsix"
    $installVsixArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $installVsixScript, "-VsixPath", $vsixPath)
    if ($UninstallVsixFirst) {
        $installVsixArgs += "-UninstallFirst"
    }
    if ($CloseRunningVisualStudio) {
        $installVsixArgs += "-CloseRunningVisualStudio"
    }
    if ($ForceCloseRunningVisualStudio) {
        $installVsixArgs += "-ForceCloseRunningVisualStudio"
    }
    if ($CloseVisualStudioTimeoutSeconds -ne 60) {
        $installVsixArgs += @("-CloseVisualStudioTimeoutSeconds", $CloseVisualStudioTimeoutSeconds)
    }
    if (-not [string]::IsNullOrWhiteSpace($DevenvPath)) {
        $installVsixArgs += @("-DevenvPath", $DevenvPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($RootSuffix)) {
        $installVsixArgs += @("-RootSuffix", $RootSuffix)
    }
    & powershell @installVsixArgs
    if ($LASTEXITCODE -ne 0) {
        throw "VSIX install failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Host "VSIX is included but not silently installed. Rerun with -InstallVsix to update Visual Studio."
}

Write-Host "Next: restart Codex or start a new thread. Restart Visual Studio after VSIX installation."
'@

    [System.IO.File]::WriteAllText($Path, $content + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Write-DshInstaller {
    param([string]$Path)

    $content = @'
[CmdletBinding()]
param(
    [string]$RuntimeDestination = (Join-Path $env:LOCALAPPDATA "VisualStudio.CSharpNavigatorMcp\mcp-server"),
    [string]$DshHome,
    [string]$Profile,
    [switch]$Remove,
    [switch]$StopRunningMcpServers,
    [switch]$ForceStopRunningMcpServers,
    [switch]$ForceRuntimeOverwrite,
    [switch]$InstallVsix,
    [switch]$UninstallVsixFirst,
    [switch]$CloseRunningVisualStudio,
    [switch]$ForceCloseRunningVisualStudio,
    [int]$CloseVisualStudioTimeoutSeconds = 60,
    [string]$DevenvPath,
    [string]$RootSuffix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$pluginName = "visual-studio-csharp-dev-workflow"
$serverExeName = "VisualStudio.CSharpNavigator.Server.exe"
$entryId = "mcp-visual-studio-csharp-navigator"
$serverName = "visual_studio_csharp_navigator"
$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRuntime = Join-Path $packageRoot "plugins\$pluginName\runtimes\mcp-server"

function Get-RunningServerProcesses {
    param([string]$TargetDirectory)

    if ([string]::IsNullOrWhiteSpace($TargetDirectory) -or -not (Test-Path -LiteralPath $TargetDirectory -PathType Container)) {
        return @()
    }

    $targetRoot = [System.IO.Path]::GetFullPath($TargetDirectory).TrimEnd('\')
    $serverProcesses = New-Object System.Collections.ArrayList
    foreach ($process in Get-CimInstance Win32_Process -Filter "name = '$serverExeName'" -ErrorAction SilentlyContinue) {
        if ([string]::IsNullOrWhiteSpace($process.ExecutablePath)) {
            continue
        }

        $exe = [System.IO.Path]::GetFullPath($process.ExecutablePath)
        if ($exe.StartsWith($targetRoot + "\", [System.StringComparison]::OrdinalIgnoreCase)) {
            [void]$serverProcesses.Add($process)
        }
    }

    return @($serverProcesses)
}

function Stop-RunningServerProcesses {
    param([bool]$Force)

    $runningServers = @(Get-RunningServerProcesses -TargetDirectory $RuntimeDestination)
    if ($runningServers.Count -eq 0) {
        Write-Host "No running MCP server processes found under $RuntimeDestination."
        return
    }

    $ids = @($runningServers | ForEach-Object { [int]$_.ProcessId })
    Write-Host "Stopping MCP server processId: $($ids -join ', ')"
    foreach ($id in $ids) {
        if ($Force) {
            Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        }
        else {
            Stop-Process -Id $id -ErrorAction SilentlyContinue
        }
    }

    foreach ($id in $ids) {
        try {
            Wait-Process -Id $id -Timeout 10 -ErrorAction Stop
        }
        catch {
            if (Get-Process -Id $id -ErrorAction SilentlyContinue) {
                throw "MCP server process did not exit within timeout: $id"
            }
        }
    }
}

function Update-CordisPatch {
    param(
        [string]$PatchPath,
        [string]$EntryText,
        [switch]$RemoveEntry
    )

    $lines = @()
    if (Test-Path -LiteralPath $PatchPath -PathType Leaf) {
        $lines = @([System.IO.File]::ReadAllText($PatchPath) -split "\r?\n" | Where-Object { -not [string]::IsNullOrEmpty($_) })
    }

    $firstItemIndex = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^- ') {
            $firstItemIndex = $index
            break
        }
    }

    $preamble = @()
    $itemLines = @()
    if ($firstItemIndex -ge 0) {
        $preamble = @($lines[0..($firstItemIndex - 1)])
        $itemLines = @($lines[$firstItemIndex..($lines.Count - 1)])
    }
    else {
        $preamble = @($lines)
        foreach ($line in $preamble) {
            $trimmed = $line.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
                continue
            }

            if ($trimmed -ne "[]") {
                throw "Unrecognized cordis.patch.yml content (expected a list or []): $line"
            }
        }
    }

    $items = New-Object System.Collections.ArrayList
    $current = $null
    foreach ($line in $itemLines) {
        if ($line -match '^- ') {
            if ($null -ne $current) {
                [void]$items.Add($current)
            }

            $current = New-Object System.Collections.ArrayList
            [void]$current.Add($line)
        }
        elseif ($null -ne $current) {
            [void]$current.Add($line)
        }
        else {
            throw "Unrecognized cordis.patch.yml line before the first list item: $line"
        }
    }

    if ($null -ne $current) {
        [void]$items.Add($current)
    }

    $keptItems = New-Object System.Collections.ArrayList
    $replaced = $false
    foreach ($item in $items) {
        $isManaged = $false
        foreach ($itemLine in $item) {
            if ($itemLine -match ("id:\s*" + [regex]::Escape($entryId) + "\s*$")) {
                $isManaged = $true
                break
            }
        }

        if (-not $isManaged) {
            [void]$keptItems.Add($item)
            continue
        }

        if ($RemoveEntry) {
            Write-Host "Removing existing patch entry: $entryId"
            continue
        }

        [void]$keptItems.Add(($EntryText -split "\r?\n" | Where-Object { -not [string]::IsNullOrEmpty($_) }))
        $replaced = $true
    }

    if (-not $RemoveEntry -and -not $replaced) {
        [void]$keptItems.Add(($EntryText -split "\r?\n" | Where-Object { -not [string]::IsNullOrEmpty($_) }))
    }

    if (Test-Path -LiteralPath $PatchPath -PathType Leaf) {
        $backupPath = "$PatchPath.bak-" + (Get-Date -Format "yyyyMMddHHmmss")
        Copy-Item -LiteralPath $PatchPath -Destination $backupPath -Force
        Write-Host "Backup created: $backupPath"
    }

    $outputLines = New-Object System.Collections.ArrayList
    foreach ($line in $preamble) {
        [void]$outputLines.Add($line)
    }

    if ($keptItems.Count -eq 0) {
        [void]$outputLines.Add("[]")
    }
    else {
        foreach ($item in $keptItems) {
            foreach ($line in $item) {
                [void]$outputLines.Add($line)
            }
        }
    }

    $parent = Split-Path -Parent $PatchPath
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    [System.IO.File]::WriteAllText($PatchPath, ($outputLines -join "`r`n") + "`r`n", [System.Text.UTF8Encoding]::new($false))
    if ($RemoveEntry) {
        Write-Host "Patch entry removed: $PatchPath"
    }
    else {
        Write-Host "Patch entry installed: $PatchPath"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $sourceRuntime $serverExeName) -PathType Leaf)) {
    throw "Packaged MCP runtime not found: $sourceRuntime"
}

if ([string]::IsNullOrWhiteSpace($DshHome)) {
    $dshSharpHome = Join-Path $env:APPDATA "DSHSharp\dsh-home"
    if (Test-Path -LiteralPath $dshSharpHome -PathType Container) {
        $DshHome = $dshSharpHome
    }
    else {
        $DshHome = Join-Path $HOME ".dsh"
    }
}

if (-not (Test-Path -LiteralPath $DshHome -PathType Container)) {
    throw "DeepSeekHarness home not found: $DshHome. Pass -DshHome with your DSH home directory."
}

if (-not [string]::IsNullOrWhiteSpace($Profile)) {
    $patchPath = Join-Path $DshHome "profiles\$Profile\cordis.patch.yml"
}
else {
    $patchPath = Join-Path $DshHome "cordis.patch.yml"
}

if ($Remove) {
    if (-not (Test-Path -LiteralPath $patchPath -PathType Leaf)) {
        Write-Host "Nothing to remove: $patchPath does not exist."
    }
    else {
        Update-CordisPatch -PatchPath $patchPath -EntryText "" -RemoveEntry
    }

    Write-Host "The copied MCP runtime under $RuntimeDestination is left in place; remove it manually if no other host uses it."
    exit 0
}

if ($StopRunningMcpServers) {
    Stop-RunningServerProcesses -Force:$ForceStopRunningMcpServers
}

$runningServers = @(Get-RunningServerProcesses -TargetDirectory $RuntimeDestination)
if ($runningServers.Count -gt 0 -and -not $ForceRuntimeOverwrite) {
    $ids = ($runningServers | ForEach-Object { $_.ProcessId }) -join ", "
    throw "Refusing to overwrite the MCP runtime while its server is running (processId: $ids). Restart DeepSeekHarness, or pass -StopRunningMcpServers."
}

if (-not (Test-Path -LiteralPath $RuntimeDestination -PathType Container)) {
    New-Item -ItemType Directory -Path $RuntimeDestination -Force | Out-Null
}

Get-ChildItem -LiteralPath $RuntimeDestination -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $sourceRuntime "*") -Destination $RuntimeDestination -Recurse -Force

$serverExe = Join-Path $RuntimeDestination $serverExeName
if (-not (Test-Path -LiteralPath $serverExe -PathType Leaf)) {
    throw "MCP server executable not found after copy: $serverExe"
}

$entryText = @"
- insert:
    - id: $entryId
      name: '@deepseek-ai/dsh-mcp-client'
      config:
        serverName: $serverName
        transport: stdio
        command: '$serverExe'
        args: []
        env:
          VisualStudioBridge__ConnectTimeoutMilliseconds: '5000'
          VisualStudioBridge__DiscoveryStaleAfterSeconds: '120'
"@

Update-CordisPatch -PatchPath $patchPath -EntryText $entryText

Write-Host "MCP server: $serverExe"
Write-Host "DSH home: $DshHome"
Write-Host "Cordis patch: $patchPath"
Write-Host "Tools surface as mcp__${serverName}__<toolName>."

if ($InstallVsix) {
    $installVsixScript = Join-Path $packageRoot "plugins\$pluginName\scripts\Install-VisualStudioCSharpNavigator.ps1"
    $vsixPath = Join-Path $packageRoot "plugins\$pluginName\vsix\VisualStudio.CSharpNavigator.Vsix.vsix"
    $vsixArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $installVsixScript, "-VsixPath", $vsixPath)
    if ($UninstallVsixFirst) {
        $vsixArgs += "-UninstallFirst"
    }
    if ($CloseRunningVisualStudio) {
        $vsixArgs += "-CloseRunningVisualStudio"
    }
    if ($ForceCloseRunningVisualStudio) {
        $vsixArgs += "-ForceCloseRunningVisualStudio"
    }
    if ($CloseVisualStudioTimeoutSeconds -ne 60) {
        $vsixArgs += @("-CloseVisualStudioTimeoutSeconds", $CloseVisualStudioTimeoutSeconds)
    }
    if (-not [string]::IsNullOrWhiteSpace($DevenvPath)) {
        $vsixArgs += @("-DevenvPath", $DevenvPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($RootSuffix)) {
        $vsixArgs += @("-RootSuffix", $RootSuffix)
    }
    & powershell @vsixArgs
    if ($LASTEXITCODE -ne 0) {
        throw "VSIX install failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Host "VSIX is included but not silently installed. Rerun with -InstallVsix to update Visual Studio."
}

Write-Host "Next: restart DeepSeekHarness (or rely on the patch hot-reload), open a C# solution in Visual Studio, then ask DSH to run a workspace-status smoke test."
'@

    [System.IO.File]::WriteAllText($Path, $content + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Write-Readme {
    param(
        [string]$Path,
        [string]$Version
    )

    $content = @"
# Visual Studio C# Dev Workflow Distribution

Version: $Version

This package installs the Visual Studio C# Dev Workflow MCP server for local
AI clients. It carries one shared MCP runtime, one Visual Studio VSIX, and two
host installers:

- `Install-CodexPlugin.ps1` installs the Codex personal plugin.
- `Install-DshMcpServer.ps1` registers the MCP server for DeepSeekHarness.

Both hosts share the same `visual_studio_csharp_navigator` MCP server and the
same VSIX bridge.

## Codex Install Or Update

1. Extract this package.
2. Close Codex if you are updating an existing installation.
3. In PowerShell, run:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-CodexPlugin.ps1
~~~

4. Restart Codex or start a new Codex thread.
5. Ask Codex to use Visual Studio C# Dev Workflow.

If the installer reports that the MCP runtime is in use, restart Codex and
rerun the command. To stop only this plugin's running MCP server processes
before updating the plugin runtime:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-CodexPlugin.ps1 -StopRunningMcpServers
~~~

Use `-ForceStopRunningMcpServers` only when the normal stop does not exit
within the timeout.

## DeepSeekHarness Install Or Update

DeepSeekHarness registers stdio MCP servers through a Cordis patch layer
(`cordis.patch.yml`), not an `mcpServers` JSON file. The installer copies the
MCP runtime to a stable directory and merges the managed patch entry into your
DSH home:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-DshMcpServer.ps1
~~~

- The DSH home is auto-detected (`%APPDATA%\DSHSharp\dsh-home`, falling back to
  `~/.dsh`); pass `-DshHome <path>` to override.
- By default the entry is written to `<DSH_HOME>\cordis.patch.yml` (all
  profiles). Pass `-Profile <name>` for one profile only.
- The previous patch file is backed up next to it before every change.
- Re-running the installer replaces the managed entry id
  `mcp-visual-studio-csharp-navigator` without touching other entries.
- Remove the registration with `-Remove`.
- If the MCP runtime is in use, restart DeepSeekHarness or pass
  `-StopRunningMcpServers`.

Tools surface to the model as `mcp__visual_studio_csharp_navigator__<toolName>`.

## Codex-Assisted Install

If you already have Codex installed, you can ask Codex to help from the extracted package directory:

~~~text
Install this Visual Studio C# Dev Workflow package for Codex.
Use .\Install-CodexPlugin.ps1.
Do not install or update the VSIX unless I explicitly approve it.
After installation, tell me whether I need to restart Codex or Visual Studio.
~~~

After restarting Codex, ask:

~~~text
Use Visual Studio C# Dev Workflow. If no C# solution is open in Visual Studio, open my solution in Visual Studio, wait for the bridge, then run a workspace-status and symbol-search smoke test.
~~~

Codex should distinguish these cases when setup is not ready: Visual Studio is not open, no C# solution is loaded, the VSIX is missing or not loaded, discovery is stale, or the MCP server is not loaded.

## Install Or Update The VSIX

The package includes the Visual Studio extension, but it is not installed silently. To install/update the VSIX at the same time:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-CodexPlugin.ps1 -InstallVsix
~~~

or, for a DeepSeekHarness-only machine:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-DshMcpServer.ps1 -InstallVsix
~~~

If Visual Studio is running and you want the installer to close it explicitly, add `-CloseRunningVisualStudio`.

Restart Visual Studio after VSIX installation.

## What The Installers Change

- `Install-CodexPlugin.ps1`:
  - Copies the plugin to `$HOME\plugins\visual-studio-csharp-dev-workflow`.
  - Updates `$HOME\.agents\plugins\marketplace.json`.
  - Rewrites the installed plugin `.mcp.json` so the MCP server command points to the installed runtime path.
  - Optionally runs `codex plugin add visual-studio-csharp-dev-workflow@personal`.
  - Installs or updates the Visual Studio VSIX only when `-InstallVsix` is passed.
  - Stops running MCP server processes only when `-StopRunningMcpServers` is passed.
- `Install-DshMcpServer.ps1`:
  - Copies the MCP runtime to `%LOCALAPPDATA%\VisualStudio.CSharpNavigatorMcp\mcp-server`.
  - Merges the managed Cordis patch entry into the chosen `cordis.patch.yml` with a timestamped backup.
  - Never edits any other patch entry and never touches Codex configuration.
  - Installs the VSIX only when `-InstallVsix` is passed.

Advanced/test installs can pass `-MarketplacePath` (Codex) or `-DshHome` / `-Profile` / `-RuntimeDestination` (DSH).
"@

    [System.IO.File]::WriteAllText($Path, $content + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Write-Marketplace {
    param([string]$Path)

$marketplace = [ordered]@{
        name = "visual-studio-csharp-dev-workflow-local"
        interface = [ordered]@{
            displayName = "Visual Studio C# Dev Workflow Local"
        }
        plugins = @(
            [ordered]@{
                name = $PluginName
                source = [ordered]@{
                    source = "local"
                    path = "./plugins/$PluginName"
                }
                policy = [ordered]@{
                    installation = "AVAILABLE"
                    authentication = "ON_INSTALL"
                }
                category = "Productivity"
            }
        )
    }

    $json = $marketplace | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Assert-NoDevelopmentPathLeak {
    param(
        [string]$FilePath,
        [string[]]$ForbiddenPaths
    )

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "Distribution file not found: $FilePath"
    }

    $raw = Get-Content -Raw -Encoding UTF8 -LiteralPath $FilePath
    foreach ($forbiddenPath in $ForbiddenPaths) {
        if (-not [string]::IsNullOrWhiteSpace($forbiddenPath) -and
            $raw.IndexOf($forbiddenPath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Distribution file leaks a development path ($forbiddenPath): $FilePath"
        }
    }
}

function Set-PackagedMcpConfig {
    param([string]$PluginPath)

    $mcpPath = Join-Path $PluginPath ".mcp.json"
    if (-not (Test-Path -LiteralPath $mcpPath -PathType Leaf)) {
        throw "Packaged MCP config not found: $mcpPath"
    }

    $mcp = Get-Content -Raw -Encoding UTF8 -LiteralPath $mcpPath | ConvertFrom-Json
    $mcp.mcpServers.visual_studio_csharp_navigator.command = $PackagedMcpServerCommand
    $mcpJson = $mcp | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($mcpPath, $mcpJson + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Assert-PackagedMcpConfig {
    param(
        [string]$PluginPath,
        [string]$ProjectRoot,
        [string]$StagingPlugin
    )

    $mcpPath = Join-Path $PluginPath ".mcp.json"
    if (-not (Test-Path -LiteralPath $mcpPath -PathType Leaf)) {
        throw "Packaged MCP config not found: $mcpPath"
    }

    $raw = Get-Content -Raw -Encoding UTF8 -LiteralPath $mcpPath
    $mcp = $raw | ConvertFrom-Json
    $command = [string]$mcp.mcpServers.visual_studio_csharp_navigator.command
    if ($command -ne $PackagedMcpServerCommand) {
        throw "Packaged MCP command must be installer placeholder, but was: $command"
    }

    foreach ($forbiddenPath in @($ProjectRoot, $StagingPlugin)) {
        if (-not [string]::IsNullOrWhiteSpace($forbiddenPath) -and
            $raw.IndexOf($forbiddenPath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Packaged MCP config leaks a development path: $forbiddenPath"
        }
    }

    if ($raw.IndexOf("artifacts\plugin-staging", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $raw.IndexOf("artifacts\\plugin-staging", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Packaged MCP config leaks artifacts/plugin-staging."
    }
}

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}
$ProjectRoot = Resolve-FullPath $ProjectRoot

if ([string]::IsNullOrWhiteSpace($DistributionRoot)) {
    $DistributionRoot = Join-Path $ProjectRoot "artifacts\distribution"
}
$DistributionRoot = [System.IO.Path]::GetFullPath($DistributionRoot)
if (-not (Test-Path -LiteralPath $DistributionRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $DistributionRoot | Out-Null
}

$updateScript = Join-Path $ProjectRoot "scripts\Update-VisualStudioCSharpNavigator.ps1"
$stagingPlugin = Join-Path $ProjectRoot "artifacts\plugin-staging\$PluginName"
if (-not $SkipBuild) {
    Write-Section "Build staging plugin"
    $updateArgs = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $updateScript,
        "-ProjectRoot",
        $ProjectRoot,
        "-Configuration",
        $Configuration,
        "-Version",
        $Version,
        "-StagingOnly"
    )
    if ($NoRestore) {
        $updateArgs += "-NoRestore"
    }

    Write-Detail "powershell $($updateArgs -join ' ')"
    & powershell @updateArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Update-VisualStudioCSharpNavigator.ps1 failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $stagingPlugin -PathType Container)) {
    throw "Staging plugin not found: $stagingPlugin"
}

$pluginManifest = Join-Path $stagingPlugin ".codex-plugin\plugin.json"
$pluginVersion = (Get-Content -Raw -Encoding UTF8 -LiteralPath $pluginManifest | ConvertFrom-Json).version
$safeVersion = $pluginVersion -replace '\+', '-'
$packageName = "$PluginName-$safeVersion"
$packageRoot = Join-Path $DistributionRoot $packageName
$zipPath = Join-Path $DistributionRoot "$packageName.zip"
$verifyRoot = Join-Path $DistributionRoot "_verify-unzip"

Write-Section "Assemble distribution"
Clear-DirectoryContent -Directory $packageRoot -ExpectedParent $DistributionRoot
New-Item -ItemType Directory -Path (Join-Path $packageRoot "plugins") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $packageRoot ".agents\plugins") -Force | Out-Null

$packagePlugin = Join-Path $packageRoot "plugins\$PluginName"
Copy-Item -LiteralPath $stagingPlugin -Destination $packagePlugin -Recurse -Force
Set-PackagedMcpConfig -PluginPath $packagePlugin
Assert-PackagedMcpConfig -PluginPath $packagePlugin -ProjectRoot $ProjectRoot -StagingPlugin $stagingPlugin
Write-Installer -Path (Join-Path $packageRoot "Install-CodexPlugin.ps1")
Write-DshInstaller -Path (Join-Path $packageRoot "Install-DshMcpServer.ps1")
Write-Readme -Path (Join-Path $packageRoot "README.md") -Version $pluginVersion
Write-Marketplace -Path (Join-Path $packageRoot ".agents\plugins\marketplace.json")

$rootScriptPaths = @(
    (Join-Path $packageRoot "Install-CodexPlugin.ps1"),
    (Join-Path $packageRoot "Install-DshMcpServer.ps1"),
    (Join-Path $packageRoot "README.md")
)
foreach ($rootScriptPath in $rootScriptPaths) {
    Assert-NoDevelopmentPathLeak -FilePath $rootScriptPath -ForbiddenPaths @($ProjectRoot, $stagingPlugin)
}

if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Section "Create zip"
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force
Write-Detail "Zip: $zipPath"
Write-Detail "SHA256: $((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash)"

Write-Section "Verify zip"
Clear-DirectoryContent -Directory $verifyRoot -ExpectedParent $DistributionRoot
Expand-Archive -LiteralPath $zipPath -DestinationPath $verifyRoot -Force
$verifyPlugin = Join-Path $verifyRoot "plugins\$PluginName"
Assert-PackagedMcpConfig -PluginPath $verifyPlugin -ProjectRoot $ProjectRoot -StagingPlugin $stagingPlugin
foreach ($verifyScriptName in @("Install-CodexPlugin.ps1", "Install-DshMcpServer.ps1", "README.md")) {
    Assert-NoDevelopmentPathLeak -FilePath (Join-Path $verifyRoot $verifyScriptName) -ForbiddenPaths @($ProjectRoot, $stagingPlugin)
}
$validatePluginScript = Join-Path $HOME ".codex\skills\.system\plugin-creator\scripts\validate_plugin.py"
if (Test-Path -LiteralPath $validatePluginScript -PathType Leaf) {
    & python $validatePluginScript $verifyPlugin
    if ($LASTEXITCODE -ne 0) {
        throw "Plugin validation failed for unzipped distribution."
    }
}

Write-Section "Done"
Write-Detail "Distribution package: $zipPath"
