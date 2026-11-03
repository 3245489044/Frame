#requires -Version 5.1
<#
.SYNOPSIS
    catch 出口批修（CatchOutletPatch）—— 「禁静默出口 T/N/D」的 catch 面**编辑期**工具。

.DESCRIPTION
    消费 Invoke-CatchTriage.ps1 产出的机读清单（catch-triage.json），对候选 catch 逐条施加**受控出口升级**：
      rethrow   把「候选」块升级为出口 T：在块闭合花括号前插入 throw;（fail-fast）
      annotate  仅插入可见标记注释 //! catch-outlet: ...（非行为变更；不改计数，供人工复核/登记）
      register  不改代码，把选中项登记进 fallback-registry.json（type=C2）作为合法出口（N/D）

    安全设计（对齐 formula:SM.PatchAnchorFlow / SM.EffectiveFaceAssertion）：
      锚点替换：以「块起始行 + 块体 SHA256 哈希」双锚定，哈希不符（源码漂移）即跳过并可见化，禁盲写。
      备份：写前逐文件复制到 PowerShellScript\backups\<时间戳>\，可逐文件回退。
      预览：默认 dry-run（不落盘，只列出拟改动）；须显式 -Apply 才写。
      只读清单：本件不重扫源码，一切以 triage 清单为准（单一生产者）。

.PARAMETER Transform
    rethrow | annotate（缺省 rethrow）。
.PARAMETER Register
    不对代码改动，改为把选中项登记进 fallback-registry.json（type=C2）。
.PARAMETER Limit
    本次处理上限（分批；缺省 20）。
.PARAMETER Apply
    真正落盘（缺省 dry-run）。
.EXAMPLE
    .\Invoke-CatchOutletPatch.ps1 -Class candidate-empty -Limit 10            # 预览
.EXAMPLE
    .\Invoke-CatchOutletPatch.ps1 -Class candidate-empty -Limit 10 -Apply    # 落盘（带备份）
#>
[CmdletBinding()]
param(
    [string]$TriageFile = '',
    [string[]]$Class = @('candidate-empty', 'candidate-logonly'),
    [string]$PathFilter = '',
    [ValidateSet('rethrow', 'annotate')]
    [string]$Transform = 'rethrow',
    [switch]$Register,
    [string]$Owner = 'unassigned',
    [string]$Reason = '',
    [int[]]$Lines = @(),
    [int[]]$SkipLines = @(),
    [int]$Limit = 20,
    [switch]$Apply,
    [string]$BackupDir = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($TriageFile)) {
    $TriageFile = Join-Path $PSScriptRoot '..\Registry\catch-triage.json'
}
if (-not (Test-Path -LiteralPath $TriageFile)) { throw "triage 清单缺失：$TriageFile（先运行 Invoke-CatchTriage.ps1）" }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($BackupDir)) { $BackupDir = Join-Path $PSScriptRoot '..\..\backups' }

$triage = Get-Content -LiteralPath $TriageFile -Raw | ConvertFrom-Json
$selected = @($triage.items | Where-Object {
        $_.candidate -and ($Class -contains $_.class) -and
        ([string]::IsNullOrWhiteSpace($PathFilter) -or $_.file -like $PathFilter) -and
        ($Lines.Count -eq 0 -or $Lines -contains [int]$_.line) -and
        ($SkipLines.Count -eq 0 -or $SkipLines -notcontains [int]$_.line)
    } | Select-Object -First $Limit)

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ("  catch 出口批修（{0} · {1}）" -f $(if ($Register) { 'register' } else { $Transform }), $(if ($Apply) { 'APPLY' } else { 'DRY-RUN' })) -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ("  选中 {0} 项（Class={1}；Limit={2}）" -f $selected.Count, ($Class -join ','), $Limit) -ForegroundColor Gray

if ($selected.Count -eq 0) { Write-Host '  [空] 无匹配候选（清单已消费完 / 过滤过严）' -ForegroundColor Green; exit 0 }

# ---- register 模式：登记合法出口（N/D），不改代码 ----
if ($Register) {
    $registryPath = Join-Path $PSScriptRoot '..\Registry\fallback-registry.json'
    $reg = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json
    $existing = @($reg.entries)
    $added = 0
    foreach ($it in $selected) {
        $dup = @($existing | Where-Object { "$($_.type)" -eq 'C2' -and ($_.file -replace '\\', '/') -eq "$($it.file):$($it.line)" })
        if ($dup.Count -gt 0) { continue }
        $existing += [pscustomobject]@{
            id = "C2-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
            type = 'C2'; file = "$($it.file):$($it.line)"; exit = 'N'
            target = 'catch'; reason = $(if ([string]::IsNullOrWhiteSpace($Reason)) { "catch 出口登记（class=$($it.class)）" } else { $Reason }); owner = $Owner; expires = $null
        }
        $added++
        Write-Host ("    [登记] {0}:{1} [{2}]" -f $it.file, $it.line, $it.class) -ForegroundColor Yellow
    }
    if ($Apply) {
        $reg.entries = $existing
        $reg | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $registryPath -Encoding UTF8
        Write-Host ("  [已写] {0}（新增 {1} 项）" -f $registryPath, $added) -ForegroundColor Green
    } else {
        Write-Host ("  [DRY-RUN] 拟新增 {0} 项（加 -Apply 落盘）" -f $added) -ForegroundColor Yellow
    }
    exit 0
}

