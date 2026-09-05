<#
.SYNOPSIS
    将 UniversalReferenceLayer.cs 按顶层 namespace 拆分为独立文件，并按"数据访问 / 引用关系"归类到文件夹。
    namespace 保持不变（消费者零改动）。方案1：仅结构性拆分，不改语义。
.DESCRIPTION
    目标布局:
      DataAccess/GraphStorage/GraphStorageRepositories.cs    -> namespace ReferenceFrame.GraphStorage
      DataAccess/ComplexData/ComplexDataRepository.cs        -> namespace ComplexDataAbstraction
      DataAccess/MetaGraph/GraphService.cs                   -> namespace MetaArchitecture
      DataAccess/ModuleFramework/ModuleCore.cs               -> UniversalModuleFramework.Core
      DataAccess/ModuleFramework/ModuleRepositories.cs       -> UniversalModuleFramework.Repositories
      DataAccess/ModuleFramework/ModuleServices.cs           -> UniversalModuleFramework.Services
      DataAccess/ModuleFramework/ModuleUnitOfWork.cs         -> UniversalModuleFramework.UnitOfWork
      DataAccess/ModuleFramework/ModuleEvents.cs             -> UniversalModuleFramework.Events
      DataAccess/ModuleFramework/ModuleCatalog.cs            -> UniversalModuleFramework.Catalog
      Reference/ReferenceManagement.cs                       -> ReferenceManagement
      Reference/FieldMapping.cs                              -> ReferenceManagement.FieldMapping
      Reference/Relationship/RelationshipCore.cs            -> RelationshipManagement.Core
      Reference/Relationship/RelationshipExceptions.cs      -> RelationshipManagement.Exceptions
      Reference/Relationship/RelationshipDispatcher.cs      -> RelationshipManagement.Dispatcher
      Reference/Relationship/RelationshipConfiguration.cs   -> RelationshipManagement.Configuration
.USAGE
    pwsh -File PowerShellScript\Split-UniversalReferenceLayer.ps1
.NOTES
    仅拆分；不会删除原文件（调用方手动改名/删除）。
#>
[CmdletBinding()]
param(
    [string]$Source = 'D:\Project\Program\WorkFlowPrj\CsharpPrj\Share\UniversalReference\UniversalReferenceLayer.cs',
    [string]$ProjectRoot = 'D:\Project\Program\WorkFlowPrj\CsharpPrj\Share\UniversalReference'
)

$ErrorActionPreference = 'Stop'

# ---- namespace -> 目标相对路径（相对于 ProjectRoot） ----
$targets = @(
    @{ ns = 'ReferenceFrame.GraphStorage';         file = 'DataAccess\GraphStorage\GraphStorageRepositories.cs' }
    @{ ns = 'ComplexDataAbstraction';              file = 'DataAccess\ComplexData\ComplexDataRepository.cs' }
    @{ ns = 'MetaArchitecture';                    file = 'DataAccess\MetaGraph\GraphService.cs' }
    @{ ns = 'UniversalModuleFramework.Core';       file = 'DataAccess\ModuleFramework\ModuleCore.cs' }
    @{ ns = 'UniversalModuleFramework.Repositories'; file = 'DataAccess\ModuleFramework\ModuleRepositories.cs' }
    @{ ns = 'UniversalModuleFramework.Services';   file = 'DataAccess\ModuleFramework\ModuleServices.cs' }
    @{ ns = 'UniversalModuleFramework.UnitOfWork'; file = 'DataAccess\ModuleFramework\ModuleUnitOfWork.cs' }
    @{ ns = 'UniversalModuleFramework.Events';     file = 'DataAccess\ModuleFramework\ModuleEvents.cs' }
    @{ ns = 'UniversalModuleFramework.Catalog';    file = 'DataAccess\ModuleFramework\ModuleCatalog.cs' }
    @{ ns = 'ReferenceManagement';                 file = 'Reference\ReferenceManagement.cs' }
    @{ ns = 'ReferenceManagement.FieldMapping';    file = 'Reference\FieldMapping.cs' }
    @{ ns = 'RelationshipManagement.Core';         file = 'Reference\Relationship\RelationshipCore.cs' }
    @{ ns = 'RelationshipManagement.Exceptions';   file = 'Reference\Relationship\RelationshipExceptions.cs' }
    @{ ns = 'RelationshipManagement.Dispatcher';   file = 'Reference\Relationship\RelationshipDispatcher.cs' }
    @{ ns = 'RelationshipManagement.Configuration';file = 'Reference\Relationship\RelationshipConfiguration.cs' }
)

