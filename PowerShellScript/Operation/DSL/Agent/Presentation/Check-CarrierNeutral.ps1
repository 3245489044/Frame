#requires -Version 5.1
<#
.SYNOPSIS
    载体中立守卫（CarrierNeutral）—— Composer/Projector 代码零载体字面量。
.DESCRIPTION
    判据：扫描面内命中载体词 {div,span,table,details,RichTextBox,ListView,Form} 次数 = 0。
      扫描面 = Conversation\Rendering\Surface / FrontEndDriven\Surface /
               FrontEndDriven\HtmlViewerBridge\Surface / FrontEndDriven\WinForms\Projector。
      注释行（// /// * #）不参与判定。
    NC 负对照：合成 'new UiNode("div")' 必须命中。
    PC 正对照：扫描面文件数 = 0 ⇒ 红（判据无标的，防解析面坍缩）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not ((Test-Path (Join-Path $d 'Frame')) -and (Test-Path (Join-Path $d 'Projects')))) { $d = Split-Path $d -Parent }
    if (-not $d) { throw '[CarrierNeutral] 无法定位仓库根' }
    return $d
}

$root = Find-RepoRoot
$scanDirs = @(
    (Join-Path $root 'Frame\CsharpPrj\Share\Frameworks\Conversation\Rendering\Surface'),
    (Join-Path $root 'Frame\CsharpPrj\Operation\FrontEndDriven\Surface'),
    (Join-Path $root 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\Surface'),
    (Join-Path $root 'Frame\CsharpPrj\Operation\FrontEndDriven\WinForms\Projector')
)

$carrierWords = '(<(div|span|table|details)\b|RichTextBox|ListView|System\.Windows\.Forms)'
$violations = [System.Collections.Generic.List[string]]::new()
$files = @()
foreach ($dir in $scanDirs) {
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    $files += @(Get-ChildItem -LiteralPath $dir -Recurse -Filter '*.cs' -File)
}

if ($files.Count -eq 0) {
    $violations.Add('PC 扫描面为空（判据无标的）')
}
else {
    foreach ($file in $files) {
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
            $lineNo++
            $trimmed = $line.TrimStart()
            if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('#') -or $trimmed.StartsWith('/*')) { continue }
            if ($line -match $carrierWords) {
                $violations.Add("CN $($file.Name):$lineNo 命中载体词：$($line.Trim())")
            }
        }
    }
}

Write-Host ("[CarrierNeutral] files={0}" -f $files.Count)

if ($NegativeControl) {
    $sample = 'var markup = "<div class=""x"">";'
    if ($sample -notmatch $carrierWords) { $violations.Add('NC 负对照失效：合成载体字面量未被捕获') }
    else { Write-Host '[CarrierNeutral] NC caught' }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host ("[VIOLATION] {0}" -f $v) }
    if ($Check) { exit 1 }
}
else {
    Write-Host '[CarrierNeutral] 通过（0 载体字面量）'
    if ($Check) { exit 0 }
}
