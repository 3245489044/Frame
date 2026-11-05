#requires -Version 7.0
<#
.SYNOPSIS
    UI 位置反解声明守卫（UiLocDeclaration）—— 「UI 呈现位置 = loc 的反解产物」的机器判据。

.DESCRIPTION
      声明面 = Projects\Plug\Agent\Config\ui\ui-loc.ushar（[Space] UiLoc 坐标集 + [Record] loc_* 属性；无反解矩阵）
      C1 闸A 编译：ui-loc.ushar 经 Usharp.Cli build ⇒ 错误 0（坐标数 = 属性记录集）
      C2 属性闭合：产物 locs[] ≡ [Space] 坐标集（G1 缺属性 / G3 死声明）
      C3 C# 引用闭合：UiLocCatalog.<Api>("...") 字面量须已在声明面
      C4 中文文案棘轮：UI 呈现链路中文字面量 ≤ 基线（基线外置 Baselines\Agent\UiText.psd1，只降不升）
      C5 负对照：未声明 loc / 缺行声明 / 动态 loc 拼接必须被判出
      C6 动态 loc 拼接闭集：前缀 + 变量只允许在 UiLocCatalog 单点
      C7 模板键字面量回潮：已登记 loc 的模板键禁在 C# 落字面量
    纯扫描（只读、0 写入；C2 仅写 %TEMP%）。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$uiDir = Join-Path $root 'Projects\Plug\Agent\Config\ui'
$declaration = Join-Path $uiDir 'ui-loc.ushar'
$agentUi = Join-Path $root 'Projects\Plug\Agent\src\Agent.Ui'
$views = Join-Path $agentUi 'Ui\Views'
$cliExe = Join-Path $root 'Frame\Usharp\BuildOutput\Debug\Usharp.Cli.exe'
$cliProj = Join-Path $root 'Frame\Usharp\src\Usharp.Cli\Usharp.Cli.csproj'

function Get-DeclarationDump {
    # 坐标集单源 = 生成产物（E7：本门禁不再自带 [Space] ushar 解析）。
    $product = Join-Path $root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\ui-declaration.generated.json'
    if (Test-Path -LiteralPath $product) {
        $decl = Get-Content -LiteralPath $product -Raw -Encoding UTF8 | ConvertFrom-Json
        $productLocs = @(@($decl.locs) | ForEach-Object { [string]$_.loc } | Where-Object { $_ })
        return [pscustomobject]@{ locs = $productLocs }
    }

    # 坐标集唯一来源 = ui-loc.ushar 的 [Space] UiLoc 块（简单列表；生成器已退役，反解 = spec 键取名）
    $locs = [System.Collections.Generic.List[string]]::new()
    $section = ''
    foreach ($raw in (Get-Content -LiteralPath $declaration -Encoding utf8)) {
        $line = $raw.TrimEnd()
        if ($line -match '^\s*#') { continue }
        if ($line -match '^\s*\[Space\]\s+UiLoc\b') { $section = 'loc'; continue }
        if ($line -match '^\s*\[') { $section = ''; continue }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($section -ne 'loc') { continue }
        $text = $line.Trim()
        if ($text -match '^([A-Za-z_][A-Za-z0-9_]*)$') { $locs.Add($Matches[1]) }
    }
    return [pscustomobject]@{ locs = @($locs) }
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

function Test-DynamicLocClosure([object[]]$Files) {
    # 受控前缀 + 变量的拼接形态：只允许出现在 UiLocCatalog 单点（GroupLabel/GroupOrder/RoleText）
    $hits = [System.Collections.Generic.List[object]]::new()
    foreach ($file in $Files) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        foreach ($p in @('"text_group_"', '"const_order_"', '"text_role_"')) {
            if ($text -match ([regex]::Escape($p) + '\s*\+')) {
                $hits.Add([pscustomobject]@{ File = $file.Name; Prefix = $p })
            }
        }
    }
    return $hits
}

