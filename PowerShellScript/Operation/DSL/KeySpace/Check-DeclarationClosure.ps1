# Check-DeclarationClosure.ps1
# 声明→绑定闭合门禁（K-02 / R1-a / R1-c / R3 机检面）
#
# 判据（-Check 时违规即退出码 1；载体 = 声明文本 + 实现登记表的结构事实）：
#   DC1 函数绑定：Config\workflows\*.ushar 的 `[Node] fn=` 必须出现在实现注册面
#                  （Agent 侧 WorkflowFunctionNames 常量集合）。
#   DC2 端口闭合：声明 outports ⊆ 实现产出键（descriptor 派生面 ∪ WorkflowOutputKeys 显式补充面）；
#                  未登记的函数跳过（灰度）。
#   DC3 冻结产物：declared-manifest.json 在位且含指纹字段（R3 判据载体）。
#   DC4 负对照：合成「声明多端口」样本必须命中 DC2（判据非空转）。
#   DC5 豁免册：Baselines\_DeclarationClosureExemptions.psd1 在位且指向 JSON；条目含 implId/Reason/DueWhen。
#
# 模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    # R3-C：严格档 —— 未登记实现产出键即违规（默认档仅列缺口、跳过判定）。
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateFileIndex.psm1') -Force

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

$violations = [System.Collections.Generic.List[string]]::new()
$workflowDir = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'
$catalogFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\WorkflowSpecCatalog.cs'
$namesDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\WorkflowFunction'
$outputKeysFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Axioms\Loops\WorkflowOutputKeys.cs'
$manifestFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Declared\declared-manifest.json'

# ---- 校验范围（单源 = WorkflowRegistrationCatalog.Validate=true；区域/电路声明豁免）----
$validatedFiles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $catalogFile) {
    $catalogText = Get-Content -LiteralPath $catalogFile -Raw
    $fileMap = @{}
    foreach ($m in [regex]::Matches($catalogText, 'public\s+const\s+string\s+(?<name>[A-Za-z0-9_]+)\s*=\s*"(?<file>[^"]+\.ushar)"')) {
        $fileMap[$m.Groups['name'].Value] = $m.Groups['file'].Value
    }
    foreach ($m in [regex]::Matches($catalogText, 'new\s+Entry\(WorkflowSpecFiles\.(?<const>[A-Za-z0-9_]+)\s*,\s*"[^"]*"\s*,\s*[^,]+,\s*true\s*\)')) {
        $const = $m.Groups['const'].Value
        if ($fileMap.ContainsKey($const)) { [void]$validatedFiles.Add($fileMap[$const]) }
    }
}
if ($validatedFiles.Count -eq 0) {
    $violations.Add('DC0 校验范围为空：WorkflowRegistrationCatalog.Validate=true 未解析出声明文件')
}

# ---- 实现注册面（函数名常量；声明文件族 = WorkflowFunctionNames.*.cs）----
$registered = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
# 常量名 → 字面值（产出键表按常量名登记，声明面按字面值引用；缺此映射 ⇒ 登记永远不命中）。
$functionNameMap = @{}
if (Test-Path -LiteralPath $namesDir) {
    $nameFiles = @(Get-GateFiles -Root $namesDir -Extension cs |
        Where-Object { (Split-Path -Leaf $_) -like 'WorkflowFunctionNames*.cs' })
    foreach ($nameFile in $nameFiles) {
        $nameText = Get-Content -LiteralPath $nameFile -Raw
        foreach ($m in [regex]::Matches($nameText, 'const\s+string\s+(?<name>[A-Za-z0-9_]+)\s*=\s*"(?<value>[A-Za-z0-9_.\-]+)"')) {
            [void]$registered.Add($m.Groups['value'].Value)
            $functionNameMap[$m.Groups['name'].Value] = $m.Groups['value'].Value
        }
    }
}

# ---- 实现产出键面（派生面 descriptor ∪ 显式补充面；仅声明项参与闭合）----
$produced = @{}

# 1) 派生面（F1）：descriptor 声明（与实现同处一行 = 唯一来源）
$descriptorRoot = Join-Path $Root 'Projects\Plug\Agent\src'
if (Test-Path -LiteralPath $descriptorRoot) {
    foreach ($file in (Get-GateFiles -Root $descriptorRoot -Extension cs)) {
        $descriptorText = Get-Content -LiteralPath $file -Raw
        foreach ($m in [regex]::Matches(
                $descriptorText,
                'new\s+NamedWorkflowFunction\(\s*(?<fn>[^,]+?)\s*,\s*[^,]+?\s*,\s*(?:producedPorts:\s*)?new\s*(?:\[\])?\s*\{(?<ports>[^}]*)\}\s*\)')) {
            $fnRaw = $m.Groups['fn'].Value.Trim()
            $fn = $null
            if ($fnRaw -match '^"(?<lit>[^"]+)"$') {
                $fn = $Matches['lit']
            }
            elseif ($fnRaw -match '(?<const>[A-Za-z0-9_]+)$' -and $functionNameMap.ContainsKey($Matches['const'])) {
                $fn = $functionNameMap[$Matches['const']]
            }
            if ([string]::IsNullOrWhiteSpace($fn)) { continue }
            $ports = @([regex]::Matches($m.Groups['ports'].Value, '"([^"]+)"') |
                ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ })
            if ($ports.Count -gt 0) { $produced[$fn] = $ports }
        }
    }
}

