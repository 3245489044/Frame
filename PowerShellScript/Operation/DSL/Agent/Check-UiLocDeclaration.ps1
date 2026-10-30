#requires -Version 7.0
<#
.SYNOPSIS
    UI 位置反解声明守卫（UiLocDeclaration）—— 「UI 呈现位置 = loc 的反解产物」的机器判据。

.DESCRIPTION
      声明面 = Projects\Plug\Agent\Config\ui\ui-loc.ushar（[Space] UiLoc + [Matrix] UiLocToSpec）
      属性面 = Projects\Plug\Agent\Config\ui\ui-loc.spec.json
      产物   = Projects\Plug\Agent\Config\ui\ui-loc.generated.json（唯一合并点 = Generate-UiLoc.ps1）
      C1 闸A 编译：ui-loc.ushar 经 Usharp.Cli build ⇒ 错误 0
      C2 产物不漂移：重跑生成器并与提交产物逐字一致
      C3 C# 引用闭合：UiLocCatalog.<Api>("...") 字面量须已在声明面
      C4 中文文案棘轮：Ui\Views 内中文字面量 ≤ 基线（只降不升）
      C5 负对照：未声明 loc / 缺行声明必须被判出
    纯扫描（只读、0 写入；C2 仅写 %TEMP%）。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
$uiDir = Join-Path $root 'Projects\Plug\Agent\Config\ui'
$declaration = Join-Path $uiDir 'ui-loc.ushar'
$spec = Join-Path $uiDir 'ui-loc.spec.json'
$generated = Join-Path $uiDir 'ui-loc.generated.json'
$generator = Join-Path $root 'Frame\PowerShellScript\Share\Usharp\Generate-UiLoc.ps1'
$agentUi = Join-Path $root 'Projects\Plug\Agent\src\Agent.Ui'
$views = Join-Path $agentUi 'Ui\Views'
$cliExe = Join-Path $root 'Frame\Usharp\BuildOutput\Debug\Usharp.Cli.exe'
$cliProj = Join-Path $root 'Frame\Usharp\src\Usharp.Cli\Usharp.Cli.csproj'

$chineseBaseline = 32

function Get-LocSet {
    $text = [System.IO.File]::ReadAllText($declaration)
    $section = ''
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^\s*#') { continue }
        if ($line -match '^\s*\[Space\]\s+UiLoc\b') { $section = 'loc'; continue }
        if ($line -match '^\s*\[Matrix\]\s+UiLocToSpec\b') { $section = 'matrix'; continue }
        if ($line -match '^\s*\[') { $section = ''; continue }
        if ($section -eq 'loc' -and $line.Trim() -match '^([A-Za-z_][A-Za-z0-9_]*)$') { [void]$set.Add($Matches[1]) }
    }
    return $set
}

function Get-CitedLocs {
    $cites = [System.Collections.Generic.List[object]]::new()
    Get-ChildItem -LiteralPath $agentUi -Recurse -Include *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
        ForEach-Object {
            $file = $_.FullName
            $text = [System.IO.File]::ReadAllText($file)
            foreach ($m in [regex]::Matches($text, 'UiLocCatalog\s*\.\s*(Text|Template|Class|LeafKey|Number|Constant|TryText|TryNumber)\s*\(\s*"([A-Za-z_][A-Za-z0-9_]*)"\s*[,)]')) {
                $cites.Add([pscustomobject]@{ File = $file; Loc = $m.Groups[2].Value; Api = $m.Groups[1].Value })
            }
        }
    return $cites
}

function Test-CitationClosure([string[]]$Locs, [object[]]$Cites) {
    $misses = [System.Collections.Generic.List[string]]::new()
    foreach ($c in $Cites) {
        if ($Locs -notcontains $c.Loc) {
            $misses.Add("C3 引用未声明 loc：$($c.Loc)（$([System.IO.Path]::GetFileName($c.File))）")
        }
    }
    return $misses
}

