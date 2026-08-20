[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ProjectRoot,

    [string]$OutputPath,

    [switch]$NoRestore,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function Get-ServerProcesses {
    param([string]$ResolvedOutputPath)

    $normalizedOutputPath = $ResolvedOutputPath.TrimEnd('\') + '\'
    return @(Get-CimInstance Win32_Process -Filter "Name='VisualStudio.CSharpNavigator.Server.exe' OR Name='VisualStudio.CSharpNavigator.Server.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $commandLine = $_.CommandLine
            -not [string]::IsNullOrWhiteSpace($commandLine) -and
            $commandLine.IndexOf($normalizedOutputPath, [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
}

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

$ProjectRoot = Resolve-FullPath $ProjectRoot
$serverProject = Join-Path $ProjectRoot "src\VisualStudio.CSharpNavigator.Server\VisualStudio.CSharpNavigator.Server.csproj"
if (-not (Test-Path -LiteralPath $serverProject -PathType Leaf)) {
    throw "Server project was not found: $serverProject"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $ProjectRoot "artifacts\mcp-server-runtime"
}

if (-not (Test-Path -LiteralPath $OutputPath -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputPath | Out-Null
}

$OutputPath = Resolve-FullPath $OutputPath

Write-Section "Inputs"
Write-Detail "ProjectRoot: $ProjectRoot"
Write-Detail "Configuration: $Configuration"
Write-Detail "Server project: $serverProject"
Write-Detail "OutputPath: $OutputPath"

$runningFromOutput = @(Get-ServerProcesses $OutputPath)
if ($runningFromOutput.Count -gt 0 -and -not $Force) {
    $processIds = ($runningFromOutput | ForEach-Object { $_.ProcessId }) -join ", "
    throw "Refusing to publish over a running MCP server from OutputPath (processId: $processIds). Stop the MCP client or pass -Force if you have already confirmed it is safe."
}

if ($runningFromOutput.Count -gt 0) {
    Write-Section "Running server processes from OutputPath"
    $runningFromOutput | Select-Object ProcessId, ParentProcessId, CommandLine | Format-Table -AutoSize
}

Write-Section "Publish"
$publishArgs = @(
    "publish",
    $serverProject,
    "-c",
    $Configuration,
    "-o",
    $OutputPath
)

if ($NoRestore) {
    $publishArgs += "--no-restore"
}

Write-Detail "dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$serverExe = Join-Path $OutputPath "VisualStudio.CSharpNavigator.Server.exe"
if (-not (Test-Path -LiteralPath $serverExe -PathType Leaf)) {
    throw "Publish completed but server executable was not found: $serverExe"
}

Write-Section "Published runtime"
Write-Detail "Server exe: $serverExe"
Write-Detail "Server hash: $(Get-ShortHash $serverExe)"

Write-Section "Codex config snippet"
Write-Host "[mcp_servers.visual_studio_csharp_navigator]"
Write-Host "command = '$serverExe'"
Write-Host "args = []"

Write-Section "Claude Code config snippet"
$jsonServerExe = $serverExe.Replace("\", "\\")
Write-Host "{"
Write-Host "  `"mcpServers`": {"
Write-Host "    `"visual_studio_csharp_navigator`": {"
Write-Host "      `"command`": `"$jsonServerExe`","
Write-Host "      `"args`": []"
Write-Host "    }"
Write-Host "  }"
Write-Host "}"

Write-Section "Done"
Write-Detail "Use this published runtime in MCP clients instead of bin\\Debug to avoid build output file locks while dogfooding."
