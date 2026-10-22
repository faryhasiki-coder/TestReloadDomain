[CmdletBinding()]
param(
    [string]$UnityEditorPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$checks = [Collections.Generic.List[object]]::new()

function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([pscustomobject]@{ Check = $Name; Passed = $Passed; Detail = $Detail })
}

foreach ($relativePath in @('AGENTS.md', '.gitignore', 'ProjectSettings/ProjectVersion.txt', 'Packages/manifest.json', 'knowledge/index.md', 'knowledge/log.md')) {
    Add-Check $relativePath (Test-Path -LiteralPath (Join-Path $projectRoot $relativePath) -PathType Leaf) $relativePath
}

$versionFile = Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'
$version = $null
if (Test-Path -LiteralPath $versionFile) {
    $versionMatch = [regex]::Match((Get-Content -LiteralPath $versionFile -Raw), '(?m)^m_EditorVersion:\s*(\S+)')
    if ($versionMatch.Success) { $version = $versionMatch.Groups[1].Value }
}
Add-Check 'Unity project version' (-not [string]::IsNullOrWhiteSpace($version)) "$version"

if (-not $UnityEditorPath -and $version) {
    $UnityEditorPath = "D:/UnityEditor/$version/Editor/Unity.exe"
}
Add-Check 'Unity editor executable' ([bool]($UnityEditorPath -and (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf))) "$UnityEditorPath"

$manifestPath = Join-Path $projectRoot 'Packages/manifest.json'
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($package in @('com.coplaydev.unity-mcp', 'com.unity.render-pipelines.universal', 'com.unity.inputsystem', 'com.unity.test-framework')) {
        $property = $manifest.dependencies.PSObject.Properties[$package]
        Add-Check $package ($null -ne $property) $(if ($null -ne $property) { [string]$property.Value } else { 'Not declared' })
    }
}

foreach ($commandName in @('pwsh', 'git', 'node')) {
    $commandInfo = Get-Command $commandName -ErrorAction SilentlyContinue | Select-Object -First 1
    Add-Check $commandName ($null -ne $commandInfo) $(if ($commandInfo) { $commandInfo.Source } else { 'Not found on PATH' })
}

Add-Check 'Shared OKF MCP launcher' (Test-Path -LiteralPath 'E:/Env/OKF/mem-mcp.js' -PathType Leaf) 'E:/Env/OKF/mem-mcp.js'
$checks | Format-Table -AutoSize -Wrap
Write-Output 'Local prerequisites only. Verify Unity and OKF connections with MCP calls.'
if ($checks.Passed -contains $false) { exit 1 }
