<#
.SYNOPSIS
    启动引导（Windows PowerShell 5.1 兼容）—— 解析 pwsh 后转发唯一入口，不依赖 PATH。

.DESCRIPTION
    为什么需要这一层：外部门户 bat 必须零逻辑（K1），而 `pwsh` 不一定在双击环境的 PATH 里
    （Store 版应用执行别名的目录常不在机器/用户 PATH）。bat 里裸写 `pwsh` ⇒ 双击即 `'pwsh' 不是内部或外部命令` ⇒ 闪退。
    本件把「找 pwsh」收敛为一个解析点：复用 Share\Env\Resolve-Pwsh.ps1（PATH → MSI → Store 别名 → Store 包目录），
    再由 pwsh 转发 Frame\Invoke\Invoke-Frame.ps1。产品/入口/参数一律原样透传，本件零业务判定。

    失败可见化：pwsh 缺失 ⇒ 打印候选来源 + 停窗（双击场景不闪退），退出码 2。
#>
[CmdletBinding()]
param(
    [string]$Product = 'filedriven',
    [string]$EntryId = '',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$RawArgs = @()
)

$ErrorActionPreference = 'Stop'

$envDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = $envDir
while ($repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $repoRoot 'Projects')))) {
    $parent = Split-Path -Parent $repoRoot
    if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $repoRoot) { $repoRoot = $null; break }
    $repoRoot = $parent
}

function Write-Fail([string]$Message) {
    Write-Host ''
    Write-Host $Message -ForegroundColor Red
    if (-not ($RawArgs -contains '--no-pause')) {
        Write-Host '按任意键关闭窗口…' -ForegroundColor DarkGray
        try { [void][Console]::ReadKey($true) } catch { }
    }
}

if ([string]::IsNullOrWhiteSpace($repoRoot)) { Write-Fail '[launch] 仓库根定位失败（Frame ∧ Projects 未同时存在）'; exit 2 }

$resolver = Join-Path $envDir 'Resolve-Pwsh.ps1'
if (-not (Test-Path -LiteralPath $resolver)) { Write-Fail ("[launch] pwsh 解析器缺失：{0}" -f $resolver); exit 2 }

$pwsh = $null
try { $pwsh = [string](& $resolver | Select-Object -First 1) } catch { $pwsh = $null }
if ([string]::IsNullOrWhiteSpace($pwsh) -or -not (Test-Path -LiteralPath $pwsh)) {
    Write-Fail ("[launch] 未找到 PowerShell 7+（已试：WF_PWSH / PATH / MSI / Store 别名 / Store 包目录）`n         解析器：{0}`n         处置：安装 PowerShell 7，或设置 WF_PWSH 指向 pwsh.exe" -f $resolver)
    exit 2
}

$invokeFrame = Join-Path $repoRoot 'Frame\Invoke\Invoke-Frame.ps1'
if (-not (Test-Path -LiteralPath $invokeFrame)) { Write-Fail ("[launch] 唯一入口缺失：{0}" -f $invokeFrame); exit 2 }

$forward = [System.Collections.Generic.List[string]]::new()
foreach ($a in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $invokeFrame, '-Action', 'launch', '-Product', $Product)) { $forward.Add($a) }
if (-not [string]::IsNullOrWhiteSpace($EntryId)) { $forward.Add('-EntryId'); $forward.Add($EntryId) }
foreach ($a in @($RawArgs)) { if (-not [string]::IsNullOrWhiteSpace([string]$a)) { $forward.Add([string]$a) } }

Write-Host ("[launch] pwsh: {0}" -f $pwsh) -ForegroundColor DarkGray
& $pwsh @forward
$rc = if ($null -eq $LASTEXITCODE) { 2 } else { [int]$LASTEXITCODE }
exit $rc
