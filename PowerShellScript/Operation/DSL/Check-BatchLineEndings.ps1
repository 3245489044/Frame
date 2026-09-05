<#
.SYNOPSIS
    校验并(可选)修复 .bat/.cmd 的 CRLF 行尾。

.DESCRIPTION
    Windows cmd.exe 对仅 LF 行尾的 .bat/.cmd 解析不稳定：含中文注释或含括号块(for /f、if (...))
    时会把后续注释行吞掉并当命令执行 → "'…' is not recognized" / "The syntax of the command is
    incorrect."（多次踩坑）。本脚本扫描指定根下(默认向上回溯定位 git 仓根)的 .bat/.cmd，
    报告"纯 LF 行尾"文件；加 -Fix 则以字节级方式插入缺失的 CR(保留原编码/BOM)拉回 CRLF。

    配合仓根 .gitattributes(`*.bat text eol=crlf`)使用：属性负责"检出恒 CRLF + 提交规范化"，
    本脚本负责兜底检测工作树漂移(编辑器以 LF 保存但尚未按属性重检出的文件)。

.PARAMETER Root
    扫描根目录。缺省自动向上回溯到最近的 git 仓根(.git 所在)。

.PARAMETER Fix
    存在则把发现的纯 LF .bat/.cmd 就地转 CRLF(字节级插入 CR)，并输出修复清单。

.EXAMPLE
    .\Check-BatchLineEndings.ps1                       # 仅报告，有违规返回 1
    .\Check-BatchLineEndings.ps1 -Fix                  # 检测并修复
    .\Check-BatchLineEndings.ps1 -Root D:\Project\Main # 指定仓
#>
[CmdletBinding()]
param(
    [string]$Root,
    [switch]$Fix
)

$ErrorActionPreference = 'Stop'

function Get-RepoRoot([string]$start) {
    $dir = [System.IO.DirectoryInfo]::new($start)
    while ($dir -ne $null) {
        if (Test-Path (Join-Path $dir.FullName '.git')) { return $dir.FullName }
        $dir = $dir.Parent
    }
    return $start
}

$scanRoot = if ($Root) { $Root } else { Get-RepoRoot (Get-Location) }
Write-Host "扫描根: $scanRoot"

$files = Get-ChildItem -Path $scanRoot -Recurse -File -Include '*.bat', '*.cmd' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|node_modules|\.git|BuildOutput)\\?' }

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($f in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    if ($bytes.Length -lt 1) { continue }
    $hasLf = $false; $prevWasCr = $false
    foreach ($b in $bytes) {
        if ($b -eq 10) { $hasLf = $true; if (-not $prevWasCr) { $violations.Add($f.FullName); break } }
        $prevWasCr = ($b -eq 13)
    }
    if (-not $hasLf -and $violations[$violations.Count - 1] -ne $f.FullName) {
        # 无 LF 亦视为异常(非文本/未知)，仅提示不误报；此处略
    }
}

if ($violations.Count -eq 0) {
    Write-Host "OK: 无纯 LF 行尾的 .bat/.cmd。"
    exit 0
}

Write-Host ("发现 {0} 个纯 LF 行尾 .bat/.cmd:" -f $violations.Count)
$violations | ForEach-Object { Write-Host ("  {0}" -f $_) }

if (-not $Fix) {
    Write-Host '建议: 加 -Fix 修复(字节级插入 CR，保留原编码/BOM)，或确认仓根已含 *.bat text eol=crlf。'
    exit 1
}

foreach ($path in $violations) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $out = [System.Collections.Generic.List[byte]]::new($bytes.Length + 16)
    $prevWasCr = $false
    foreach ($b in $bytes) {
        if ($b -eq 10 -and -not $prevWasCr) { $out.Add(13) }
        $out.Add($b)
        $prevWasCr = ($b -eq 13)
    }
    if ($out.Count -ne $bytes.Length) {
        [System.IO.File]::WriteAllBytes($path, $out.ToArray())
        Write-Host ("已修复: {0}" -f $path)
    }
}
Write-Host '完成。'
exit 0
