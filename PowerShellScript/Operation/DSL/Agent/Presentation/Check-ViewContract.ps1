#requires -Version 7.0
<#
.SYNOPSIS
    视图读面契约守卫（ViewContract）—— 声明编译 → 唯一装配的**结构契约**，分阶段加锁。

.DESCRIPTION
    背景：6 份声明齐备但无「声明 → 表 → 面」的端到端判据时，门禁全绿也可能「跑起来没气泡」。
    本门禁锁的是**结构性契约**（载体 = 源码结构事实，非执行者叙述）：

      C1 编译入口单源：`ViewCompiler.Compile(` 定义点 = 1（禁平行编译器）。
      C2 UI 契约单源：`ViewSnapshot` 定义点 = 1（UI 唯一读入契约）。
      C3 契约件齐备：FrameTable / ReadPlan / ViewPlan / ViewSnapshot / ViewCompiler / ViewEngine。
      C4 平台层零产品依赖：View\ 内不出现 `using Agent.*` / `namespace Agent.*`（Share 层不得反向依赖产品）。
      C5 装配器端口化：ViewEngine 不出现数据源字面量（smsg / obs. / arch.），端口键只来自声明。
      C6（Stage=p2 起）UI 不写呈现身份判断：Agent.Ui 内 `== MessageChannel.*` / `Terminal == true` 形态 = 0（P2 接线后可开）。
      NC 负对照：双 ViewCompiler 定义 / 引入 `using Agent.` / 装配器含 smsg 字面量 → 必红；合规样本 → 绿。

    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 自证可判红；-Stage p1|p2 选择阶段。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl,
    [ValidateSet('p1', 'p2')]
    [string]$Stage = 'p1'
)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and $d -ne (Split-Path $d -Parent)) {
        if ((Test-Path (Join-Path $d 'Frame')) -and (Test-Path (Join-Path $d 'Projects'))) { return $d }
        $d = Split-Path $d -Parent
    }
    throw '[ViewContract] 无法定位仓库根（上溯未找到同时含 Frame\ 与 Projects\ 的目录）'
}

$root = Find-RepoRoot
$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }

$requiredFiles = @('FrameTable.cs', 'ReadPlan.cs', 'ViewPlan.cs', 'ViewSnapshot.cs', 'ViewCompiler.cs', 'ViewEngine.cs')

function Count-Match([string]$text, [string]$pattern) {
    if ([string]::IsNullOrEmpty($text)) { return 0 }
    return ([regex]::Matches($text, $pattern)).Count
}

# 纯结构判定（便于负对照复用；输入 = 文件名 → 文本）
function Test-ViewLayout($names, $texts) {
    $errors = [System.Collections.Generic.List[string]]::new()

    if ((Count-Match ([string]$texts['ViewCompiler.cs']) 'public\s+static\s+ViewCompiled\?\s+Compile\s*\(') -ne 1) {
        $errors.Add('[C1] ViewCompiler.Compile 定义点 ≠ 1（编译入口须单源）')
    }

    if ((Count-Match ([string]$texts['ViewSnapshot.cs']) 'public\s+sealed\s+record\s+ViewSnapshot\s*\(') -ne 1) {
        $errors.Add('[C2] ViewSnapshot 定义点 ≠ 1（UI 唯一读入契约）')
    }

    foreach ($required in $requiredFiles) {
        if (@($names) -notcontains $required) { $errors.Add("[C3] 契约件缺失：$required") }
    }

    foreach ($key in @($texts.Keys)) {
        $text = [string]$texts[$key]
        if ((Count-Match $text '(?m)^\s*using\s+Agent\.') -gt 0 -or (Count-Match $text '(?m)^\s*namespace\s+Agent\.') -gt 0) {
            $errors.Add("[C4] 平台层出现产品依赖：$key（using/namespace Agent.*）")
        }
    }

    $engine = [string]$texts['ViewEngine.cs']
    if ((Count-Match $engine 'smsg|obs\.|arch\.') -gt 0) {
        $errors.Add('[C5] ViewEngine 出现数据源字面量（端口键须来自声明：smsg/obs./arch.）')
    }

    return $errors
}

