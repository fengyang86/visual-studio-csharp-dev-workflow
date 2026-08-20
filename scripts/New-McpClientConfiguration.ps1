[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Codex", "ClaudeCode", "DeepSeekHarness")]
    [string]$ClientHost,

    [Parameter(Mandatory = $true)]
    [string]$ServerExe,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ServerExe -PathType Leaf)) {
    throw "MCP server executable was not found: $ServerExe"
}

if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Output file already exists: $OutputPath. Use -Force after reviewing it."
}

$serverPath = (Resolve-Path -LiteralPath $ServerExe).ProviderPath
$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

if ($ClientHost -eq "Codex") {
    $content = @"
[mcp_servers.visual_studio_csharp_navigator]
command = '$serverPath'
args = []

[mcp_servers.visual_studio_csharp_navigator.env]
VisualStudioBridge__ConnectTimeoutMilliseconds = '5000'
VisualStudioBridge__DiscoveryStaleAfterSeconds = '120'
"@
}
else {
    $configuration = [ordered]@{
        mcpServers = [ordered]@{
            visual_studio_csharp_navigator = [ordered]@{
                command = $serverPath
                args = @()
                env = [ordered]@{
                    VisualStudioBridge__ConnectTimeoutMilliseconds = "5000"
                    VisualStudioBridge__DiscoveryStaleAfterSeconds = "120"
                }
            }
        }
    }
    $content = $configuration | ConvertTo-Json -Depth 10
}

[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($OutputPath),
    $content.TrimEnd() + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Generated $ClientHost MCP configuration: $([System.IO.Path]::GetFullPath($OutputPath))"
