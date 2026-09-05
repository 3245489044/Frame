#requires -Version 5.1
<#
.SYNOPSIS
    模块函数索引编译器 — 从源 .psm1 的 Export-ModuleMember 扫描生成函数→模块映射表

.DESCRIPTION
    Import-Modules.ps1 顶部的 $functionModuleMap 原为手工维护（随新增/重命名/删除函数而脱钩，
    导致 @requires 按需加载时"未找到映射"被静默吞掉）。

    本脚本将该表改为【编译产物】：扫描 PowerShellScript/ 全部 .psm1 的
    Export-ModuleMember -Function 导出（支持单行逗号 / @(...) 数组 / 跨行逗号三种形态），
    递归从源码编译出 `函数名 → 模块相对路径` 映射，重写回 Import-Modules.ps1 的
    $functionModuleMap 区（以标记注释圈定，禁止手改）。

    冲突处理（同一函数被多个模块导出，如 DockerCommon 重导出 BasicMethod）：
    - 若现有映射已为该函数指定权威模块 → 保留现有归属（避免改变已验证行为）；
    - 否则取首模块并输出警告，便于人工确认。

    $moduleOrder（全量加载顺序）因内含真实依赖顺序语义，不在编译范围（保持人工维护）。
    未配置依赖 Priority 的模块默认追加到其所在分组内。

    PowerShell 无 Export-ModuleMember 时回退：扫描 `function <Name> {` 定义点全部导出。

.PARAMETER ImportModulesPath
    Import-Modules.ps1 路径（默认 = Modules\Import-Modules.ps1 相对本脚本）

.PARAMETER PowerShellRoot
    PowerShellScript\ 根目录（默认 = 本脚本上级目录），作为映射相对路径基准

.PARAMETER WhatIf
    仅预览生成结果，不写盘

.EXAMPLE
    .\DSL\Build-ModuleIndex.ps1
.EXAMPLE
    .\DSL\Build-ModuleIndex.ps1 -WhatIf
#>
[CmdletBinding()]
param(
    [string]$ImportModulesPath,
    [string]$PowerShellRoot,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

if (-not $PowerShellRoot) {
    $PowerShellRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
}
if (-not $ImportModulesPath) {
    $ImportModulesPath = Join-Path $PowerShellRoot 'Modules\Import-Modules.ps1'
}

$powerShellRoot = [System.IO.Path]::GetFullPath($PowerShellRoot)
$importModulesPath = [System.IO.Path]::GetFullPath($ImportModulesPath)

# 读取现有映射（冲突归属优先级依据）+ 现有 $functionModuleMap 块文本
$existingLines = @(Get-Content -LiteralPath $importModulesPath -Encoding UTF8)
$existingMap = @{}
$mapStartMarker = 'PROTO_AUTO:functionModuleMap:BEGIN'
$mapEndMarker   = 'PROTO_AUTO:functionModuleMap:END'
$fcMapStartIdx = $null; $fcMapEndIdx = $null

# 若文件已含标记，则定位标记区间；否则定位现有 $functionModuleMap = @{ ... } 块
for ($li = 0; $li -lt $existingLines.Count; $li++) {
    if ($existingLines[$li] -match '#\s*<!--?\s*' + $mapStartMarker) { $fcMapStartIdx = $li }
    if ($existingLines[$li] -match '#\s*<!--?\s*' + $mapEndMarker)   { $fcMapEndIdx   = $li }
}
if ($null -eq $fcMapStartIdx -or $null -eq $fcMapEndIdx) {
    # 无标记 → 定位 $functionModuleMap 赋值块（起始行到第 0 列独立的闭合 }
    $fcMapStartIdx = $null; $fcMapEndIdx = $null
    for ($li = 0; $li -lt $existingLines.Count; $li++) {
        if ($existingLines[$li] -match '^\$functionModuleMap\s*=\s*@{') { $fcMapStartIdx = $li; break }
    }
    if ($null -ne $fcMapStartIdx) {
        for ($li = $fcMapStartIdx + 1; $li -lt $existingLines.Count; $li++) {
            if ($existingLines[$li].Trim() -eq '}') { $fcMapEndIdx = $li; break }
        }
    }
}
if ($null -eq $fcMapStartIdx -or $null -eq $fcMapEndIdx -or $fcMapEndIdx -lt $fcMapStartIdx) {
    throw "无法在 $importModulesPath 中定位 $functionModuleMap 块（请确认脚本顶部含该映射表）"
}

# 现有映射归属（函数名 → 相对路径），作为冲突裁决优先权
for ($li = $fcMapStartIdx; $li -le $fcMapEndIdx; $li++) {
    if ($existingLines[$li] -match '^\s*''([^'']+)''\s*=\s*''([^'']+)''' -or
        $existingLines[$li] -match '^\s*"([^"]+)"\s*=\s*"([^"]+)"') {
        $existingMap[$Matches[1]] = $Matches[2]
    }
}

Write-Host "== 模块函数索引编译器 ==" -ForegroundColor Cyan
Write-Host "索引根: $powerShellRoot"
Write-Host "目标文件: $importModulesPath"

# ---------- 收集全部 .psm1 ----------
$psmFiles = @(Get-ChildItem -Path $powerShellRoot -Recurse -Filter '*.psm1' -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.FullName -notmatch '\\node_modules\\|\\obj\\|\\bin\\|\\_deprecated\\|\\backups\\|\\BuildOutput\\'
    })

