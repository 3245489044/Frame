<#
.SYNOPSIS
    解除构建产物占用（结束宿主进程 + 关闭构建服务器 + 等待文件句柄释放）。

.DESCRIPTION
    场景：宿主（FileDriven.Launcher）长期运行，加载了 BuildOutput 下的 dll，
    `dotnet build` 在复制阶段报 MSB3027/MSB3021（文件被锁定）⇒ 无法更新生效面。

    判据（结构事实，非叙述）：
      P1 进程面：进程名命中，且（默认）可执行文件路径位于本仓根之下 —— 只杀本仓宿主，禁误杀。
      P2 释放面：代表件以「独占读写」打开成功（FileShare.None）⇒ 句柄已释放；失败 ⇒ 仍被持有。
      P3 构建服务器：`dotnet build-server shutdown`（MSBuild / VBCSCompiler 常持有产物副本）。

    退出码：0 = 无占用或已释放 | 1 = 等待超时仍被占用 | 2 = 前置失败（参数/路径）。
    -DryRun 仅报告（不杀进程、不关服务器），用于诊断。

.PARAMETER RepoRoot
    仓库根（默认 = 框架自动检测的 Main 根）。

.PARAMETER ProcessName
    宿主进程名（不含 .exe）；为空 = 不结束进程，仅关闭构建服务器并探测句柄。

.PARAMETER Configuration
    产物配置子目录（Debug/Release），用于缺省探针路径。

.PARAMETER ProbeFile
    独占探测的代表件（相对 RepoRoot 或绝对路径）；缺省 = 本仓常见锁冲突产物。

.PARAMETER GraceSeconds
    先请求主窗口关闭（CloseMainWindow）后的等待秒数；超时才强杀进程树。

.PARAMETER WaitSeconds
    强杀后等待文件句柄释放的最长秒数（500ms 轮询）。

.PARAMETER AnyPath
    结束任意路径下的同名进程（默认仅本仓；跨仓/多副本场景显式开启）。

.PARAMETER SkipBuildServers
    不关闭 dotnet 构建服务器（仅在确认锁不来自构建服务器时使用）。

.EXAMPLE
    pwsh -File Frame\PowerShellScript\Share\Build\Stop-LauncherLocks.ps1 -ProcessName FileDriven.Launcher
    pwsh -File Frame\PowerShellScript\Share\Build\Stop-LauncherLocks.ps1 -ProcessName FileDriven.Launcher -DryRun
    pwsh -File Frame\PowerShellScript\Share\Build\Stop-LauncherLocks.ps1 -ProcessName FileDriven.Launcher -AnyPath -WaitSeconds 30
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [string]$ProcessName = '',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string[]]$ProbeFile = @(),
    [int]$GraceSeconds = 3,
    [int]$WaitSeconds = 15,
    [switch]$AnyPath,
    [switch]$SkipBuildServers,
    [switch]$DryRun,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Text, [string]$Color = 'Gray')
    if (-not $Quiet) { Write-Host $Text -ForegroundColor $Color }
}

