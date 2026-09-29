[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$installerScript = Join-Path $PSScriptRoot "..\..\scripts\Install-VisualStudioCSharpNavigator.ps1"
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path -LiteralPath $installerScript).ProviderPath,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "Installer script contains syntax errors."
}

foreach ($name in @("Write-Detail", "Join-ProcessArguments", "Invoke-VsixInstaller")) {
    $function = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $function) {
        throw "Missing function: $name"
    }
    . ([scriptblock]::Create($function.Extent.Text))
}

$shell = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
$clock = [Diagnostics.Stopwatch]::StartNew()
Invoke-VsixInstaller -VsixInstallerPath $shell `
    -Arguments @("-NoProfile", "-Command", "Start-Sleep -Milliseconds 1200; exit 0") `
    -Label "Wait test"
if ($clock.ElapsedMilliseconds -lt 1200) {
    throw "Installer returned before the process completed."
}

$reportedFailure = $false
$global:LASTEXITCODE = 0
try {
    Invoke-VsixInstaller -VsixInstallerPath $shell `
        -Arguments @("-NoProfile", "-Command", "exit 37") `
        -Label "Failure test"
}
catch {
    if ($_.Exception.Message -notmatch "exit code 37") {
        throw
    }
    $reportedFailure = $true
}
if (-not $reportedFailure) {
    throw "Installer reported success for a nonzero exit code."
}

Write-Output "PASS: installer waits for completion and propagates process failure."
