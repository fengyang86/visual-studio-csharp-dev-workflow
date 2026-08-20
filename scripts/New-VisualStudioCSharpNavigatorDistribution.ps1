[CmdletBinding()]
param(
    [string]$ProjectRoot,

    [string]$DistributionRoot,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

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
    $matches = New-Object 'System.Collections.Generic.List[object]'
    foreach ($process in Get-CimInstance Win32_Process -Filter "name = 'VisualStudio.CSharpNavigator.Server.exe'" -ErrorAction SilentlyContinue) {
        if ([string]::IsNullOrWhiteSpace($process.ExecutablePath)) {
            continue
        }

        $exe = [System.IO.Path]::GetFullPath($process.ExecutablePath)
        if ($exe.StartsWith($targetRoot + "\", [System.StringComparison]::OrdinalIgnoreCase)) {
            [void]$matches.Add($process)
        }
    }

    return @($matches)
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
    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $installVsixScript, "-VsixPath", $vsixPath)
    if ($UninstallVsixFirst) {
        $args += "-UninstallFirst"
    }
    if ($CloseRunningVisualStudio) {
        $args += "-CloseRunningVisualStudio"
    }
    if ($ForceCloseRunningVisualStudio) {
        $args += "-ForceCloseRunningVisualStudio"
    }
    if ($CloseVisualStudioTimeoutSeconds -ne 60) {
        $args += @("-CloseVisualStudioTimeoutSeconds", $CloseVisualStudioTimeoutSeconds)
    }
    if (-not [string]::IsNullOrWhiteSpace($DevenvPath)) {
        $args += @("-DevenvPath", $DevenvPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($RootSuffix)) {
        $args += @("-RootSuffix", $RootSuffix)
    }
    & powershell @args
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

function Write-Readme {
    param(
        [string]$Path,
        [string]$Version
    )

    $content = @"
# Visual Studio C# Dev Workflow Codex Plugin

Version: $Version

The installed plugin id is visual-studio-csharp-dev-workflow. The user-facing skill and workflow name is Visual Studio C# Dev Workflow.

## Install Or Update

1. Extract this package.
2. Close Codex if you are updating an existing installation.
3. In PowerShell, run:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-CodexPlugin.ps1
~~~

4. Restart Codex or start a new Codex thread.
5. Ask Codex to use Visual Studio C# Dev Workflow.

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

If Visual Studio is running and you want the installer to close it explicitly:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-CodexPlugin.ps1 -InstallVsix -CloseRunningVisualStudio
~~~

Restart Visual Studio after VSIX installation.

If the installer reports that the MCP runtime is in use, restart Codex and rerun the command. This protects active MCP server files from being overwritten.

If you want the installer to stop only this plugin's running MCP server processes before updating the plugin runtime:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-CodexPlugin.ps1 -StopRunningMcpServers
~~~

Use `-ForceStopRunningMcpServers` only when the normal stop does not exit within the timeout.

## What The Installer Changes

- Copies the plugin to `$HOME\plugins\visual-studio-csharp-dev-workflow`.
- Updates `$HOME\.agents\plugins\marketplace.json`.
- Rewrites the installed plugin `.mcp.json` so the MCP server command points to the installed runtime path.
- Optionally runs `codex plugin add visual-studio-csharp-dev-workflow@personal`.
- Installs or updates the Visual Studio VSIX only when `-InstallVsix` is passed.
- Stops running MCP server processes only when `-StopRunningMcpServers` is passed.

Advanced/test installs can pass `-MarketplacePath` to write marketplace metadata somewhere other than `$HOME\.agents\plugins\marketplace.json`.
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
$version = (Get-Content -Raw -Encoding UTF8 -LiteralPath $pluginManifest | ConvertFrom-Json).version
$safeVersion = $version -replace '\+', '-'
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
Write-Readme -Path (Join-Path $packageRoot "README.md") -Version $version
Write-Marketplace -Path (Join-Path $packageRoot ".agents\plugins\marketplace.json")

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
$validatePluginScript = Join-Path $HOME ".codex\skills\.system\plugin-creator\scripts\validate_plugin.py"
if (Test-Path -LiteralPath $validatePluginScript -PathType Leaf) {
    & python $validatePluginScript $verifyPlugin
    if ($LASTEXITCODE -ne 0) {
        throw "Plugin validation failed for unzipped distribution."
    }
}

Write-Section "Done"
Write-Detail "Distribution package: $zipPath"
