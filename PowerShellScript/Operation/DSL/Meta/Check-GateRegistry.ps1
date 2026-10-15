# Check-GateRegistry.ps1
# 门禁注册表对账（P4 · 声明门 ⇔ 已登记运行期实现）
#
# 声明面权威 = .clinerules/formula.md 的 [Gate] 符号集 D（解析 '^[Gate] NAME:'）。
# 注册表 = Operation\DSL\GateRegistry.psd1 键集 R（已实现的运行期门）。
# 判据：
#   G1 悬空实现：R 项 Impl 文件不在位 → 失败。
#   G2 实现漂移：R 项 Impl 文件不含其符号名 → 失败。
#   G3 未声明登记：R 项 Declared 非空但 Declared ∉ D → 失败。
#   G4 可见化：D\R（已声明但无运行期实现；行为门预期非空，仅报告不失败）。
# 模式：默认报告（不设退出码）；-Check 时 G1/G2/G3 违规 → 退出码 1。只读仓库，零侧写。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$registryPath = Join-Path $PSScriptRoot '..\Registry\GateRegistry.psd1'
$formulaPath = Join-Path $Root '.clinerules\formula.md'

if (-not (Test-Path -LiteralPath $registryPath)) { Write-Error "门禁注册表不存在：$registryPath"; exit 2 }
if (-not (Test-Path -LiteralPath $formulaPath)) { Write-Error "声明面不存在：$formulaPath"; exit 2 }

$registry = Import-PowerShellDataFile -LiteralPath $registryPath
$formulaText = Get-Content -LiteralPath $formulaPath -Raw

# 声明门集 D：[Gate] NAME
$declared = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($m in [regex]::Matches($formulaText, '(?m)^\[Gate\]\s+([A-Za-z0-9_.]+)\s*:')) {
    [void]$declared.Add($m.Groups[1].Value)
}

$failures = [System.Collections.Generic.List[string]]::new()
$registered = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($symbol in $registry.Keys) {
    $entry = $registry[$symbol]
    [void]$registered.Add($symbol)

    $implRel = [string]$entry.Impl
    if ([string]::IsNullOrWhiteSpace($implRel)) {
        $failures.Add("G1 $symbol：Impl 缺省（登记项须给实现落点）")
        continue
    }

    $implFull = Join-Path $Root ($implRel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $implFull)) {
        $failures.Add("G1 $symbol：悬空实现（文件不在位）$implRel")
        continue
    }

    $implText = Get-Content -LiteralPath $implFull -Raw
    if ($implText -notmatch ("\b" + [regex]::Escape($symbol) + "\b")) {
        $failures.Add("G2 $symbol：实现漂移（$implRel 内不含符号名）")
    }

    $declaredSym = [string]$entry.Declared
    if (-not [string]::IsNullOrWhiteSpace($declaredSym) -and -not $declared.Contains($declaredSym)) {
        $failures.Add("G3 $symbol：登记指向未声明门 '$declaredSym'（formula.md 无此 [Gate]）")
    }
}

# G4 可见化：已声明但无运行期实现（行为门，预期非空）
$covered = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($symbol in $registry.Keys) {
    $d = [string]$registry[$symbol].Declared
    if (-not [string]::IsNullOrWhiteSpace($d)) { [void]$covered.Add($d) }
}
$unimplemented = @($declared | Where-Object { -not $covered.Contains($_) } | Sort-Object)

Write-Host '=== [GateRegistry] 门禁注册表对账（声明门 ⇔ 运行期实现） ==='
Write-Host ("声明门 D={0} | 登记实现 R={1} | G4 未实现(行为门)={2}" -f $declared.Count, $registered.Count, $unimplemented.Count)

if ($unimplemented.Count -gt 0) {
    Write-Host '--- [G4 可见化] 已声明但无运行期实现（行为门，非失败） ---'
    Write-Host ("  " + ($unimplemented -join ', '))
}

if ($failures.Count -gt 0) {
    Write-Host '--- 违规清单（G1/G2/G3） ---'
    $failures | ForEach-Object { Write-Host ("  [违规] {0}" -f $_) }
}

if ($Check -and $failures.Count -gt 0) {
    Write-Host '校验失败：门禁注册表对账未通过（悬空/漂移/未声明登记）。'
    exit 1
}

if ($failures.Count -eq 0) { Write-Host 'GateRegistry: OK —— 登记实现无悬空/漂移，且均指向已声明门。' }
exit 0