# ---------------- 前置：仓库根（框架统一解析） ----------------
$coreModule = Join-Path $PSScriptRoot '..\..\Modules\Core\ReadProjectConfig.ps1'
if (-not (Test-Path -LiteralPath $coreModule)) {
    Write-Step ("[ERROR] 缺少框架路径模块：{0}" -f $coreModule) 'Red'
    exit 2
}
. $coreModule

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Get-ProjectRoot
}
if (-not (Test-Path -LiteralPath $RepoRoot)) {
    Write-Step ("[ERROR] 仓库根不存在：{0}" -f $RepoRoot) 'Red'
    exit 2
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

# 代表件 = 锁冲突高发位置（独占打开探测；只读判据，零写入）
# 缺省为本仓 Main 拓扑的常见产物；其他使用者可传 -ProbeFile 覆盖。
if ($null -eq $ProbeFile -or $ProbeFile.Count -eq 0) {
    $ProbeFile = @(
        "Projects\FileDriven\BuildOutput\$Configuration\FileDriven.Launcher.exe"
        "Projects\Plug\Agent\BuildOutput\$Configuration\Agent.Launcher.dll"
        "Projects\Plug\Agent\BuildOutput\$Configuration\Agent.Ui.dll"
        "Frame\CsharpPrj\BuildOutput\$Configuration\Plate.HtmlViewer.dll"
        "Frame\CsharpPrj\BuildOutput\$Configuration\FrontEndDriven.dll"
    )
}

# ---------------- P1：候选进程（默认仅本仓） ----------------
$candidates = @()
foreach ($proc in @(if (-not [string]::IsNullOrWhiteSpace($ProcessName)) { Get-Process -Name $ProcessName -ErrorAction SilentlyContinue })) {
    $path = $null
    try { $path = $proc.Path } catch { $path = $null }
    $inRepo = [bool]($path -and $path.StartsWith($RepoRoot, [StringComparison]::OrdinalIgnoreCase))
    if ($AnyPath -or $inRepo) {
        $candidates += [pscustomobject]@{ Process = $proc; Path = $path }
    } else {
        Write-Step ("[WARN] 跳过仓外同名进程 PID={0} path={1}（确需结束：-AnyPath）" -f $proc.Id, $path) 'Yellow'
    }
}

Write-Step ("[INFO] 仓库根：{0}" -f $RepoRoot)
Write-Step ("[INFO] 目标进程：{0}（命中 {1} 个，范围={2}）" -f `
    $ProcessName, $candidates.Count, $(if ($AnyPath) { '任意路径' } else { '本仓' }))

foreach ($c in $candidates) {
    $proc = $c.Process
    $alive = $true
    try { $alive = -not $proc.HasExited } catch { $alive = $false }
    Write-Step ("[INFO] 结束 PID={0} path={1} started={2}" -f $proc.Id, $c.Path, $proc.StartTime) 'Cyan'
    if ($DryRun -or -not $alive) { continue }

    # 先优雅关闭（保存 UI 状态/释放句柄），超时才树杀
    $graceful = $false
    try { $graceful = $proc.CloseMainWindow() } catch { $graceful = $false }
    if ($graceful) {
        $deadline = (Get-Date).AddSeconds([Math]::Max($GraceSeconds, 0))
        while ((Get-Date) -lt $deadline) {
            try { if ($proc.HasExited) { break } } catch { break }
            Start-Sleep -Milliseconds 200
        }
    }

    $stillAlive = $true
    try { $stillAlive = -not $proc.HasExited } catch { $stillAlive = $false }
    if ($stillAlive) {
        # 进程树强杀：宿主可能派生 dotnet/子进程并继承 dll 句柄
        & taskkill.exe /PID $proc.Id /T /F 2>$null | Out-Null
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            try { if ($proc.HasExited) { break } } catch { break }
            Start-Sleep -Milliseconds 200
        }
        Write-Step ("[INFO] 已强杀进程树 PID={0}" -f $proc.Id) 'Yellow'
    } else {
        Write-Step ("[INFO] 已优雅关闭 PID={0}" -f $proc.Id) 'Green'
    }
}

# ---------------- P3：构建服务器（持有产物副本） ----------------
if (-not $SkipBuildServers -and -not $DryRun) {
    try {
        & dotnet build-server shutdown 2>$null | Out-Null
        Write-Step '[INFO] 已关闭 dotnet 构建服务器（MSBuild / VBCSCompiler）' 'Green'
    } catch {
        Write-Step ("[WARN] 关闭构建服务器失败：{0}" -f $_.Exception.Message) 'Yellow'
    }
}

# ---------------- P2：句柄释放探测（500ms 轮询） ----------------
$deadline = (Get-Date).AddSeconds([Math]::Max($WaitSeconds, 1))
$locked = @()
do {
    $locked = @()
    foreach ($rel in $ProbeFile) {
        $full = if ([IO.Path]::IsPathRooted($rel)) { $rel } else { Join-Path $RepoRoot $rel }
        if (-not (Test-Path -LiteralPath $full)) { continue }
        try {
            $fs = [IO.File]::Open($full, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $fs.Close()
        } catch {
            $locked += $rel
        }
    }
    if ($locked.Count -eq 0) { break }
    if ($DryRun) { break }
    Start-Sleep -Milliseconds 500
} while ((Get-Date) -lt $deadline)

if ($locked.Count -eq 0) {
    Write-Step '[OK] 产物句柄已释放（可安全编译）' 'Green'
    exit 0
}

Write-Step ("[WARN] 以下产物仍被占用（{0} 个）：" -f $locked.Count) 'Yellow'
foreach ($l in $locked) { Write-Step ("        {0}" -f $l) 'Yellow' }
if ($DryRun) {
    Write-Step '[INFO] DryRun：未执行结束动作（去掉 -DryRun 才会真正解占用）' 'Cyan'
    exit 0
}

Write-Step '[ERROR] 等待超时仍被占用：可能被 IDE / 其它宿主 / 反病毒扫描持有；关闭对应程序或用 handle.exe 定位。' 'Red'
Write-Step '        诊断：pwsh -File Frame\PowerShellScript\Share\Build\Stop-LauncherLocks.ps1 -ProcessName FileDriven.Launcher -DryRun' 'Cyan'
exit 1
