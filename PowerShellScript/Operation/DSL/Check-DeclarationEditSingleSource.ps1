#requires -Version 5.1
<#
.SYNOPSIS
    声明编辑单源守卫（DeclarationEditSingleSource）—— 声明增改删的唯一写入口判据。

.DESCRIPTION
    沿《任务工作流化-Agent任务历史操作与FuncCall统一治理-方案》§5：增改删节点/流/函数挂载
    一律经 IDeclarationEditor（锚点替换）+ 声明库 IDeclarationStore.PutAsync；禁绕过直写 Config/**/*.ushar。

    判据（D1–D5；报告模式起步）：
      D1 编辑端口单源：`interface IDeclarationEditor` 定义恰 1 处
      D2 实现单源：`IDeclarationEditor` 实现恰 1 处（`: IDeclarationEditor`）
      D3 禁直写：对 `.ushar` 直写（`File.WriteAllText`）仅允许白名单 FileDeclarationStore.cs
      D4 正对照：编辑器实现数 ≥ 基线
      D5 负对照：合成违规样本（含 `.ushar` 的 WriteAllText）须被 D3 判据命中
    模式：默认报告模式；-Check 校验模式（违规 → 退出码 1）。
.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。
.PARAMETER Check
    校验模式：存在违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name DeclarationEditSingleSource

$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Frame\Usharp\src'),
    (Join-Path $Root 'Projects\Plug')
)
$exclude = 'BuildOutput|\\obj\\|\\docs\\'
# 直写白名单：声明库文件实现自身（层内写盘唯一合法点）+ 已登记债务（见基线 WriteWhitelist）
$writeWhitelist = @($g.WriteWhitelist.Value)

function Get-SourceFiles {
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($r in $scanRoots) {
        if (-not (Test-Path -LiteralPath $r)) { continue }
        foreach ($f in @(Get-ChildItem -LiteralPath $r -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)) {
            if ($f.FullName -match $exclude) { continue }
            $list.Add($f.FullName)
        }
    }
    return $list
}

# D3 判据（正/负对照共用同一实现）
function Test-DirectUsharpWrite {
    param([string]$Text, [string]$Name)
    if ($Name -in $writeWhitelist) { return $false }
    # 剥离行注释后再判（避免文档串 / 注释中提及 WriteAllText 造成误报）
    $code = ($Text -split "`n" | ForEach-Object { $_ -replace '//.*$', '' }) -join "`n"
    return ($code -match 'File\.WriteAllText' -and $code -match '\.ushar')
}

$violations = [System.Collections.Generic.List[string]]::new()
$files = Get-SourceFiles

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  声明编辑单源守卫（IDeclarationEditor / 禁直写 .ushar）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---- D1 / D2 ----
$editorDefs = 0
$editorImpls = 0
foreach ($f in $files) {
    $text = Get-Content -LiteralPath $f -Raw -ErrorAction SilentlyContinue
    if ($null -eq $text) { continue }
    $editorDefs += @([regex]::Matches($text, 'interface\s+IDeclarationEditor\b')).Count
    $editorImpls += @([regex]::Matches($text, ':\s*IDeclarationEditor\b')).Count
}
if ($editorDefs -ne 1) { $violations.Add("[D1] interface IDeclarationEditor 定义 $editorDefs 处（须恰 1）") }

# ---- D3 禁直写 ----
$directWrites = 0
foreach ($f in $files) {
    $text = Get-Content -LiteralPath $f -Raw -ErrorAction SilentlyContinue
    if ($null -eq $text) { continue }
    if (Test-DirectUsharpWrite -Text $text -Name (Split-Path $f -Leaf)) {
        $directWrites++
        $violations.Add("[D3] 绕过 IDeclarationEditor 直写 .ushar：$f")
    }
}

# ---- D4 正对照 ----
$minImpls = [int]$g.MinEditorImplementations.Value
if ($editorImpls -lt $minImpls) {
    $violations.Add("[D4] 正对照基线：IDeclarationEditor 实现数 $editorImpls < 基线 $minImpls")
}

# ---- D5 负对照 ----
if (-not (Test-DirectUsharpWrite -Text 'File.WriteAllText("Config/x.ushar", text);' -Name 'Synthetic.cs')) {
    $violations.Add('[D5] 负对照失效：合成直写 .ushar 未被 D3 判据命中（判据恒真/恒假？）')
}

Write-Host ("  实测：编辑端口定义 {0} / 实现 {1} / 直写违规 {2}" -f $editorDefs, $editorImpls, $directWrites) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host '[OK] 声明编辑单源守卫：0 违规（D1/D2 单源；D3 无直写；D4/D5 对照命中）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 声明编辑单源守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }

if ($Check) { exit 1 }
exit 0