$raw = [System.IO.File]::ReadAllText($Source)

# ---- 构建行号 -> 字符偏移 ----
$lineOffsets = New-Object System.Collections.Generic.List[int]
$lineOffsets.Add(0)
for ($k = 0; $k -lt $raw.Length; $k++) {
    if ($raw[$k] -eq "`n") { $lineOffsets.Add($k + 1) }
}
function Get-LineOffset([int]$line) { # line 1-based
    if ($line -lt $lineOffsets.Count) { return $lineOffsets[$line - 1] }
    return $raw.Length
}

# ---- 括号匹配扫描器（注释/字符串/插值感知） ----
function Find-MatchingBrace([string]$text, [int]$openIndex) {
    $n = $text.Length
    $depth = 0
    $i = $openIndex
    $mode = 'code'
    $interpDepth = 0
    while ($i -lt $n) {
        $c = $text[$i]
        $n2 = if ($i + 1 -lt $n) { $text[$i + 1] } else { [char]0 }
        switch ($mode) {
            'code' {
                if ($c -eq '/' -and $n2 -eq '/') { $mode = 'line'; $i += 2; continue }
                if ($c -eq '/' -and $n2 -eq '*') { $mode = 'block'; $i += 2; continue }
                if ($c -eq '"') {
                    if ($i -ge 2 -and $text[$i - 1] -eq '@' -and $text[$i - 2] -eq '$') { $mode = 'strIv' }
                    elseif ($i -ge 1 -and $text[$i - 1] -eq '@') { $mode = 'strv' }
                    elseif ($i -ge 1 -and $text[$i - 1] -eq '$') { $mode = 'strI' }
                    else { $mode = 'str' }
                    $i++; continue
                }
                if ($c -eq "'") { $mode = 'char'; $i++; continue }
                if ($c -eq '{') { $depth++; if ($depth -eq 1) { $openBrace = $i }; $i++; continue }
                if ($c -eq '}') { $depth--; if ($depth -eq 0) { return $i }; $i++; continue }
                $i++; continue
            }
            'line' { if ($c -eq "`n") { $mode = 'code' }; $i++; continue }
            'block' { if ($c -eq '*' -and $n2 -eq '/') { $mode = 'code'; $i += 2; continue }; $i++; continue }
            'str'  { if ($c -eq '\') { $i += 2; continue }; if ($c -eq '"') { $mode = 'code'; $i++; continue }; $i++; continue }
            'strv' { if ($c -eq '"' -and $n2 -eq '"') { $i += 2; continue }; if ($c -eq '"') { $mode = 'code'; $i++; continue }; $i++; continue }
            'char' { if ($c -eq '\') { $i += 2; continue }; if ($c -eq "'") { $mode = 'code'; $i++; continue }; $i++; continue }
            'strI' {
                if ($c -eq '\') { $i += 2; continue }
                if ($c -eq '"') { $mode = 'code'; $i++; continue }
                if ($c -eq '{' -and $n2 -eq '{') { $i += 2; continue }
                if ($c -eq '{') { $interpDepth = 1; $mode = 'interp'; $i++; continue }
                if ($c -eq '}' -and $n2 -eq '}') { $i += 2; continue }
                $i++; continue
            }
            'strIv' {
                if ($c -eq '"' -and $n2 -eq '"') { $i += 2; continue }
                if ($c -eq '"') { $mode = 'code'; $i++; continue }
                if ($c -eq '{' -and $n2 -eq '{') { $i += 2; continue }
                if ($c -eq '{') { $interpDepth = 1; $mode = 'interp'; $i++; continue }
                if ($c -eq '}' -and $n2 -eq '}') { $i += 2; continue }
                $i++; continue
            }
            'interp' {
                if ($c -eq '\') { $i += 2; continue }
                if ($c -eq '"') { $mode = 'interpStr'; $i++; continue }
                if ($c -eq "'") { $mode = 'interpChar'; $i++; continue }
                if ($c -eq '/' -and $n2 -eq '/') { $mode = 'interpLine'; $i += 2; continue }
                if ($c -eq '/' -and $n2 -eq '*') { $mode = 'interpBlock'; $i += 2; continue }
                if ($c -eq '{') { $interpDepth++; $i++; continue }
                if ($c -eq '}') {
                    $interpDepth--
                    if ($interpDepth -eq 0) { $mode = 'strI'; $i++; continue }
                    $i++; continue
                }
                $i++; continue
            }
            'interpStr'  { if ($c -eq '\') { $i += 2; continue }; if ($c -eq '"') { $mode = 'interp'; $i++; continue }; $i++; continue }
            'interpChar' { if ($c -eq '\') { $i += 2; continue }; if ($c -eq "'") { $mode = 'interp'; $i++; continue }; $i++; continue }
            'interpLine' { if ($c -eq "`n") { $mode = 'interp' }; $i++; continue }
            'interpBlock'{ if ($c -eq '*' -and $n2 -eq '/') { $mode = 'interp'; $i += 2; continue }; $i++; continue }
        }
    }
    return -1
}

# ---- 共享 using 头（取自原文件顶部，逐文件复制以保持可解析性） ----
$sharedUsings = @(
    'using TConfig.ContextFrame.Abstractions;'
    'using GraphFoundation;'
    'using GraphFoundation.Abstractions;'
    'using GraphFoundation.Core;'
    'using System;'
    'using System.Collections.Concurrent;'
    'using System.Collections.Generic;'
    'using System.IO;'
    'using System.Linq;'
    'using System.Linq.Expressions;'
    'using System.Reactive.Subjects;'
    'using System.Reflection;'
    'using System.Text.Json;'
    'using System.Threading;'
    'using System.Threading.Tasks;'
    'using UniversalGraphStorage;'
    'using UniversalModuleFramework.Core;'
    'using UniversalModuleFramework.Repositories;'
    'using UniversalModuleFramework.UnitOfWork;'
)

$header = @(
    '// <auto-generated>'
    '// 由 PowerShellScript\Split-UniversalReferenceLayer.ps1 从 UniversalReferenceLayer.cs 按 namespace 拆分生成。'
    '// namespace 保持不变；仅结构性整理，不改语义。'
    '// </auto-generated>'
    '#nullable enable'
    ''
) + $sharedUsings

# ---- 定位每个 namespace 的声明行 ----
$nsLine = @{}
foreach ($t in $targets) {
    $name = $t.ns
    $re = '^namespace\s+' + [regex]::Escape($name) + '\s*$'
    $m = [regex]::Match($raw, $re, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $m.Success) { throw "namespace $name 未找到" }
    $off = $m.Index
    $ln = 1
    for ($z = 0; $z -lt $lineOffsets.Count - 1; $z++) { if ($lineOffsets[$z] -le $off -and $off -lt $lineOffsets[$z + 1]) { $ln = $z + 1; break } }
    $nsLine[$name] = $ln
}

# ---- 拆分并写出 ----
$created = @()
foreach ($t in $targets) {
    $name = $t.ns
    $startLine = $nsLine[$name]
    $startOff = Get-LineOffset $startLine

    # 找 namespace 声明后的第一个 '{'
    $openIdx = -1
    $i = $startOff
    while ($i -lt $raw.Length) {
        if ($raw[$i] -eq '{') { $openIdx = $i; break }
        $i++
    }
    if ($openIdx -lt 0) { throw "namespace $name 未找到开括号" }

    $closeIdx = Find-MatchingBrace $raw $openIdx
    if ($closeIdx -lt 0) { throw "namespace $name 未找到配对的闭括号" }

    $block = $raw.Substring($startOff, $closeIdx - $startOff + 1)

    $outPath = Join-Path $ProjectRoot $t.file
    $outDir = Split-Path $outPath -Parent
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $content = ($header -join "`r`n") + "`r`n`r`n" + $block + "`r`n"
    [System.IO.File]::WriteAllText($outPath, $content, [System.Text.UTF8Encoding]::new($false))
    $created += $outPath
    Write-Host ("OK  {0}" -f $t.file)
}

Write-Host "`n已生成 $($created.Count) 个文件："
$created | ForEach-Object { Write-Host ("  " + $_) }


