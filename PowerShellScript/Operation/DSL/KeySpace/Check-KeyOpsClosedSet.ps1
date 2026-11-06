# Check-KeyOpsClosedSet.ps1
# KB2 固有函数闭集（棘轮）：键上下文内的拼接/插值/前缀判据/切片只允许经 KeyOps。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    # 机器可读出口（P2）：-Json 整块结果；-EmitActual 只输出 actual 三态表（供一次写基线）。
    [switch]$Json,
    [switch]$EmitActual
)

$ErrorActionPreference = 'Stop'
$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateFileIndex.psm1') -Force
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name KeyOpsClosedSet
# 违规清单须先于白名单解析就位（白名单缺 Kind/Reason 也在本单收集，禁 null 方法调用）。
$violations = [System.Collections.Generic.List[string]]::new()

$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)
$exclude = 'Modules[\\/]KeySpace[\\/]|Modules[\\/]Parsing[\\/]Keys[\\/]|[\\/](?:obj|bin|BuildOutput)[\\/]|[\\/](?:Tests?|\.Tests)[\\/]'
$rules = [ordered]@{
    concat = '(?:ProcessKeys|SegmentKeys|SessionViewKeys|ObservabilityKeys|ArchiveKeys|MessageStreamKeys)\.(?:FamilyPrefix|PositionKeyTemplate|IdentityTemplate)\s*\+|(?:FamilyPrefix|KeyPrefix)\s*\+\s*(?:container|session|task|round|kind|ordinal)'
    interp = '\$"[^"\r\n]*\{(?:round|seq|ordinal)\}[^"\r\n]*"'
    starts = 'StartsWith\([^)]*FamilyPrefix'
    slice  = '(?:NodeKey|KeyPrefix|FamilyPrefix)\.(?:Substring|Split)\('
    # 形态判据（治 G1「判据锚在符号而非形态」）：局部变量 / 分隔符字面量拼接也须判红。
    concatLiteral = '\b[A-Za-z_][A-Za-z0-9_\.]*\s*\+\s*"\."\s*\+'
    interpDot = '\$"[^"\r\n]*\{(?:nodeKey|container|session|task|workflowId|round|kind|ordinal|seq)\}\.[^"\r\n]*\{'
    startsLiteral = 'StartsWith\(\s*"(?:smsg|proc|session|win|ans|sv|obs|arch|seg)\.'
    splitDot = '\.Split\(\s*[''"]\.[''"]\s*\)'

}

