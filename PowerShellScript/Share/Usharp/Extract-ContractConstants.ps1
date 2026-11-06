<#
.SYNOPSIS
    一次性提取器（P2）—— 从 C# 源码逐字提取 public const，产出 [Const] 声明面。

.DESCRIPTION
    输入 = Agent.Contract 源码树；输出 = [Const] 单元的 .ushar 文本 + residual 清单。

    纪律：
      · 值**逐字**取自源码（正则捕获原文，禁手抄、禁命名转换、禁引号改写）；
      · 非白名单表达式（拼接 / 方法调用 / 非字面量引用）不入声明，落 residual 清单（可见化，禁静默丢）；
      · 生成物自身（*.g.cs）与 static readonly 不在采集面；
      · 同 (target, valueType) 的成员保源码序。

.PARAMETER SourceRoot
    源码根（缺省 = Projects\Plug\Agent\src\Agent.Contract）。

.PARAMETER OutFile
    输出 .ushar 路径（缺省 = Projects\Plug\Agent\Config\keyspaces\contract\contract-consts.ushar）。

.PARAMETER ResidualFile
    residual 清单路径（缺省 = 与 OutFile 同目录 contract-consts.residual.txt）。

.PARAMETER Check
    只统计（不写盘）：打印 单元数 / 成员数 / residual 数。

.PARAMETER Force
    允许覆盖已存在的声明面。**默认拒绝覆盖**：源侧常量删除后再扫描会得到「几乎为空」的声明面
    （会把声明面缩小成不可恢复的错误状态），故重扫必须显式 -Force 并配合 git diff 复核。
#>
[CmdletBinding()]
param(
    [string]$SourceRoot = 'Projects\Plug\Agent\src\Agent.Contract',
    [string]$OutFile = 'Projects\Plug\Agent\Config\keyspaces\contract\contract-consts.ushar',
    [string]$ResidualFile = '',
    [switch]$Check,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
Import-Module (Join-Path $PSScriptRoot 'UsharpSourceScan.psm1') -Force
function Resolve-Repo([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return (Join-Path $root $Path)
}

$sourceAbs = Resolve-Repo $SourceRoot
$outAbs = Resolve-Repo $OutFile
if (-not $ResidualFile) { $ResidualFile = Join-Path (Split-Path $outAbs -Parent) 'contract-consts.residual.txt' }
$residualAbs = Resolve-Repo $ResidualFile

$typeDecl = '^\s*(?:public|internal)\s+(?:(static|sealed|abstract)\s+)?(partial\s+)?(class|record|struct)\s+([A-Za-z_]\w*)'
$constDecl = '^\s*public\s+const\s+(string|int)\s+([A-Za-z_]\w*)\s*=\s*(.+?);\s*$'
$namespaceDecl = '^\s*namespace\s+([A-Za-z_][\w.]*)'

$units = [ordered]@{}          # key: "target|valueType"
$residual = New-Object System.Collections.Generic.List[string]
$typeCount = 0

foreach ($file in (Get-UsharpSourceFiles -SourceRoot $sourceAbs)) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    $namespace = ''
    $depth = 0
    $typeStack = New-Object System.Collections.Generic.List[object]

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $startDepth = $depth

        # 行首扫描前先判声明（声明行本身不含 '{' 常见；含 '{' 的同一行也由下方括号计数处理）
        if ($line -match $namespaceDecl) { $namespace = $Matches[1] }

        $top = if ($typeStack.Count -gt 0) { $typeStack[$typeStack.Count - 1] } else { $null }

        if ($line -match $typeDecl) {
            $modifier = if ($Matches[1]) { $Matches[1] } else { 'plain' }
            $kind = $Matches[3]
            $name = $Matches[4]
            $target = if ($null -ne $top) { "$($top.Target).$name" }
                      elseif ($namespace) { "$namespace.$name" }
                      else { $name }
            $frame = [pscustomobject]@{
                Target    = $target
                Kind      = $kind
                Modifier  = $modifier
                BodyDepth = $startDepth + 1
                Opened    = $false
                Nested    = ($null -ne $top)
            }
            $typeStack.Add($frame)
            if ($null -eq $top) { $typeCount++ }
            else { $residual.Add(("{0}:{1} 嵌套类型 {2} 不声明化（其 const 保持手写）" -f $file.Name, ($i + 1), $target)) }
            $top = $frame
        }

        # 成员采集：类型体内一层
        if ($null -ne $top -and $startDepth -eq $top.BodyDepth -and $line -match $constDecl) {
            $valueType = $Matches[1]
            $member = $Matches[2]
            $expr = $Matches[3].Trim()

            $allowed =
                ($expr -match '^"([^"\\]|\\.)*"$') -or
                ($expr -match '^@"([^"]|"")*"$') -or
                ($expr -match '^-?(0|[1-9]\d*)$') -or
                ($expr -match '^nameof\(\s*[A-Za-z_]\w*(\.[A-Za-z_]\w*)+\s*\)$') -or
                ($expr -match '^[A-Za-z_]\w*(\.[A-Za-z_]\w*)+$')

            if ($top.Nested) {
                $residual.Add(("{0}:{1} {2}.{3} = {4}（嵌套类型内 const，保持手写）" -f $file.Name, ($i + 1), $top.Target, $member, $expr))
            }
            elseif (-not $allowed) {
                $residual.Add(("{0}:{1} {2}.{3} = {4}（表达式不在白名单）" -f $file.Name, ($i + 1), $top.Target, $member, $expr))
            }
            else {
                $key = '{0}|{1}' -f $top.Target, $valueType
                if (-not $units.Contains($key)) {
                    $units[$key] = [pscustomobject]@{
                        Target    = $top.Target
                        Kind      = $top.Kind
                        Modifier  = $top.Modifier
                        ValueType = $valueType
                        Members   = New-Object System.Collections.Generic.List[object]
                    }
                }
                $units[$key].Members.Add([pscustomobject]@{ Name = $member; Value = $expr })
            }
        }

        # 括号计数（去字符串字面量后计数，避免 '"{"' 干扰）
        $scan = [regex]::Replace($line, '"(?:[^"\\]|\\.)*"', '""')
        $scan = [regex]::Replace($scan, "@""(?:[^""]|"""")*""", '@""')
        $scan = [regex]::Replace($scan, '//.*$', '')
        $depth += ([regex]::Matches($scan, '\{')).Count - ([regex]::Matches($scan, '\}')).Count

        # 类型闭合清栈（须已开体：声明行本身不含 '{' 时不得立即清栈）
        while ($typeStack.Count -gt 0) {
            $frame = $typeStack[$typeStack.Count - 1]
            if ($depth -ge $frame.BodyDepth) { $frame.Opened = $true; break }
            if ($frame.Opened) { $typeStack.RemoveAt($typeStack.Count - 1) }
            else { break }
        }
    }
}

