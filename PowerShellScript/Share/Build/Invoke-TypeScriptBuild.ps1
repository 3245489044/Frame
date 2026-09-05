<#
.SYNOPSIS
    编译 TypeScriptPrj 中所有 TypeScript 项目

.DESCRIPTION
    专门编译 TypeScriptPrj 目录下所有 TypeScript 子项目。
    通过 TypeScriptPrj/tsconfig.json 的 references 自动发现所有子项目。
    支持单独编译指定子项目、清理后编译、跳过确认等模式。

    流程:
      步骤 1: 前置检查（node/npx 可用性、tsconfig.json 存在性）
      步骤 2: 安装依赖（如果 node_modules 缺失）
      步骤 3: 全量编译 TypeScript（npx tsc --build）
      步骤 4: 验证输出完整性

    用法:
        Invoke-TypeScriptBuild.ps1                   交互模式（确认后执行编译）
        Invoke-TypeScriptBuild.ps1 -Force            跳过确认，直接执行
        Invoke-TypeScriptBuild.ps1 -Clean            先清理 .tsbuildinfo 再编译
        Invoke-TypeScriptBuild.ps1 -Project BasicMethod  只编译指定子项目
        Invoke-TypeScriptBuild.ps1 -Help             显示帮助

    示例:
        Invoke-TypeScriptBuild.ps1
        Invoke-TypeScriptBuild.ps1 -Force
        Invoke-TypeScriptBuild.ps1 -Clean -Force
        Invoke-TypeScriptBuild.ps1 -Project CsprojContext

.NOTES
    依赖: OutputHelpers, ProjectPaths, BuildHelpers 模块（通过 Import-Modules.ps1 加载）
    注意: 子项目间存在依赖关系（MCPFramework → BasicMethod, MCPBridge → MCPFramework）
          单独编译子项目时需确保其依赖已先行编译
# @requires Write-Header, Write-Success, Write-Info, Write-SubHeader
# @requires Confirm-Action, Invoke-Step, Invoke-VerificationCheck
# @requires Test-CommandExists, Get-ProjectRoot
#>

param(
    # 跳过确认提示
    [switch]$Force,

    # 先清理 .tsbuildinfo 再编译
    [switch]$Clean,

    # 只编译指定子项目（名称如 BasicMethod, CsprojContext 等）
    [string]$Project = "",

    # 显示帮助
    [switch]$Help
)

# ============================================================
# 帮助
# ============================================================

if ($Help) {
    $scriptName = Split-Path -Leaf $MyInvocation.MyCommand.Path
    Write-Host @"
Invoke-TypeScriptBuild.ps1 — 编译 TypeScriptPrj 中所有 TypeScript 项目

用法:
    $scriptName                       交互模式（确认后执行编译）
    $scriptName -Force                跳过确认，直接执行
    $scriptName -Clean                先清理 .tsbuildinfo 再编译
    $scriptName -Project BasicMethod  只编译指定子项目
    $scriptName -Help                 显示本帮助

流程:
    1. 前置检查（node/npx 可用性、tsconfig.json 存在性）
    2. 安装依赖（如果 node_modules 缺失）
    3. 全量编译 TypeScript（npx tsc --build）
    4. 验证输出完整性

示例:
    $scriptName
    $scriptName -Force
    $scriptName -Clean -Force
    $scriptName -Project CsprojContext
"@
    exit 0
}

# ============================================================
# 加载模块
# ============================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleDir = Resolve-Path (Join-Path $scriptDir "..\Modules")
. "$moduleDir\Import-Modules.ps1"


# ============================================================
# 路径配置
# ============================================================

$projectRoot = Get-ProjectRoot
$tsProjectRoot = Join-Path $projectRoot "TypeScriptPrj"
$tsConfigFile = Join-Path $tsProjectRoot "tsconfig.json"
$buildOutputRoot = Join-Path $projectRoot "BuildOutput"

# 从 tsconfig.json 读取所有子项目引用
$tsConfigContent = Get-Content $tsConfigFile -Raw -Encoding UTF8 | ConvertFrom-Json
$allSubProjects = @()
if ($tsConfigContent.references) {
    $allSubProjects = $tsConfigContent.references | ForEach-Object {
        $refPath = $_.path
        # 提取项目名称（路径最后一部分）
        $projName = Split-Path -Leaf $refPath
        [PSCustomObject]@{
            Name = $projName
            Path = Join-Path $projectRoot $refPath
            ConfigFile = Join-Path $projectRoot "$refPath\tsconfig.json"
        }
    }
}

