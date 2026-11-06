#requires -Version 5.1
<#
.SYNOPSIS
    冻结声明产物守卫（DeclaredBundle）—— G5：门禁判据从「源码文本正则」迁移到「编译产物结构事实」。

.DESCRIPTION
    载体 = 冻结产物 declared-manifest.json（G1 单点编译产出，嵌 Agent.Core 程序集），
    而非源码正则 / 硬编码清单。判据（结构事实，禁执行者叙述）：
      D1 产物存在：manifest / manifest-gz / round-spec / reference-schema 四件在位。
      D2 schema 哨兵：manifest.schema == 'declared-manifest-v1'（手改/覆盖即失守）。
      D3 嵌入副本：manifest.json.gz 解压后与 manifest.json 等价（防嵌入陈旧副本）。
      C1 非空正对照：flows > 0 ∧ functions > 0 ∧ vocabulary > 0（防产物被掏空 ⇒ 判据无标的假绿）。
      V1 词表单源（J7）：组装器 HistoryViewportAssembler.cs 内词表字面量命中 = 0
         （词表取值唯一经 DeclaredVocabulary；唯一允许的回退字面量在 DeclaredVocabulary.cs）。
      U  正/负对照：词表字面量探测器对合成样本行为正确。

    模式：默认报告模式（不设退出码）；-Check 校验模式（违规 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Detail:$ShowDetail -Key 'DeclaredBundle'
$Root = $ctx.Root

$violations = [System.Collections.Generic.List[string]]::new()

$declaredDir = Join-Path (Get-MappedPath -Key 'AgentSrc') 'Agent.Core\Declared'
$manifestPath = Join-Path $declaredDir 'declared-manifest.json'
$manifestGzPath = Join-Path $declaredDir 'declared-manifest.json.gz'
$roundSpecPath = Join-Path $declaredDir 'round-spec.generated.json'
$schemaPath = Join-Path $declaredDir 'reference-schema.generated.json'
$assemblerPath = Join-Path (Get-MappedPath -Key 'AgentSrc') 'Agent.Session\Projection\HistoryViewportAssembler.cs'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  冻结声明产物守卫（DeclaredBundle：产物结构事实）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---- D1 产物存在 ----
foreach ($pair in @(@{N = 'manifest'; P = $manifestPath }, @{N = 'manifest-gz'; P = $manifestGzPath }, @{N = 'round-spec'; P = $roundSpecPath }, @{N = 'reference-schema'; P = $schemaPath })) {
    if (-not (Test-Path -LiteralPath $pair.P)) {
        $violations.Add("[D1] 产物缺失：$($pair.N)（$($pair.P)）（修复：pwsh Frame\PowerShellScript\Share\Usharp\Generate-DeclaredBundle.ps1 -ConfigRoot Projects\Plug\Agent\Config -OutDir Projects\Plug\Agent\src\Agent.Core\Declared）")
    }
}

# ---- D2 / C1：解析 manifest + 哨兵 + 非空 ----
$manifest = $null
if (Test-Path -LiteralPath $manifestPath) {
    try { $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { $violations.Add("[D2] manifest 非法 JSON：$($_.Exception.Message)") }
}
if ($null -ne $manifest) {
    if ([string]$manifest.schema -ne 'declared-manifest-v1') {
        $violations.Add("[D2] manifest.schema 哨兵不符（期望 'declared-manifest-v1'，实得 '$($manifest.schema)'）⇒ 产物疑被手改")
    }
    $flowCount = @($manifest.flows).Count
    $fnCount = @($manifest.functions).Count
    $vocabCount = @($manifest.vocabulary).Count
    if ($flowCount -le 0) { $violations.Add('[C1] 正对照失效：manifest.flows 为空（产物被掏空？）') }
    if ($fnCount -le 0) { $violations.Add('[C1] 正对照失效：manifest.functions 为空（产物被掏空？）') }
    if ($vocabCount -le 0) { $violations.Add('[C1] 正对照失效：manifest.vocabulary 为空（词表真源缺失？）') }
    if ($ShowDetail) { Write-Host ("  flows={0} functions={1} vocabulary={2}" -f $flowCount, $fnCount, $vocabCount) -ForegroundColor Gray }
}

# ---- D3 嵌入副本一致：declared-manifest.json.gz 解压后须与 declared-manifest.json 等价 ----
if ((Test-Path -LiteralPath $manifestPath) -and (Test-Path -LiteralPath $manifestGzPath)) {
    try {
        $fs = [System.IO.File]::OpenRead($manifestGzPath)
        $gz = [System.IO.Compression.GZipStream]::new($fs, [System.IO.Compression.CompressionMode]::Decompress)
        $reader = [System.IO.StreamReader]::new($gz, [System.Text.Encoding]::UTF8)
        $gzText = $reader.ReadToEnd()
        $reader.Dispose(); $gz.Dispose(); $fs.Dispose()

        $plainNorm = (Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json | ConvertTo-Json -Depth 100 -Compress)
        $gzNorm = ($gzText | ConvertFrom-Json | ConvertTo-Json -Depth 100 -Compress)
        if ($plainNorm -ne $gzNorm) {
            $violations.Add('[D3] manifest 嵌入副本与 JSON 不一致（修复：重跑 Generate-DeclaredBundle.ps1 -OutDir ...）')
        }
    }
    catch {
        $violations.Add("[D3] manifest 嵌入副本不可解压/非法 JSON：$($_.Exception.Message)")
    }
}

# ---- V1 词表单源（J7）：组装器内词表字面量 = 0 ----
function Test-VocabLiteral {
    param([Parameter(Mandatory)][string]$Text)
    $hits = [System.Collections.Generic.List[string]]::new()
    foreach ($lit in @('"window"', '"semantic"', '"stable"')) {
        $count = ([regex]::Matches($Text, [regex]::Escape($lit))).Count
        if ($count -gt 0) { $hits.Add("$lit x$count") }
    }
    return $hits
}
if (Test-Path -LiteralPath $assemblerPath) {
    $text = Get-Content -LiteralPath $assemblerPath -Raw -Encoding UTF8
    $hits = @(Test-VocabLiteral -Text $text)
    if ($hits.Count -gt 0) {
        $violations.Add("[V1] 组装器含词表字面量（应经 DeclaredVocabulary 读产物）：$($hits -join ', ')（$assemblerPath）")
    }
} else {
    $violations.Add("[V1] 判据不可判：找不到组装器 $assemblerPath")
}

# ---- U 正/负对照 ----
$posOk = (@(Test-VocabLiteral -Text 'var k = "window";')).Count -gt 0
$negOk = (@(Test-VocabLiteral -Text 'var k = DeclaredVocabulary.WindowKind;')).Count -eq 0
if (-not $posOk) { $violations.Add('[U] 正对照失效：词表字面量探测器未捕获合成样本') }
if (-not $negOk) { $violations.Add('[U] 负对照失效：无字面量样本被误判') }

Write-Host ''
if ($violations.Count -eq 0) {
    Write-Host '[OK] DeclaredBundle：0 违规（产物在位 ∧ 哨兵 ∧ 非空 ∧ 组装器零词表字面量）' -ForegroundColor Green
    exit 0
}
Write-Host "[FAIL] DeclaredBundle 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