# ---------- 从单个模块解析导出的函数名 ----------
function Get-ExportedFunctions {
    param([string]$Path)
    [System.Collections.Generic.List[string]]$fns = [System.Collections.Generic.List[string]]::new()
    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    if ($null -eq $raw) { return @($fns) }

    $re = [regex]'(?m)Export-ModuleMember\s+-\s*Function'
    foreach ($m in ($re.Matches($raw))) {
        $i = $m.Index + $m.Length
        # 跳过空白
        while ($i -lt $raw.Length -and [char]::IsWhiteSpace($raw[$i])) { $i++ }
        if ($i -ge $raw.Length) { continue }
        # 数组语法为 @(...)：先跳过 @ 字符
        if ($raw[$i] -eq '@') {
            $i++
            while ($i -lt $raw.Length -and [char]::IsWhiteSpace($raw[$i])) { $i++ }
        }

        $tokenChars = ''
        if ($i -lt $raw.Length -and $raw[$i] -eq '(') {
            # @(...) 数组形态：括号感知 + 引号/注释感知解析
            $depth = 1; $i++
            while ($i -lt $raw.Length) {
                $c = $raw[$i]
                if ($c -eq "'" -or $c -eq '"') {
                    $q = $c; $i++
                    $tok = ''
                    while ($i -lt $raw.Length -and $raw[$i] -ne $q) { $tok += $raw[$i]; $i++ }
                    if ($i -lt $raw.Length) { $i++ }   # 跳过闭合引号
                    if ($tokenChars.Length -gt 0) { $tokenChars += ',' }
                    $tokenChars += $tok
                    continue
                }
                elseif ($c -eq '#') {
                    while ($i -lt $raw.Length -and $raw[$i] -ne "`n") { $i++ }
                }
                elseif ($c -eq '(') { $depth++ }
                elseif ($c -eq ')') {
                    $depth--
                    if ($depth -le 0) { break }
                }
                $i++
            }
        }
        else {
            # 单行/跨行逗号续行形态：名称间以逗号分隔，直到一行不以逗号结尾
            $segment = Get-BareFunctionSegment -Raw $raw -Start $i
            $tokenChars = $segment.Text
            $i = $segment.Next
        }

        foreach ($t in ($tokenChars -split ',')) {
            $name = $t.Trim()
            $name = $name.Trim("'").Trim('"')
            $name = $name.Trim()
            if ([string]::IsNullOrEmpty($name)) { continue }
            if ($name.StartsWith('$')) { continue }             # 动态导出（如 $fn）跳过
            if ($raw -match '#\s*@skip-export') { }             # 预留：模块级跳过标记
            if ($name -match '^[A-Za-z][A-Za-z0-9\-_.]*$' -and -not $fns.Contains($name)) {
                $fns.Add($name)
            }
        }
    }
    return @($fns)
}

# 读取"无括号"逗号形态的片段：逐行累积，直到某行不以逗号结尾
function Get-BareFunctionSegment {
    param([string]$Raw, [int]$Start)
    $sb = [System.Text.StringBuilder]::new()
    $i = $Start
    while ($i -lt $Raw.Length) {
        # 取当前行
        $nl = $Raw.IndexOf("`n", $i)
        if ($nl -lt 0) { $nl = $Raw.Length }
        $seg = $Raw.Substring($i, $nl - $i)
        if ($seg -match '#') { $seg = $seg.Substring(0, $seg.IndexOf('#')) }
        if ($sb.Length -gt 0) { [void]$sb.Append(',') }
        [void]$sb.Append($seg)
        if (-not $seg.TrimEnd().EndsWith(',')) { break }   # 不以逗号结尾 → 语句结束
        $i = $nl + 1   # 跳到下一行继续
    }
    return @{ Text = $sb.ToString(); Next = $i }
}

