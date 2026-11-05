<#
.SYNOPSIS
    框架快速编译脚本 — 烘焙 Frame + 编译 FileDriven，使 UI启动.bat 可直接启动

.DESCRIPTION
    按依赖顺序执行增量编译，产出 UI启动.bat 所需的 FileDriven.Launcher.exe 及其运行期全部依赖：

      Frame 烘焙   1. Frame\CsharpPrj\MainProj.sln                    全仓唯一权威产物源
      平台库       2. Frame\DataCenter\DataCenter.slnx                 DataCenter.*（Agent 上游 + 启动器探针兜底）
                   3. Frame\Usharp\Usharp.sln                          Usharp.Syntax/Semantics/Emit/Matrix（Agent 上游）
                   4. Frame\CallCenter\CallCenter.slnx                 CallCenter.*（Agent.McpRegistryHost 上游）
      扩展产品     5. Projects\Plug\Engine\GameEditor.sln              Engine 扩展
                   6. Projects\Plug\Engine\GameEditor.Host.csproj      GameEditor.Host（EngineLibrary + 扩展清单）
                   7. Projects\Plug\Produce\Produce.slnx               Produce 扩展（ProduceUiLibrary + 扩展清单）
                   8. Projects\Plug\Agent\Agent.sln                    Agent 扩展（Agent.* + 扩展清单）
      宿主         9. Projects\FileDriven\FileDriven.sln              FileDriven.Launcher.exe（UI启动.bat 的目标）

    设计要点：
      - 路径由框架统一解析（ReadProjectConfig.Get-ProjectRoot），不依赖脚本位置，零硬编码。
      - 全部为增量编译：内容未变时 MSBuild 自行判定 up-to-date 跳过；脚本不做任何清理。
      - 顺序不可颠倒：FileDriven/Agent 经二进制引用消费 Frame 权威烘焙 dll（不 ProjectReference Frame 源码），
        Agent 又消费 DataCenter/Usharp/CallCenter，故上游必须先出产物。
      - 扩展产品缺一不可：FileDriven 的 FrameworkInitializer 对函数库注册失败 fail-fast，
        Agent/Engine/Produce 的 deps.json 缺失会导致对应扩展函数库加载失败并阻断启动。

.PARAMETER Configuration
    构建配置，默认 Debug（FileDriven 仅产出 Debug；选 Release 只会烘焙 Frame 侧）

.PARAMETER SkipFramework
    跳过 Frame 权威烘焙，沿用既有 Frame\CsharpPrj\BuildOutput

.PARAMETER SkipExtensions
    跳过平台库与扩展产品（步骤 2-8），只编译 Frame 烘焙 + FileDriven 宿主

.PARAMETER SkipRestore
    追加 --no-restore，跳过 NuGet 还原（仅在包已还原、无新增依赖时使用）

.PARAMETER StopRunning
    构建前先结束正在运行的 FileDriven.Launcher（避免运行目录 dll 被占用导致构建失败）

.PARAMETER Launch
    编译成功后直接启动 UI启动.bat（路径可由 -LauncherBat 覆盖）

.PARAMETER LauncherBat
    -Launch 的目标批处理；默认 = <RepoRoot>\EnableMent\UI启动.bat

.PARAMETER LogPath
    日志文件；默认 = %TEMP%\QuickBuild.log

.PARAMETER DryRun
    只打印将要执行的命令，不做编译与校验

.PARAMETER Help
    显示帮助

.EXAMPLE
    .\Invoke-QuickBuild.ps1
    .\Invoke-QuickBuild.ps1 -SkipExtensions
    .\Invoke-QuickBuild.ps1 -StopRunning -Launch

.NOTES
    退出码: 0=成功 | 1=编译失败或未产出启动器 | 2=前置条件不满足（dotnet 缺失 / 解决方案文件缺失 / 启动器在运行）
    执行日志: %TEMP%\QuickBuild.log
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$SkipFramework,
    [switch]$SkipExtensions,
    [switch]$SkipRestore,
    [switch]$StopRunning,
    [switch]$Launch,
    [switch]$DryRun,

    # 编译成功后 -Launch 的目标批处理（默认 = <RepoRoot>\EnableMent\UI启动.bat）
    [string]$LauncherBat = '',

    # 日志文件（默认 = %TEMP%\QuickBuild.log）
    [string]$LogPath = '',

    [switch]$Help
)

$ErrorActionPreference = 'Continue'