$actual = @{}
foreach ($scanRoot in $scanRoots) {
    if (-not (Test-Path -LiteralPath $scanRoot)) { continue }
    foreach ($file in @(Get-GateFiles -Root $scanRoot -Extension cs)) {
        $rel = $file.Substring($Root.Length + 1).Replace('\', '/')
        if ($rel -match $exclude) { continue }
        $text = Get-Content -LiteralPath $file -Raw
        if ($null -eq $text) { continue }
        # 注释不是键上下文（先剥注释，防文档示例/说明文字被计入）。
        $text = [regex]::Replace($text, '/\*.*?\*/', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        $text = [regex]::Replace($text, '//[^\r\n]*', '')
        foreach ($rule in $rules.Keys) {
            $count = ([regex]::Matches($text, $rules[$rule])).Count
            if ($count -gt 0) { $actual["$rel|$rule"] = $count }
        }
    }
}

$allowed = @{}
foreach ($row in @($baseline.Value.Value)) {
    if ($row -is [string]) { $allowed[$row] = 1; continue }
    $key = [string]$row['Key']; if ([string]::IsNullOrWhiteSpace($key)) { continue }
    $allowed[$key] = if ($row.Contains('Count')) { [int]$row['Count'] } else { 1 }
}

# 非键白名单（结构事实，非"调宽正则"）：每条须带 Kind + Reason，重复命中按 Count 校验。
# Kind ∈ {path, css, type-name, tag, log, ui-text, port-contract, identity-token}。
$whitelist = @{}
if ($baseline.ContainsKey('Whitelist')) {
    foreach ($row in @($baseline.Whitelist.Value)) {
        if ($row -is [string]) { continue }
        $key = [string]$row['Key']; if ([string]::IsNullOrWhiteSpace($key)) { continue }
        $kind = [string]$row['Kind']
        $reason = [string]$row['Reason']
        if ([string]::IsNullOrWhiteSpace($kind) -or [string]::IsNullOrWhiteSpace($reason)) {
            $violations.Add("白名单条目缺 Kind/Reason：$key（显式归因是白名单成立前提）")
            continue
        }
        $whitelist[$key] = if ($row.Contains('Count')) { [int]$row['Count'] } else { 1 }
    }
}

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($key in $actual.Keys) {
    if ($whitelist.ContainsKey($key)) {
        if ([int]$actual[$key] -gt [int]$whitelist[$key]) {
            $violations.Add("超白名单：$key（actual=$($actual[$key]), whitelist=$($whitelist[$key])）")
        }
        continue
    }
    if (-not $allowed.ContainsKey($key)) { $violations.Add("未登记键上下文拼接/切片：$key（actual=$($actual[$key])）") }
    elseif ([int]$actual[$key] -gt [int]$allowed[$key]) {
        $violations.Add("超基线：$key（actual=$($actual[$key]), baseline=$($allowed[$key])）")
    }
}

# actual 三态表（P2）：供 -EmitActual 一次写基线（已登记 / 白名单 / 未登记；未命中项也列出，便于机械对账）。
$actualOut = [ordered]@{}
foreach ($key in $actual.Keys) {
    $state = if ($whitelist.ContainsKey($key)) { 'whitelist' }
             elseif ($allowed.ContainsKey($key)) { 'registered' }
             else { 'unregistered' }
    $limit = if ($whitelist.ContainsKey($key)) { [int]$whitelist[$key] }
             elseif ($allowed.ContainsKey($key)) { [int]$allowed[$key] }
             else { 0 }
    $actualOut[$key] = [ordered]@{ count = [int]$actual[$key]; state = $state; limit = $limit }
}
foreach ($key in $allowed.Keys) {
    if (-not $actualOut.Contains($key)) { $actualOut[$key] = [ordered]@{ count = 0; state = 'registered'; limit = [int]$allowed[$key] } }
}
foreach ($key in $whitelist.Keys) {
    if (-not $actualOut.Contains($key)) { $actualOut[$key] = [ordered]@{ count = 0; state = 'whitelist'; limit = [int]$whitelist[$key] } }
}

if ($NegativeControl) {
    $probe = 'var p = ProcessKeys.FamilyPrefix + session;'
    if ($probe -notmatch $rules['concat']) { $violations.Add('负对照失败：拼接样本未命中') }
    $probe2 = 'var q = $"session.{session}.r{round}.";'
    if ($probe2 -notmatch $rules['interp']) { $violations.Add('负对照失败：插值样本未命中') }
    if ('FamilyPrefix.Substring(1)' -notmatch $rules['slice']) { $violations.Add('负对照失败：切片样本未命中') }

    # 形态规则正对照（必须命中：文档 §5.1/§5.3 样本）。
    if ('var k = nodeKey + "." + port;' -notmatch $rules['concatLiteral']) { $violations.Add('负对照失败：字面量拼接样本未命中') }
    if ('var w = WorkflowId + "." + ToStorageKey();' -notmatch $rules['concatLiteral']) { $violations.Add('负对照失败：方法调用拼接样本未命中') }
    if ('var x = $"{nodeKey}.{port}";' -notmatch $rules['interpDot']) { $violations.Add('负对照失败：占位后接点号样本未命中') }
    if ('var y = k.StartsWith("smsg.");' -notmatch $rules['startsLiteral']) { $violations.Add('负对照失败：字面前缀样本未命中') }
    if ("var z = k.Split('.');" -notmatch $rules['splitDot']) { $violations.Add('负对照失败：点号切片样本未命中') }

    # 形态规则负对照（必须不命中：白名单词法 / 非键字符串）。
    if ('var s = a + b;' -match $rules['concatLiteral']) { $violations.Add('负对照失败：普通相加被误判为拼接') }
    if ('var t = path.Replace(".", "/");' -match $rules['concatLiteral']) { $violations.Add('负对照失败：Replace 被误判为拼接') }
    if ('var u = "a\r\nb";' -match $rules['interpDot']) { $violations.Add('负对照失败：转义字符串被误判为插值键') }
    if ("var v = k.Split(',');" -match $rules['splitDot']) { $violations.Add('负对照失败：逗号切片被误判为点号切片') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyOpsClosedSet' -Json:$Json -EmitActual:$EmitActual -Actual $actualOut
