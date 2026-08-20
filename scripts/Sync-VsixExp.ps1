[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$RootSuffix = "Exp",

    [string]$SolutionPath,

    [string]$ProjectRoot,

    [string]$DevenvPath,

    [switch]$Build,

    [switch]$InstallVsix,

    [switch]$UninstallFirst,

    [switch]$Sync,

    [switch]$AllowSyncWhileExpRunning,

    [switch]$StopExistingExp,

    [switch]$UpdateConfiguration,

    [switch]$Launch,

    [switch]$WaitForDiscovery,

    [int]$DiscoveryTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ExtensionId = "VisualStudio.CSharpNavigator.Vsix"
$RepoRelativeVsixProject = "src\VisualStudio.CSharpNavigator.Vsix"

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

        return (Resolve-Path -LiteralPath $ExplicitPath).ProviderPath
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
                return (Resolve-Path -LiteralPath $candidate).ProviderPath
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
            return (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    }

    throw "Could not locate devenv.exe. Pass -DevenvPath explicitly."
}

function Find-VsixInstaller {
    param([string]$ResolvedDevenvPath)

    $ideDirectory = Split-Path -Parent $ResolvedDevenvPath
    $candidate = Join-Path $ideDirectory "VSIXInstaller.exe"
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        return (Resolve-Path -LiteralPath $candidate).ProviderPath
    }

    $command = Get-Command VSIXInstaller.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and (Test-Path -LiteralPath $command.Source -PathType Leaf)) {
        return $command.Source
    }

    throw "Could not locate VSIXInstaller.exe near devenv.exe."
}

function Get-ExpRoots {
    param([string]$RequestedRootSuffix)

    $vsLocal = Join-Path $env:LOCALAPPDATA "Microsoft\VisualStudio"
    if (-not (Test-Path -LiteralPath $vsLocal -PathType Container)) {
        return @()
    }

    $pattern = "*" + $RequestedRootSuffix
    return @(Get-ChildItem -LiteralPath $vsLocal -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ieq $RequestedRootSuffix -or $_.Name -like $pattern } |
        Sort-Object FullName)
}

function Get-CSharpNavigatorExtensionDirs {
    param([System.IO.DirectoryInfo[]]$ExpRoots)

    $dirs = New-Object 'System.Collections.Generic.List[System.IO.DirectoryInfo]'

    foreach ($root in $ExpRoots) {
        $extensionsRoot = Join-Path $root.FullName "Extensions"
        if (-not (Test-Path -LiteralPath $extensionsRoot -PathType Container)) {
            continue
        }

        foreach ($candidate in Get-ChildItem -LiteralPath $extensionsRoot -Directory -ErrorAction SilentlyContinue) {
            $dllPath = Join-Path $candidate.FullName "$ExtensionId.dll"
            $manifestPath = Join-Path $candidate.FullName "extension.vsixmanifest"
            $matchesManifest = $false

            if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
                $matchesManifest = Select-String -LiteralPath $manifestPath -SimpleMatch $ExtensionId -Quiet
            }

            if ((Test-Path -LiteralPath $dllPath -PathType Leaf) -or $matchesManifest) {
                [void]$dirs.Add($candidate)
            }
        }
    }

    return @($dirs | Sort-Object FullName -Unique)
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

function Get-DevenvProcesses {
    return @(Get-CimInstance Win32_Process -Filter "name = 'devenv.exe'" -ErrorAction SilentlyContinue |
        Select-Object ProcessId, CommandLine)
}

function Get-ExpDevenvProcesses {
    param([string]$RequestedRootSuffix)

    $escapedRootSuffix = [regex]::Escape($RequestedRootSuffix)
    $regex = "(?i)(^|\s)/RootSuffix(\s+`?$escapedRootSuffix`?|\s*:\s*`?$escapedRootSuffix`?)(\s|$)"
    return @(Get-DevenvProcesses | Where-Object { $_.CommandLine -match $regex })
}

