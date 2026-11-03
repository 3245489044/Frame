<#
.SYNOPSIS
    声明单源门禁：编译入口唯一（UsharpCompilationHost），声明装载不自建 IO。

.DESCRIPTION
    判据载体 = 源码静态调用点计数（结构事实）：
      · UsharpCompilation.CompileSourcesRaw 生产调用点 = 0（唯一入口 = UsharpCompilationHost）；
      · 公共 Compile/CompileSources/CompileFiles 已是「闭包 + Host」门面（调用它们不算绕过）；
      · 声明消费方目录内 File.ReadAllText / Directory.EnumerateFiles 命中 = 0。

    测试工程（*.Tests）与内核自身不计入。
#>
[CmdletBinding()]
param(
    [string[]]$ScanRoots = @('Frame\Usharp\src', 'Projects\Plug\Agent\src'),
    [string[]]$DeclarationConsumers = @(
        'Projects\Plug\Agent\src\Agent.Launcher\Composition\Hosting\Declaration',
        'Projects\Plug\Agent\src\Agent.McpRegistryHost\Faces',
        'Projects\Plug\Agent\src\Agent.Core\Orchestration\SessionWorkflow'
    )
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
function Resolve-Repo([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return (Join-Path $root $Path)
}
function Get-IndexedFiles([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    Get-ChildItem -LiteralPath $Path -Recurse -File -Filter *.cs |
        Where-Object {
            $_.FullName -notmatch '\\(obj|bin)\\' -and
            $_.FullName -notmatch '\.Tests\\' -and
            $_.Name -notin @('UsharpCompilation.cs', 'UsharpCompilationHost.cs')
        }
}

$violations = 0
# 判据更新（P1b）：公共 Compile* 已是「闭包 + Host」门面；唯一可绕过门面的形态 = CompileSourcesRaw。
$compilePattern = 'UsharpCompilation\.CompileSourcesRaw\('

foreach ($scanRoot in $ScanRoots) {
    foreach ($file in (Get-IndexedFiles (Resolve-Repo $scanRoot))) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $count = [regex]::Matches($text, $compilePattern).Count
        if ($count -gt 0) {
            Write-Host ("[MULTI-ENTRY] {0}：UsharpCompilation.Compile* 调用 {1} 处（应收敛到 UsharpCompilationHost）" -f $file.FullName, $count) -ForegroundColor Red
            $violations += $count
        }
    }
}

foreach ($consumer in $DeclarationConsumers) {
    foreach ($file in (Get-IndexedFiles (Resolve-Repo $consumer))) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        foreach ($m in [regex]::Matches($text, '(File\.ReadAllText|Directory\.EnumerateFiles|GetFiles\()')) {
            Write-Host ("[OWN-IO] {0}：声明闭包内自建 IO（{1}）" -f $file.FullName, $m.Value) -ForegroundColor Red
            $violations++
        }
    }
}

if ($violations -gt 0) { exit 1 }
Write-Host '[OK] 声明单源：编译入口 1、消费方自建 IO 0' -ForegroundColor Green
exit 0
