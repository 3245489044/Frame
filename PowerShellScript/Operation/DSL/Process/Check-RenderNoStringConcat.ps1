#requires -Version 7.0
<#
.SYNOPSIS
    渲染层「零字符串拼接」守卫（RenderNoStringConcat）—— 桥内不得再自行构造 HTML。
.DESCRIPTION
    判据载体 = 源码静态结构（字符串字面量形态 / 类型使用），非执行者叙述。
      N1 HTML 标签字面量 = 0：`"<标签` / `</标签>` 形态。
      N2 StringBuilder = 0（序列化出口在 plate，由 FragmentSerializer 承担）。
      N3 拼接式字符串 = 0：`+ "` / `+ $"`。
      N4 string.Concat = 0（聚合用 string.Join 或节点 Concat）。
      N5 领域中文字面量 = 0（文案由宿主 loc 注入；注释不计）。
      N6 负对照：合成样本（内联 HTML / 拼串 / 中文）必被判红。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
while ($repo -and $repo -ne (Split-Path $repo -Parent)) {
    if ((Test-Path (Join-Path $repo 'Projects')) -and (Test-Path (Join-Path $repo 'Frame'))) { break }
    $repo = Split-Path $repo -Parent
}

$target = Join-Path $repo 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge'
# 节点写出器已下沉 plate：同判据覆盖该文件（桥内 + 写出器，禁自行构造 HTML）。
$extraSources = @(Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Render\UiFragmentWriter.cs')

function Get-SourceText([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { throw "缺目录: $dir" }
    Get-ChildItem -LiteralPath $dir -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
}

# 去注释（只扫代码面；注释里的示例不算违规）
function Remove-Comments([string]$text) {
    $text = [regex]::Replace($text, '/\*.*?\*/', '', 'Singleline')
    return [regex]::Replace($text, '//[^\r\n]*', '')
}

function Test-NoStringConcat([string[]]$sources) {
    $violations = [System.Collections.Generic.List[string]]::new()
    $joined = ($sources | ForEach-Object { Remove-Comments $_ }) -join "`n"
    if ($joined -match '\$?@?"\s*</?[A-Za-z][A-Za-z0-9]*[\s>/]') { $violations.Add('[N1] HTML 标签字面量') }
    if ($joined -match 'new\s+StringBuilder\s*\(') { $violations.Add('[N2] StringBuilder') }
    if ($joined -match '\+\s*(\$?"|\$@")') { $violations.Add('[N3] 拼接式字符串') }
    if ($joined -match 'string\.Concat\s*\(') { $violations.Add('[N4] string.Concat') }
    if ($joined -match '"[^"\r\n]*[\u4e00-\u9fff][^"\r\n]*"') { $violations.Add('[N5] 领域中文字面量') }
    return $violations
}

if ($NegativeControl) {
    $bad = @('var s = "<div class=""x"">" + value + "</div>";', 'var sb = new StringBuilder();', 'var t = "操作 / 任务";')
    $caught = (Test-NoStringConcat $bad).Count
    if ($caught -lt 3) { throw "负对照失败：仅捕获 $caught 项（期望 >=3）" }
    $good = @('var node = new UiNode("div", Attributes: attrs, Children: kids);', 'var text = string.Format(format, count);')
    if ((Test-NoStringConcat $good).Count -ne 0) { throw '负对照失败：合法节点构造被误报' }
    Write-Host '[OK] 负对照通过：内联 HTML / StringBuilder / 中文字面量均被捕获。' -ForegroundColor Green
    return
}

if (-not $Check) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

$sources = @(Get-SourceText $target)
foreach ($extra in $extraSources) {
    if (Test-Path -LiteralPath $extra) { $sources += Get-Content -LiteralPath $extra -Raw }
}
$violations = Test-NoStringConcat $sources
if ($violations.Count -gt 0) {
    Write-Host ("RenderNoStringConcat: {0} 项违规（桥内禁自行构造 HTML/文案）。" -f $violations.Count) -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host 'RenderNoStringConcat: 全部通过。' -ForegroundColor Green
exit 0
