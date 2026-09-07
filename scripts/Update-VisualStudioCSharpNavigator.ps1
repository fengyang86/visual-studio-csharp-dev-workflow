[CmdletBinding()]
param(
    [string]$ProjectRoot,

    [string]$PluginPath = (Join-Path $HOME "plugins\visual-studio-csharp-dev-workflow"),

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "0.1.0",

    [switch]$NoRestore,

    [switch]$StagingOnly,

    [switch]$InstallVsix,

    [switch]$UninstallVsixFirst,

    [switch]$QuietVsixInstaller,

    [switch]$CloseRunningVisualStudio,

    [switch]$ForceCloseRunningVisualStudio,

    [int]$CloseVisualStudioTimeoutSeconds = 60,

    [string]$DevenvPath,

    [string]$RootSuffix,

    [int]$DiscoveryTimeoutSeconds = 60,

    [switch]$ForcePluginRuntimeOverwrite,

    [switch]$StopRunningMcpServers,

    [switch]$ForceStopRunningMcpServers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ServerExeName = "VisualStudio.CSharpNavigator.Server.exe"

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

function Get-RunningPluginServerProcesses {
    param([string]$TargetPlugin)

    if (-not (Test-Path -LiteralPath $TargetPlugin -PathType Container)) {
        return @()
    }

    $targetRoot = [System.IO.Path]::GetFullPath($TargetPlugin).TrimEnd('\')
    $matches = New-Object System.Collections.ArrayList
    foreach ($process in Get-CimInstance Win32_Process -Filter "name = '$ServerExeName'" -ErrorAction SilentlyContinue) {
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
        Write-Detail "No running MCP server processes found under plugin path."
        return
    }

    $ids = @($runningServers | ForEach-Object { [int]$_.ProcessId })
    Write-Detail "Stopping MCP server processId: $($ids -join ', ')"
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

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

$ProjectRoot = Resolve-FullPath $ProjectRoot
$packageScript = Join-Path $ProjectRoot "scripts\Package-CodexPlugin.ps1"
$installScript = Join-Path $ProjectRoot "scripts\Install-VisualStudioCSharpNavigator.ps1"
$validatePluginScript = Join-Path $HOME ".codex\skills\.system\plugin-creator\scripts\validate_plugin.py"

if (-not (Test-Path -LiteralPath $packageScript -PathType Leaf)) {
    throw "Package script not found: $packageScript"
}
if (-not (Test-Path -LiteralPath $installScript -PathType Leaf)) {
    throw "VSIX install script not found: $installScript"
}

if ($StagingOnly) {
    $PluginPath = Join-Path $ProjectRoot "artifacts\plugin-staging\visual-studio-csharp-dev-workflow"
}

Write-Section "Update plan"
Write-Detail "ProjectRoot: $ProjectRoot"
Write-Detail "PluginPath: $PluginPath"
Write-Detail "Configuration: $Configuration"
Write-Detail "Version: $Version"
Write-Detail "StagingOnly: $StagingOnly"
Write-Detail "InstallVsix: $InstallVsix"
Write-Detail "ForcePluginRuntimeOverwrite: $ForcePluginRuntimeOverwrite"
Write-Detail "StopRunningMcpServers: $StopRunningMcpServers"

if ($StopRunningMcpServers -and -not $StagingOnly) {
    Write-Section "Stop running MCP servers"
    Stop-RunningPluginServerProcesses -TargetPlugin $PluginPath -Force:$ForceStopRunningMcpServers
}

Write-Section "Package Codex plugin"
$packageArgs = @(
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    $packageScript,
    "-ProjectRoot",
    $ProjectRoot,
    "-PluginPath",
    $PluginPath,
    "-Configuration",
    $Configuration,
    "-Version",
    $Version,
    "-Build"
)
if ($NoRestore) {
    $packageArgs += "-NoRestore"
}
if ($ForcePluginRuntimeOverwrite) {
    $packageArgs += "-Force"
}

Write-Detail "powershell $($packageArgs -join ' ')"
& powershell @packageArgs
if ($LASTEXITCODE -ne 0) {
    throw "Package-CodexPlugin.ps1 failed with exit code $LASTEXITCODE."
}

$pluginServerExe = Join-Path $PluginPath "runtimes\mcp-server\VisualStudio.CSharpNavigator.Server.exe"
$pluginVsix = Join-Path $PluginPath "vsix\VisualStudio.CSharpNavigator.Vsix.vsix"

Write-Section "Validate package"
if (Test-Path -LiteralPath $validatePluginScript -PathType Leaf) {
    Write-Detail "python $validatePluginScript $PluginPath"
    & python $validatePluginScript $PluginPath
    if ($LASTEXITCODE -ne 0) {
        throw "Plugin validation failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Detail "Plugin validation script not found; skipped: $validatePluginScript"
}

Write-Detail "MCP server: $pluginServerExe"
Write-Detail "MCP server hash: $(Get-ShortHash $pluginServerExe)"
Write-Detail "VSIX: $pluginVsix"
Write-Detail "VSIX hash: $(Get-ShortHash $pluginVsix)"

if ($InstallVsix) {
    Write-Section "Install VSIX"
    $installArgs = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $installScript,
        "-VsixPath",
        $pluginVsix,
        "-DiscoveryTimeoutSeconds",
        $DiscoveryTimeoutSeconds
    )
    if ($UninstallVsixFirst) {
        $installArgs += "-UninstallFirst"
    }
    if ($QuietVsixInstaller) {
        $installArgs += "-Quiet"
    }
    if ($CloseRunningVisualStudio) {
        $installArgs += "-CloseRunningVisualStudio"
    }
    if ($ForceCloseRunningVisualStudio) {
        $installArgs += "-ForceCloseRunningVisualStudio"
    }
    if ($CloseVisualStudioTimeoutSeconds -ne 60) {
        $installArgs += @("-CloseVisualStudioTimeoutSeconds", $CloseVisualStudioTimeoutSeconds)
    }
    if (-not [string]::IsNullOrWhiteSpace($DevenvPath)) {
        $installArgs += @("-DevenvPath", $DevenvPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($RootSuffix)) {
        $installArgs += @("-RootSuffix", $RootSuffix)
    }

    Write-Detail "powershell $($installArgs -join ' ')"
    & powershell @installArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Install-VisualStudioCSharpNavigator.ps1 failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Section "VSIX install skipped"
    Write-Detail "Run again with -InstallVsix to update the Visual Studio extension."
}

Write-Section "Next steps"
if ($StagingOnly) {
    Write-Detail "Staging package is ready. It has not updated the personal Codex plugin directory."
}
else {
    Write-Detail "Restart Codex so it loads the updated MCP tool schema from the packaged plugin."
}
if ($InstallVsix) {
    Write-Detail "Restart Visual Studio or reopen the target solution so the updated VSIX bridge is loaded."
}
else {
    Write-Detail "Install the VSIX before expecting Visual Studio-side bridge behavior changes."
}
