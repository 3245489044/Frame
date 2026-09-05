<#
.SYNOPSIS
    新增一条公理：写 1 行自描述契约（到 DSL 承载技能正文）→ 定向生成公理类 + 幂等合并 mapping → 重编译 references 索引。
    把「改契约 + 跑生成器 + 合并 mapping + 更新索引」收敛为一次调用（对齐框架优化方案 B2）。

.DESCRIPTION
    契约写法（自描述元数据，消除层级/轴启发式猜测）：
        [AX] Name: 公式 | @level:L2 | @axis:infra | irr:"structural"
    Body（公式）仍需人工写；@level/@axis 可选（缺省按 LevelMappingRules 启发式 + 文件默认）。
    契约源统一为 DSL 承载技能（skills/**/SKILL.md，如 l0-logic），生成器自动剥离 front-matter。

.EXAMPLE
    .\Add-Axiom.ps1 -Name DerivedDataAutoMaintenance -Body "可重建派生数据由存储层自动维护" -Level L2 -Axis infra
    .\Add-Axiom.ps1 -Name NewAxiom -Body "..." -Level L1 -Axis round -Section "## B5 L2 领域公理（可版本化）"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Name,
    [string]$Body = "",
    [ValidateSet('L0','L0.5','L1','L2','L3')][string]$Level,
    [string]$Axis,
    [string]$Prompt = 'Agent/skills/l0/l0-logic/SKILL.md',
    [string]$Section = '## B5 L2 领域公理（可版本化）',
    [string]$GeneratorProject = 'Agent/Axioms.Generator',
    [string]$GeneratorOut = 'Agent/Axioms'
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# ---------- 1) 在契约技能正文 $Section 末追加声明 ----------
$promptPath = Join-Path $Root $Prompt
if (-not (Test-Path $promptPath)) { throw "契约技能不存在: $promptPath" }
$lines = Get-Content $promptPath -Encoding UTF8
$idx = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq $Section) { $idx = $i }
}
if ($idx -lt 0) { throw "未找到节标题 '$Section'（现有节: $(Select-String -Path $promptPath -Pattern '^## ' | ForEach-Object { $_.Line } | Join-String -Separator ' | ')）" }

# 追加到该节末尾（下一个 ## 之前），若已存在同名公理则报错避免重复
$dup = Select-String -Path $promptPath -Pattern "\[[A-Z-]+\]\s*$([regex]::Escape($Name))\s*:"
if ($dup) { throw "公理 $Name 已存在于 $promptPath :$($dup[0].LineNumber)" }

$line = "[AX] ${Name}: $Body"
if ($Level) { $line += " | @level:$Level" }
if ($Axis)  { $line += " | @axis:$Axis" }

$insertAt = $lines.Count  # 默认追加到文件末
for ($i = $idx + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## ') { $insertAt = $i; break }
}
$newLines = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($i -eq $insertAt) { $newLines.Add($line); $newLines.Add("") }
    $newLines.Add($lines[$i])
}
if ($insertAt -eq $lines.Count) { $newLines.Add(""); $newLines.Add($line) }
$newLines | Set-Content $promptPath -Encoding UTF8
Write-Host "[1/3] 契约已写入 ${Prompt} :" -ForegroundColor Cyan
Write-Host "      $line"

# ---------- 2) 定向生成（永不全量；mapping 幂等合并） ----------
Write-Host "[2/3] 定向生成 $Name ..." -ForegroundColor Cyan
# 契约源统一为 DSL 承载技能（SKILL.md，生成器自动剥离 front-matter）
$CarrierFiles = @(
    (Join-Path $Root 'Agent/skills/l0/l0-system/SKILL.md'),
    (Join-Path $Root 'Agent/skills/l0/l0-loop/SKILL.md'),
    (Join-Path $Root 'Agent/skills/l0/l0-logic/SKILL.md'),
    (Join-Path $Root 'Agent/skills/l0/l0-format/SKILL.md'),
    (Join-Path $Root 'Agent/skills/internal/l1l2/SKILL.md'),
    (Join-Path $Root 'Agent/skills/rare/boundaries/SKILL.md')
)
Push-Location $Root
try {
    $genArgs = @('--project', $GeneratorProject, '--', $GeneratorOut)
    foreach ($c in $CarrierFiles) { $genArgs += '--source'; $genArgs += $c }
    $genArgs += '--include'; $genArgs += $Name
    dotnet run @genArgs
    if ($LASTEXITCODE -ne 0) { throw "Axioms.Generator 运行失败 (exit=$LASTEXITCODE)" }
}
finally { Pop-Location }

# ---------- 3) 重编译 references 索引 ----------
$validator = Join-Path $Root 'PowerShellScript/DSL/Check-ClinerulesReferences.ps1'
if (Test-Path $validator) {
    Write-Host "[3/3] 重编译 references 索引 ..." -ForegroundColor Cyan
    & $validator -RulesRoot (Join-Path $Root '.clinerules') -UpdateIndex
}
else { Write-Host "[3/3] 未找到校验器 $validator，跳过索引重编译" }

Write-Host "完成。生成路径: $GeneratorOut/$(if ($Level) {$Level} else {'<由启发式决定>'})/$Name.cs" -ForegroundColor Green
