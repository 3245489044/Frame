#requires -Version 5.1
<#
.SYNOPSIS
    构建 owner-aware 函数索引：函数名 -> 所属文件集合（允许同名多属主，不丢弃）。
.DESCRIPTION
    扫描真实 .ps1/.psm1 定义点 + # @exports 声明，产出 ps-function-index.json。
    调用侧据此做 (owner,function) 限定，同名异文件实现互不覆盖。
.PARAMETER ScanRoots
    相对仓库根扫描的目录列表（默认 PowerShellScript + CentralTest/PowerShellTests）
.PARAMETER IndexOut
    输出 json（默认 = 本脚本同目录 ps-function-index.json）
.NOTES
    产物 = JSON 编译表（禁止手改）；仓库根经 .git 哨兵自锚定。
#>
param(
    [string[]]$ScanRoots = @('PowerShellScript','CentralTest/PowerShellTests'),
    [string]$IndexOut = ''
)
$ErrorActionPreference = 'Stop'

# --- 仓库根（.git 哨兵自锚定，无深度语义） ---
$repo = $PSScriptRoot
while ($repo -and -not (Test-Path -LiteralPath (Join-Path $repo '.git'))) { $repo = Split-Path -Parent $repo }
if (-not $repo) { throw '找不到仓库根（.git）' }

if (-not $IndexOut) { $IndexOut = Join-Path $PSScriptRoot 'ps-function-index.json' }

$files = @{}
$owners = @{}
$skipRx = '(\\|/)($|\.git)(\\|/)|node_modules|\\obj\\|\\bin\\|backups|Temp|_deprecated|\.vs\\'

foreach ($root in $ScanRoots) {
    $abs = Join-Path $repo $root
    if (-not (Test-Path -LiteralPath $abs)) { continue }
    Get-ChildItem -LiteralPath $abs -Recurse -Include '*.ps1','*.psm1' -File -ErrorAction SilentlyContinue | ForEach-Object {
        $full = $_.FullName
        if ($full -match $skipRx) { return }
        $rel = $full.Substring($repo.Length + 1).Replace('\','/')
        $kind = if ($_.Extension -eq '.psm1') { 'psm1' } else { 'ps1' }
        $funcs = [System.Collections.Generic.List[string]]::new()
        $tags  = [System.Collections.Generic.List[string]]::new()
        $n = 0
        foreach ($line in (Get-Content -LiteralPath $full -Encoding UTF8 -ErrorAction SilentlyContinue)) {
            $n++
            $t = $line.Trim()
            if ($t -match '^#\s*@exports\s*:?\s*(.+)$') { ($Matches[1] -split ',') | ForEach-Object { $x=$_.Trim(); if($x){[void]$tags.Add($x)} } ; if ($n -ge 80){continue} }
            if ($t -match '^function\s+([A-Za-z_][A-Za-z0-9_-]*)') { if (-not $funcs.Contains($Matches[1])) { [void]$funcs.Add($Matches[1]) } }
            if ($n -ge 3000) { break }
        }
        $all = @($funcs + ($tags | Where-Object { $_ -notin $funcs }))
        if ($all.Count -eq 0) { return }
        $files[$rel] = [ordered]@{ kind = $kind; funcs = @($funcs); exportsTag = @($tags) }
        foreach ($f in $all) {
            if (-not $owners.ContainsKey($f)) { $owners[$f] = [System.Collections.Generic.List[string]]::new() }
            if (-not $owners[$f].Contains($rel)) { [void]$owners[$f].Add($rel) }
        }
    }
}

# 归序（确定性）
$ownersSorted = [ordered]@{}
foreach ($k in ($owners.Keys | Sort-Object)) {
    $ownersSorted[$k] = @($owners[$k] | Sort-Object)
}

$index = [ordered]@{
    schema      = 'ps-function-owner-index-v1'
    builtBy     = 'Build-FunctionIndex.ps1'
    builtAt     = (Get-Date -Format o)
    repoAnchor  = '.git'
    fileCount   = $files.Count
    functionCount = $ownersSorted.Count
    ambiguousCount = @($ownersSorted.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 }).Count
    files       = $files
    owners      = $ownersSorted
}
($index | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $IndexOut -Encoding UTF8
Write-Host ('[Build-FunctionIndex] 文件={0} 函数={1} 多属主={2}' -f $files.Count,$ownersSorted.Count,$index.ambiguousCount) -ForegroundColor Cyan
Write-Host ('[Build-FunctionIndex] 写出: {0}' -f $IndexOut) -ForegroundColor Green