# ============================================================
#  路径（框架统一解析：ReadProjectConfig.Get-ProjectRoot）
# ============================================================

$CoreModule = Join-Path $PSScriptRoot '..\..\Modules\Core\ReadProjectConfig.ps1'
if (-not (Test-Path -LiteralPath $CoreModule)) {
    Write-Host "[ERROR] 缺少框架路径模块: $CoreModule" -ForegroundColor Red
    exit 2
}
. $CoreModule

$RepoRoot          = Get-ProjectRoot
$EnableMentDir     = Join-Path $RepoRoot 'EnableMent'
# 编译入口（顺序即依赖序；均为各产品的权威解决方案/工程）
$CsharpSln         = Join-Path $RepoRoot 'Frame\CsharpPrj\MainProj.sln'
$DataCenterSln     = Join-Path $RepoRoot 'Frame\DataCenter\DataCenter.slnx'
$UsharpSln         = Join-Path $RepoRoot 'Frame\Usharp\Usharp.sln'
$CallCenterSln     = Join-Path $RepoRoot 'Frame\CallCenter\CallCenter.slnx'
$EngineSln         = Join-Path $RepoRoot 'Projects\Plug\Engine\GameEditor.sln'
$EngineHostCsproj  = Join-Path $RepoRoot 'Projects\Plug\Engine\GameEditor.Host\GameEditor.Host.csproj'
$ProduceSln        = Join-Path $RepoRoot 'Projects\Plug\Produce\Produce.slnx'
$AgentSln          = Join-Path $RepoRoot 'Projects\Plug\Agent\Agent.sln'
$FileDrivenSln     = Join-Path $RepoRoot 'Projects\FileDriven\FileDriven.sln'

$FileDrivenRoot    = Join-Path $RepoRoot 'Projects\FileDriven'
$FileDrivenOutRoot = Join-Path $FileDrivenRoot "BuildOutput\$Configuration"
$RepoOutRoot       = Join-Path $RepoRoot "BuildOutput\$Configuration"
$CsharpOutDir      = Join-Path $RepoRoot "Frame\CsharpPrj\BuildOutput\$Configuration"
if ([string]::IsNullOrWhiteSpace($LauncherBat)) {
    $LauncherBat = Join-Path $EnableMentDir 'UI启动.bat'
}

# 扩展层装配清单（FileDriven.Launcher\Bootstrap\LauncherBootstrap.cs 的运行期探测点，缺一即对应扩展函数库注册失败）
# 扩展清单来自装配闭包声明（单一真源 = assembly-closure.json）；禁在此内联产品清单
# （门禁 BuildClosure K2 会把内联清单判红）。
$closureModule = Join-Path $RepoRoot 'Frame\PowerShellScript\Share\AssemblyClosure\AssemblyClosure.psm1'
if (-not (Test-Path -LiteralPath $closureModule)) { throw "装配闭包实现缺失：$closureModule" }
Import-Module $closureModule -Force -DisableNameChecking
$ExtensionManifests = @(Get-ClosureStates -RepoRoot $RepoRoot -Configuration $Configuration |
    Where-Object { $_.Required -and -not [string]::IsNullOrWhiteSpace($_.Manifest) } |
    ForEach-Object { @{ Label = $_.Id; Path = $_.Manifest } })

if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path $env:TEMP 'QuickBuild.log'
}
$script:LogPath   = $LogPath
$script:StartTime = Get-Date

# ============================================================
#  输出辅助（控制台 + 日志文件双写）
# ============================================================

function Write-Log {
    param(
        [string]$Text = '',
        [string]$Color = 'Gray'
    )
    Write-Host $Text -ForegroundColor $Color
    try {
        Add-Content -Path $script:LogPath -Value $Text -Encoding UTF8 -ErrorAction SilentlyContinue
    } catch {
        # 日志写入失败不阻断编译
    }
}

function Write-Step { param([string]$Text) Write-Log '' ; Write-Log "▶ $Text" 'Cyan' }
function Write-Info { param([string]$Text) Write-Log "  $Text" 'Gray' }
function Write-Ok   { param([string]$Text) Write-Log "  [OK] $Text" 'Green' }
function Write-Note { param([string]$Text) Write-Log "  [WARN] $Text" 'Yellow' }
function Write-Fail { param([string]$Text) Write-Log "  [ERROR] $Text" 'Red' }