# 验证清单（编译后检查的关键文件）
# tsconfig.json 中 outDir 设为 ./build，所以输出在 TypeScriptPrj/build/ 下
$verificationFiles = @(
    @{ Path = "TypeScriptPrj\BasicMethod\build\index.js"; Label = "TS: BasicMethod" }
    @{ Path = "TypeScriptPrj\CsprojContext\build\index.js"; Label = "TS: CsprojContext" }
    @{ Path = "TypeScriptPrj\MCPBridge\build\index.js"; Label = "TS: MCPBridge" }
    @{ Path = "TypeScriptPrj\MCPFramework\build\index.js"; Label = "TS: MCPFramework" }
)

# ============================================================
# 步骤 0：前置检查
# ============================================================

Write-Header "Invoke-TypeScriptBuild — TypeScript 项目编译"
Write-Host "  TypeScript 项目根目录: $tsProjectRoot" -ForegroundColor Gray
Write-Host "  配置文件: $tsConfigFile" -ForegroundColor Gray
if ($Clean) {
    Write-Host "  模式: 清理后编译" -ForegroundColor Yellow
}
if ($Project) {
    Write-Host "  目标项目: $Project" -ForegroundColor Cyan
} else {
    Write-Host "  目标项目: 全部 $($allSubProjects.Count) 个子项目" -ForegroundColor Cyan
}
Write-Host ""

# 前置检查
$preCheckFailed = $false
if (-not (Test-CommandExists "node")) { $preCheckFailed = $true }
if (-not (Test-CommandExists "npx")) { $preCheckFailed = $true }

if ($preCheckFailed) {
    Write-Error "前置检查失败，请安装 Node.js 后重试"
    exit 1
}

# 检查 tsconfig.json
if (-not (Test-Path $tsConfigFile)) {
    Write-Error "找不到 TypeScript 配置文件: $tsConfigFile"
    exit 1
}

# 如果指定了子项目，验证其是否存在
if ($Project) {
    $matchedProject = $allSubProjects | Where-Object { $_.Name -eq $Project }
    if (-not $matchedProject) {
        Write-Error "找不到指定的子项目 '$Project'"
        Write-Host "  可用子项目: $($allSubProjects.Name -join ', ')" -ForegroundColor Yellow
        exit 1
    }
    if (-not (Test-Path $matchedProject.ConfigFile)) {
        Write-Error "子项目 '$Project' 的 tsconfig.json 不存在: $($matchedProject.ConfigFile)"
        exit 1
    }
}

# 检查 node_modules（TypeScript 编译时 Push-Location 到 TypeScriptPrj，node_modules 在 TypeScriptPrj/ 下）
$tsNodeModules = Join-Path $tsProjectRoot "node_modules"
$needInstall = -not (Test-Path $tsNodeModules)

# ============================================================
# 确认提示
# ============================================================

if (-not $Force) {
    $actionDesc = @()
    if ($Clean) {
        $actionDesc += "清理 .tsbuildinfo 文件"
    }
    $actionDesc += "安装依赖（如需）"
    $actionDesc += "编译 TypeScript 项目"
    $actionDesc += "验证输出完整性"

    $confirmed = Confirm-Action -ActionDescription $actionDesc
    if (-not $confirmed) { exit 0 }
}

# ============================================================
# 步骤 1：清理 .tsbuildinfo（可选）
# ============================================================

if ($Clean) {
    Invoke-Step -Name "步骤 1/4：清理 .tsbuildinfo 文件" -Critical $false -ScriptBlock {
        $tsBuildInfoFiles = Get-ChildItem -Path $tsProjectRoot -Recurse -Filter "*.tsbuildinfo" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\node_modules\\' }

        if ($tsBuildInfoFiles.Count -eq 0) {
            Write-Host "  未找到 .tsbuildinfo 文件，跳过" -ForegroundColor Gray
        } else {
            foreach ($file in $tsBuildInfoFiles) {
                Remove-Item -Path $file.FullName -Force -ErrorAction SilentlyContinue
                Write-Host "  [已删除] $($file.FullName.Replace($tsProjectRoot, '').TrimStart('\'))" -ForegroundColor Green
            }
            Write-Host "  共清理 $($tsBuildInfoFiles.Count) 个 .tsbuildinfo 文件" -ForegroundColor Gray
        }
    }
} else {
    Write-Host ""
    Write-SubHeader "▶ 跳过清理（未指定 -Clean）"
}

# ============================================================
# 步骤 2（或 1）：安装依赖（如需）
# ============================================================