function Invoke-AndWait {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$Label,
        [switch]$Hidden
    )

    Write-Detail "$Label`: $FilePath $(Join-ProcessArguments $Arguments)"

    $startProcessArguments = @{
        FilePath = $FilePath
        ArgumentList = (Join-ProcessArguments $Arguments)
        Wait = $true
        PassThru = $true
    }

    if ($Hidden) {
        $startProcessArguments.WindowStyle = "Hidden"
    }

    $process = Start-Process @startProcessArguments
    if ($process.ExitCode -ne 0) {
        throw "$Label failed with exit code $($process.ExitCode)."
    }
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
    param(
        [string]$ExpectedSolutionPath,
        [int]$TimeoutSeconds,
        [int]$ExpectedProcessId = 0
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $resolvedExpected = $null
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSolutionPath) -and (Test-Path -LiteralPath $ExpectedSolutionPath -PathType Leaf)) {
        $resolvedExpected = Resolve-FullPath $ExpectedSolutionPath
    }

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $instances = Read-BridgeInstances
        $alive = @($instances | Where-Object { $_.ProcessAlive })
        if ($resolvedExpected) {
            $alive = @($alive | Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.SolutionPath) -and
                ([System.IO.Path]::GetFullPath($_.SolutionPath) -ieq $resolvedExpected)
            })
        }

        if ($ExpectedProcessId -gt 0) {
            $alive = @($alive | Where-Object { $_.ProcessId -eq $ExpectedProcessId })
        }

        if ($alive.Count -gt 0) {
            return $alive
        }

        Start-Sleep -Seconds 2
    }

    return @()
}

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

$ProjectRoot = Resolve-FullPath $ProjectRoot
$SolutionFile = Join-Path $ProjectRoot "VisualStudio.CSharpNavigatorMcp.sln"
$VsixProjectRoot = Join-Path $ProjectRoot $RepoRelativeVsixProject
$VsixOutputDir = Join-Path $VsixProjectRoot "bin\$Configuration\net472"
$SourceDll = Join-Path $VsixOutputDir "$ExtensionId.dll"
$SourceVsix = Join-Path $VsixOutputDir "$ExtensionId.vsix"
$ExpectedDiscoveryProcessId = 0

if (-not (Test-Path -LiteralPath $SolutionFile -PathType Leaf)) {
    throw "Solution file not found: $SolutionFile"
}

if ($Build) {
    Write-Section "Build"
    Write-Detail "dotnet build $SolutionFile --no-restore -c $Configuration"
    & dotnet build $SolutionFile --no-restore -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $SourceDll -PathType Leaf)) {
    throw "VSIX output DLL not found: $SourceDll. Run with -Build or build the solution first."
}

$resolvedDevenv = Find-Devenv $DevenvPath
$expRoots = @(Get-ExpRoots $RootSuffix)
$extensionDirs = @(Get-CSharpNavigatorExtensionDirs $expRoots)
$sourceHash = Get-ShortHash $SourceDll

Write-Section "Inputs"
Write-Detail "ProjectRoot: $ProjectRoot"
Write-Detail "Configuration: $Configuration"
Write-Detail "RootSuffix: $RootSuffix"
Write-Detail "devenv.exe: $resolvedDevenv"
Write-Detail "VSIX output: $VsixOutputDir"
Write-Detail "Source DLL hash: $sourceHash"
if (Test-Path -LiteralPath $SourceVsix -PathType Leaf) {
    Write-Detail "VSIX package: $SourceVsix"
}
else {
    Write-Detail "VSIX package: not found"
}

Write-Section "Visual Studio processes"
$devenvProcesses = @(Get-DevenvProcesses)
if ($devenvProcesses.Count -eq 0) {
    Write-Detail "No devenv.exe process is currently running."
}
else {
    $devenvProcesses | Format-Table ProcessId, CommandLine -AutoSize
}

Write-Section "Exp roots"
if ($expRoots.Count -eq 0) {
    Write-Detail "No Visual Studio root directory matching '*$RootSuffix' was found under %LOCALAPPDATA%\Microsoft\VisualStudio."
}
else {
    foreach ($root in $expRoots) {
        Write-Detail $root.FullName
    }
}

