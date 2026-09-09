# ============================================================
# Bake-CsharpPrj.ps1
# CsharpPrj 权威烘焙 —— 主构建入口（Phase 4）
#
# 作用：等价于官方入口 `dotnet build Frame\CsharpPrj\MainProj.sln`（6 伞递归全集），
#       之后再刷新完整性 manifest+stamp。
#       注：MainProj.sln 本身就是权威烘焙入口（6 伞 ProjectReference 递归全部叶子工程），
#       本脚本只是“官方入口 + manifest 刷新”的一键封装；
#       正常开发增量直接 build MainProj.sln 即可，无需手动跑本脚本。
#
# 用法：pwsh Frame\CsharpPrj\Bake-CsharpPrj.ps1 [-Configuration Debug|Release]
# 设计依据：Docs/analysis/2026/9/9.8/2026-09-08-烘焙框架彻底完善-落地方案.md Phase 4
# ============================================================
param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sln = Join-Path $root 'MainProj.sln'
if (-not (Test-Path $sln)) { throw "缺少 MainProj.sln：$sln" }

# 权威烘焙 = 官方入口 MainProj.sln（6 伞递归全部叶子工程），单命令等价旧 6 条逐伞构建
Write-Host "[Bake-CsharpPrj] dotnet build MainProj.sln（权威烘焙全集）…" -ForegroundColor Cyan
& dotnet build $sln -c $Configuration -v m --nologo
if ($LASTEXITCODE -ne 0) { throw "烘焙失败（exit=$LASTEXITCODE）：$sln" }

Write-Host '[Bake-CsharpPrj] 刷新完整性 manifest + stamp …' -ForegroundColor Cyan
$gen = Join-Path $root 'Generate-CsharpBakeManifest.ps1'
& pwsh -NoProfile -File $gen -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "manifest 刷新失败（exit=$LASTEXITCODE）：$gen" }

Write-Host '[Bake-CsharpPrj] 完成。权威目录：' (Join-Path $root "BuildOutput\$Configuration") -ForegroundColor Green
