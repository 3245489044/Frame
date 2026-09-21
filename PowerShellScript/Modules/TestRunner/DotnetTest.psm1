<#
.SYNOPSIS
    dotnet test 执行面（通用）：runsettings 发现 + 单运行单元执行（MsTest / ConsoleCheck）。

.DESCRIPTION
    原实现位于 `Environment\CentralTest\TestRunner\TestRunner.psm1`，本模块为抽取目标（N1/M1，函数体原样搬移）。
    工程发现与目标解析（含 spec 套件语义）不在本模块 —— 留 CentralTest 侧。

    依赖方向（硬约束）：CentralTest → Frame（单向）。
#>

Set-StrictMode -Version Latest

# 同族依赖（R-3 禁静默降级）：调用期需 DotnetTestRunner.psm1 的算子（参数构造 / 计数解析 / 失败名提取）。
# 文件名带 Dotnet 前缀是必需的：CentralTest 侧同名模块 `TestRunner.psm1` 在 M2 切换期共存，
# 同名模块 -Force 重导入会互相顶掉导出面（实测遮蔽）。
$script:RunnerSibling = Join-Path $PSScriptRoot 'DotnetTestRunner.psm1'
if (-not (Test-Path -LiteralPath $script:RunnerSibling)) {
    throw "[Modules\TestRunner\DotnetTest] 同族模块缺失（应含 DotnetTestRunner.psm1）：$script:RunnerSibling"
}
# 单独 Import 本件时按需装配同族：已可用即跳过（装配器 _init.ps1 已按序装配）。
# -Global 是必需的：模块初始化期以 -Force 重复导入会把同族落成**嵌套模块**，
# 其导出面在会话中不可见（实测：运行器算子全部丢失）。
if (-not (Get-Command 'New-DotnetTestArgs' -ErrorAction SilentlyContinue)) {
    Import-Module $script:RunnerSibling -Force -DisableNameChecking -Global
}

function Find-RunSettings {
    <#
    .SYNOPSIS
        就近查找 runsettings：工程内 test.runsettings 优先；UI 类工程回落中央 TestRunner\test.runsettings。

    .DESCRIPTION
        找不到时返回空串，调用方不得传 --settings（消除"指向不存在文件"的失效路径）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [string]$Root = '',
        [string[]]$CentralFor = @('WdUI', '\.Ui\.', 'Ui\.Tests')
    )

    $local = Join-Path (Split-Path -Parent $ProjectPath) 'test.runsettings'
    if (Test-Path $local) { return $local }
    if ($Root) {
        $central = Join-Path $Root 'TestRunner\test.runsettings'
        if (Test-Path $central) {
            foreach ($pattern in $CentralFor) {
                if ($ProjectPath -match $pattern) { return $central }
            }
        }
    }
    return ''
}

function Invoke-TestUnit {
    <#
    .SYNOPSIS
        执行单个运行单元（MsTest -> dotnet test；ConsoleCheck -> 控制台自检 exe）。

    .OUTPUTS
        pscustomobject：{ Kind, ProjectName, ExitCode, Passed, Failed, Skipped, Total, Parsed,
                          Output, DurationMs, RunSettings, Command }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Unit,
        [string]$Root = '',
        [string]$Configuration = 'Debug',
        [switch]$NoBuild,
        [string]$ResultsDir = '',
        [string[]]$Logger = @('console;verbosity=minimal')
    )

    if ($Unit.Kind -eq 'ConsoleCheck') {
        $exe = Get-ChildItem -Path (Join-Path $Root 'BuildOutput') -Recurse -Filter "$($Unit.ProjectName).exe" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if (-not $exe) {
            return [pscustomobject]@{
                Kind = 'ConsoleCheck'; ProjectName = $Unit.ProjectName; ExitCode = -1
                Passed = 0; Failed = 0; Skipped = 0; Total = 0; Parsed = $false; NoTestsMatched = $false
                FailedNames = @()
                Output = "[WARN] $($Unit.ProjectName).exe 未找到（请先构建）"
                DurationMs = 0; RunSettings = ''; Command = "$($Unit.ProjectName).exe"
            }
        }
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $consoleOutput = (& $exe.FullName 2>&1 | Out-String)
        $sw.Stop()
        $allPass = ($consoleOutput -match 'ALL PASS')
        return [pscustomobject]@{
            Kind = 'ConsoleCheck'; ProjectName = $Unit.ProjectName
            ExitCode = $(if ($allPass) { 0 } else { 1 })
            Passed = $(if ($allPass) { 1 } else { 0 }); Failed = $(if ($allPass) { 0 } else { 1 })
            Skipped = 0; Total = 1; Parsed = $true; NoTestsMatched = $false
            FailedNames = @()
            Output = $consoleOutput; DurationMs = [int]$sw.ElapsedMilliseconds
            RunSettings = ''; Command = $exe.FullName
        }
    }

    $runSettings = Find-RunSettings -ProjectPath $Unit.ProjectPath -Root $Root
    $testArgs = New-DotnetTestArgs -ProjectPath $Unit.ProjectPath -Filters @($Unit.Filters) `
        -Configuration $Configuration -NoBuild:$NoBuild -RunSettings $runSettings `
        -ResultsDir $ResultsDir -Logger $Logger

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $output = (& dotnet @testArgs 2>&1 | Out-String)
    $code = $LASTEXITCODE
    $sw.Stop()
    $counts = Get-TestResultCounts -Output $output

    return [pscustomobject]@{
        Kind = 'MsTest'; ProjectName = $Unit.ProjectName; ExitCode = $code
        Passed = $counts.Passed; Failed = $counts.Failed; Skipped = $counts.Skipped
        Total = $counts.Total; Parsed = $counts.Parsed; NoTestsMatched = $counts.Empty
        FailedNames = Get-FailedTestNames -Output $output
        Output = $output; DurationMs = [int]$sw.ElapsedMilliseconds
        RunSettings = $runSettings; Command = ('dotnet ' + ($testArgs -join ' '))
    }
}

Export-ModuleMember -Function @('Find-RunSettings', 'Invoke-TestUnit')