# ============================================================
#  编译单步（dotnet build 增量）
# ============================================================

function Invoke-BuildStep {
    param(
        [string]$Name,
        [string]$ProjectPath,
        [string[]]$ExtraArgs = @()
    )

    Write-Step $Name
    Write-Info "目标: $ProjectPath"

    if (-not (Test-Path $ProjectPath)) {
        Write-Fail "编译目标不存在: $ProjectPath"
        return $false
    }

    $cmdArgs = @('build', $ProjectPath, '-c', $Configuration, '--nologo', '-v', 'minimal')
    if ($SkipRestore) { $cmdArgs += '--no-restore' }
    if ($ExtraArgs.Count -gt 0) { $cmdArgs += $ExtraArgs }

    Write-Info ("命令: dotnet " + ($cmdArgs -join ' '))

    if ($DryRun) {
        Write-Note 'DryRun：跳过实际编译'
        return $true
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $lines = & dotnet @cmdArgs 2>&1 | ForEach-Object { $_.ToString() }
    $exitCode = $LASTEXITCODE
    $sw.Stop()
    $seconds = [int][Math]::Round($sw.Elapsed.TotalSeconds)

    if ($exitCode -eq 0) {
        $tail = $lines | Where-Object { $_.Trim() -ne '' } | Select-Object -Last 6
        foreach ($line in $tail) { Write-Log ("    " + $line.Trim()) 'DarkGray' }
        Write-Ok "$Name 完成（$seconds 秒）"
        return $true
    }

    $errors = $lines | Where-Object { $_ -match 'error|错误|失败' }
    if (-not $errors) { $errors = $lines | Select-Object -Last 20 }
    Write-Log ''
    foreach ($line in ($errors | Select-Object -Last 40)) { Write-Log ("    " + $line.Trim()) 'Red' }
    Write-Fail "$Name 失败（退出码 $exitCode，$seconds 秒）"
    return $false
}

# ============================================================
#  产物查找（与 UI启动.bat → ReadProjectConfig.Get-LauncherOutputDir 同口径：
#  BuildOutput 下递归搜索，浅层优先）
# ============================================================

function Find-LauncherExe {
    param([string]$Root)

    if (-not (Test-Path $Root)) { return $null }

    $hit = Get-ChildItem -Path $Root -Recurse -Filter 'FileDriven.Launcher.exe' -File -ErrorAction SilentlyContinue |
        Sort-Object @{ Expression = { ($_.FullName -split '\\').Count } }, @{ Expression = { $_.LastWriteTime }; Descending = $true } |
        Select-Object -First 1

    if ($hit) { return $hit.FullName }
    return $null
}

# ============================================================
#  帮助
# ============================================================

if ($Help) {
    Write-Host @"
Invoke-QuickBuild.ps1 — 框架快速编译脚本

用法:
    Invoke-QuickBuild.ps1                    增量编译全链（Frame 烘焙 + 平台库 + 扩展产品 + FileDriven）
    Invoke-QuickBuild.ps1 -SkipExtensions    只编译 Frame 烘焙 + FileDriven 宿主（扩展产物已最新时）
    Invoke-QuickBuild.ps1 -SkipFramework     沿用既有 Frame 权威烘焙产物
    Invoke-QuickBuild.ps1 -SkipRestore       跳过 NuGet 还原
    Invoke-QuickBuild.ps1 -StopRunning       构建前结束正在运行的 Launcher（避免 dll 占用）
    Invoke-QuickBuild.ps1 -Launch            编译成功后直接启动 UI启动.bat
    Invoke-QuickBuild.ps1 -LauncherBat <path> 指定 -Launch 的目标批处理（默认 <RepoRoot>\EnableMent\UI启动.bat）
    Invoke-QuickBuild.ps1 -LogPath <path>    指定日志文件（默认 %TEMP%\QuickBuild.log）
    Invoke-QuickBuild.ps1 -DryRun            只打印命令，不编译
    Invoke-QuickBuild.ps1 -Help              显示本帮助

流程（顺序即依赖序）:
    1. Frame\CsharpPrj\MainProj.sln                     Frame 权威烘焙
    2. Frame\DataCenter\DataCenter.slnx                 平台库
    3. Frame\Usharp\Usharp.sln                          平台库
    4. Frame\CallCenter\CallCenter.slnx                 平台库
    5. Projects\Plug\Engine\GameEditor.sln              Engine 扩展
    6. Projects\Plug\Engine\GameEditor.Host.csproj      Engine Host（扩展清单）
    7. Projects\Plug\Produce\Produce.slnx               Produce 扩展
    8. Projects\Plug\Agent\Agent.sln                    Agent 扩展
    9. Projects\FileDriven\FileDriven.sln               FileDriven 宿主（UI启动.bat 的目标）
    校验: 启动器 + Agent/Engine/Produce 扩展清单 + Frame 烘焙 dll

退出码: 0=成功 | 1=编译失败或未产出启动器 | 2=前置条件不满足
"@ -ForegroundColor Gray
    exit 0
}

# ============================================================
#  前置条件
# ============================================================

Write-Log '' 
Write-Log '============================================' 'Cyan'
Write-Log '  Framework QuickBuild' 'Cyan'
Write-Log '============================================' 'Cyan'
Write-Log ''
Write-Info "仓库根   : $RepoRoot"
Write-Info "配置     : $Configuration"
Write-Info "日志     : $script:LogPath"
try { Set-Content -Path $script:LogPath -Value ("[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] QuickBuild 开始 (配置=$Configuration)") -Encoding UTF8 -ErrorAction SilentlyContinue } catch { }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Fail '未找到 dotnet CLI，请先安装 .NET SDK'
    exit 2
}
Write-Info ("dotnet: " + ((dotnet --version 2>&1) -join ''))

