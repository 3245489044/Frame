# Check-VerifySingleShot.ps1
# 验证单入口守卫（G8）—— 把「回归/验证只经 Frame\Invoke 单入口」从 README 的口头不变式变成机器判据。
# 判据：
#   ① Frame\Invoke\*.ps1 不得出现原生 `dotnet test` / `dotnet build`（薄转发，dotnet 只在实现脚本里）
#   ② 唯一验证命令的接线不漂移：axioms.md 声明 `% VERIFY_SINGLE_ENTRY=string:<入口>` ∧ 该入口存在于 Frame\Invoke
#      ∧ project.md §1.10 登记 `VERIFY_SINGLE_ENTRY`
#   ③ 正/负对照（防判据空转）：入口目录存在且件数 ≥ 基线；原生 dotnet 检测正则对合成样本必须命中
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

# ---- 报告 ----
Write-Host '=== 验证单入口守卫（VerifySingleShot） ==='
Write-Host '检查项: ① 入口禁原生 dotnet  ② VERIFY_SINGLE_ENTRY 接线  ③ 正/负对照'
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
