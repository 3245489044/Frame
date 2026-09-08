# ============================================================
# Bake-CsharpPrj.ps1
# CsharpPrj 权威烘焙 —— 主构建入口（Phase 4）
#
# 作用：一键烘焙 6 伞工程闭包到权威目录并刷新完整性 manifest+stamp。
#       这是“权威烘焙”唯一推荐入口（勿再以 MainProj.sln 作为构建入口 —— sln 顶层只含 6 伞、不递归子工程）。
#
# 用法：pwsh Frame\CsharpPrj\Bake-CsharpPrj.ps1 [-Configuration Debug|Release]
# 设计依据：Docs/analysis/2026/9/9.8/2026-09-08-烘焙框架彻底完善-落地方案.md Phase 4
# ============================================================
param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$umbrellas = @(
    'BasicMethod\BasicMethod.csproj',
    'Modules\Modules.csproj',
    'Share\Share.csproj',
    'Operation\Operation.csproj',
    'Plate\Plate.csproj',
    'Strategy\Strategy.csproj'
)

foreach ($u in $umbrellas) {
    $p = Join-Path $root $u
    if (-not (Test-Path $p)) { throw "伞工程不存在：$p" }
    Write-Host "[Bake-CsharpPrj] 构建伞工程：$p" -ForegroundColor Cyan
    & dotnet build $p -c $Configuration -v m --nologo
    if ($LASTEXITCODE -ne 0) { throw "烘焙失败（exit=$LASTEXITCODE）：$p" }
}

Write-Host '[Bake-CsharpPrj] 刷新完整性 manifest + stamp …' -ForegroundColor Cyan
$gen = Join-Path $root 'Generate-CsharpBakeManifest.ps1'
& pwsh -NoProfile -File $gen -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "manifest 刷新失败（exit=$LASTEXITCODE）：$gen" }

Write-Host '[Bake-CsharpPrj] 完成。权威目录：' (Join-Path $root "BuildOutput\$Configuration") -ForegroundColor Green
