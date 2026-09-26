#requires -Version 5.1
<#
.SYNOPSIS
    键文法单源守卫（KeyGrammar）—— 「八套键文法并存」收敛的机器判据。

.DESCRIPTION
    扫描面：Frame\CsharpPrj\**\*.cs + Projects\Plug\Agent\src\**\*.cs
            （排除 obj/bin/_deprecated；跳过注释/XML 文档行；测试工程不在扫描面）。

    判据（键面收敛 = 构造/解析/族判据/键序各恰一份实现；权威 = Modules\Parsing\Keys 内核）：
      K1 键族字面量单源：键族前缀/分隔符字面量（"#R" / "proc." / "cline.session." / ".round." /
         "keymapping." / "checkpoint." / "relations."）只允许出现在「键文法单源白名单」文件内；
         基线 = 已实测落点数（只减不增）。
      K2 零手写键切片：键面文件（文件名含 Key/Keys）禁 `Substring(` 手写切片（反解一律经文法）。
      K3 零就地键序：`OrderBy(...ParseRound/ParseSeq...)` 就地比较 = 0（须用键文法比较器）。
      K4 零自建比较器：键面文件 `Comparer<string>.Create` = 0（比较器单源 = 内核）。
      K5 正对照（防假绿）：内核 `KeyGrammar.Create(` 定义点数 ≥ 基线（防"文法被删/未注册而门禁恒绿"）。
      K6 正对照（阶段 4）：声明绑定用例在位（`DeclarationBindingTests` 存在于内核单测工程）。

    模式：默认报告模式（列出命中，不设退出码）；-Check 校验模式（命中 > 0 → 退出码 1）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Parsing → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（K1–K4）或正对照失效（K5/K6）→ 退出码 1。

.PARAMETER ShowDetail
    输出逐条实测计数（含达标项）。

.EXAMPLE
    .\Check-KeyGrammar.ps1 -ShowDetail

.EXAMPLE
    .\Check-KeyGrammar.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$pending    = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  键文法单源守卫（键面构造/解析/族判据/键序单一实现）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# ---------------- 扫描面 ----------------
$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)

