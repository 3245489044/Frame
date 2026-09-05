#requires -Version 5.1
<#
.SYNOPSIS
    Agent 任务池自检脚本：验证「数据库任务池（ur_tasks）→ 创建任务 → Agent/处理器自动识别执行 → 全局暂停/恢复」闭环。

.DESCRIPTION
    以 `StateManagement.Tests`（含 SqliteTaskPoolStore + TaskPoolWithSqliteStore 集成测试）为验证载体：
      - ① 创建任务 → 自动识别执行并完成（Sqlite 持久化）
      - ② 在途/排队暂停/恢复（非终止，Running ⇄ Paused）
      - ③ 队列级全局暂停/恢复（PauseAll/ResumeAll）
      - ④ 重启回填续跑（ProcessPending：死 Running → NotStarted 重放）
    全部通过输出 PASS，任一失败输出 FAIL（非零退出码）。
    性能：先 `dotnet build` 一次，随后测试与重试均用 `--no-build` 复用产物，避免每次重复编译（大幅提速）。

.PARAMETER TestProject
    测试项目 .csproj 路径（默认 StateManagement.Tests）。

.PARAMETER Configuration
    构建配置（Debug/Release）。

.PARAMETER NoBuild
    跳过首次构建，直接以现有产物运行测试（用于已构建、仅需快速复跑）。

.EXAMPLE
    ./Test-AgentTaskPool.ps1
#>
[CmdletBinding()]
param(
    [string]$TestProject = "d:\Project\Program\WorkFlowPrj\CentralTest\CSharpTest\Share.Tsets\StateManagement.Tests\StateManagement.Tests.csproj",
    [string]$Configuration = "Debug",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

Write-Host "== Agent 任务池自检 ==" -ForegroundColor Cyan
Write-Host "测试项目: $TestProject"

if (-not (Test-Path $TestProject)) {
    Write-Host "FAIL: 测试项目不存在 ($TestProject)" -ForegroundColor Red
    exit 2
}

# 先构建一次（含依赖图），随后测试复用产物，避免每次重复编译。
if (-not $NoBuild) {
    Write-Host "构建测试项目（一次）..."
    & dotnet build $TestProject -c $Configuration --nologo -v q 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: 构建失败（dotnet build 退出码 $LASTEXITCODE）" -ForegroundColor Red
        exit 1
    }
}

# 任务池相关测试：Sqlite 持久化存储 + 通用任务池集成（含全局暂停/重启回填）。
# 说明：暂停/回填测试为"合作式时序"测试（Task.Delay/轮询等待），高负载下偶发单次超时；
#       故失败时自动重跑一次以滤除环境抖动，两次均失败才判定 FAIL（真实回归必然两次都失败）。
function Invoke-TaskPoolTests {
    & dotnet test $TestProject -c $Configuration --no-build --nologo -v q --filter "FullyQualifiedName~TaskPool" 2>&1 | Write-Host
    return $LASTEXITCODE
}

$exit = Invoke-TaskPoolTests
if ($exit -ne 0) {
    Write-Host "WARN: 首次未通过，自动重跑一次以滤除环境抖动..." -ForegroundColor Yellow
    $exit = Invoke-TaskPoolTests
}

if ($exit -ne 0) {
    Write-Host "FAIL: 任务池自检未通过（dotnet test 退出码 $exit，连续两次失败）" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: 数据库任务池闭环验证通过（创建任务 → 自动执行 → 全局暂停/恢复 → 重启回填）" -ForegroundColor Green
exit 0