Write-Section "Installed VisualStudio CSharpNavigator extension directories"
if ($extensionDirs.Count -eq 0) {
    Write-Detail "No existing extension directory was found. Use -InstallVsix after building the VSIX, or launch/debug the VSIX once from Visual Studio."
}
else {
    $extensionDirs | ForEach-Object {
        $dll = Join-Path $_.FullName "$ExtensionId.dll"
        [pscustomobject]@{
            Directory = $_.FullName
            DllHash = Get-ShortHash $dll
            MatchesSource = ((Get-ShortHash $dll) -eq $sourceHash)
        }
    } | Format-Table -AutoSize
}

if ($Launch -and -not $Sync -and -not $InstallVsix -and $extensionDirs.Count -gt 0) {
    $mismatchedExtensionDirs = @($extensionDirs | Where-Object {
            $dll = Join-Path $_.FullName "$ExtensionId.dll"
            (Get-ShortHash $dll) -ne $sourceHash
        })
    if ($mismatchedExtensionDirs.Count -gt 0) {
        $mismatchedPaths = ($mismatchedExtensionDirs | ForEach-Object { $_.FullName }) -join "; "
        Write-Warning "Launching /RootSuffix $RootSuffix without syncing or installing the VSIX, but installed extension DLL hash does not match the current output. This launch cannot prove the latest VSIX binary is loaded. Mismatched directories: $mismatchedPaths"
    }
}

if ($StopExistingExp) {
    Write-Section "Stop existing Exp instances"
    $expProcesses = @(Get-ExpDevenvProcesses $RootSuffix)
    if ($expProcesses.Count -eq 0) {
        Write-Detail "No /RootSuffix $RootSuffix devenv.exe process found."
    }
    else {
        foreach ($process in $expProcesses) {
            Write-Detail "Stopping devenv.exe process $($process.ProcessId)."
            Stop-Process -Id $process.ProcessId -Force
        }
        foreach ($process in $expProcesses) {
            Wait-Process -Id $process.ProcessId -Timeout 30 -ErrorAction SilentlyContinue
        }
    }
}

if ($UninstallFirst -and -not $InstallVsix) {
    throw "-UninstallFirst requires -InstallVsix."
}