# 读面单路径判据（C9–C11；输入 = 文本，便于负对照复用）。
function Test-ReadFace($engineText, $builderText, $uiViewText, $sourcesText) {
    $errors = [System.Collections.Generic.List[string]]::new()

    if ((Count-Match ([string]$engineText) 'public\s+async\s+Task<ConversationReadOutcome>\s+ReadPlanAsync\s*\(') -ne 1) {
        $errors.Add('[C9] ConversationReadEngine.ReadPlanAsync 定义点 ≠ 1（读入口须单源）')
    }

    if ((Count-Match ([string]$builderText) 'ReadMarkedAsync|ReadArchiveRoundsAsync|ReadDeclaredSourcesAsync|AppendNodeOutputsAsync|ReadArchiveAsync\s*\(') -gt 0) {
        $errors.Add('[C9] StandardContextBuilder 仍含源选择/直读（须只经执行器）')
    }

    if ((Count-Match ([string]$engineText) 'OrderBy\(static s => s\.Precedence\)') -lt 1) {
        $errors.Add('[C10] 执行器未按声明 precedence 排序（读序不得来自数组序）')
    }

    if ((Count-Match ([string]$uiViewText) 'ReadDeclaredSourcesAsync|historyFallback') -gt 0) {
        $errors.Add('[C10] UI 仍有第二读序（ReadDeclaredSourcesAsync/historyFallback）')
    }

    if ((Count-Match ([string]$sourcesText) 'Terminal:\s*(true|false)') -gt 0) {
        $errors.Add('[C11] 非事实源写了 Terminal 常量（须 null = 未知）')
    }

    return $errors
}

