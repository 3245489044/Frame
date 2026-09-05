<#
.SYNOPSIS
    安装/修复 TypeScript 项目的 node_modules 依赖，解决类型定义文件缺失问题。
.DESCRIPTION
    本脚本自动检测 TypeScriptPrj 下各子项目的依赖安装状态，
    对缺少 node_modules 或 @types/node 的项目执行 npm install。
    支持指定项目、全部修复、仅检查模式。
.PARAMETER ProjectName
    指定要修复的项目名称（如 TsSln、BasicMethod），不指定则检查所有项目。
.PARAMETER CheckOnly
    仅检查依赖状态，不执行安装。
.PARAMETER Force
    强制重新安装依赖（先删除 node_modules 再安装）。
.EXAMPLE
    .\PowerShellScript\Tools\Install-TypeScriptDeps.ps1
    检查所有 TypeScript 项目，对缺少依赖的自动安装。

    .\PowerShellScript\Tools\Install-TypeScriptDeps.ps1 -ProjectName TsSln
    仅修复 TsSln 项目的依赖。

    .\PowerShellScript\Tools\Install-TypeScriptDeps.ps1 -CheckOnly
    仅检查所有项目的依赖状态，不执行安装。

    .\PowerShellScript\Tools\Install-TypeScriptDeps.ps1 -ProjectName TsSln -Force
    强制重新安装 TsSln 项目的依赖。
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$ProjectName,

    [switch]$CheckOnly,

    [switch]$Force
)

$ProjectRoot = Resolve-Path "$PSScriptRoot\..\..\.."
$TypeScriptPrj = Join-Path $ProjectRoot "TypeScriptPrj"

$basicMethodOutput = Join-Path $ProjectRoot "PowerShellScript\BasicMethod\Core\Output.psm1"
if (Test-Path $basicMethodOutput) {
    Import-Module $basicMethodOutput -Force -DisableNameChecking
}

function Write-OK    { Write-Success $args[0] }
function Write-Action{ Write-Info $args[0] }

# 检测项目是否需要安装依赖
function Test-NeedsInstall {
    param([string]$ProjectDir)

    $pkgJson = Join-Path $ProjectDir "package.json"
    $nodeModules = Join-Path $ProjectDir "node_modules"
    $typesNode = Join-Path $nodeModules "@types\node"

    if (-not (Test-Path $pkgJson)) {
        return $false  # 没有 package.json，跳过
    }

    if (-not (Test-Path $nodeModules)) {
        return $true   # node_modules 不存在
    }

    if (-not (Test-Path $typesNode)) {
        return $true   # @types/node 不存在
    }

    # 检查 @types/node 是否有有效的 index.d.ts
    $indexFile = Join-Path $typesNode "index.d.ts"
    if (-not (Test-Path $indexFile)) {
        return $true
    }

    return $false  # 一切正常
}

# 获取项目大小（用于展示）
function Get-DirSize {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    $size = (Get-ChildItem $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
             Measure-Object -Property Length -Sum).Sum
    return [math]::Round($size / 1MB, 2)
}

# 安装依赖
function Install-ProjectDeps {
    param([string]$ProjectDir, [string]$Name)

    $nodeModules = Join-Path $ProjectDir "node_modules"

    if ($Force -and (Test-Path $nodeModules)) {
        Write-Action "正在删除 $Name/node_modules/ ..."
        Remove-Item $nodeModules -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $nodeModules) {
            Write-Warn "删除 $Name/node_modules/ 不完整，请检查权限。"
        } else {
            Write-OK "已删除 $Name/node_modules/"
        }
    }

    Write-Action "正在安装 $Name 的依赖 ..."
    Push-Location $ProjectDir
    try {
        $output = npm install 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-OK "$Name 依赖安装完成"
            # 显示安装的包数量
            $addedLine = $output | Select-String "added"
            if ($addedLine) {
                Write-Info "  $($addedLine.Line.Trim())"
            }
        } else {
            Write-Error "$Name 依赖安装失败"
            Write-Host $output
        }
    }
    catch {
        Write-Error "安装 $Name 时发生异常: $_"
    }
    finally {
        Pop-Location
    }
}