if ($NegativeControl) {
    $locs = @('text_ok')
    $bad = @([pscustomobject]@{ File = 'x.cs'; Loc = 'text_missing'; Api = 'Text' })
    $good = @([pscustomobject]@{ File = 'x.cs'; Loc = 'text_ok'; Api = 'Text' })
    if ((Test-CitationClosure $locs $bad).Count -eq 0) { throw '负对照失败：C3 未捕获未声明 loc' }
    if ((Test-CitationClosure $locs $good).Count -ne 0) { throw '负对照失败：C3 对已声明 loc 误报' }

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('ui-loc-neg-' + [guid]::NewGuid().ToString('N') + '.ushar')
    @'
[Space] UiLoc: kind=keys
  text_a
[Space] UiSpec: kind=keys
  text_a
[Matrix] UiLocToSpec: over=UiSpec
  text_b = spec_x
'@ | Set-Content -LiteralPath $tmp -Encoding utf8
    $out = & $generator -Ushar $tmp -Spec $spec -OutFile (Join-Path ([System.IO.Path]::GetTempPath()) 'ui-loc-neg.json') *>&1 | Out-String
    $rc = $LASTEXITCODE
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    if ($rc -ne 1) { throw '负对照失败：生成器未判出反解表缺行（G1）' }
    Write-Host '[OK] 负对照通过：未声明 loc 与缺行声明均被捕获。' -ForegroundColor Green
    exit 0
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

$violations = [System.Collections.Generic.List[string]]::new()

# ---- C1 闸A 编译 ----
if (-not (Test-Path -LiteralPath $declaration)) {
    $violations.Add("[C1] 缺件（判据不可判）：$declaration")
} else {
    $output = if (Test-Path -LiteralPath $cliExe) { & $cliExe build $declaration 2>&1 | Out-String }
              elseif (Test-Path -LiteralPath $cliProj) { & dotnet run --project $cliProj -- build $declaration 2>&1 | Out-String }
              else { $null }
    if ($null -eq $output) {
        $violations.Add('[C1] 未找到 Usharp 编译器（闸A 不可判，禁静默通过）')
    } elseif ($output -match '错误\s*(?<n>\d+)') {
        if ([int]$Matches['n'] -ne 0) { $violations.Add("[C1] ui-loc.ushar 闸A 编译错误 $($Matches['n']) 处") }
    } else {
        $violations.Add('[C1] 闸A 未取得编译错误计数（禁假绿）')
    }
}

# ---- C2 产物不漂移 ----
if ((Test-Path -LiteralPath $generator) -and (Test-Path -LiteralPath $generated)) {
    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ('ui-loc-drift-' + [guid]::NewGuid().ToString('N') + '.json')
    & $generator -OutFile $tempFile | Out-Null
    $actual = [System.IO.File]::ReadAllText($generated)
    $expected = [System.IO.File]::ReadAllText($tempFile)
    Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
    if ($actual -ne $expected) {
        $violations.Add('[C2] ui-loc.generated.json 与声明面不一致（重生成：Generate-UiLoc.ps1）')
    }
} else {
    $violations.Add('[C2] 缺生成器或产物（判据不可判）')
}

# ---- C3 C# 引用闭合 ----
$locs = @(Get-LocSet)
$cites = @(Get-CitedLocs)
foreach ($v in (Test-CitationClosure $locs $cites)) { $violations.Add($v) }

# ---- C4 中文文案棘轮 ----
$chinese = 0
$chineseFiles = Get-ChildItem -LiteralPath $views -Recurse -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }
foreach ($file in $chineseFiles) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $chinese += [regex]::Matches($text, '(?<!//)"[^"\r\n]*[\u4e00-\u9fff][^"\r\n]*"').Count
}
if ($chinese -gt $chineseBaseline) {
    $violations.Add("[C4] Views 中文字面量 $chinese > 基线 $chineseBaseline（只降不升；文案须经 UiLocCatalog.Text）")
}

Write-Host ''
Write-Host '============================================================'
Write-Host '  UI 位置反解声明守卫（UiLocDeclaration）'
Write-Host '============================================================'
Write-Host ("  实测：loc {0} / C# 引用 {1} / Views 中文字面量 {2}（基线 {3}）" -f $locs.Count, $cites.Count, $chinese, $chineseBaseline) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host '[OK] UiLocDeclaration：0 违规（声明可编译 / 产物无漂移 / 引用闭合 / 文案棘轮不升）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] UiLocDeclaration 命中 $($violations.Count) 处：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Red }
if ($Check) { exit 1 }
exit 0