if (-not (Test-Path $CsharpSln)) {
    Write-Fail "解决方案不存在: $CsharpSln"
    exit 2
}
if (-not (Test-Path $FileDrivenSln)) {
    Write-Fail "解决方案不存在: $FileDrivenSln"
    exit 2
}
if ($Configuration -ne 'Debug') {
    Write-Note "FileDriven 仅产出 Debug 配置（Directory.Build.props 已禁用 Release），本次仅烘焙 Frame 侧"
}

# 运行中的 Launcher 会占用输出目录 dll，导致构建后期复制失败
$running = Get-Process -Name 'FileDriven.Launcher' -ErrorAction SilentlyContinue
if ($running) {
    if ($StopRunning) {
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        Write-Note '已结束正在运行的 FileDriven.Launcher'
    } else {
        Write-Fail '检测到 FileDriven.Launcher 正在运行，输出目录 dll 被占用'
        Write-Info '请先关闭窗口后重试，或使用 -StopRunning 自动结束'
        exit 2
    }
}

# ============================================================
#  编译步骤（顺序即依赖序，不可颠倒）
#    framework  : Frame\CsharpPrj\MainProj.sln —— 全仓唯一权威产物源，其余一切的上游
#    extensions : 平台库（DataCenter/Usharp/CallCenter）+ 扩展产品（Engine/Produce/Agent）
#                 Agent 经二进制引用消费 DataCenter/Usharp/CallCenter，故三者必须先行
#    host       : FileDriven 宿主（消费 Frame 烘焙 dll，产出 UI启动.bat 所需的启动器）
# ============================================================

$buildSteps = @(
    @{ Group = 'framework';  Name = 'Frame 权威烘焙（MainProj.sln）';              Path = $CsharpSln }
    @{ Group = 'extensions'; Name = 'Frame 平台库（DataCenter.slnx）';             Path = $DataCenterSln }
    @{ Group = 'extensions'; Name = 'Usharp 语言框架（Usharp.sln）';               Path = $UsharpSln }
    @{ Group = 'extensions'; Name = 'CallCenter 平台库（CallCenter.slnx）';        Path = $CallCenterSln }
    @{ Group = 'extensions'; Name = 'Engine 扩展（GameEditor.sln）';               Path = $EngineSln }
    @{ Group = 'extensions'; Name = 'Engine Host（GameEditor.Host.csproj）';       Path = $EngineHostCsproj }
    @{ Group = 'extensions'; Name = 'Produce 扩展（Produce.slnx）';                Path = $ProduceSln }
    @{ Group = 'extensions'; Name = 'Agent 扩展（Agent.sln）';                     Path = $AgentSln }
    @{ Group = 'host';       Name = 'FileDriven 宿主（FileDriven.sln）';           Path = $FileDrivenSln }
)