# 2) 显式补充面（非 descriptor 场景，如动态工具面）；迁入 descriptor 后应删除对应行。
#    字面值口径：端口名直接写字符串（禁 RoundKeys.X 之类的二次映射表 —— 那会形成第三份清单）。
if (Test-Path -LiteralPath $outputKeysFile) {
    $text = Get-Content -LiteralPath $outputKeysFile -Raw
    foreach ($block in [regex]::Matches($text, '\[WorkflowFunctionNames\.([A-Za-z0-9_]+)\]\s*=\s*new\[\]\s*\{([^}]*)\}')) {
        $fnConst = $block.Groups[1].Value
        $fn = if ($functionNameMap.ContainsKey($fnConst)) { $functionNameMap[$fnConst] } else { $fnConst }
        if ($produced.ContainsKey($fn)) { continue }
        $ports = @([regex]::Matches($block.Groups[2].Value, '"([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ })
        if ($ports.Count -gt 0) { $produced[$fn] = $ports }
    }
}

# ---- DC1/DC2：声明面逐节点 ----
$unregistered = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $workflowDir) {
    foreach ($file in (Get-ChildItem -LiteralPath $workflowDir -Filter *.ushar -File)) {
        if ($validatedFiles.Count -gt 0 -and -not $validatedFiles.Contains($file.Name)) { continue }
        foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
            $m = [regex]::Match($line, '^\[Node\]\s+(?<node>[A-Za-z0-9_.]+):\s*fn=(?<fn>[A-Za-z0-9_.]+)[^\r\n]*outports=(?<ports>[^\r\n|]*)')
            if (-not $m.Success) { continue }
            $node = $m.Groups['node'].Value
            $fn = $m.Groups['fn'].Value
            if (-not $registered.Contains($fn)) {
                $violations.Add("DC1 未注册函数：$($file.Name):node=$node, fn=$fn")
                continue
            }
            if (-not $produced.ContainsKey($fn)) {
                # 未登记 ⇒ 默认档跳过（灰度 + 缺口可见化）；-Strict 档 = 违规（R3-C）。
                $entry = "$($file.Name):node=$node, fn=$fn"
                $unregistered.Add($entry)
                if ($Strict) { $violations.Add("DC2s 未登记实现产出键：$entry") }
                continue
            }
            $declared = @($m.Groups['ports'].Value.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            $missing = @($declared | Where-Object { $produced[$fn] -notcontains $_ })
            if ($missing.Count -gt 0) {
                $violations.Add("DC2 端口未闭合：$($file.Name):node=$node, fn=$fn, missing=[$($missing -join ',')]")
            }
        }
    }
} else {
    $violations.Add('DC0 声明目录缺失：Projects\Plug\Agent\Config\workflows')
}

# ---- DC3：冻结产物 ----
if (-not (Test-Path -LiteralPath $manifestFile)) {
    $violations.Add('DC3 冻结产物缺失：declared-manifest.json')
} else {
    $json = Get-Content -LiteralPath $manifestFile -Raw
    if ($json -notmatch '"fingerprint"') { $violations.Add('DC3 冻结产物缺指纹字段：declared-manifest.json') }
}

# ---- DC5：孤儿豁免册（F3；单源 = JSON，运行期共读）----
$exemptRegister = Join-Path $Root 'Frame\PowerShellScript\Baselines\_DeclarationClosureExemptions.psd1'
$exemptData = Join-Path $Root 'Frame\PowerShellScript\Baselines\Declarations\declaration-exemptions.json'
if (-not (Test-Path -LiteralPath $exemptRegister)) {
    $violations.Add('DC5 豁免册登记缺失：Baselines\_DeclarationClosureExemptions.psd1')
} else {
    $psdText = Get-Content -LiteralPath $exemptRegister -Raw
    if ($psdText -notmatch 'Declaration\\Declarations\\declaration-exemptions\.json|Declarations\\declaration-exemptions\.json') {
        $violations.Add('DC5 豁免册登记未指向 DataFile：declaration-exemptions.json')
    }
}
if (-not (Test-Path -LiteralPath $exemptData)) {
    $violations.Add('DC5 豁免册数据缺失：Baselines\Declarations\declaration-exemptions.json')
} else {
    try {
        $exemptDoc = Get-Content -LiteralPath $exemptData -Raw | ConvertFrom-Json
        foreach ($item in @($exemptDoc.items)) {
            if ($null -eq $item) { continue }
            if ($item -is [string]) {
                if ([string]::IsNullOrWhiteSpace($item)) { $violations.Add('DC5 豁免条目为空串') }
                continue
            }
            foreach ($key in @('implId', 'Reason', 'DueWhen')) {
                $present = @($item.PSObject.Properties.Name) -contains $key
                if (-not $present -or [string]::IsNullOrWhiteSpace([string]$item.$key)) {
                    $violations.Add("DC5 豁免条目缺字段：$key（implId=$($item.implId)）")
                }
            }
        }
    }
    catch {
        $violations.Add("DC5 豁免册解析失败：$($_.Exception.Message)")
    }
}

if (-not $Strict -and $unregistered.Count -gt 0) {
    Write-Host ("DC2 缺口（未登记实现产出键，共 {0} 项；-Strict 时判红）：" -f $unregistered.Count)
    foreach ($entry in $unregistered) { Write-Host ("  - " + $entry) }
}

if ($NegativeControl) {
    $sampleFn = 'guard_input'
    $samplePorts = @('filtered_input')
    if ($produced.ContainsKey($sampleFn)) {
        $probe = @($samplePorts + 'not_declared_port')
        $hit = @($probe | Where-Object { $produced[$sampleFn] -notcontains $_ })
        if ($hit.Count -eq 0) { $violations.Add('DC4 负对照失败：未声明端口样本未命中') }
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'DeclarationClosure'
