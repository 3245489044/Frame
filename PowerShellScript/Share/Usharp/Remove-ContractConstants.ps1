<#
.SYNOPSIS
    源侧改造（P3）—— 63 类型加 partial + 删原 const（受控写，须先 -DryRun 再 -Apply）。

.DESCRIPTION
    输入 = [Const] 声明面（contract-consts.ushar）+ C# 源码树。
    对每个声明目标类型：① 类型声明行补 partial；② 删除该类型体内被声明覆盖的 public const 行。

    纪律：只动「已声明的成员」（声明面为唯一驱动）；未列入声明的 const（residual）保持原样；
    -DryRun 只报告改动清单（0 写）；-Apply 才写盘。禁由本器推导成员名（一律读声明面）。

.PARAMETER DeclFile
    [Const] 声明面（缺省 = Projects\Plug\Agent\Config\keyspaces\contract-consts.ushar）。

.PARAMETER SourceRoot
    源码根（缺省 = Projects\Plug\Agent\src\Agent.Contract）。

.PARAMETER Apply
    写盘（缺省 = DryRun）。
#>
[CmdletBinding()]
param(
    [string]$DeclFile = 'Projects\Plug\Agent\Config\keyspaces\contract-consts.ushar',
    [string]$SourceRoot = 'Projects\Plug\Agent\src\Agent.Contract',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
function Resolve-Repo([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return (Join-Path $root $Path)
}

$declAbs = Resolve-Repo $DeclFile
$sourceAbs = Resolve-Repo $SourceRoot
if (-not (Test-Path -LiteralPath $declAbs)) { Write-Error "声明面缺失：$declAbs"; exit 2 }

# ---- 读声明面：target → 成员名集 ----
$declared = @{}
$current = $null
foreach ($line in [System.IO.File]::ReadAllLines($declAbs)) {
    if ($line -match '^\[Const\]\s+([^:\s]+)') {
        $current = $Matches[1]
        if (-not $declared.ContainsKey($current)) { $declared[$current] = New-Object System.Collections.Generic.HashSet[string] }
    }
    elseif ($null -ne $current -and $line -match '^\s+([A-Za-z_]\w*)\s*=') { [void]$declared[$current].Add($Matches[1]) }
}

$typeDecl = '^\s*(?:public|internal)\s+(?:(static|sealed|abstract)\s+)?(partial\s+)?(class|record|struct)\s+([A-Za-z_]\w*)'
$constDecl = '^\s*public\s+const\s+(string|int)\s+([A-Za-z_]\w*)\s*=\s*(.+?);\s*$'
$namespaceDecl = '^\s*namespace\s+([A-Za-z_][\w.]*)'

$changedFiles = 0
$partialAdded = 0
$removedConsts = 0

foreach ($file in (Get-ChildItem -LiteralPath $sourceAbs -Recurse -File -Filter *.cs | Sort-Object FullName)) {
    if ($file.Name -like '*.g.cs') { continue }
    if ($file.FullName -match '\\Generated\\') { continue }

    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    $namespace = ''
    $depth = 0
    $stack = New-Object System.Collections.Generic.List[object]
    $delete = New-Object System.Collections.Generic.HashSet[int]
    $patchPartial = New-Object System.Collections.Generic.HashSet[int]

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $startDepth = $depth
        if ($line -match $namespaceDecl) { $namespace = $Matches[1] }

        $top = if ($stack.Count -gt 0) { $stack[$stack.Count - 1] } else { $null }

        if ($line -match $typeDecl) {
            $name = $Matches[4]
            $target = if ($null -ne $top) { "$($top.Target).$name" }
                      elseif ($namespace) { "$namespace.$name" }
                      else { $name }
            $frame = [pscustomobject]@{
                Target    = $target
                BodyDepth = $startDepth + 1
                DeclLine  = $i
                Opened    = $false
                Nested    = ($null -ne $top)
            }
            $stack.Add($frame)
            $top = $frame

            if (-not $frame.Nested -and $declared.ContainsKey($target) -and $line -notmatch '\bpartial\b') {
                [void]$patchPartial.Add($i)
            }
        }

        $isDeclaredBody = ($null -ne $top) -and (-not $top.Nested) -and $declared.ContainsKey($top.Target) -and ($startDepth -eq $top.BodyDepth)
        if ($isDeclaredBody -and ($line -match $constDecl)) {
            if ($declared[$top.Target].Contains($Matches[2])) { [void]$delete.Add($i) }
        }

        $scan = [regex]::Replace($line, '"(?:[^"\\]|\\.)*"', '""')
        $scan = [regex]::Replace($scan, "@""(?:[^""]|"""")*""", '@""')
        $scan = [regex]::Replace($scan, '//.*$', '')
        $depth += ([regex]::Matches($scan, '\{')).Count - ([regex]::Matches($scan, '\}')).Count

        while ($stack.Count -gt 0) {
            $frame = $stack[$stack.Count - 1]
            if ($depth -ge $frame.BodyDepth) { $frame.Opened = $true; break }
            if ($frame.Opened) { $stack.RemoveAt($stack.Count - 1) } else { break }
        }
    }

    if ($delete.Count -eq 0 -and $patchPartial.Count -eq 0) { continue }

    foreach ($i in $patchPartial) { $partialAdded++ }
    foreach ($i in $delete) { $removedConsts++ }

    Write-Host ("{0}: partial+{1} const-{2}" -f $file.Name, $patchPartial.Count, $delete.Count) -ForegroundColor Cyan

    if ($Apply) {
        $out = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($delete.Contains($i)) { continue }
            $text = $lines[$i]
            if ($patchPartial.Contains($i)) {
                $text = $text -replace '^(\s*(?:public|internal)\s+)(?:(static|sealed|abstract)\s+)?(class|record|struct)\s+', '$1${2}partial $3 '
            }
            $out.Add($text)
        }
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllLines($file.FullName, $out, $utf8NoBom)
        $changedFiles++
    }
}

$mode = if ($Apply) { 'APPLIED' } else { 'DRYRUN' }
Write-Host ("[{0}] files={1} partial=+{2} const=-{3} (declared targets={4})" -f $mode, $changedFiles, $partialAdded, $removedConsts, $declared.Count) -ForegroundColor Green
exit 0