$files = @()
foreach ($scanRoot in $scanRoots)
{
    if (-not (Test-Path $scanRoot)) { continue }
    $files += @(Get-ChildItem -Path $scanRoot -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' })
}

# 代码行提取（跳过注释 / XML 文档行）
function Get-CodeLines {
    param([string]$Path)
    $result = [System.Collections.Generic.List[object]]::new()
    $lines = @(Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue)
    for ($i = 0; $i -lt $lines.Count; $i++)
    {
        $text = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $trimmed = $text.Trim()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*')) { continue }
        $result.Add([pscustomobject]@{ Line = $i + 1; Text = $trimmed })
    }
    return , $result
}

# ---------------- K1 键族字面量单源 ----------------
# 白名单 = 键文法单源（内核 + 各域文法单源文件）；新增落点即违规
$grammarWhitelist = @(
    'Modules\Parsing\Keys\',
    'Abstractions\SessionMessageKeys.cs',
    'Abstractions\NodeKeyPrefix.cs',
    'Abstractions\KeyMappingKeys.cs',
    'Abstractions\RelationDeclaration.cs',
    'Ids\FactIdGrammar.cs',
    'Ids\FactIdMigration.cs',
    'NodeDomain\ProcessKeys.cs',
    'Instances\InstanceRegistry.cs'
)

# 基线 = 2026-09-26 实测落点文件数（只减不增）
$k1Patterns = @(
    [pscustomobject]@{ Id = 'K1a'; Literal = '"#R"';                Baseline = 1 }
    [pscustomobject]@{ Id = 'K1b'; Literal = '"proc."';             Baseline = 1 }
    [pscustomobject]@{ Id = 'K1c'; Literal = '"cline.session."';    Baseline = 0 }
    [pscustomobject]@{ Id = 'K1d'; Literal = '".round."';           Baseline = 0 }
    [pscustomobject]@{ Id = 'K1e'; Literal = '"keymapping."';       Baseline = 1 }
    [pscustomobject]@{ Id = 'K1f'; Literal = '"checkpoint."';       Baseline = 1 }
    [pscustomobject]@{ Id = 'K1g'; Literal = '"relations."';        Baseline = 1 }
)

foreach ($entry in $k1Patterns)
{
    $hits = [System.Collections.Generic.List[object]]::new()
    foreach ($file in $files)
    {
        $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
        $isWhitelisted = $false
        foreach ($allowed in $grammarWhitelist)
        {
            if ($rel -like "*$allowed*") { $isWhitelisted = $true; break }
        }

        foreach ($code in (Get-CodeLines -Path $file.FullName))
        {
            if ($code.Text.Contains($entry.Literal))
            {
                $hits.Add([pscustomobject]@{ Rel = $rel; Line = $code.Line; Text = $code.Text; Allowed = $isWhitelisted })
            }
        }
    }

    $outside = @($hits | Where-Object { -not $_.Allowed })
    $fileCount = @($hits | ForEach-Object { $_.Rel } | Sort-Object -Unique).Count

    foreach ($hit in $outside)
    {
        $violations.Add("[K1] $($entry.Id) 键族字面量 $($entry.Literal) 出现在白名单外：$($hit.Rel):$($hit.Line)")
    }

    if ($fileCount -gt $entry.Baseline)
    {
        $violations.Add("[K1] $($entry.Id) 键族字面量 $($entry.Literal) 落点数 $fileCount > 基线 $($entry.Baseline)（新增落点）")
    }
    elseif ($fileCount -lt $entry.Baseline -and $ShowDetail)
    {
        $pending.Add("[K1] $($entry.Id) $($entry.Literal)：$fileCount → 目标 ≤ $($entry.Baseline)（基线可下调）")
    }

    if ($ShowDetail) { Write-Host ("  {0} {1}：落点 {2} 文件 / 白名单外 {3}" -f $entry.Id, $entry.Literal, $fileCount, $outside.Count) -ForegroundColor Gray }
}

# ---------------- K2 零手写键切片（键面文件禁 Substring 手写切片） ----------------
$k2Baseline = 5   # 2026-09-26 实测（键面既有落点，随迁移消除）
$keyFaceFiles = @($files | Where-Object { $_.Name -match 'Key' })
$k2Hits = @($keyFaceFiles |
    Select-String -Pattern 'Substring\(' -ErrorAction SilentlyContinue |
    Where-Object { $t = $_.Line.Trim(); -not ($t.StartsWith('//') -or $t.StartsWith('*')) })
$k2Files = @($k2Hits | ForEach-Object { $_.Path } | Sort-Object -Unique)

if ($k2Files.Count -gt $k2Baseline)
{
    $violations.Add("[K2] 键面手写切片落点 $($k2Files.Count) > 基线 $k2Baseline（反解一律经键文法内核）")
    foreach ($hit in $k2Hits) { $violations.Add("[K2] $($hit.Path):$($hit.LineNumber) 键面 Substring 手写切片") }
}
elseif ($k2Files.Count -lt $k2Baseline)
{
    $pending.Add("[K2] 键面手写切片：$($k2Files.Count) → 目标 ≤ $k2Baseline（基线可下调）")
}
if ($ShowDetail)
{
    Write-Host "  K2 键面 Substring：$($k2Files.Count) 文件（基线 $k2Baseline）" -ForegroundColor Gray
    foreach ($file in $k2Files) { Write-Host "      $($file.Substring($Root.Length).TrimStart('\'))" -ForegroundColor DarkGray }
}

# ---------------- K3 零就地键序（OrderBy + ParseRound/ParseSeq） ----------------
$k3Baseline = 0   # 2026-09-26 实测：就地键序已归零（拦存在而非拦新增）
$k3Hits = @($files |
    Select-String -Pattern 'OrderBy\(.*(ParseRound|ParseSeq)' -ErrorAction SilentlyContinue |
    Where-Object { $t = $_.Line.Trim(); -not ($t.StartsWith('//') -or $t.StartsWith('*')) })

if ($k3Hits.Count -gt $k3Baseline)
{
    $violations.Add("[K3] 就地键序落点 $($k3Hits.Count) > 基线 $k3Baseline（须用键文法比较器）")
    foreach ($hit in $k3Hits) { $violations.Add("[K3] $($hit.Path):$($hit.LineNumber) 就地 OrderBy + Parse*") }
}
elseif ($k3Hits.Count -lt $k3Baseline)
{
    $pending.Add("[K3] 就地键序：$($k3Hits.Count) → 目标 ≤ $k3Baseline（基线可下调）")
}
if ($ShowDetail) { Write-Host "  K3 就地键序：$($k3Hits.Count) 处（基线 $k3Baseline）" -ForegroundColor Gray }

# ---------------- K4 零自建比较器（键面禁 Comparer<string>.Create） ----------------
$k4Baseline = 1   # 2026-09-26 实测
$k4Hits = @($files |
    Select-String -Pattern 'Comparer<string>\s*\.\s*Create' -ErrorAction SilentlyContinue |
    Where-Object { $t = $_.Line.Trim(); -not ($t.StartsWith('//') -or $t.StartsWith('*')) })
$k4Files = @($k4Hits | ForEach-Object { $_.Path } | Sort-Object -Unique)

if ($k4Files.Count -gt $k4Baseline)
{
    $violations.Add("[K4] 自建比较器落点 $($k4Files.Count) > 基线 $k4Baseline（比较器单源 = 键文法内核）")
    foreach ($hit in $k4Hits) { $violations.Add("[K4] $($hit.Path):$($hit.LineNumber) Comparer<string>.Create") }
}
elseif ($k4Files.Count -lt $k4Baseline)
{
    $pending.Add("[K4] 自建比较器：$($k4Files.Count) → 目标 ≤ $k4Baseline（基线可下调）")
}
if ($ShowDetail) { Write-Host "  K4 自建比较器：$($k4Files.Count) 文件（基线 $k4Baseline）" -ForegroundColor Gray }

# ---------------- K5 正对照：内核文法定义点数 ≥ 基线（防假绿） ----------------
$k5Baseline = 6   # 2026-09-26 实测（instance-id / session-message / nodes.* ×4）
$k5Hits = @($files |
    Select-String -Pattern 'KeyGrammar\.Create\(' -ErrorAction SilentlyContinue |
    Where-Object { $t = $_.Line.Trim(); -not ($t.StartsWith('//') -or $t.StartsWith('*')) })

if ($k5Hits.Count -lt $k5Baseline)
{
    $violations.Add("[K5] 键文法定义点数 $($k5Hits.Count) < 基线 $k5Baseline（文法被删或未注册 ⇒ 键面判据失效，禁假绿）")
}
elseif ($ShowDetail) { Write-Host "  K5 键文法定义点：$($k5Hits.Count) 处（基线 ≥ $k5Baseline）" -ForegroundColor Gray }

# ---------------- K6 正对照：声明绑定用例在位（阶段 4 判据） ----------------
$bindingTest = Join-Path $Root 'Environment\CentralTest\CSharpTest\Modules.Tests\Parsing.Keys.Tests\Parity\DeclarationBindingTests.cs'
if (-not (Test-Path -LiteralPath $bindingTest))
{
    $violations.Add("[K6] 声明绑定用例缺失：...\Parsing.Keys.Tests\Parity\DeclarationBindingTests.cs（声明模板 ↔ 键文法 的绑定点）")
}
elseif ((Get-Content -LiteralPath $bindingTest -Raw) -notmatch '\[TestMethod\]')
{
    $violations.Add("[K6] 声明绑定用例文件不含 [TestMethod]（绑定判据被掏空）")
}
elseif ($ShowDetail) { Write-Host "  K6 声明绑定用例：在位" -ForegroundColor Gray }

# ---------------- 汇总 ----------------
Write-Host ""
if ($ShowDetail -and $pending.Count -gt 0)
{
    Write-Host "[KG] --- 基线可下调（无新增即通过） ---" -ForegroundColor DarkGray
    foreach ($item in $pending) { Write-Host "  - $item" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0)
{
    Write-Host "[OK] 键文法单源守卫：0 违规（构造/解析/族判据/键序均走内核单源）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 键文法单源守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host "  修复口径：键族字面量/切分/键序 ⇒ 注册 KeyGrammar 并调用内核（Modules\Parsing\Keys）；" -ForegroundColor Gray
Write-Host "            单源白名单见本脚本 \$grammarWhitelist（新增落点须先登记理由）。" -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0