# ---- patch 模式：锚点 + 哈希校验 ----
Import-Module (Join-Path $PSScriptRoot '..\Meta\TextOps.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\Meta\CatchOutlet.psm1') -Force -DisableNameChecking

$runBackupDir = Join-Path $BackupDir (Get-Date -Format 'yyyyMMdd_HHmmss')
$applied = 0; $skipped = 0; $backed = 0

foreach ($g in ($selected | Group-Object file)) {
    $abs = Join-Path $repoRoot ($g.Name -replace '/', '\')
    if (-not (Test-Path -LiteralPath $abs)) { Write-Host ("  !! 缺件（跳过）{0}" -f $g.Name) -ForegroundColor Red; $skipped += $g.Count; continue }
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($l in [System.IO.File]::ReadAllLines($abs)) { $lines.Add($l) }

    $edits = [System.Collections.Generic.List[object]]::new()
    foreach ($it in $g.Group) {
        $start = [int]$it.line - 1
        $end = [int]$it.endLine - 1
        if ($start -lt 0 -or $end -ge $lines.Count) { $skipped++; continue }
        $body = ((@($lines[$start..$end] | ForEach-Object { (Remove-LineComment -Text $_) }) -join ' ') + ' ')
        if ((Get-CatchBodyHash -Body $body) -ne $it.bodyHash) {
            Write-Host ("    !! 漂移（哈希不符，跳过）{0}:{1}" -f $it.file, $it.line) -ForegroundColor Red
            $skipped++; continue
        }
        $edits.Add([pscustomobject]@{ Start = $start; End = $end; Item = $it })
    }
    if ($edits.Count -eq 0) { continue }

    foreach ($e in ($edits | Sort-Object Start -Descending)) {
        if ($Transform -eq 'rethrow') {
            # 语义守卫：块体若已无条件转移控制（return/continue/break/goto），末尾插入 throw; 为不可达死代码
            # ⇒ 计数会降而行为不变（假绿）。此类须手工把吞并语句改为 throw;，本件跳过并可见化。
            $codeLines = @($lines[$e.Start..$e.End] | Where-Object { $_ -notmatch '^\s*(//|/\*|\*)' })
            if (@($codeLines | Where-Object { $_ -match '\b(return|continue|break|goto)\b' }).Count -gt 0) {
                Write-Host ("    !! 不可达（块体已含 return/continue/break/goto ⇒ 须手工改 throw;，跳过）{0}:{1}" -f $e.Item.file, $e.Item.line) -ForegroundColor Yellow
                $skipped++; continue
            }
            if ($e.Start -eq $e.End) {
                $ln = $lines[$e.Start]; $idx = $ln.LastIndexOf('}')
                if ($idx -lt 0) { $skipped++; continue }
                $lines[$e.Start] = $ln.Substring(0, $idx) + 'throw; ' + $ln.Substring($idx)
            } else {
                $indent = ([regex]::Match($lines[$e.End], '^\s*')).Value
                $lines.Insert($e.End, ($indent + '    throw;'))
            }
        } else {
            $indent = ([regex]::Match($lines[$e.Start], '^\s*')).Value
            $lines.Insert($e.Start + 1, ($indent + "//! catch-outlet: $($e.Item.class) owner=$Owner"))
        }
        $applied++
        Write-Host ("    [拟改] {0}:{1} [{2}] → {3}" -f $e.Item.file, $e.Item.line, $e.Item.class, $Transform) -ForegroundColor Yellow
    }

    if ($Apply) {
        if (-not (Test-Path $runBackupDir)) { New-Item -ItemType Directory -Path $runBackupDir -Force | Out-Null }
        Copy-Item -LiteralPath $abs -Destination (Join-Path $runBackupDir (($g.Name -replace '[/\\]', '__'))) -Force
        $backed++
        Set-Content -LiteralPath $abs -Value ($lines -join [Environment]::NewLine) -Encoding UTF8
        Write-Host ("    [已写+备份] {0}" -f $g.Name) -ForegroundColor Green
    }
}

Write-Host ''
Write-Host ("  汇总：拟改/已改 {0} · 跳过（漂移/缺件）{1} · 备份文件 {2}" -f $applied, $skipped, $backed) -ForegroundColor Cyan
if (-not $Apply) { Write-Host '  [DRY-RUN] 未落盘（加 -Apply 执行）' -ForegroundColor Yellow }
exit 0