# ============================================================
# 主逻辑
# ============================================================
Write-Banner -Title "TypeScript 依赖安装/修复工具" -Color "Cyan"
Write-Info "项目根目录: $ProjectRoot"
Write-Info "TypeScriptPrj: $TypeScriptPrj"

if (-not (Test-Path $TypeScriptPrj)) {
    Write-Error "TypeScriptPrj 目录不存在: $TypeScriptPrj"
    exit 1
}

# 收集需要检查的项目目录
$projects = @()

if ($ProjectName) {
    # 指定项目
    $candidates = @(
        @{ Name = $ProjectName; Dir = Join-Path $TypeScriptPrj $ProjectName }
    )
    # 也检查 TsWorkspace 下的子项目
    $wsDir = Join-Path $TypeScriptPrj "TsWorkspace"
    if (Test-Path $wsDir) {
        $candidates += @{ Name = "TsWorkspace/$ProjectName"; Dir = Join-Path $wsDir $ProjectName }
    }

    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c.Dir "package.json")) {
            $projects += $c
        }
    }

    if ($projects.Count -eq 0) {
        Write-Error "未找到项目 '$ProjectName'（在 TsSln/ 或 TsWorkspace/ 下均未找到）"
        exit 1
    }
}
else {
    # 自动检测所有 TypeScript 项目
    # 1. TsSln
    $tsSln = Join-Path $TypeScriptPrj "TsSln"
    if (Test-Path (Join-Path $tsSln "package.json")) {
        $projects += @{ Name = "TsSln"; Dir = $tsSln }
    }

    # 2. TsWorkspace 下的子项目
    $wsDir = Join-Path $TypeScriptPrj "TsWorkspace"
    if (Test-Path $wsDir) {
        $wsProjects = Get-ChildItem $wsDir -Directory
        foreach ($p in $wsProjects) {
            if (Test-Path (Join-Path $p.FullName "package.json")) {
                $projects += @{ Name = "TsWorkspace/$($p.Name)"; Dir = $p.FullName }
            }
        }
    }
}

if ($projects.Count -eq 0) {
    Write-Warn "未找到任何 TypeScript 项目（无 package.json）"
    exit 0
}

# 检查并安装
Write-Host "`n发现 $($projects.Count) 个 TypeScript 项目:" -ForegroundColor Yellow
$needsInstall = @()

foreach ($p in $projects) {
    $needed = Test-NeedsInstall $p.Dir
    $status = if ($needed) { "需要安装" } else { "正常" }
    $size = Get-DirSize (Join-Path $p.Dir "node_modules")
    $sizeStr = if ($size -gt 0) { "${size} MB" } else { "空" }
    Write-Host "  [$status] $($p.Name) (node_modules: $sizeStr)"

    if ($needed) {
        $needsInstall += $p
    }
}

if ($CheckOnly) {
    Write-Host "`n--- 检查完成（仅检查模式）---" -ForegroundColor Cyan
    if ($needsInstall.Count -gt 0) {
        Write-Warn "$($needsInstall.Count) 个项目需要安装依赖"
        Write-Info "执行以下命令安装:"
        Write-Host "  .\PowerShellScript\Tools\Install-TypeScriptDeps.ps1" -ForegroundColor Gray
    }
    else {
        Write-OK "所有项目依赖正常"
    }
    exit 0
}

if ($needsInstall.Count -eq 0) {
    Write-OK "`n所有项目依赖已就绪，无需安装。"
    exit 0
}

Write-Host "`n--- 开始安装 $($needsInstall.Count) 个项目的依赖 ---" -ForegroundColor Yellow

foreach ($p in $needsInstall) {
    Install-ProjectDeps -ProjectDir $p.Dir -Name $p.Name
}

# 最终验证
Write-StepTitle "安装完成，最终状态"
$allOk = $true
foreach ($p in $projects) {
    $needed = Test-NeedsInstall $p.Dir
    $status = if ($needed) { "异常" } else { "正常" }
    if ($needed) { $allOk = $false }
    $icon = if ($needed) { "❌" } else { "✅" }
    $color = if ($needed) { "Red" } else { "Green" }
    Write-Host "  $icon $($p.Name): $status" -ForegroundColor $color
}

if ($allOk) {
    Write-Success "所有 TypeScript 项目依赖已就绪！"
}
else {
    Write-Warn "部分项目仍存在问题，请检查上述异常项目。"
}
