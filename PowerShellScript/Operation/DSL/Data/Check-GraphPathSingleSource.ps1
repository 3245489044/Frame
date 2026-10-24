#requires -Version 5.1
<#
.SYNOPSIS
    图库路径单源守卫（GraphPathSingleSource）—— 图库路径解析/文件名/启动指纹各恰一处。

.DESCRIPTION
    基线 = Baselines\Data\GraphPathSingleSource.psd1。检查项：
      GP1 图库路径解析函数定义点 = 1（DatabaseBootstrapper.ResolveGraphLogPath）
      GP2 图库文件名单源常量定义点 = 1（GraphLogFileName）
      GP3 启动指纹发射点 = 1（StorageBundleFingerprint 日志）
      GP4 两库同目录断言存在 ≥1（StorageCoreCompositionRoot）

    违例 → 退出码 1（-Report 仅报告）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Report,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name GraphPathSingleSource

$files = @()
foreach ($rel in @('Frame\CsharpPrj', 'Projects\Plug\Agent\src')) {
    $path = Join-Path $Root $rel
    if (-not (Test-Path $path)) { continue }
    $files += Get-ChildItem -Path $path -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\BuildOutput\\|\\_deprecated\\' }
}

function Count-Hits([string]$pattern) {
    $hits = @($files | Select-String -Pattern $pattern -SimpleMatch -ErrorAction SilentlyContinue)
    if ($ShowDetail) {
        foreach ($h in $hits) {
            Write-Host ("    {0}:{1}  {2}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber, $h.Line.Trim()) -ForegroundColor DarkGray
        }
    }
    return $hits.Count
}

$resolveDef = Count-Hits 'public static string? ResolveGraphLogPath'
$fileNameDef = Count-Hits 'public const string GraphLogFileName'
$fingerprint = Count-Hits 'StorageBundleFingerprint'
$sameDir = Count-Hits '两库必须同目录'

$checks = @(
    @{ Id = 'GP1'; Name = '图库路径解析定义点'; Actual = $resolveDef; Expected = [int]$baseline.GraphPathResolveDefinition.Value },
    @{ Id = 'GP2'; Name = '图库文件名常量定义点'; Actual = $fileNameDef; Expected = [int]$baseline.GraphFileNameDefinition.Value },
    @{ Id = 'GP3'; Name = '启动指纹发射点'; Actual = $fingerprint; Expected = [int]$baseline.BundleFingerprintEmission.Value },
    @{ Id = 'GP4'; Name = '两库同目录断言'; Actual = $sameDir; Expected = [int]$baseline.SameDirectoryAssertion.Value }
)

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  图库路径单源守卫（GraphPathSingleSource）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($c in $checks) {
    Write-Host ("  {0} {1}：实测 {2} / 期望 {3}" -f $c.Id, $c.Name, $c.Actual, $c.Expected) -ForegroundColor Gray
    if ($c.Actual -ne $c.Expected) { $violations.Add(("{0} {1}：实测 {2}，期望 {3}" -f $c.Id, $c.Name, $c.Actual, $c.Expected)) }
}

if ($violations.Count -eq 0) {
    Write-Host ""
    Write-Host "[OK] GraphPathSingleSource：0 违例" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] GraphPathSingleSource 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Report) { exit 0 }
exit 1
