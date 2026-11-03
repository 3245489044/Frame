#requires -Version 5.1
<#
.SYNOPSIS
    catch 出口盘点（CatchTriage）—— 「禁静默出口 T/N/D」的 catch 面**诊断期**工具（只读，零裁决）。

.DESCRIPTION
    背景：Docs\analysis\2026\10\10.17\2026-10-17-禁静默出口-通用框架-M0建面与后续施工单.md（M3）
    与 Docs\analysis\2026\10\10.5\2026-10-04-框架回退逻辑彻底消除-重构方案.md（C2 catch 吞并）。

    分类内核单一实现 = Meta\CatchOutlet.psm1（与门禁 Check-FallbackInventory 同源，防判据分叉）。
    本件把「全库 catch 块」摊平为**机读清单**（file:line / 异常类型 / 分类 / 块体哈希 / 是否已登记），
    供 Invoke-CatchOutletPatch（批修）逐批消费；自身只读仓库、零侧写、不产 pass/fail 裁决（TH-17）。

    登记口径：fallback-registry.json 的 entries（type=C2，file 可带 :line）视为合法；
    未登记且分类 ∈ {candidate-*} 者计入候选（与门禁棘轮同一计数面）。

.PARAMETER Root
    仓库超根（缺省 = 上溯五级）。
.PARAMETER OutFile
    机读清单输出（缺省 = Registry\catch-triage.json）。
.PARAMETER Top
    控制台输出的热点文件条数（缺省 15）。
.PARAMETER ShowDetail
    逐条列出候选（file:line + 分类）。
.EXAMPLE
    .\Invoke-CatchTriage.ps1
.EXAMPLE
    .\Invoke-CatchTriage.ps1 -ShowDetail -Top 30
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$OutFile = '',
    [int]$Top = 15,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $OutFile = Join-Path $PSScriptRoot '..\Registry\catch-triage.json'
}

Import-Module (Join-Path $PSScriptRoot '..\Meta\TextOps.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\Meta\CatchOutlet.psm1') -Force -DisableNameChecking

# 分类内核自证（防判据空转）
$selfFail = Test-CatchOutletClassifier

$registryPath = Join-Path $PSScriptRoot '..\Registry\fallback-registry.json'
$entries = @()
if (Test-Path -LiteralPath $registryPath) {
    $reg = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json
    if ($null -ne $reg.entries) { $entries = @($reg.entries) }
}

function Test-CatchRegistered([string]$Rel, [int]$Line) {
    $relNorm = $Rel -replace '\\', '/'
    foreach ($e in $entries) {
        if ($null -eq $e -or [string]::IsNullOrWhiteSpace($e.file)) { continue }
        if ("$($e.type)" -ne 'C2') { continue }
        $parts = ($e.file -replace '\\', '/') -split ':'
        $f = $parts[0]
        $ln = $null
        if ($parts.Count -gt 1 -and $parts[1] -match '^\d+$') { $ln = [int]$parts[1] }
        if ($relNorm -like "*$f" -and ($null -eq $ln -or $ln -eq $Line)) { return $true }
    }
    return $false
}

$scanRoots = @()
foreach ($sub in @('Frame\CsharpPrj', 'Projects\Plug\Agent\src')) {
    $p = Join-Path $Root $sub
    if (Test-Path -LiteralPath $p) { $scanRoots += (Resolve-Path $p).Path }
}
$excludeRx = '\\(obj|bin|_deprecated|BuildOutput|backups)\\'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  catch 出口盘点（CatchTriage：禁静默出口 T/N/D · catch 面）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$items = [System.Collections.Generic.List[object]]::new()

# 1) 扫描文件清单（PS 负责扫描/排除；分类交 Roslyn 工具）
$files = [System.Collections.Generic.List[string]]::new()
foreach ($scanRoot in $scanRoots) {
    foreach ($f in @(Get-ChildItem -LiteralPath $scanRoot -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch $excludeRx })) {
        $files.Add($f.FullName)
    }
}

# 2) 调 Roslyn 工具（复用 RoslynMethod.Core：CSharpSourceProvider / QuerySkeleton / RoslynSourceLocator）
$engine = 'text'
$rawItems = $null
if ($files.Count -gt 0) {
    $listFile = Join-Path ([System.IO.Path]::GetTempPath()) ('catch-files-' + [guid]::NewGuid().ToString('N') + '.txt')
    $rawFile = Join-Path ([System.IO.Path]::GetTempPath()) ('catch-raw-' + [guid]::NewGuid().ToString('N') + '.json')
    Set-Content -LiteralPath $listFile -Value ($files -join [Environment]::NewLine) -Encoding UTF8
    $toolDll = Join-Path $Root 'Frame\CsharpPrj\BuildOutput\Debug\CatchOutletTriage.dll'
    $toolCsproj = Join-Path $Root 'Frame\CsharpPrj\Plate\RoslynMethod.Tools\CatchOutletTriage\CatchOutletTriage.csproj'
    if (Test-Path -LiteralPath $toolDll) {
        & dotnet $toolDll -files $listFile -out $rawFile 2>&1 | ForEach-Object { Write-Host ("    " + $_) -ForegroundColor DarkGray }
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $rawFile)) { $rawItems = (Get-Content -LiteralPath $rawFile -Raw | ConvertFrom-Json).items; $engine = 'roslynmethod' }
    } elseif (Test-Path -LiteralPath $toolCsproj) {
        & dotnet run --project $toolCsproj -c Debug -- -files $listFile -out $rawFile 2>&1 | ForEach-Object { Write-Host ("    " + $_) -ForegroundColor DarkGray }
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $rawFile)) { $rawItems = (Get-Content -LiteralPath $rawFile -Raw | ConvertFrom-Json).items; $engine = 'roslynmethod' }
    }
}