# 回退：无 Export-ModuleMember 时，扫描 function <Name> { 定义点
function Get-FunctionsFromDeclarations {
    param([string]$Path)
    [System.Collections.Generic.List[string]]$fns = [System.Collections.Generic.List[string]]::new()
    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    if ($null -eq $raw) { return @($fns) }
    foreach ($fnm in ([regex]::Matches($raw, '(?m)^\s*function\s+([A-Za-z][A-Za-z0-9\-_.]*)\s*\{'))) {
        $name = $fnm.Groups[1].Value
        if (-not $fns.Contains($name)) { $fns.Add($name) }
    }
    return @($fns)
}

# ---------- 编译映射（函数名 → 相对路径，冲突保留现有归属） ----------
$buildMap = @{}               # 函数名 → 相对路径（最终）
$origin = @{}                 # 函数名 → 首个来源文件（诊断）
$conflictMap = @{}            # 函数名 → 备选模块列表
$modFuncCount = @{}           # 相对路径 → 函数数（统计）

foreach ($f in $psmFiles) {
    $rel = $f.FullName.Substring($powerShellRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    $fns = @(Get-ExportedFunctions -Path $f.FullName)
    if ($fns.Count -eq 0) {
        $fns = @(Get-FunctionsFromDeclarations -Path $f.FullName)
    }
    $modFuncCount[$rel] = $fns.Count
    foreach ($fn in $fns) {
        if (-not $buildMap.ContainsKey($fn)) {
            $buildMap[$fn] = $rel
            $origin[$fn] = $f.Name
        }
        elseif ($buildMap[$fn] -ne $rel) {
            # 冲突：记录备选
            if (-not $conflictMap.ContainsKey($fn)) { $conflictMap[$fn] = [System.Collections.Generic.List[string]]::new() }
            if ($buildMap[$fn] -notin $conflictMap[$fn]) { $conflictMap[$fn].Add($buildMap[$fn]) }
            if ($rel -notin $conflictMap[$fn]) { $conflictMap[$fn].Add($rel) }
        }
    }
}

# 冲突裁决：现有 map 若为该函数指定了权威模块 → 覆盖为现有归属
foreach ($fn in $conflictMap.Keys) {
    if ($existingMap.ContainsKey($fn)) {
        $resolved = $existingMap[$fn]
        if ($resolved -match '\\') { $resolved = $resolved -replace '\\', '/' }
        Write-Warning "[冲突已保留现有归属] $fn → $resolved (候选: $($conflictMap[$fn] -join ' / '))"
        $buildMap[$fn] = $resolved
    } else {
        Write-Warning "[冲突未决] $fn → 默认 $($buildMap[$fn]) (候选: $($conflictMap[$fn] -join ' / '))"
    }
}

# ---------- 渲染新映射文本（按目录分组 + 对齐缩进） ----------
$groups = [ordered]@{}
foreach ($fn2 in ($buildMap.Keys | Sort-Object)) {
    $relP = $buildMap[$fn2]
    if (-not $groups.Contains($relP)) { $groups[$relP] = [System.Collections.Generic.List[string]]::new() }
    $groups[$relP].Add($fn2)
}

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("# ============================================================")
[void]$sb.AppendLine("# 函数→模块映射表（IoC 核心）— 编译产物")
[void]$sb.AppendLine("# 由 DSL\Build-ModuleIndex.ps1 从源 .psm1 Export-ModuleMember 扫描生成，禁止手改")
[void]$sb.AppendLine("# 新增/重命名/删除函数后，重新运行：.\\DSL\\Build-ModuleIndex.ps1")
[void]$sb.AppendLine("# <!-- PROTO_AUTO:functionModuleMap:BEGIN -->")
[void]$sb.AppendLine("# ============================================================")
[void]$sb.AppendLine('$functionModuleMap = @{')
foreach ($relP in ($groups.Keys | Sort-Object)) {
    $sec = Split-Path $relP -Parent
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("    # ===== $sec ===== ".TrimEnd())
    $funcList = @($groups[$relP])
    $width = 0
    foreach ($fn2 in $funcList) { if ($fn2.Length -gt $width) { $width = $fn2.Length } }
    foreach ($fn2 in $funcList) {
        $pad = ' ' * ([Math]::Max(0, $width - $fn2.Length + 4))
        [void]$sb.AppendLine("    '" + $fn2 + "'" + $pad + " = '" + $relP + "'")
    }
}
[void]$sb.AppendLine('}')
[void]$sb.AppendLine('# <!-- PROTO_AUTO:functionModuleMap:END -->')
$newMapBlock = $sb.ToString().TrimEnd("`r", "`n")

# ---------- 预览 ----------
Write-Host ""
Write-Host "--- 编译统计 ---" -ForegroundColor Cyan
Write-Host "扫描模块文件 : $($psmFiles.Count)"
Write-Host "映射函数总数 : $($buildMap.Keys.Count)"
Write-Host "冲突函数数   : $($conflictMap.Keys.Count)"
$groupSizes = @($groups.GetEnumerator() | ForEach-Object { "$($_.Key) [$($_.Value.Count)]" })
Write-Host "模块分组     : $($groupSizes -join ', ')"

if ($WhatIf) {
    Write-Host ""
    Write-Host "--- [WhatIf] 生成的新映射块预览 ---" -ForegroundColor Yellow
    $newMapBlock -split "`n" | ForEach-Object { Write-Host $_ }
    return
}

# ---------- 写回（替换映射区） ----------
$hasMarkers = ($existingLines | Where-Object { $_ -match '<!--\s*PROTO_AUTO:functionModuleMap' }).Count -gt 0

$newLines = [System.Collections.Generic.List[string]]::new()
if ($hasMarkers) {
    # 已有标记：保留 BEGIN 标记之前的行
    for ($li = 0; $li -lt $fcMapStartIdx; $li++) { $newLines.Add($existingLines[$li]) }
    # BEGIN 标记行本身（编译头）作为新块开头
    foreach ($ln in ($newMapBlock -split "`n")) { $newLines.Add($ln) }
    # 跳过原 BEGIN→END 的全部行，从 END 之后继续
    $afterEnd = $fcMapEndIdx + 1
    for ($li = $afterEnd; $li -lt $existingLines.Count; $li++) { $newLines.Add($existingLines[$li]) }
} else {
    # 无标记（首次）：保留 $functionModuleMap 之前的行，替换原块，保留其后行
    $keepBefore = @()
    for ($li = 0; $li -lt $fcMapStartIdx; $li++) { $keepBefore += $existingLines[$li] }
    # 去掉块前紧邻的分隔注释行（# ====...），由新编译头接管
    while ($keepBefore.Count -gt 0 -and $keepBefore[-1].Trim() -match '^#\s*=+') {
        $keepBefore = @($keepBefore[0..($keepBefore.Count - 2)])
    }
    foreach ($ln in $keepBefore) { $newLines.Add($ln) }
    foreach ($ln in ($newMapBlock -split "`n")) { $newLines.Add($ln) }
    for ($li = $fcMapEndIdx + 1; $li -lt $existingLines.Count; $li++) {
        $candidate = $existingLines[$li]
        # 跳过块后紧邻的空行/分隔线，落到真正后续内容
        if ($li -eq $fcMapEndIdx + 1 -and $candidate.Trim() -match '^(#\s*=+|)$') { continue }
        $newLines.Add($candidate)
    }
}

# 写盘 + 读回校验
$newContent = ($newLines -join "`n")
Set-Content -LiteralPath $importModulesPath -Value $newContent -Encoding UTF8

$verifyText = Get-Content -LiteralPath $importModulesPath -Raw -Encoding UTF8
$wroteOk = $verifyText.TrimEnd("`r", "`n") -eq $newContent.TrimEnd("`r", "`n")

Write-Host ""
Write-Host "--- 写回结果 ---" -ForegroundColor Cyan
if ($wroteOk) {
    $h = [System.BitConverter]::ToString([System.Security.Cryptography.SHA1]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($newContent)))
    Write-Host "写回校验 : OK($($h.Substring(0,8))) 函数映射 $($buildMap.Keys.Count) 个 → $importModulesPath" -ForegroundColor Green
} else {
    Write-Host "写回校验 : FAILED（读回内容与写入不一致）" -ForegroundColor Red
    exit 1
}
Write-Host "新增/重命名/删除函数后重新运行本脚本即可重新编译映射（幂等）。" -ForegroundColor DarkGray