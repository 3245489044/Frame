#requires -Version 5.1
<#
.SYNOPSIS
    门禁框架引导入口（独立进程一行调用：定位 GateRuntime + 导入 + 返回仓库根）。

.DESCRIPTION
    动机（GD §5.1 G1）：36+ 处门禁脚本各自保留「向上找 GateRuntime.psm1 并 import」的 while 循环，
    形成「找模块需先导入模块」的自举循环。本脚本是该循环的唯一框架侧副本：
      · 从 -ScriptRoot（缺省 = 本脚本目录）向上定位 <Repo>\Frame\PowerShellScript\Baselines\GateRuntime.psm1；
      · 以 -Scope Global 导入 GateRuntime（幂等；重复调用不重建，-Force 才重建）；
      · 返回仓库根（已经 DirectoryMap.Get-SuperRoot 规范化）。

    域侧替换口径（删除 while 循环与二次 import）：
        $repoRoot = & (Join-Path ([regex]::Match($PSScriptRoot, '^(.+?)\\(?:Frame|Projects|Environment)\\').Groups[1].Value) 'Frame\PowerShellScript\Baselines\Initialize-GateModule.ps1')

.PARAMETER ScriptRoot
    调用方脚本目录（缺省 = 本脚本目录）；用于从任意深度定位仓库根。
.PARAMETER Force
    强制重建 GateRuntime（会重置别名与退出码常量；一般不需要）。
.PARAMETER PassThru
    返回 `{ Root; Runtime; Module }` 而不是仅仓库根（编排/诊断用）。

.OUTPUTS
    默认 [string] 仓库根；-PassThru 时 [pscustomobject]。

.NOTES
    命名：域侧此前分别叫 Initialize-GateModule / Initialize-GateRuntime，现统一为 Initialize-GateModule。
    本脚本负责「跨进程自举」；GateRuntime.psm1 内的同名函数负责「已导入后的进程内规范化」。
#>
[CmdletBinding()]
param(
    [string]$ScriptRoot = '',
    [switch]$Force,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'

$start = $ScriptRoot
if ([string]::IsNullOrWhiteSpace($start)) { $start = $PSScriptRoot }

# 唯一自举循环（框架侧持有）：域侧不再复制。
$probe = $start
while ($probe -and -not (Test-Path -LiteralPath (Join-Path $probe 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) {
    $probe = Split-Path -Parent $probe
}
if ([string]::IsNullOrWhiteSpace($probe)) {
    throw "Initialize-GateModule 未定位 GateRuntime.psm1（起点：$start；期望 <Repo>\Frame\PowerShellScript\Baselines\GateRuntime.psm1）"
}

$runtime = Join-Path $probe 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'
Import-Module $runtime -Scope Global -DisableNameChecking -Force:$Force | Out-Null

# 规范化仓库根（DirectoryMap.Get-SuperRoot：含 Frame\ + Projects\ 的目录；与深度无关）。
$root = Get-GateRepoRoot -ScriptRoot $start
if ([string]::IsNullOrWhiteSpace([string]$root)) {
    throw "Initialize-GateModule 已导入 GateRuntime，但 Get-GateRepoRoot 未解析出仓库根（起点：$start）"
}
$root = ([string]$root).TrimEnd([char[]]@('\', '/'))

if ($PassThru) {
    return [pscustomobject]@{
        Root    = $root
        Runtime = $runtime
        Module  = Get-Module -Name 'GateRuntime' -ErrorAction SilentlyContinue
    }
}
return $root
