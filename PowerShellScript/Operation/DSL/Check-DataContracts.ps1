# Check-DataContracts.ps1
# 统一数据传递标准 —— 一致性校验器（PowerShell 版，仿照 DSL/Check-ClinerulesReferences.ps1 的两遍扫描模式）。
# 职责：扫描 Agent 源码中所有 RoundKeys.<Name> 使用点，断言每个键都已在 DataContractRegistrar.RegisterAll 登记。
#   - 报告模式（默认）：列出未覆盖键，不设退出码。
#   - 校验模式（-Check）：存在未覆盖键时以非零退出码失败（CI 门禁，防魔法键/新键静默流通）。
# 规则定死的是"每个使用键必须登记"；内容不定死（新增键 = 在 RegisterAll 加一条契约，本脚本自动识别）。

param(
    [string]$Root = "d:\Project\Program\WorkFlowPrj",
    [switch]$Check
)

$src = Join-Path $Root "Projects\Agent\src"
$regFile = Join-Path $src "Agent.Axioms\Loops\DataContractRegistrar.cs"
if (-not (Test-Path $regFile)) { Write-Error "未找到 DataContractRegistrar.cs: $regFile"; exit 2 }

$pattern = 'RoundKeys\.([A-Za-z0-9_]+)'

# 第一遍：收集源码全部 RoundKeys.<Name> 使用点（排除 RoundKeys.cs 自身常量声明）。
$usages = @{}
Get-ChildItem -Path $src -Recurse -Include *.cs | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    foreach ($m in [regex]::Matches($text, $pattern)) {
        $usages[$m.Groups[1].Value] = $true
    }
}
# RoundKeys.cs 内常量声明不是"使用"，剔除其自身文件后重扫该文件的依赖引用保持一致性
$roundKeysFile = Join-Path $src "Agent.Axioms\Loops\RoundKeys.cs"
if (Test-Path $roundKeysFile) { $usages.Remove((Get-Content $roundKeysFile -Raw) -match '.') }

# 第二遍：解析注册表登记的 RoundKeys.<Name>。
$regNames = @{}
foreach ($m in [regex]::Matches((Get-Content $regFile -Raw), $pattern)) {
    $regNames[$m.Groups[1].Value] = $true
}

$uncovered = @($usages.Keys | Where-Object { -not $regNames.ContainsKey($_) } | Sort-Object)

Write-Host "=== DataContract 一致性检查 ==="
Write-Host ("使用中的 RoundKeys 数: {0} | 已登记契约数: {1} | 未覆盖: {2}" -f $usages.Count, $regNames.Count, $uncovered.Count)
if ($uncovered.Count -gt 0) {
    Write-Host "--- 未登记(被使用但无契约)的键 ---"
    $uncovered | ForEach-Object { Write-Host ("  {0}" -f $_) }
}

if ($Check -and $uncovered.Count -gt 0) {
    Write-Host "校验失败：存在未登记的数据键，请在 DataContractRegistrar.RegisterAll 补齐契约。"
    exit 1
}
exit 0