if ($NegativeControl) {
    $locs = @('text_ok')
    $bad = @([pscustomobject]@{ File = 'x.cs'; Loc = 'text_missing'; Api = 'Text' })
    $good = @([pscustomobject]@{ File = 'x.cs'; Loc = 'text_ok'; Api = 'Text' })
    if ((Test-CitationClosure $locs $bad).Count -eq 0) { throw '负对照失败：C3 未捕获未声明 loc' }
    if ((Test-CitationClosure $locs $good).Count -ne 0) { throw '负对照失败：C3 对已声明 loc 误报' }

    $dynTmp = Join-Path ([System.IO.Path]::GetTempPath()) ('ui-loc-dyn-' + [guid]::NewGuid().ToString('N') + '.cs')
    'var x = UiLocCatalog.TryText("text_role_" + role);' | Set-Content -LiteralPath $dynTmp -Encoding utf8
    $dynHits = @(Test-DynamicLocClosure @([System.IO.FileInfo]::new($dynTmp)))
    Remove-Item -LiteralPath $dynTmp -Force -ErrorAction SilentlyContinue
    if ($dynHits.Count -eq 0) { throw '负对照失败：C6 未捕获动态 loc 拼接' }

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('ui-loc-neg-' + [guid]::NewGuid().ToString('N') + '.ushar')
    @'
[Space] UiLoc: kind=keys
  text_empty_cards
  text_negative_control_missing
'@ | Set-Content -LiteralPath $tmp -Encoding utf8
    $out = $null
    $tmpLocs = @('text_empty_cards', 'text_negative_control_missing')
    $specKeys = @((Get-Content -LiteralPath $spec -Raw -Encoding utf8 | ConvertFrom-Json).specs.PSObject.Properties.Name)
    $missing = @($tmpLocs | Where-Object { $specKeys -notcontains $_ })
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    if ($missing.Count -eq 0) { throw '负对照失败：G1 未判出「loc 缺属性声明」' }
    Write-Host '[OK] 负对照通过：未声明 loc / 缺属性 loc / 动态拼接均被捕获。' -ForegroundColor Green
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

# 坐标数（闸A 输出的事实；C2 的判据输入，禁本门禁自建 [Space] 解析）。
$declaredCount = -1
if ($null -ne $output -and $output -match '坐标\s*(?<c>\d+)') { $declaredCount = [int]$Matches['c'] }

# ---- C2 属性面 ≡ 声明坐标集（G1 缺属性 / G3 死声明；单源 = ui-loc.ushar） ----
try {
    $declaredLocs = @((Get-DeclarationDump).locs)
    if ($declaredLocs.Count -eq 0) { $violations.Add('[C2] 判据不可判：产物 locs[] 为空（先跑 Generate-UiDeclaration.ps1 -Apply）') }
    if ($declaredCount -lt 0) {
        $violations.Add('[C2] 判据不可判：闸A 未取得坐标数（C1 未产出事实）')
    } elseif ($declaredCount -ne $declaredLocs.Count) {
        $violations.Add("[C2] G1/G3 键集不闭合：声明坐标 $declaredCount ≠ 产物 locs $($declaredLocs.Count)")
    }
    foreach ($loc in $declaredLocs) {
        if ([string]::IsNullOrWhiteSpace([string]$loc)) { $violations.Add('[C2] G1 产物 loc 为空') }
    }
} catch {
    $violations.Add("[C2] 判据不可判：$($_.Exception.Message)")
}

# ---- C3 C# 引用闭合 ----
$locs = @()
try { $locs = @((Get-DeclarationDump).locs) }
catch { $violations.Add("[C3] 声明面坐标集不可得（判据不可判）：$($_.Exception.Message)") }
$cites = @(Get-CitedLocs)
foreach ($v in (Test-CitationClosure $locs $cites)) { $violations.Add($v) }

# ---- C4 中文文案棘轮（基线外置：Baselines\Agent\UiText.psd1；禁脚本内嵌数值） ----
$chinese = 0
$chineseBaseline = 0
$baselinePath = Join-Path $root 'Frame\PowerShellScript\Baselines\Agent\UiText.psd1'
if (-not (Test-Path -LiteralPath $baselinePath)) {
    $violations.Add("[C4] 缺件（判据不可判）：$baselinePath")
} else {
    $textBaseline = Import-PowerShellDataFile -LiteralPath $baselinePath
    $chineseBaseline = [int]$textBaseline.ChineseBaseline
    $exemptPaths = @($textBaseline.ExemptPaths)
    foreach ($rootRel in @($textBaseline.ScanRoots)) {
        $scanRoot = Join-Path $root $rootRel
        if (-not (Test-Path -LiteralPath $scanRoot)) { $violations.Add("[C4] 扫描根缺失：$rootRel"); continue }
        $scanFiles = Get-ChildItem -LiteralPath $scanRoot -Recurse -Include *.cs -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' -and $_.Name -notlike '*.g.cs' }
        foreach ($file in $scanFiles) {
            $rel = $file.FullName.Substring($root.Length + 1)
            $exempt = $false
            foreach ($e in $exemptPaths) {
                if ($rel.StartsWith($e, [System.StringComparison]::OrdinalIgnoreCase)) { $exempt = $true; break }
            }
            if ($exempt) { continue }
            $text = [System.IO.File]::ReadAllText($file.FullName)
            $chinese += [regex]::Matches($text, '(?<!//)"[^"\r\n]*[\u4e00-\u9fff][^"\r\n]*"').Count
        }
    }
    foreach ($p in @($textBaseline.PendingRoots)) { if ($p) { Write-Host ("  [DEBT] 未纳管扫描根：$p") -ForegroundColor Yellow } }
    if ($chinese -gt $chineseBaseline) {
        $violations.Add("[C4] UI 中文字面量 $chinese > 基线 $chineseBaseline（只降不升；文案须经 UiLocCatalog.Text）")
    }
}

# ---- C6 动态 loc 拼接闭集（E5）：前缀 + 变量只允许在 UiLocCatalog 单点 ----
$singlePoint = Join-Path $agentUi 'Ui\Views\Declaration\UiLocCatalog.cs'
$dynamicScan = Get-ChildItem -LiteralPath $agentUi -Recurse -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' -and $_.FullName -ne $singlePoint -and $_.Name -notlike '*.g.cs' }
foreach ($h in (Test-DynamicLocClosure @($dynamicScan))) {
    $violations.Add("[C6] 动态 loc 拼接绕过单点（$($h.Prefix) + 变量）：$($h.File)")
}

# ---- C8 框架模板中文残留棘轮（D4 债务：只减不增，修好须同步销账）----
$templateDebtPath = Join-Path $root 'Frame\PowerShellScript\Baselines\Agent\UiTextTemplateDebt.psd1'
$defaultsPath = Join-Path $root 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\templates.default.json'
if (-not (Test-Path -LiteralPath $templateDebtPath)) {
    $violations.Add("[C8] 缺件（判据不可判）：$templateDebtPath")
} elseif (-not (Test-Path -LiteralPath $defaultsPath)) {
    $violations.Add("[C8] 缺件（判据不可判）：$defaultsPath")
} else {
    $textDebt = Import-PowerShellDataFile -LiteralPath $templateDebtPath
    $defaultTemplates = Get-Content -Raw -LiteralPath $defaultsPath | ConvertFrom-Json -Depth 16
    $templatesWithChinese = @($defaultTemplates.PSObject.Properties |
        Where-Object { [string]$_.Value -match '[\u4e00-\u9fff]' } |
        ForEach-Object { $_.Name })
    if ($templatesWithChinese.Count -gt [int]$textDebt.MaxTemplatesWithChinese) {
        $violations.Add("[C8] 含中文的框架模板数 $($templatesWithChinese.Count) > 基线 $($textDebt.MaxTemplatesWithChinese)（只降不升）")
    }
    foreach ($known in @($textDebt.Known)) {
        if ($templatesWithChinese -notcontains $known) {
            $violations.Add("[C8] 债务条目已消失：$known（请同步下调 Baselines\Agent\UiTextTemplateDebt.psd1 的 MaxTemplatesWithChinese / Known）")
        }
    }
}
# ---- C7 模板键字面量回潮（E8）：已提交 loc 的模板键禁在 C# 落字面量 ----
$templateKeyLiterals = @('"costView"', '"costSummary"', '"costGroup"', '"costUnpriced"')
foreach ($file in $dynamicScan) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($k in $templateKeyLiterals) {
        if ($text.Contains($k)) { $violations.Add("[C7] 模板键字面量 $k（须经 UiLocCatalog.Template）：$($file.Name)") }
    }
}

Write-Host ''
Write-Host '============================================================'
Write-Host '  UI 位置反解声明守卫（UiLocDeclaration）'
Write-Host '============================================================'
Write-Host ("  实测：loc {0} / C# 引用 {1} / UI 中文字面量 {2}（基线 {3}）" -f $locs.Count, $cites.Count, $chinese, $chineseBaseline) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host '[OK] UiLocDeclaration：0 违规（声明可编译 / 产物无漂移 / 引用闭合 / 文案棘轮不升）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] UiLocDeclaration 命中 $($violations.Count) 处：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Red }
if ($Check) { exit 1 }
exit 0