# 3) 组装（工具给分类；PS 给块体哈希 + 登记过滤）
if ($null -ne $rawItems) {
    $lineCache = @{}
    foreach ($ri in $rawItems) {
        $rel = $ri.file
        if ($rel.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) { $rel = $rel.Substring($Root.Length).TrimStart('\') }
        if (-not $lineCache.ContainsKey($ri.file)) { $lineCache[$ri.file] = [System.IO.File]::ReadAllLines($ri.file) }
        $lines = $lineCache[$ri.file]
        $s = [int]$ri.startLine; $e = [int]$ri.endLine
        $body = ''
        if ($e -ge $s -and $s -ge 1 -and $e -le $lines.Count) {
            $body = ((@($lines[($s - 1)..($e - 1)] | ForEach-Object { (Remove-LineComment -Text $_) }) -join ' ') + ' ')
        }
        $registered = Test-CatchRegistered -Rel $rel -Line $s
        $items.Add([pscustomobject]@{
            file       = ($rel -replace '\\', '/')
            line       = $s
            endLine    = $e
            exception  = $ri.exception
            class      = $ri.cls
            bodyHash   = (Get-CatchBodyHash -Body $body)
            registered = $registered
            candidate  = ($ri.cls -like 'candidate-*') -and (-not $registered)
        })
    }
} else {
    Write-Host '  [降级] Roslyn 工具不可用 ⇒ 文本引擎（CatchOutlet.psm1）' -ForegroundColor Yellow
    foreach ($fp in $files) {
        $rel = $fp
        if ($rel.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) { $rel = $rel.Substring($Root.Length).TrimStart('\') }
        $lines = [System.IO.File]::ReadAllLines($fp)
        foreach ($b in (Get-CatchBlocks -Lines $lines)) {
            $registered = Test-CatchRegistered -Rel $rel -Line $b.StartLine
            $items.Add([pscustomobject]@{
                file       = ($rel -replace '\\', '/')
                line       = $b.StartLine
                endLine    = $b.EndLine
                exception  = $b.Exception
                class      = $b.Class
                bodyHash   = $b.BodyHash
                registered = $registered
                candidate  = ($b.Class -like 'candidate-*') -and (-not $registered)
            })
        }
    }
}

$all = @($items)
$candidate = @($all | Where-Object { $_.candidate })
$byClass = $all | Group-Object class | Sort-Object Name |
    ForEach-Object { [pscustomobject]@{ class = $_.Name; count = $_.Count } }

$payload = [ordered]@{
    schema    = 'catch-triage-v1'
    generated = (Get-Date -Format 'yyyy-MM-dd')
    engine    = $engine
    scan      = [ordered]@{ roots = $scanRoots; exclude = @('obj', 'bin', '_deprecated', 'BuildOutput', 'backups') }
    totals    = [ordered]@{
        catchTotal = $all.Count
        candidate  = $candidate.Count
        registered = @($all | Where-Object { $_.registered }).Count
    }
    byClass   = $byClass
    items     = $all
}
$payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutFile -Encoding UTF8

Write-Host ("  扫面: {0}" -f (($scanRoots | ForEach-Object { $_.Substring($Root.Length).TrimStart('\') }) -join ' ∪ ')) -ForegroundColor Gray
Write-Host ("  catch 总数 {0} · 候选（未登记）{1} · 已登记 {2}" -f $all.Count, $candidate.Count, (@($all | Where-Object { $_.registered }).Count)) -ForegroundColor Cyan
foreach ($c in $byClass) { Write-Host ("    {0,-20} {1}" -f $c.class, $c.count) -ForegroundColor Gray }

if ($candidate.Count -gt 0) {
    Write-Host ("  --- 候选热点（Top {0}）---" -f $Top) -ForegroundColor Yellow
    foreach ($g in ($candidate | Group-Object file | Sort-Object Count -Descending | Select-Object -First $Top)) {
        Write-Host ("    {0,4}  {1}" -f $g.Count, $g.Name) -ForegroundColor Yellow
    }
}
if ($ShowDetail) {
    foreach ($it in $candidate) { Write-Host ("    [候选] {0}:{1}  [{2}]" -f $it.file, $it.line, $it.class) -ForegroundColor DarkGray }
}

Write-Host ("  清单: {0}" -f (Resolve-Path $OutFile).Path) -ForegroundColor Gray
if ($selfFail -gt 0) {
    Write-Host ("  [自检] 分类内核正/负对照失败 {0} 项（判据可能被改坏）" -f $selfFail) -ForegroundColor Red
    exit 1
}
Write-Host '  [成功] 分类内核正/负对照通过（零裁决：产物 = 事实清单）' -ForegroundColor Green
exit 0
