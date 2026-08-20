[CmdletBinding()]
param(
    [string]$VsixPath,

    [string]$DevenvPath,

    [string]$RootSuffix,

    [switch]$UninstallFirst,

    [switch]$Quiet,

    [switch]$AllowRunningVisualStudio,

    [switch]$CloseRunningVisualStudio,

    [switch]$ForceCloseRunningVisualStudio,

    [int]$CloseVisualStudioTimeoutSeconds = 60,

    [int]$DiscoveryTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ExtensionId = "VisualStudio.CSharpNavigator.Vsix"

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

function Join-ProcessArguments {
    param([string[]]$Arguments)

    $escaped = foreach ($argument in $Arguments) {
        if ($argument -match '[\s"]') {
            '"' + ($argument -replace '"', '\"') + '"'
        }
        else {
            $argument
        }
    }

    return ($escaped -join " ")
}

function Get-ShortHash {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.Substring(0, 12)
}

function Find-Devenv {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "DevenvPath does not exist: $ExplicitPath"
        }

        return Resolve-FullPath $ExplicitPath
    }

    $vswhereCandidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }

    foreach ($vswhere in $vswhereCandidates) {
        $installPath = & $vswhere -latest -prerelease -property installationPath 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($installPath)) {
            $candidate = Join-Path $installPath "Common7\IDE\devenv.exe"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return Resolve-FullPath $candidate
            }
        }
    }

    $command = Get-Command devenv.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and (Test-Path -LiteralPath $command.Source -PathType Leaf)) {
        return $command.Source
    }

    $knownCandidates = @(
        "D:\Microsoft Visual Studio\18\Enterprise\Common7\IDE\devenv.exe",
        "D:\Microsoft Visual Studio\18\Professional\Common7\IDE\devenv.exe",
        "D:\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe"
    )

    foreach ($candidate in $knownCandidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return Resolve-FullPath $candidate
        }
    }

    throw "Could not locate devenv.exe. Pass -DevenvPath explicitly."
}

function Find-VsixInstaller {
    param([string]$ResolvedDevenvPath)

    $ideDirectory = Split-Path -Parent $ResolvedDevenvPath
    $candidate = Join-Path $ideDirectory "VSIXInstaller.exe"
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        return Resolve-FullPath $candidate
    }

    $command = Get-Command VSIXInstaller.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and (Test-Path -LiteralPath $command.Source -PathType Leaf)) {
        return $command.Source
    }

    throw "Could not locate VSIXInstaller.exe near devenv.exe."
}

function Get-RunningVisualStudioProcesses {
    param([string]$ResolvedDevenvPath)

    $resolved = [System.IO.Path]::GetFullPath($ResolvedDevenvPath)
    return @(
        Get-CimInstance Win32_Process -Filter "name = 'devenv.exe'" -ErrorAction SilentlyContinue |
            Where-Object {
                if ([string]::IsNullOrWhiteSpace($_.ExecutablePath)) {
                    return $false
                }

                $path = [System.IO.Path]::GetFullPath([string]$_.ExecutablePath)
                return $path.Equals($resolved, [System.StringComparison]::OrdinalIgnoreCase)
            }
    )
}

function Close-RunningVisualStudio {
    param(
        [object[]]$Processes,
        [int]$TimeoutSeconds,
        [bool]$Force
    )

    if ($Processes.Count -eq 0) {
        return
    }

    Write-Section "Close running Visual Studio"
    $ids = ($Processes | ForEach-Object { $_.ProcessId }) -join ", "
    Write-Detail "Requesting graceful shutdown for devenv processId: $ids"

    foreach ($processInfo in $Processes) {
        $process = Get-Process -Id $processInfo.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            continue
        }

        [void]$process.CloseMainWindow()
    }

    $deadline = [DateTimeOffset]::Now.AddSeconds($TimeoutSeconds)
    do {
        $remaining = @($Processes | Where-Object { $null -ne (Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue) })
        if ($remaining.Count -eq 0) {
            Write-Detail "All target Visual Studio processes exited."
            return
        }

        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::Now -lt $deadline)

    if ($Force) {
        $remainingIds = @($remaining | ForEach-Object { $_.ProcessId })
        Write-Detail "Force-stopping remaining devenv processId: $($remainingIds -join ', ')"
        foreach ($processId in $remainingIds) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
        return
    }

    $blockedIds = ($remaining | ForEach-Object { $_.ProcessId }) -join ", "
    throw "Visual Studio did not exit within $TimeoutSeconds seconds (processId: $blockedIds). Save/close Visual Studio manually, or rerun with -ForceCloseRunningVisualStudio if you accept losing unsaved work."
}

