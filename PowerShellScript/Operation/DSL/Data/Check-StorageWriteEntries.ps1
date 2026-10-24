#requires -Version 5.1
<#
.SYNOPSIS
    存储写入入口守卫（StorageWriteEntries）—— 双库写入口收敛判据（结构事实：调用点计数）。

.DESCRIPTION
    基线 = Baselines\Data\StorageWriteEntries.psd1。检查项：
      SW1 图库 owner 创建点 = 1（ReferenceGraphCoordinator.CreateAsync）
      SW2 SqliteConnectionFactory 构造点 ≤ 基线（禁新增第三处连接工厂）
      SW3 直构 SqliteNodeStore ≤ 基线（旁路节点库 ⇒ 必须经组合根 bundle）
      SW4 生产侧 UpdatePayloadAsync 调用点 ≤ 基线（轮边界唯一写点）

    模式：默认校验；-Report 仅报告。违例 → 退出码 1。

.PARAMETER Root
    仓库根（默认由脚本位置上溯五级）。
.PARAMETER Report
    仅报告模式（不判失败）。
.PARAMETER ShowDetail
    输出逐命中点。
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
$baseline = Import-GateBaseline -Name StorageWriteEntries

function Get-CsFiles([string]$relativeRoot) {
    $path = Join-Path $Root $relativeRoot
    if (-not (Test-Path $path)) { return @() }
    Get-ChildItem -Path $path -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\BuildOutput\\|\\_deprecated\\' }
}

function Count-Hits([object[]]$files, [string]$pattern) {
    $hits = @($files | Select-String -Pattern $pattern -SimpleMatch -ErrorAction SilentlyContinue)
    if ($ShowDetail) {
        foreach ($h in $hits) {
            Write-Host ("    {0}:{1}  {2}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber, $h.Line.Trim()) -ForegroundColor DarkGray
        }
    }
    return $hits.Count
}

$frame = Get-CsFiles 'Frame\CsharpPrj'
$agent = Get-CsFiles 'Projects\Plug\Agent\src'

$graphCreate = Count-Hits $frame 'ReferenceGraphCoordinator.CreateAsync'
$connFactory = Count-Hits ($frame + $agent) 'new SqliteConnectionFactory('
$nodeStoreNew = Count-Hits ($frame + $agent) 'new SqliteNodeStore('
$payloadWrite = Count-Hits $agent '.UpdatePayloadAsync('

$checks = @(
    @{ Id = 'SW1'; Name = '图库 owner 创建点'; Actual = $graphCreate; Expected = [int]$baseline.GraphCoordinatorCreate.Value; Mode = 'eq' },
    @{ Id = 'SW2'; Name = 'SqliteConnectionFactory 构造点'; Actual = $connFactory; Expected = [int]$baseline.ConnectionFactoryNew.Value; Mode = 'le' },
    @{ Id = 'SW3'; Name = '直构 SqliteNodeStore'; Actual = $nodeStoreNew; Expected = [int]$baseline.DirectNodeStoreNew.Value; Mode = 'le' },
    @{ Id = 'SW4'; Name = '生产侧 UpdatePayloadAsync 调用点'; Actual = $payloadWrite; Expected = [int]$baseline.TaskPayloadWrite.Value; Mode = 'le' }
)

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  存储写入入口守卫（StorageWriteEntries）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($c in $checks) {
    Write-Host ("  {0} {1}：实测 {2} / 基线 {3}（{4}）" -f $c.Id, $c.Name, $c.Actual, $c.Expected, $c.Mode) -ForegroundColor Gray
    $bad = if ($c.Mode -eq 'eq') { $c.Actual -ne $c.Expected } else { $c.Actual -gt $c.Expected }
    if ($bad) { $violations.Add(("{0} {1}：实测 {2}，期望 {3} {4}" -f $c.Id, $c.Name, $c.Actual, $c.Mode, $c.Expected)) }
}

if ($violations.Count -eq 0) {
    Write-Host ""
    Write-Host "[OK] StorageWriteEntries：0 违例" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] StorageWriteEntries 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Report) { exit 0 }
exit 1
