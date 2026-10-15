# Check-VerifySingleShot.ps1
# 验证单入口守卫（G8）—— 把「回归/验证只经 Frame\Invoke 单入口」从 README 的口头不变式变成机器判据。
# 判据：
#   ① Frame\Invoke\*.ps1 不得出现原生 `dotnet test` / `dotnet build`（薄转发，dotnet 只在实现脚本里）
#   ② 唯一验证命令的接线不漂移：axioms.md 声明 `% VERIFY_SINGLE_ENTRY=string:<入口>` ∧ 该入口存在于 Frame\Invoke
#      ∧ project.md §1.10 登记 `VERIFY_SINGLE_ENTRY`
#   ③ 正/负对照（防判据空转）：入口目录存在且件数 ≥ 基线；原生 dotnet 检测正则对合成样本必须命中
#   ④ 默认禁全量接线：axioms.md 声明 `% VERIFY_FULL_RUN_AUTH_REQUIRED` ∧ 注册表 _verify-scope.psd1
#      （FullRunRequired=$true）∧ 裁决原语 VerifyScope.psm1（Resolve-FullRunGrant）∧ 两执行体
#      （Invoke-Verify / Invoke-AgentRegression）带 [VERIFY-SCOPE-GATE] 裁决且走同一原语；负对照防判据空转
# 边界声明：本件只扫**文件面**（脚本/规则），拦不住 agent 运行期直呼 dotnet —— 运行面由 project.md §1.10
#   相位白名单 + 回复内 [批次声明] 自审承担（= 规则义务，非门禁能力）。
# 模式：-Check 违规 exit 1；默认报告模式（不设退出码）。
# 依赖方向（硬约束）：本件只读仓库；零侧写。

param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 定位仓库根（含 Projects\ 与 Frame\）—— 相对 $PSScriptRoot 上溯，禁硬编码绝对路径
$__repo = $PSScriptRoot
while ($__repo -and $__repo -ne (Split-Path $__repo -Parent)) {
    if ((Test-Path (Join-Path $__repo 'Projects')) -and (Test-Path (Join-Path $__repo 'Frame'))) { break }
    $__repo = Split-Path $__repo -Parent
}

$invokeDir = Join-Path $__repo 'Frame\Invoke'
$axiomsPath = Join-Path $__repo '.clinerules\axioms.md'
$projectPath = Join-Path $__repo '.clinerules\project.md'
$violations = [System.Collections.Generic.List[string]]::new()

$nativeDotnet = '\bdotnet\s+(test|build)\b'

# 基线单源 = Baselines\VerifySingleShot.psd1（禁内联数值基线；守卫 GateBaselines U2/U7）
Import-Module (Join-Path $__repo 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name VerifySingleShot
$entryBaseline = [int]$g.EntryCount.Value   # 入口数下限（Search-Repo/Test-JsonFiles/Invoke-GateCheck/Invoke-Regression/Locate-Test/...）

# ---- 判据 ③a：正对照（入口目录在位且件数 ≥ 基线） ----
if (-not (Test-Path -LiteralPath $invokeDir)) {
    $violations.Add("[正对照] 入口目录不存在：Frame\Invoke")
}
$entryFiles = @()
if (Test-Path -LiteralPath $invokeDir) {
    $entryFiles = @(Get-ChildItem -LiteralPath $invokeDir -Filter '*.ps1' -File | Sort-Object Name)
    if ($entryFiles.Count -lt $entryBaseline) {
        $violations.Add("[正对照] 入口件数 $($entryFiles.Count) < 基线 $entryBaseline（判据无标的 ⇒ 假绿）")
    }
}

# ---- 判据 ③b：负对照（原生 dotnet 检测正则对合成样本必须命中） ----
if ('dotnet test' -notmatch $nativeDotnet) {
    $violations.Add("[负对照] 原生 dotnet 检测正则失效（合成样本 'dotnet test' 未命中）")
}

# ---- 判据 ①：入口不得出现原生 dotnet（跳过注释行；薄转发 ⇒ 实现脚本才允许） ----
foreach ($f in $entryFiles) {
    $lines = @(Get-Content -LiteralPath $f.FullName)
    $inBlock = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $trimmed = $line.TrimStart()
        if ($inBlock) { if ($line -match '#>') { $inBlock = $false }; continue }
        if ($trimmed.StartsWith('<#')) { if ($line -notmatch '#>') { $inBlock = $true }; continue }
        if ($trimmed.StartsWith('#')) { continue }
        if ($line -match $nativeDotnet) {
            $violations.Add("[$($f.Name):$($i+1)] 入口出现原生 dotnet（应转发到唯一实现）：$($line.Trim())")
        }
    }
}

