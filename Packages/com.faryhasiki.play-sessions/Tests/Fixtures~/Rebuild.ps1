param([Parameter(Mandatory=$true)][string]$UnityEditorDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$artifactDir = Join-Path $projectRoot 'work/play-sessions/dll-fixture'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$dllOutput = Join-Path $artifactDir 'PlaySessions.LegacyFixture.dll'
$unityData = Join-Path $UnityEditorDirectory 'Data'
& (Join-Path $unityData 'MonoBleedingEdge/bin/mono.exe') (Join-Path $unityData 'MonoBleedingEdge/lib/mono/4.5/csc.exe') /nologo /target:library /deterministic+ "/out:$dllOutput" (Join-Path $PSScriptRoot 'LegacyLibrary.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
$fixtureDir = Join-Path $PSScriptRoot '../Editor/Fixtures'
New-Item -ItemType Directory -Force -Path $fixtureDir | Out-Null
Copy-Item -LiteralPath $dllOutput -Destination (Join-Path $fixtureDir 'PlaySessions.LegacyFixture.dll')
Get-FileHash -Algorithm SHA256 -LiteralPath $dllOutput