$memberTotal = (($units.Values | ForEach-Object { $_.Members.Count }) | Measure-Object -Sum).Sum
Write-Host ("[SCAN] types={0} units={1} members={2} residual={3}" -f $typeCount, $units.Count, $memberTotal, $residual.Count) -ForegroundColor Cyan

if ($Check) { exit 0 }

if ((Test-Path -LiteralPath $outAbs) -and (-not $Force)) {
    Write-Host ("[REFUSE] 声明面已存在：{0}（源侧可能已删常量；重扫会缩小声明面）。确需重建请加 -Force。" -f $outAbs) -ForegroundColor Yellow
    exit 4
}


# ---- 写出 .ushar（按 target → valueType 升序；成员保源码序；全文逐字）----
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('# [Const] 成员级声明面（P2 生成：Extract-ContractConstants.ps1 自 C# 源码逐字提取）')
[void]$sb.AppendLine('# 消费方 = Generate-ContractConstants.ps1 → Agent.Contract\Generated\GeneratedContractConstants.g.cs')
[void]$sb.AppendLine('')

foreach ($unit in ($units.Values | Sort-Object -Property Target, ValueType)) {
    [void]$sb.AppendLine(('[Const] {0}: kind={1} | modifier={2} | valueType={3}' -f $unit.Target, $unit.Kind, $unit.Modifier, $unit.ValueType))
    foreach ($member in $unit.Members) {
        [void]$sb.AppendLine(('  {0} = {1}' -f $member.Name, $member.Value))
    }
    [void]$sb.AppendLine('')
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$dir = Split-Path $outAbs -Parent
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllText($outAbs, $sb.ToString(), $utf8NoBom)
Write-Host ("  写出: {0}" -f $outAbs) -ForegroundColor Green

# residual 清单（非白名单表达式，需人工裁决或回退手写）
if ($residual.Count -gt 0) {
    [System.IO.File]::WriteAllLines($residualAbs, $residual, $utf8NoBom)
    Write-Host ("  residual: {0} 条 → {1}" -f $residual.Count, $residualAbs) -ForegroundColor Yellow
}
elseif (Test-Path -LiteralPath $residualAbs) {
    Remove-Item -LiteralPath $residualAbs -Force
}

exit 0
