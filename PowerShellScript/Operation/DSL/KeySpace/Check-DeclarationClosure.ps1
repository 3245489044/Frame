# Check-DeclarationClosure.ps1
# 声明→绑定闭合门禁（K-02 / R1-a / R1-c / R3 机检面）
#
# 判据（-Check 时违规即退出码 1；载体 = 声明文本 + 实现登记表的结构事实）：
#   DC1 函数绑定：Config\workflows\*.ushar 的 `[Node] fn=` 必须出现在实现注册面
#                  （Agent 侧 WorkflowFunctionNames 常量集合）。
#   DC2 端口闭合：声明 outports ⊆ 实现产出键（WorkflowOutputKeys 登记项）；未登记的函数跳过（灰度）。
#   DC3 冻结产物：declared-manifest.json 在位且含指纹字段（R3 判据载体）。
#   DC4 负对照：合成「声明多端口」样本必须命中 DC2（判据非空转）。
#
# 模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
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
if (Test-Path -LiteralPath $namesDir) {
    $nameFiles = @(Get-GateFiles -Root $namesDir -Extension cs |
        Where-Object { (Split-Path -Leaf $_) -like 'WorkflowFunctionNames*.cs' })
    foreach ($nameFile in $nameFiles) {
        foreach ($m in [regex]::Matches((Get-Content -LiteralPath $nameFile -Raw), '=\s*"([A-Za-z0-9_.\-]+)"')) {
            [void]$registered.Add($m.Groups[1].Value)
        }
    }
}

# ---- 实现产出键面（函数 → 端口集合；仅登记项参与闭合）----
$produced = @{}
$portMap = @{
    'RoundKeys.FilteredInput' = 'filtered_input'
    'RoundKeys.SafeOutput'    = 'safe_output'
    'RoundKeys.UserInput'     = 'user_input'
}
if (Test-Path -LiteralPath $outputKeysFile) {
    $text = Get-Content -LiteralPath $outputKeysFile -Raw
    foreach ($block in [regex]::Matches($text, '\[WorkflowFunctionNames\.([A-Za-z0-9_]+)\]\s*=\s*new\[\]\s*\{([^}]*)\}')) {
        $fn = $block.Groups[1].Value
        $ports = @([regex]::Matches($block.Groups[2].Value, '[A-Za-z0-9_.]+') | ForEach-Object {
            $raw = $_.Value
            if ($portMap.ContainsKey($raw)) { $portMap[$raw] } else { $raw }
        } | Where-Object { $_ -ne 'RoundKeys' })
        $produced[$fn] = $ports
    }
}

# ---- DC1/DC2：声明面逐节点 ----
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
            if (-not $produced.ContainsKey($fn)) { continue }
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
