#requires -Version 7.0
<#
.SYNOPSIS
    文本安全守卫（P4 / C4）：进入 DOM 的值必须经 HtmlText（非标量 ⇒ ⊘ + 诊断）。
.DESCRIPTION
    C1 单源在位：Ui\Views\HtmlText.cs 存在且含非标量占位分支（NonScalar）；
    C2 HtmlSectionItem 构造点全部经 HtmlText（构造点行数 ≤ 同一文件 HtmlText.Readable 次数）；
    C3 Views 内不得把对象字段直拼进文本（`.Payload`/`.Fields` 直接 Add/+）；
    C4 负对照：合成 `details.Add(row.Payload)` 必须被 C3 判据捕获，合法 `HtmlText.Readable(row.Payload)` 不误报。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$views = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views'
$textPath = Join-Path $views 'HtmlText.cs'
$rendererPath = Join-Path $views 'Rendering\ConversationPageRenderer.cs'

$unsafePattern = '\.Add\(\s*[A-Za-z_]\w*\.(Payload|Fields|Raw)\s*\)|\+\s*[A-Za-z_]\w*\.(Payload|Fields|Raw)\b'

function Test-UnsafeText([string]$text) { $text -match $unsafePattern }

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = 'HtmlText 单源在位（非标量 ⇒ ⊘）'
        Ok = { (Test-Path -LiteralPath $textPath) -and ((Get-Content -LiteralPath $textPath -Raw) -match 'NonScalar') } }
    [pscustomobject]@{ Id = 'C2'; Title = 'HtmlSectionItem 构造点经 HtmlText'
        Ok = {
            $text = Get-Content -LiteralPath $rendererPath -Raw
            $sites = [regex]::Matches($text, 'new\s+HtmlSectionItem\(').Count
            $guarded = [regex]::Matches($text, 'HtmlText\.Readable\(').Count
            return $sites -gt 0 -and $guarded -ge $sites
        } }
    [pscustomobject]@{ Id = 'C3'; Title = 'Views 内禁对象字段直拼文本'
        Ok = {
            $bad = @()
            Get-ChildItem -LiteralPath $views -Recurse -Include *.cs -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
                ForEach-Object {
                    if (Test-UnsafeText (Get-Content -LiteralPath $_.FullName -Raw)) { $bad += $_.FullName }
                }
            return $bad.Count -eq 0
        } }
)

if ($NegativeControl) {
    $bad = 'details.Add(row.Payload);'
    $good = 'details.Add(HtmlText.Readable(row.Payload));'
    if (-not (Test-UnsafeText $bad)) { throw '负对照失败：C3 未捕获对象直拼' }
    if (Test-UnsafeText $good) { throw '负对照失败：C3 对经 HtmlText 的样本误报' }
    Write-Host '[OK] 负对照通过：对象直拼被捕获，HtmlText 包装不误报。' -ForegroundColor Green
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

if ($Check) {
    $failed = 0
    foreach ($r in $rules) {
        $pass = $false
        try { $pass = [bool](& $r.Ok) } catch { $pass = $false }
        $mark = if ($pass) { 'PASS' } else { 'FAIL' }
        if (-not $pass) { $failed++ }
        Write-Host ("[{0}] {1} {2}" -f $mark, $r.Id, $r.Title) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
    }
    if ($failed -gt 0) {
        Write-Host "UiTextSafe: $failed 项违规（文本未单源/对象直拼）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'UiTextSafe: 全部通过。' -ForegroundColor Green
}

exit 0