if ($InstallVsix) {
    if (-not (Test-Path -LiteralPath $SourceVsix -PathType Leaf)) {
        throw "VSIX package not found: $SourceVsix. Run with -Build first."
    }

    Write-Section "VSIXInstaller"
    $vsixInstaller = Find-VsixInstaller $resolvedDevenv
    $logDir = Join-Path $ProjectRoot "artifacts\vsix-exp"
    if (-not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Path $logDir | Out-Null
    }

    if ($UninstallFirst) {
        $uninstallLog = Join-Path $logDir ("vsix-uninstall-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
        Invoke-VsixInstaller -VsixInstallerPath $vsixInstaller -Arguments @("/quiet", "/rootSuffix:$RootSuffix", "/uninstall:$ExtensionId", "/logFile:$uninstallLog") -Label "VSIX uninstall"
        Write-Detail "Uninstall log: $uninstallLog"
    }

    $installLog = Join-Path $logDir ("vsix-install-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
    Invoke-VsixInstaller -VsixInstallerPath $vsixInstaller -Arguments @("/quiet", "/rootSuffix:$RootSuffix", "/force", $SourceVsix, "/logFile:$installLog") -Label "VSIX install"
    Write-Detail "Install log: $installLog"

    $expRoots = @(Get-ExpRoots $RootSuffix)
    $extensionDirs = @(Get-CSharpNavigatorExtensionDirs $expRoots)
}

if ($Sync) {
    Write-Section "Sync extension directories"
    if ($extensionDirs.Count -eq 0) {
        throw "No existing VisualStudio CSharpNavigator extension directory was found to sync."
    }

    $runningExpProcesses = @(Get-ExpDevenvProcesses $RootSuffix)
    if ($runningExpProcesses.Count -gt 0 -and -not $AllowSyncWhileExpRunning) {
        $processIds = ($runningExpProcesses | ForEach-Object { $_.ProcessId }) -join ", "
        throw "Refusing to sync while /RootSuffix $RootSuffix devenv.exe is running (processId: $processIds). Use -StopExistingExp or explicitly pass -AllowSyncWhileExpRunning."
    }

    foreach ($directory in $extensionDirs) {
        $extensionsRoot = Split-Path -Parent $directory.FullName
        Assert-UnderDirectory $directory.FullName $extensionsRoot
        Write-Detail "Copying $VsixOutputDir -> $($directory.FullName)"
        Copy-DirectoryContent $VsixOutputDir $directory.FullName

        $targetDll = Join-Path $directory.FullName "$ExtensionId.dll"
        $targetHash = Get-ShortHash $targetDll
        if ($targetHash -ne $sourceHash) {
            throw "Hash mismatch after sync. Target='$targetDll'; expected=$sourceHash actual=$targetHash"
        }

        Write-Detail "Verified $targetDll hash=$targetHash"
    }
}

$shouldUpdateConfiguration = $UpdateConfiguration -or ($InstallVsix -and ($Launch -or $WaitForDiscovery))
if ($shouldUpdateConfiguration) {
    Write-Section "Update Visual Studio configuration"
    if (-not $UpdateConfiguration) {
        Write-Detail "VSIX was installed in this run; refreshing the $RootSuffix root before launch/discovery."
    }

    Invoke-AndWait -FilePath $resolvedDevenv -Arguments @("/RootSuffix", $RootSuffix, "/updateConfiguration") -Label "devenv updateConfiguration" -Hidden
}

if ($Launch) {
    Write-Section "Launch Exp instance"
    $arguments = @("/RootSuffix", $RootSuffix)
    if (-not [string]::IsNullOrWhiteSpace($SolutionPath)) {
        if (-not (Test-Path -LiteralPath $SolutionPath -PathType Leaf)) {
            throw "SolutionPath does not exist: $SolutionPath"
        }

        $arguments += (Resolve-FullPath $SolutionPath)
    }

    Write-Detail "Launching: $resolvedDevenv $(Join-ProcessArguments $arguments)"
    $launched = Start-Process -FilePath $resolvedDevenv -ArgumentList (Join-ProcessArguments $arguments) -PassThru
    $ExpectedDiscoveryProcessId = [int]$launched.Id
    Write-Detail "Started devenv.exe processId=$($launched.Id)"
}

if ($WaitForDiscovery) {
    Write-Section "Wait for bridge discovery"
    $matches = @(Wait-BridgeDiscovery -ExpectedSolutionPath $SolutionPath -TimeoutSeconds $DiscoveryTimeoutSeconds -ExpectedProcessId $ExpectedDiscoveryProcessId)
    if ($matches.Count -eq 0) {
        $processHint = if ($ExpectedDiscoveryProcessId -gt 0) { " for launched processId=$ExpectedDiscoveryProcessId" } else { "" }
        throw "Timed out waiting for VSIX bridge discovery$processHint after $DiscoveryTimeoutSeconds seconds."
    }

    $matches | Format-Table InstanceId, ProcessId, ProcessAlive, SolutionPath, PipeName, AgeSeconds -AutoSize
}

Write-Section "Discovery records"
$records = @(Read-BridgeInstances)
if ($records.Count -eq 0) {
    Write-Detail "No bridge discovery records found."
}
else {
    $records | Format-Table InstanceId, ProcessId, ProcessAlive, SolutionPath, PipeName, AgeSeconds -AutoSize
}

Write-Section "Final hash check"
$extensionDirs = @(Get-CSharpNavigatorExtensionDirs (Get-ExpRoots $RootSuffix))
if ($extensionDirs.Count -eq 0) {
    Write-Detail "No installed VisualStudio CSharpNavigator extension directories found."
}
else {
    $extensionDirs | ForEach-Object {
        $dll = Join-Path $_.FullName "$ExtensionId.dll"
        [pscustomobject]@{
            Directory = $_.FullName
            DllHash = Get-ShortHash $dll
            MatchesSource = ((Get-ShortHash $dll) -eq $sourceHash)
        }
    } | Format-Table -AutoSize
}

Write-Host ""
Write-Host "Done."