# ---- 判据 ②：唯一验证命令接线不漂移 ----
$singleEntry = $null
if (-not (Test-Path -LiteralPath $axiomsPath)) {
    $violations.Add("[接线] 缺 .clinerules\axioms.md（VERIFY_SINGLE_ENTRY 权威处）")
} else {
    $m = [regex]::Match((Get-Content -LiteralPath $axiomsPath -Raw), 'VERIFY_SINGLE_ENTRY\s*=\s*string\s*:\s*(?<v>[^\s|]+)')
    if (-not $m.Success) {
        $violations.Add("[接线] axioms.md §9 未声明 % VERIFY_SINGLE_ENTRY")
    } else {
        $singleEntry = $m.Groups['v'].Value
        if (-not (Test-Path -LiteralPath (Join-Path $invokeDir $singleEntry))) {
            $violations.Add("[接线] VERIFY_SINGLE_ENTRY 指向的入口不存在：Frame\Invoke\$singleEntry")
        }
    }
}
if (Test-Path -LiteralPath $projectPath) {
    if ((Get-Content -LiteralPath $projectPath -Raw) -notmatch 'VERIFY_SINGLE_ENTRY') {
        $violations.Add("[接线] project.md §1.10 未登记 VERIFY_SINGLE_ENTRY")
    }
} else {
    $violations.Add("[接线] 缺 .clinerules\project.md（§1.10 相位/预算处）")
}

# ---- 判据 ④：默认禁全量接线（局部免授权 / 全量须授权） ----
# 权威 % VERIFY_FULL_RUN_AUTH_REQUIRED（.clinerules\axioms.md §9）；注册表单源 = Baselines\_verify-scope.psd1
#   裁决原语单源 = Baselines\VerifyScope.psm1（消费方禁各自手写解析 ⇒ 对齐 Strategy.SingleImplementation）
$scopeMarker = 'VERIFY-SCOPE-GATE'
$scopeRegPath = Join-Path $__repo 'Frame\PowerShellScript\Baselines\_verify-scope.psd1'
$scopeModulePath = Join-Path $__repo 'Frame\PowerShellScript\Baselines\VerifyScope.psm1'

# 判据 ④a 负对照（防判据空转）：标记匹配器对合成样本必须命中
if ('# [VERIFY-SCOPE-GATE] 合成样本' -notmatch [regex]::Escape($scopeMarker)) {
    $violations.Add('[负对照] 默认禁全量标记匹配器失效（合成样本未命中）')
}

# 判据 ④b 权威声明
if (Test-Path -LiteralPath $axiomsPath) {
    if ((Get-Content -LiteralPath $axiomsPath -Raw) -notmatch 'VERIFY_FULL_RUN_AUTH_REQUIRED') {
        $violations.Add('[接线] axioms.md §9 未声明 % VERIFY_FULL_RUN_AUTH_REQUIRED')
    }
}

# 判据 ④c 注册表（FullRunRequired 须为 $true ⇒ 默认禁止全量）
if (-not (Test-Path -LiteralPath $scopeRegPath)) {
    $violations.Add('[接线] 缺验证面注册表：Frame\PowerShellScript\Baselines\_verify-scope.psd1')
} elseif ((Get-Content -LiteralPath $scopeRegPath -Raw) -notmatch 'FullRunRequired\s*=\s*@\{\s*Value\s*=\s*\$true') {
    $violations.Add('[接线] _verify-scope.psd1 未声明 FullRunRequired.Value=$true（默认禁全量失效）')
}

# 判据 ④d 裁决原语（授权来源解析单源）
if (-not (Test-Path -LiteralPath $scopeModulePath)) {
    $violations.Add('[接线] 缺授权裁决原语：Frame\PowerShellScript\Baselines\VerifyScope.psm1')
} elseif ((Get-Content -LiteralPath $scopeModulePath -Raw) -notmatch 'function\s+Resolve-FullRunGrant') {
    $violations.Add('[接线] VerifyScope.psm1 缺 Resolve-FullRunGrant（授权来源解析）')
}

# 判据 ④e 执行体接线（两处裁决标记 + 同一原语）
foreach ($consumerRel in @('..\..\Environment\CentralTest\Invoke-Verify.ps1', '..\..\Environment\CentralTest\Invoke-AgentRegression.ps1')) {
    $consumerPath = Join-Path $invokeDir $consumerRel
    if (-not (Test-Path -LiteralPath $consumerPath)) {
        $violations.Add("[接线] 默认禁全量消费方缺件：$consumerRel")
        continue
    }
    $consumerText = Get-Content -LiteralPath $consumerPath -Raw
    if ($consumerText -notmatch [regex]::Escape($scopeMarker)) {
        $violations.Add("[接线] $(Split-Path -Leaf $consumerPath) 缺 [$scopeMarker] 裁决标记（默认禁全量未接线）")
    }
    if ($consumerText -notmatch 'Resolve-FullRunGrant') {
        $violations.Add("[接线] $(Split-Path -Leaf $consumerPath) 未走授权裁决原语 Resolve-FullRunGrant")
    }
}

# ---- 报告 ----
Write-Host '=== 验证单入口守卫（VerifySingleShot） ==='
Write-Host '检查项: ① 入口禁原生 dotnet  ② VERIFY_SINGLE_ENTRY 接线  ③ 正/负对照  ④ 默认禁全量接线'
Write-Host ("入口件数: {0} | 唯一验证命令: {1}" -f $entryFiles.Count, $(if ($singleEntry) { $singleEntry } else { '(未声明)' }))
Write-Host ("违规数: {0}" -f $violations.Count)

if ($violations.Count -gt 0) {
    Write-Host '--- 违规清单 ---'
    foreach ($v in $violations) { Write-Host "  [FAIL] $v" }
    if ($Check) {
        Write-Host "校验失败：验证单入口守卫发现 $($violations.Count) 项违规"
        exit 1
    }
    exit 0
}

Write-Host '  [OK] 验证单入口守卫通过'
exit 0