function Invoke-VsixInstaller {
    param(
        [string]$VsixInstallerPath,
        [string[]]$Arguments,
        [string]$Label
    )

    Write-Detail "$Label`: $VsixInstallerPath $(Join-ProcessArguments $Arguments)"
    & $VsixInstallerPath @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Label failed with exit code $exitCode."
    }
}

function Read-BridgeInstances {
    $discoveryDir = Join-Path $env:LOCALAPPDATA "VisualStudio.CSharpNavigatorMcp\instances"
    if (-not (Test-Path -LiteralPath $discoveryDir -PathType Container)) {
        return @()
    }

    $instances = New-Object 'System.Collections.Generic.List[object]'
    foreach ($file in Get-ChildItem -LiteralPath $discoveryDir -Filter "*.json" -File -ErrorAction SilentlyContinue) {
        try {
            $json = Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName
            $record = $json | ConvertFrom-Json
            $process = Get-Process -Id $record.processId -ErrorAction SilentlyContinue
            $lastSeen = [DateTimeOffset]::Parse($record.lastSeenUtc)
            $ageSeconds = [int]([DateTimeOffset]::UtcNow - $lastSeen.ToUniversalTime()).TotalSeconds

            [void]$instances.Add([pscustomobject]@{
                InstanceId = [string]$record.instanceId
                ProcessId = [int]$record.processId
                ProcessAlive = $null -ne $process
                SolutionPath = [string]$record.solutionPath
                SolutionName = [string]$record.solutionName
                PipeName = [string]$record.pipeName
                LastSeenUtc = $lastSeen.UtcDateTime.ToString("o")
                AgeSeconds = $ageSeconds
                File = $file.FullName
            })
        }
        catch {
            Write-Warning "Could not parse discovery record '$($file.FullName)': $($_.Exception.Message)"
        }
    }

    return @($instances | Sort-Object ProcessAlive, AgeSeconds, SolutionPath -Descending)
}

function Wait-BridgeDiscovery {
    param([int]$TimeoutSeconds)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $alive = @(Read-BridgeInstances | Where-Object { $_.ProcessAlive })
        if ($alive.Count -gt 0) {
            return $alive
        }

        Start-Sleep -Seconds 2
    }

    return @()
}

if ([string]::IsNullOrWhiteSpace($VsixPath)) {
    $scriptRoot = Resolve-FullPath $PSScriptRoot
    $pluginCandidate = Join-Path (Split-Path -Parent $scriptRoot) "vsix\VisualStudio.CSharpNavigator.Vsix.vsix"
    $repoCandidate = Join-Path (Split-Path -Parent $scriptRoot) "src\VisualStudio.CSharpNavigator.Vsix\bin\Debug\net472\VisualStudio.CSharpNavigator.Vsix.vsix"

    if (Test-Path -LiteralPath $pluginCandidate -PathType Leaf) {
        $VsixPath = $pluginCandidate
    }
    elseif (Test-Path -LiteralPath $repoCandidate -PathType Leaf) {
        $VsixPath = $repoCandidate
    }
    else {
        throw "VsixPath was not provided and no default VSIX package was found."
    }
}

$VsixPath = Resolve-FullPath $VsixPath
$resolvedDevenv = Find-Devenv $DevenvPath
$vsixInstaller = Find-VsixInstaller $resolvedDevenv

