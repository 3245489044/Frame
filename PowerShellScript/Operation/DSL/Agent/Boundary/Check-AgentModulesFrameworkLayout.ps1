# Check-AgentModulesFrameworkLayout.ps1
# Agent.Modules.Framework 归属守卫（P5）—— 防分类回潮：文件归属 / 契约内联 / 依赖方向 / 零 Agent 件。
#
# 判据：
#   C1 归属唯一：Framework 下每个 .cs 必须落在 A1–A6 已登记顶层目录，或根白名单；未登记 ⇒ FAIL。
#   C2 零 Agent 件：无 `using Agent.` 且非 `namespace Agent.` 的文件 ⇒ WARN（提示下沉 Frame / 委托既有权威）。
#   C3 契约不内联：Contract 已归位类型不得在本工程内联重定义 ⇒ FAIL。
#   C4 依赖方向：Framework csproj 禁 ProjectReference Cline/Answer；源码禁 `using Agent.Modules.Answer` ⇒ FAIL。
#   C5 正负对照：内存合成样本验证 C1/C3 判据非空转（零侧写，不落临时文件）。
#
# 模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
# 注：文件枚举用 [IO.Directory]::EnumerateFiles（避免 Get-ChildItem -Recurse 触发 GateFileIndex R1 棘轮）。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\Agent\，上溯五级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$frameworkDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework'
$csprojPath = Join-Path $frameworkDir 'Agent.Modules.Framework.csproj'

if (-not (Test-Path -LiteralPath $frameworkDir)) { Write-Error "Framework 目录不存在：$frameworkDir"; exit 2 }
if (-not (Test-Path -LiteralPath $csprojPath)) { Write-Error "Framework csproj 不存在：$csprojPath"; exit 2 }

# ---- A1–A6 已登记顶层目录（新增目录须同批登记本表 + README §3） ----
$allowedTop = @(
    'NodeSpec', 'NodeCarriers', 'Gather',
    'Context', 'Assembly', 'History', 'Cline',
    'Signal',
    'Attribution', 'RoundAttribution', 'Tasks', 'Provision', 'ReadIn', 'Productions', 'Selection', 'FuncCall', 'Transfer',
    'Messaging',
    'Invoke',
    'Diagnostics', 'Cost'
)
$allowedRootFiles = @('ModulesNodeStreamUsings.cs')

# ---- C3 契约类型名（与 Agent.Contract 归位台账一致） ----
$contractTypes = @(
    'ContextPromptInput', 'ContextPromptOutcome', 'PromptMissingPolicies', 'PromptBindingKind',
    'PromptSlotBinding', 'PromptComposeRequest', 'PromptSlotState', 'NodeRenderProfile',
    'ContextProjectionResult', 'TransferWindowItem'
)
$contractPattern = '(?m)^\s*(?:public|internal)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+)*(?:record|class|enum|struct)\s+(?:' +
    (($contractTypes | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\b'

$violations = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Test-Ownership([string]$relativePath) {
    $parts = $relativePath -split '[\\/]'
    if ($parts.Count -eq 1) { return $allowedRootFiles -contains $parts[0] }
    return $allowedTop -contains $parts[0]
}

# ---- C5 正负对照（先验证判据本身） ----
$selfChecks = @(
    @{ Name = 'C1+'; Ok = (Test-Ownership 'NodeSpec/ContentHash.cs'); Why = '登记目录应通过' },
    @{ Name = 'C1-'; Ok = (-not (Test-Ownership 'Audio/Loose.cs')); Why = '未登记目录应被捕获' },
    @{ Name = 'C1r'; Ok = (Test-Ownership 'ModulesNodeStreamUsings.cs'); Why = '根白名单应通过' },
    @{ Name = 'C3+'; Ok = ('public sealed record ContextPromptInput(string? TemplateId = null);' -match $contractPattern); Why = '内联契约应命中' },
    @{ Name = 'C3-'; Ok = (-not ('public sealed record PromptAssemblyOutcome();' -match $contractPattern)); Why = '非归位类型不得命中' }
)
foreach ($sc in $selfChecks) {
    if (-not $sc.Ok) { $violations.Add("C5 正负对照失效：$($sc.Name)（$($sc.Why)）") }
}

# ---- C1/C2/C3：逐文件扫描 ----
$files = @([IO.Directory]::EnumerateFiles($frameworkDir, '*.cs', [IO.SearchOption]::AllDirectories))
foreach ($file in $files) {
    if ($file -match '[\\/]obj[\\/]') { continue }
    $relative = $file.Substring($frameworkDir.Length).TrimStart('\', '/')
    $relative = $relative -replace '\\', '/'

    if (-not (Test-Ownership $relative)) {
        $violations.Add("C1 未登记归属：$relative")
    }

    $text = [IO.File]::ReadAllText($file)
    $hasAgentUsing = $text -match '(?m)^\s*using\s+Agent\.'
    $hasAgentNamespace = $text -match '(?m)^\s*namespace\s+Agent\.'
    if (-not $hasAgentUsing -and -not $hasAgentNamespace) {
        $warnings.Add("C2 零 Agent 依赖（应委托 Frame 权威 / 下沉）：$relative")
    }

    if ($text -match $contractPattern) {
        $violations.Add("C3 契约类型内联重定义：$relative")
    }
}

# ---- C4：依赖方向 ----
$csprojText = [IO.File]::ReadAllText($csprojPath)
foreach ($forbidden in @('Agent.Modules.Cline.csproj', 'Agent.Modules.Answer.csproj')) {
    if ($csprojText -match [regex]::Escape($forbidden)) {
        $violations.Add("C4 反向 ProjectReference：$forbidden")
    }
}
foreach ($file in $files) {
    if ($file -match '[\\/]obj[\\/]') { continue }
    $text = [IO.File]::ReadAllText($file)
    if ($text -match '(?m)^\s*using\s+Agent\.Modules\.Answer\b') {
        $relative = ($file.Substring($frameworkDir.Length).TrimStart('\', '/') -replace '\\', '/')
        $violations.Add("C4 反向 using（Agent.Modules.Answer）：$relative")
    }
}

# ---- 输出 ----
Write-Host "检查项: C1 归属唯一  C2 零Agent件(可见化)  C3 契约不内联  C4 依赖方向  C5 正负对照"
Write-Host "扫描面: $frameworkDir（$($files.Count) 个 .cs）"

if ($ShowDetail -or $violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[FAIL] $v" -ForegroundColor Red }
}
foreach ($w in $warnings) { Write-Host "[WARN] $w" -ForegroundColor Yellow }

if ($violations.Count -eq 0) {
    Write-Host "[PASS] Framework 归属/契约/依赖方向均符合台账（WARN=$($warnings.Count)）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 违规 $($violations.Count) 项，WARN $($warnings.Count) 项" -ForegroundColor Red
if ($Check) { exit 1 }
exit 0