if ($SkipFramework) {
    Write-Step '跳过 Frame 权威烘焙（-SkipFramework）'
    Write-Info "沿用现有产物: $CsharpOutDir"
    if (-not (Test-Path (Join-Path $CsharpOutDir 'BasicMethod.dll'))) {
        Write-Note "该目录缺少 BasicMethod.dll，后续步骤很可能因缺 Frame 烘焙 dll 而失败"
    }
}
if ($SkipExtensions) {
    Write-Step '跳过平台库与扩展产品（-SkipExtensions）'
    Write-Info '适用场景：仅改动 Frame/FileDriven 且扩展产物已最新'
}

$activeSteps = @($buildSteps | Where-Object {
        ($_.Group -ne 'framework' -or -not $SkipFramework) -and
        ($_.Group -ne 'extensions' -or -not $SkipExtensions)
    })
$total = $activeSteps.Count
$index = 0

foreach ($step in $activeSteps) {
    $index++
    $label = "步骤 $index/$total：$($step.Name)"
    if (-not (Invoke-BuildStep -Name $label -ProjectPath $step.Path)) {
        Write-Log ''
        Write-Fail "编译中止：$($step.Name) 失败"
        exit 1
    }
}

# ============================================================
#  校验产物（启动器 + 扩展清单 + Frame 烘焙）
# ============================================================

Write-Step '校验产物'

$elapsedSec = [int][Math]::Round(((Get-Date) - $script:StartTime).TotalSeconds)

if ($DryRun) {
    Write-Note 'DryRun：跳过产物校验'
    Write-Log ''
    Write-Log "耗时 $elapsedSec 秒" 'Cyan'
    exit 0
}

$missing = 0

$launcherExe = Find-LauncherExe -Root $FileDrivenOutRoot
if (-not $launcherExe) { $launcherExe = Find-LauncherExe -Root $RepoOutRoot }

if (-not $launcherExe) {
    Write-Log ''
    Write-Fail "未找到 FileDriven.Launcher.exe（已搜索: $FileDrivenOutRoot）"
    Write-Info 'UI启动.bat 将回退到自动构建，启动会明显变慢，请检查上方编译错误'
    exit 1
}

$launcherDir = Split-Path $launcherExe -Parent
$launcherDll = Join-Path $launcherDir 'FileDriven.Launcher.dll'
$exeTime = (Get-Item $launcherExe).LastWriteTime

Write-Ok "启动器: $launcherExe"
Write-Info "  构建时间: $exeTime"
if (-not (Test-Path $launcherDll)) {
    Write-Note '同目录缺少 FileDriven.Launcher.dll，产物可能不完整'
}

# 扩展层装配清单：缺失则对应扩展函数库注册失败，FrameworkInitializer 会 fail-fast 阻断启动
foreach ($m in $ExtensionManifests) {
    if (Test-Path $m.Path) {
        Write-Info "  扩展清单 $($m.Label): 就绪"
    } else {
        Write-Note "  扩展清单 $($m.Label) 缺失（$($m.Path)）→ 启动时该扩展函数库将注册失败"
        $missing++
    }
}

# Frame 烘焙 dll 是否就位（运行期由 LauncherBootstrap 从该目录就地解析）
$probe = Join-Path $CsharpOutDir 'BasicMethod.dll'
if (Test-Path $probe) {
    Write-Info "  Frame 烘焙: $CsharpOutDir（$( (Get-ChildItem $CsharpOutDir -Filter *.dll -File -ErrorAction SilentlyContinue).Count ) 个 dll）"
} else {
    Write-Note "  Frame 烘焙目录缺少 BasicMethod.dll: $CsharpOutDir（运行期可能解析失败）"
    $missing++
}

# ============================================================
#  完成
# ============================================================

Write-Log ''
Write-Log '============================================' 'Green'
Write-Log "  编译完成（耗时 $elapsedSec 秒）" 'Green'
Write-Log '============================================' 'Green'
Write-Log ''
Write-Info "启动器目录: $launcherDir"
Write-Info "可直接启动: $LauncherBat"
if ($missing -gt 0) {
    Write-Note "有 $missing 项产物缺失（见上方 [WARN]），启动可能失败或被阻断"
}
Write-Log ''

if ($Launch) {
    if (Test-Path $LauncherBat) {
        Write-Info "正在启动 UI启动.bat ..."
        # 经 cmd.exe 转发启动：本机应用控制策略会拦截直接 Start-Process 批处理文件（危险扩展名启发式）
        Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "`"$LauncherBat`"" -WorkingDirectory $RepoRoot
    } else {
        Write-Note "未找到 UI启动.bat: $LauncherBat"
    }
}

exit 0