$logRoot = Join-Path $env:TEMP "VisualStudio.CSharpNavigatorMcp"
if (-not (Test-Path -LiteralPath $logRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $logRoot | Out-Null
}

Write-Section "Inputs"
Write-Detail "VSIX: $VsixPath"
Write-Detail "VSIX hash: $(Get-ShortHash $VsixPath)"
Write-Detail "devenv.exe: $resolvedDevenv"
Write-Detail "VSIXInstaller.exe: $vsixInstaller"
if (-not [string]::IsNullOrWhiteSpace($RootSuffix)) {
    Write-Detail "RootSuffix: $RootSuffix"
}

$runningVisualStudio = @(Get-RunningVisualStudioProcesses -ResolvedDevenvPath $resolvedDevenv)
if ($runningVisualStudio.Count -gt 0 -and $CloseRunningVisualStudio) {
    Close-RunningVisualStudio -Processes $runningVisualStudio -TimeoutSeconds $CloseVisualStudioTimeoutSeconds -Force:$ForceCloseRunningVisualStudio
    $runningVisualStudio = @(Get-RunningVisualStudioProcesses -ResolvedDevenvPath $resolvedDevenv)
}

if ($runningVisualStudio.Count -gt 0 -and -not $AllowRunningVisualStudio) {
    $processIds = ($runningVisualStudio | ForEach-Object { $_.ProcessId }) -join ", "
    throw "Visual Studio is running from the target installation (processId: $processIds). Close Visual Studio, rerun with -CloseRunningVisualStudio, or pass -AllowRunningVisualStudio only if you accept VSIXInstaller's interactive shutdown behavior."
}

$commonArguments = @()
if ($Quiet) {
    $commonArguments += "/quiet"
}
if (-not [string]::IsNullOrWhiteSpace($RootSuffix)) {
    $commonArguments += "/rootSuffix:$RootSuffix"
}

Write-Section "Install VSIX"
if ($UninstallFirst) {
    $uninstallLog = Join-Path $logRoot ("vsix-uninstall-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
    Invoke-VsixInstaller -VsixInstallerPath $vsixInstaller -Arguments ($commonArguments + @("/uninstall:$ExtensionId", "/logFile:$uninstallLog")) -Label "VSIX uninstall"
    Write-Detail "Uninstall log: $uninstallLog"
}

$installLog = Join-Path $logRoot ("vsix-install-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Invoke-VsixInstaller -VsixInstallerPath $vsixInstaller -Arguments ($commonArguments + @("/force", $VsixPath, "/logFile:$installLog")) -Label "VSIX install"
Write-Detail "Install log: $installLog"

Write-Section "Discovery check"
$records = @(Read-BridgeInstances)
if ($records.Count -eq 0) {
    Write-Detail "No bridge discovery records found yet. Open or restart Visual Studio with a C# solution, then ask Codex to call list_visual_studio_instances."
}
else {
    $records | Format-Table InstanceId, ProcessId, ProcessAlive, SolutionPath, PipeName, AgeSeconds -AutoSize
}

if ($DiscoveryTimeoutSeconds -gt 0) {
    Write-Section "Wait for active bridge"
    $alive = @(Wait-BridgeDiscovery -TimeoutSeconds $DiscoveryTimeoutSeconds)
    if ($alive.Count -eq 0) {
        Write-Detail "No active bridge appeared within $DiscoveryTimeoutSeconds seconds. This is expected if Visual Studio is not open or has not loaded a solution yet."
    }
    else {
        $alive | Format-Table InstanceId, ProcessId, ProcessAlive, SolutionPath, PipeName, AgeSeconds -AutoSize
    }
}

Write-Section "Next steps"
Write-Detail "Restart or open Visual Studio, load a C# solution, then ask Codex to list Visual Studio instances."
Write-Detail "If multiple instances are listed, use targetInstanceId for follow-up tool calls."
