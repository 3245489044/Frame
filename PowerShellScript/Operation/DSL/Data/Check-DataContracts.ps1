# Check-DataContracts.ps1
# 统一数据传递标准 —— 一致性校验器（PowerShell 版，仿照 DSL/Check-ClinerulesReferences.ps1 的两遍扫描模式）。
# 判据（M3/T3 派生改造后两条）：
#   [D1] 扫描 Agent 源码中所有 RoundKeys.<Name> 使用点，断言每个键都已在 DataContractRegistrar.RegisterAll 登记
#        —— 规则定死「每个使用键必须登记」；内容不定死（新增键 = 在 RegisterAll 加一条契约，本脚本自动识别）。
#   [D2] 台账 ports.json **不得承载 data 段**：数据标准的唯一作者 = DataContractRegistrar 播种的运行时契约，
#        声明面由 PortsLedger/DataStandardProjection 运行时派生并入（手写 data 段 = 第二作者回归，必然漂移）。
#   - 报告模式（默认）：列出违规，不设退出码。
#   - 校验模式（-Check）：存在违规时以非零退出码失败（CI 门禁）。

param(
    [string]$Root = '',
    [switch]$Check
)

# 哨兵定位仓库根：上溯找同时含 Projects\ 与 Frame\ 的目录
if (-not $Root) {
    $search = $PSScriptRoot
    while ($search -and $search -ne (Split-Path $search -Parent)) {
        if ((Test-Path (Join-Path $search 'Projects')) -and (Test-Path (Join-Path $search 'Frame'))) {
            $Root = $search; break
        }
        $search = Split-Path $search -Parent
    }
    if (-not $Root) { Write-Error "找不到仓库根（含 Projects\ 与 Frame\）"; exit 2 }
}

$src = Join-Path $Root "Projects\Plug\Agent\src"
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
# RoundKeys.cs 内常量声明不是"使用"，按名剔除
$roundKeysFile = Join-Path $src "Agent.Axioms\Loops\RoundKeys.cs"
if (Test-Path $roundKeysFile) {
    $raw = Get-Content $roundKeysFile -Raw
    foreach ($m in [regex]::Matches($raw, $pattern)) {
        $usages.Remove($m.Groups[1].Value) | Out-Null
    }
}

# 第二遍：解析注册表登记的 RoundKeys.<Name>。
$regNames = @{}
foreach ($m in [regex]::Matches((Get-Content $regFile -Raw), $pattern)) {
    $regNames[$m.Groups[1].Value] = $true
}

$uncovered = @($usages.Keys | Where-Object { -not $regNames.ContainsKey($_) } | Sort-Object)

# [D2] 第二作者禁止：台账不得承载 data 段（判据与 DataContractStandardTests [D1] 同源）。
$ledgerFile = Join-Path $src "Agent.Contract\Observability\ports.json"
$ledgerViolations = @()
if (-not (Test-Path $ledgerFile)) {
    $ledgerViolations += "未找到台账文件: $ledgerFile"
} else {
    $ledgerText = Get-Content $ledgerFile -Raw
    foreach ($m in [regex]::Matches($ledgerText, '(?m)^[ \t]*"data"[ \t]*:')) {
        $line = ($ledgerText.Substring(0, $m.Index) -split "`n").Count
        $ledgerViolations += ("ports.json:{0} 出现手写 data 段（数据标准唯一作者 = DataContractRegistrar；派生段由 PortsLedger 并入）" -f $line)
        break
    }
}

Write-Host "=== DataContract 一致性检查 ==="
Write-Host ("使用中的 RoundKeys 数: {0} | 已登记契约数: {1} | 未覆盖: {2}" -f $usages.Count, $regNames.Count, $uncovered.Count)
if ($uncovered.Count -gt 0) {
    Write-Host "--- 未登记(被使用但无契约)的键 ---"
    $uncovered | ForEach-Object { Write-Host ("  {0}" -f $_) }
}

Write-Host ("台账 data 段（应缺席）违规: {0}" -f $ledgerViolations.Count)
foreach ($v in $ledgerViolations) { Write-Host ("  {0}" -f $v) }

if ($Check -and ($uncovered.Count -gt 0 -or $ledgerViolations.Count -gt 0)) {
    if ($uncovered.Count -gt 0) {
        Write-Host "校验失败：存在未登记的数据键，请在 DataContractRegistrar.RegisterAll 补齐契约。"
    }
    if ($ledgerViolations.Count -gt 0) {
        Write-Host "校验失败：台账承载了手写 data 段（数据段由运行时契约派生，禁在 ports.json 再写一份）。"
    }
    exit 1
}
exit 0
