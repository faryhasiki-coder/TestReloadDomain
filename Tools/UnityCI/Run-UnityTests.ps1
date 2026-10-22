<#
.SYNOPSIS
  以 batchmode 运行 Unity Test Runner 与 Easy Reload Guard 检查。

.DESCRIPTION
  工程正被 Unity 编辑器打开时（Temp/UnityLockfile 被占用），先把 Assets、Packages、ProjectSettings
  与 Library/PackageCache 镜像到工作目录再运行，不影响正在使用的编辑器；否则直接在工程上运行。
  测试在全新域中执行，满足“本域尚未进入过 Play”的前提。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/UnityCI/Run-UnityTests.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/UnityCI/Run-UnityTests.ps1 -TestFilter "EasyReloadGuard|DomainReloadCases"

.NOTES
  退出码：0 全部通过；1 测试失败或检查存在阻断问题；2 流水线自身失败。
#>
param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$UnityPath,
    [string]$WorkRoot = (Join-Path $env:TEMP 'EasyReloadGuardCI'),
    [ValidateSet('EditMode', 'PlayMode')]
    [string]$TestPlatform = 'EditMode',
    [string]$TestFilter,
    [switch]$InPlace,
    [switch]$SkipAudit
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Resolve-UnityPath([string]$project) {
    $versionLine = Get-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') |
        Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    $version = ($versionLine -split ':', 2)[1].Trim()
    $candidates = @(
        "C:\Program Files\Unity $version\Editor\Unity.exe",
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "找不到 Unity $version，请用 -UnityPath 指定 Unity.exe。"
}

function Test-ProjectLocked([string]$project) {
    $lock = Join-Path $project 'Temp/UnityLockfile'
    if (-not (Test-Path $lock)) { return $false }
    try {
        $stream = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $stream.Close()
        return $false
    }
    catch {
        return $true
    }
}

function Sync-Directory([string]$source, [string]$destination) {
    if (-not (Test-Path $source)) { return }
    robocopy $source $destination /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "镜像失败（robocopy 退出码 $LASTEXITCODE）：$source" }
}

function Invoke-Unity([string[]]$arguments) {
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    return $process.ExitCode
}

function Quote([string]$value) { return '"' + $value + '"' }

try {
    if (-not $UnityPath) { $UnityPath = Resolve-UnityPath $ProjectPath }

    $runProject = $ProjectPath
    if (-not $InPlace -and (Test-ProjectLocked $ProjectPath)) {
        $runProject = Join-Path $WorkRoot (Split-Path $ProjectPath -Leaf)
        Write-Host "工程正被编辑器打开，镜像到 $runProject"
        foreach ($dir in 'Assets', 'Packages', 'ProjectSettings', 'Library/PackageCache') {
            Sync-Directory (Join-Path $ProjectPath $dir) (Join-Path $runProject $dir)
        }
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $outDir = Join-Path $WorkRoot "results/$stamp"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $resultsXml = Join-Path $outDir "$TestPlatform-results.xml"
    $testLog = Join-Path $outDir "$TestPlatform.log"

    $testArgs = @('-batchmode', '-nographics', '-projectPath', (Quote $runProject), '-runTests',
        '-testPlatform', $TestPlatform, '-testResults', (Quote $resultsXml), '-logFile', (Quote $testLog))
    if ($TestFilter) { $testArgs += @('-testFilter', (Quote $TestFilter)) }

    Write-Host "Unity：$UnityPath"
    Write-Host "运行 $TestPlatform 测试……日志：$testLog"
    $testExit = Invoke-Unity $testArgs

    $failed = $true
    if (Test-Path $resultsXml) {
        [xml]$xml = Get-Content $resultsXml -Encoding UTF8
        $run = $xml.'test-run'
        Write-Host ("测试结果：{0}，共 {1}，通过 {2}，失败 {3}，跳过 {4}" -f $run.result, $run.total, $run.passed, $run.failed, $run.skipped)
        foreach ($case in $xml.SelectNodes("//test-case[@result='Failed']")) {
            Write-Host ("  失败：{0}`n    {1}" -f $case.fullname, $case.failure.message.InnerText.Trim())
        }
        $failed = [int]$run.failed -gt 0 -or [int]$run.total -eq 0
    }
    else {
        Write-Host "没有生成测试结果（Unity 退出码 $testExit），请查看日志：$testLog"
    }

    $auditFailed = $false
    if (-not $SkipAudit) {
        $report = Join-Path $outDir 'EasyReloadGuardReport.md'
        $auditLog = Join-Path $outDir 'audit.log'
        $auditArgs = @('-batchmode', '-nographics', '-projectPath', (Quote $runProject),
            '-executeMethod', 'EasyReloadGuard.Editor.ReloadGuardCli.Audit',
            '-easyReloadGuardReport', (Quote $report), '-logFile', (Quote $auditLog))
        Write-Host "运行 Easy Reload Guard 检查……"
        $auditExit = Invoke-Unity $auditArgs
        Write-Host "检查退出码：$auditExit（0 通过，1 存在未基线问题，2 检查失败），报告：$report"
        $auditFailed = $auditExit -ne 0
    }

    Write-Host "输出目录：$outDir"
    if ($failed -or $auditFailed) { exit 1 }
    exit 0
}
catch {
    Write-Host "流水线失败：$($_.Exception.Message)"
    exit 2
}