# ---- C1–C5（平台层契约） ----
$viewDir = Join-Path $root 'Frame\CsharpPrj\Share\Frameworks\Conversation\View'
if (-not (Test-Path -LiteralPath $viewDir)) {
    Add-Violation 'C0' '视图契约目录缺失：Frame\CsharpPrj\Share\Frameworks\Conversation\View'
}
else {
    $files = @(Get-ChildItem -LiteralPath $viewDir -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|BuildOutput' })
    $names = @($files | ForEach-Object { $_.Name })
    $texts = @{}
    foreach ($file in $files) { $texts[$file.Name] = Get-Content -LiteralPath $file.FullName -Raw }

    foreach ($e in (Test-ViewLayout $names $texts)) { Add-Violation 'P1' $e }
    Write-Host ("[ViewContract] 平台层：files={0} stage={1}" -f $names.Count, $Stage)
}

# ---- C9–C11（会话读面单路径） ----
$sessionDir = Join-Path $root 'Projects\Plug\Agent\src\Agent.Session'
$enginePath = Join-Path $sessionDir 'Projection\ConversationReadEngine.cs'
$builderPath = Join-Path $sessionDir 'Projection\StandardContextBuilder.cs'
$sourcesPath = Join-Path $sessionDir 'Projection\ConversationSources.cs'
$uiViewPath = Join-Path $root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.View.cs'

$readFaceInputs = @{
    Engine = if (Test-Path -LiteralPath $enginePath) { Get-Content -LiteralPath $enginePath -Raw } else { '' }
    Builder = if (Test-Path -LiteralPath $builderPath) { Get-Content -LiteralPath $builderPath -Raw } else { '' }
    UiView = if (Test-Path -LiteralPath $uiViewPath) { Get-Content -LiteralPath $uiViewPath -Raw } else { '' }
    Sources = if (Test-Path -LiteralPath $sourcesPath) { Get-Content -LiteralPath $sourcesPath -Raw } else { '' }
}

if ([string]::IsNullOrWhiteSpace($readFaceInputs.Engine)) { Add-Violation 'C9' '执行器缺失：Agent.Session/Projection/ConversationReadEngine.cs' }
if ([string]::IsNullOrWhiteSpace($readFaceInputs.Builder)) { Add-Violation 'C9' '构建器缺失：Agent.Session/Projection/StandardContextBuilder.cs' }
foreach ($e in (Test-ReadFace $readFaceInputs.Engine $readFaceInputs.Builder $readFaceInputs.UiView $readFaceInputs.Sources)) { Add-Violation 'P1' $e }

# C12：灰度开关必须有删除日期（dep），过期判红。
if (-not [string]::IsNullOrWhiteSpace($readFaceInputs.Builder)) {
    $dep = [regex]::Match([string]$readFaceInputs.Builder, 'dep:\s*(\d{4}-\d{2}-\d{2})')
    if (-not $dep.Success) {
        Add-Violation 'C12' 'ConversationReadEngineSwitch 缺删除日期（// dep:yyyy-MM-dd）'
    }
    else {
        $depDate = [datetime]::ParseExact($dep.Groups[1].Value, 'yyyy-MM-dd', $null)
        if ($depDate -lt [datetime]::Today) {
            Add-Violation 'C12' ("灰度开关已过期：{0}（须删除旧路径）" -f $dep.Groups[1].Value)
        }
    }
}

# ---- C6（Stage=p2：UI 不写呈现身份判断） ----
if ($Stage -eq 'p2') {
    $uiDir = Join-Path $root 'Projects\Plug\Agent\src\Agent.Ui'
    if (Test-Path -LiteralPath $uiDir) {
        $uiFiles = @(Get-ChildItem -LiteralPath $uiDir -Recurse -Filter '*.cs' -File |
            Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|BuildOutput|\\_deprecated\\' })
        # 判据 = 身份判断形态（非引用计数）：展示标签与数据映射不算身份判断。
        $hits = @($uiFiles | Select-String -Pattern '==\s*MessageChannel\.|MessageChannel\.[A-Za-z]+\s*==|Terminal\s*==\s*true' -ErrorAction SilentlyContinue)
        if ($hits.Count -gt 0) {
            foreach ($hit in $hits) {
                $rel = $hit.Path.Substring($root.Length).TrimStart('\', '/')
                Add-Violation 'C6' ("UI 直取数据端口：{0}:{1}" -f $rel, $hit.LineNumber)
            }
        }
    }
}
else {
    Write-Host '[ViewContract] Stage=p1：C6（UI 直取端口）未开锁（P2 接线后置 p2）'
}

# ---- C8（黄金样本：四态覆盖 + 唯一 id） ----
$samplesPath = Join-Path $root 'Frame\PowerShellScript\Baselines\View\view-golden.samples.json'
if (-not (Test-Path -LiteralPath $samplesPath)) {
    Add-Violation 'C8' '黄金样本缺失：Frame\PowerShellScript\Baselines\View\view-golden.samples.json'
}
else {
    $samplesDecl = Get-Content -LiteralPath $samplesPath -Raw | ConvertFrom-Json -Depth 12
    $sampleList = @($samplesDecl.samples)
    if ($sampleList.Count -lt 4) { Add-Violation 'C8' ("黄金样本不足 4 条（实={0}）" -f $sampleList.Count) }

    $answers = @($sampleList | ForEach-Object { [string]$_.expect.answer })
    foreach ($expected in @('Answered', 'NoAnswer', 'ProcessOnly', 'Unknown')) {
        if ($answers -notcontains $expected) { Add-Violation 'C8' ("四态未覆盖：{0}" -f $expected) }
    }

    $ids = @($sampleList | ForEach-Object { [string]$_.id })
    foreach ($g in @($ids | Group-Object | Where-Object { $_.Count -gt 1 })) {
        Add-Violation 'C8' ("样本 id 重复：{0}" -f $g.Name)
    }

    Write-Host ("[ViewContract] 黄金样本：count={0} answers={1}" -f $sampleList.Count, ($answers -join ','))
}

# ---- 负对照（判据自证可判红） ----
if ($NegativeControl) {
    $good = @{
        'ViewCompiler.cs' = 'public static class ViewCompiler { public static ViewCompiled? Compile() => null; }'
        'ViewSnapshot.cs' = 'public sealed record ViewSnapshot(string View);'
        'ViewEngine.cs' = 'public sealed class ViewEngine { }'
    }
    $goodNames = @('FrameTable.cs', 'ReadPlan.cs', 'ViewPlan.cs', 'ViewSnapshot.cs', 'ViewCompiler.cs', 'ViewEngine.cs')

    if ((Test-ViewLayout $goodNames $good).Count -ne 0) {
        Add-Violation 'NC' '正对照误报：合规样本被判违规'
    }

    $bad = @{
        'ViewCompiler.cs' = "using Agent.Core;`npublic static class A { public static ViewCompiled? Compile() => null; }`npublic static class B { public static ViewCompiled? Compile() => null; }"
        'ViewSnapshot.cs' = 'public sealed record ViewSnapshot(string View);'
        'ViewEngine.cs' = 'public sealed class ViewEngine { string k = "smsg.x"; }'
    }
    $badErrors = Test-ViewLayout $goodNames $bad
    $codes = @($badErrors | ForEach-Object { ($_ -split ']')[0].TrimStart('[') })
    foreach ($expected in @('C1', 'C4', 'C5')) {
        if ($codes -notcontains $expected) { Add-Violation 'NC' "负对照未命中：期望判据 $expected 未触发" }
    }

    Write-Host ("[ViewContract] 负对照：codes={0}" -f ($codes -join ','))

    # 读面单路径（C9/C10/C11）负对照：合成违规形态必须被判红。
    $badRead = Test-ReadFace `
        'public sealed class E { }' `
        'if (marked.Count > 0) return marked; ReadMarkedAsync(); AppendNodeOutputsAsync();' `
        'var x = ReadDeclaredSourcesAsync(key); // historyFallback' `
        'new ConversationMessage(role: ConversationRole.Tool, Terminal: false);'
    $readCodes = @($badRead | ForEach-Object { ($_ -split ']')[0].TrimStart('[') })
    foreach ($expected in @('C9', 'C10', 'C11')) {
        if ($readCodes -notcontains $expected) { Add-Violation 'NC' "读面负对照未命中：期望判据 $expected 未触发" }
    }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[VIOLATION] $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[ViewContract] 通过（0 违规；编译入口单源 + 快照契约单源 + 平台层零产品依赖 + 装配器端口化）'
if ($Check) { exit 0 }