$stepNumber = if ($Clean) { 2 } else { 1 }
$totalSteps = if ($Clean) { 4 } else { 3 }

if ($needInstall) {
    Invoke-Step -Name "步骤 $stepNumber/$totalSteps：安装 npm 依赖" -Critical $true -ScriptBlock {
        Write-Host "  TypeScriptPrj 的 node_modules 不存在，正在安装依赖..." -ForegroundColor Yellow
        Write-Host ""

        Push-Location $tsProjectRoot
        $npmOutput = & npm install 2>&1
        Pop-Location

        $npmErrorLines = $npmOutput | Where-Object { $_ -match 'error|ERR!' }
        if ($npmErrorLines.Count -gt 0) {
            foreach ($err in ($npmErrorLines | Select-Object -First 5)) {
                Write-Host "    $err" -ForegroundColor Red
            }
        }

        if ($LASTEXITCODE -ne 0) {
            throw "npm install 失败"
        }

        Write-Host "  依赖安装完成" -ForegroundColor Green
    }
} else {
    Write-Host ""
    Write-SubHeader "▶ 步骤 $stepNumber/$totalSteps：跳过依赖安装（node_modules 已存在）"
}

# ============================================================
# 步骤 3（或 2）：编译 TypeScript
# ============================================================

$compileStepNumber = if ($Clean) { 3 } else { 2 }

Invoke-Step -Name "步骤 $compileStepNumber/$totalSteps：编译 TypeScript" -Critical $true -ScriptBlock {
    # node_modules 在 TypeScriptPrj/ 下，使用 tsc.cmd 的绝对路径
    $tscCmd = Join-Path $tsProjectRoot "node_modules\.bin\tsc.cmd"

    if ($Project) {
        # 只编译指定子项目
        $matchedProject = $allSubProjects | Where-Object { $_.Name -eq $Project }
        Write-Host "  编译子项目: $Project" -ForegroundColor Cyan
        Write-Host "  配置文件: $($matchedProject.ConfigFile)" -ForegroundColor Gray
        Write-Host ""

        $tsBuildOutput = & $tscCmd --build $matchedProject.ConfigFile 2>&1
    } else {
        # 全量编译所有子项目
        Write-Host "  编译全部 $($allSubProjects.Count) 个子项目" -ForegroundColor Cyan
        Write-Host "  配置文件: $tsConfigFile" -ForegroundColor Gray
        Write-Host ""

        $tsBuildOutput = & $tscCmd --build $tsConfigFile 2>&1
    }

    $tsBuildSucceeded = $LASTEXITCODE -eq 0

    if (-not $tsBuildSucceeded) {
        Write-Host "  TypeScript 编译输出:" -ForegroundColor DarkGray
        $tsBuildOutput | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        throw "TypeScript 编译失败"
    }

    Write-Host "  TypeScript 编译成功" -ForegroundColor Green
}

# ============================================================
# 步骤 4（或 3）：验证输出完整性
# ============================================================

$verifyStepNumber = if ($Clean) { 4 } else { 3 }

Invoke-Step -Name "步骤 $verifyStepNumber/$totalSteps：验证输出完整性" -Critical $false -ScriptBlock {
    # 如果只编译了指定子项目，只验证该项目的输出
    $activeVerification = if ($Project) {
        $verificationFiles | Where-Object { $_.Label -match "^TS: $Project$" }
    } else {
        $verificationFiles
    }

    Invoke-VerificationCheck -Files $activeVerification -ProjectRoot $projectRoot
}

# ============================================================
# 完成
# ============================================================

Write-Host ""
Write-Header "TypeScript 编译完成"
Write-Host ""
Write-Host "  编译摘要:" -ForegroundColor Cyan
if ($Project) {
    Write-Host "  • 子项目: $Project" -ForegroundColor Gray
} else {
    Write-Host "  • 子项目: 全部 $($allSubProjects.Count) 个" -ForegroundColor Gray
}
Write-Host "  • 输出目录: $buildOutputRoot\TypeScriptPrj" -ForegroundColor Gray
Write-Host ""
Write-Host "  提示:" -ForegroundColor Yellow
Write-Host "  1. 如需清理后编译: Invoke-TypeScriptBuild.ps1 -Clean" -ForegroundColor Gray
Write-Host "  2. 如需编译指定子项目: Invoke-TypeScriptBuild.ps1 -Project <名称>" -ForegroundColor Gray
Write-Host "  3. 可用子项目: $($allSubProjects.Name -join ', ')" -ForegroundColor Gray
Write-Host ""
