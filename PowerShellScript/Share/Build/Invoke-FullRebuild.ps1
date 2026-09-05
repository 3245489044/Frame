<#
.SYNOPSIS
    项目集成全量编译脚本 — 清理后全量编译 C# + TypeScript

.DESCRIPTION
    一键完成"彻底清理 → 全量编译 → 验证输出"的完整流程。
    支持 C# (.NET) 和 TypeScript 两个子系统的独立/联合编译。
    清理步骤委托给 Invoke-CleanAll.ps1（删除 BuildOutput + bin/obj + .tsbuildinfo）。

    流程:
      步骤 1: 集中清理（BuildOutput + bin/obj + .tsbuildinfo）
      步骤 2: 全量编译 C#（dotnet build MainProj.sln）
      步骤 3: 全量编译 TypeScript（tsc --build）
      步骤 4: 验证输出完整性（检查关键文件是否存在）

    用法:
        Invoke-FullRebuild.ps1                   交互模式（确认后执行全量编译）
        Invoke-FullRebuild.ps1 -Force            跳过确认，直接执行
        Invoke-FullRebuild.ps1 -Configuration Release  编译 Release 配置
        Invoke-FullRebuild.ps1 -SkipClean        只编译，不清理
        Invoke-FullRebuild.ps1 -SkipTypeScript   只编译 C#，跳过 TypeScript
        Invoke-FullRebuild.ps1 -Help             显示帮助

    示例:
        Invoke-FullRebuild.ps1
        Invoke-FullRebuild.ps1 -Force
        Invoke-FullRebuild.ps1 -Configuration Release -SkipTypeScript
        Invoke-FullRebuild.ps1 -SkipClean -Force

.NOTES
    依赖: OutputHelpers, ProjectPaths, BuildHelpers 模块（通过 Import-Modules.ps1 加载）
    注意: 不删除 node_modules，避免需要联网重新安装
# @requires Write-Header, Write-Success, Write-Info, Write-SubHeader, Write-Warning, Write-Error
# @requires Confirm-Action, Invoke-Step, Invoke-VerificationCheck
# @requires Test-CommandExists, Get-ProjectRoot
#>

param(
    # 构建配置（Debug / Release）
    [string]$Configuration = "Debug",

    # 跳过清理步骤，只编译
    [switch]$SkipClean,

    # 跳过 TypeScript 编译
    [switch]$SkipTypeScript,

    # 跳过确认提示
    [switch]$Force,

    # 显示帮助
    [switch]$Help
)

# ============================================================
# 帮助
# ============================================================

if ($Help) {
    $scriptName = Split-Path -Leaf $MyInvocation.MyCommand.Path
    Write-Host @"
Invoke-FullRebuild.ps1 — 项目集成全量编译脚本

用法:
    $scriptName                       交互模式（确认后执行全量编译）
    $scriptName -Force                跳过确认，直接执行
    $scriptName -Configuration Release 编译 Release 配置
    $scriptName -SkipClean            只编译，不清理
    $scriptName -SkipTypeScript       只编译 C#，跳过 TypeScript
    $scriptName -Help                 显示本帮助

流程:
    1. 集中清理（BuildOutput + bin/obj + .tsbuildinfo）
    2. 全量编译 C#（dotnet build MainProj.sln）
    3. 全量编译 TypeScript（tsc --build）
    4. 验证输出完整性（检查关键文件是否存在）

示例:
    $scriptName
    $scriptName -Force
    $scriptName -Configuration Release -SkipTypeScript
    $scriptName -SkipClean -Force
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

# 项目根目录（通过 ProjectPaths 模块获取）
$projectRoot = Get-ProjectRoot

# C# 解决方案路径
$slnFile = Join-Path $projectRoot "CsharpPrj\MainProj.sln"

# TypeScript 项目根目录
$tsProjectRoot = Join-Path $projectRoot "TypeScriptPrj"
$tsConfigFile = Join-Path $projectRoot "TypeScriptPrj\tsconfig.json"

# 构建输出根目录
$buildOutputRoot = Join-Path $projectRoot "BuildOutput"

# 验证清单（编译后检查的关键文件）
$verificationFiles = @(
    # C# 核心 DLL
    @{ Path = "BuildOutput\$Configuration\net10.0\BasicMethod.dll"; Label = "C#: BasicMethod" }
    @{ Path = "BuildOutput\$Configuration\net10.0\FileDriven.dll"; Label = "C#: FileDriven" }
    @{ Path = "BuildOutput\$Configuration\net10.0\Modules.dll"; Label = "C#: Modules" }
    @{ Path = "BuildOutput\$Configuration\net10.0\Share.dll"; Label = "C#: Share" }
    @{ Path = "BuildOutput\$Configuration\net10.0\Operation.dll"; Label = "C#: Operation" }
    @{ Path = "BuildOutput\$Configuration\net10.0\Plate.dll"; Label = "C#: Plate" }
    @{ Path = "BuildOutput\$Configuration\net10.0\PythonModel.Bridge.dll"; Label = "C#: PythonModel.Bridge" }

    # TypeScript 输出
    @{ Path = "BuildOutput\TypeScriptPrj\BasicMethod\index.js"; Label = "TS: BasicMethod" }
    @{ Path = "BuildOutput\TypeScriptPrj\CsprojContext\index.js"; Label = "TS: CsprojContext" }
    @{ Path = "BuildOutput\TypeScriptPrj\MCPBridge\index.js"; Label = "TS: MCPBridge" }
    @{ Path = "BuildOutput\TypeScriptPrj\MCPFramework\index.js"; Label = "TS: MCPFramework" }
)

# ============================================================
# 步骤 0：前置检查
# ============================================================

Write-Header "Invoke-FullRebuild — 项目集成全量编译"
Write-Host "  配置: $Configuration" -ForegroundColor Gray
Write-Host "  项目根目录: $projectRoot" -ForegroundColor Gray
Write-Host "  跳过清理: $(if ($SkipClean) { '是' } else { '否' })" -ForegroundColor Gray
Write-Host "  跳过 TypeScript: $(if ($SkipTypeScript) { '是' } else { '否' })" -ForegroundColor Gray
Write-Host ""

# 前置检查：dotnet 和 node 是否可用
$preCheckFailed = $false
if (-not (Test-CommandExists "dotnet")) { $preCheckFailed = $true }
if (-not $SkipTypeScript -and -not (Test-CommandExists "node")) { $preCheckFailed = $true }
if (-not $SkipTypeScript -and -not (Test-CommandExists "npx")) { $preCheckFailed = $true }

if ($preCheckFailed) {
    Write-Error "前置检查失败，请安装缺失的工具后重试"
    exit 1
}

# 检查 .sln 文件
if (-not (Test-Path $slnFile)) {
    Write-Error "找不到解决方案文件: $slnFile"
    exit 1
}

# 检查 tsconfig.json
if (-not $SkipTypeScript -and -not (Test-Path $tsConfigFile)) {
    Write-Warning "找不到 TypeScript 配置文件: $tsConfigFile，将跳过 TypeScript 编译"
    $SkipTypeScript = $true
}

# 确认提示
if (-not $Force) {
    $actionDesc = @()
    if (-not $SkipClean) {
        $actionDesc += "集中清理（BuildOutput + bin/obj + .tsbuildinfo）"
    } else {
        $actionDesc += "[跳过清理] 直接编译"
    }
    $actionDesc += "全量编译 C# ($Configuration)"
    if (-not $SkipTypeScript) {
        $actionDesc += "全量编译 TypeScript（tsc --build）"
    }
    $actionDesc += "验证输出完整性"

    $confirmed = Confirm-Action -ActionDescription $actionDesc
    if (-not $confirmed) { exit 0 }
}

# ============================================================
# 步骤 1：集中清理（委托 Invoke-CleanAll.ps1）
# ============================================================

$cleanAllScript = Join-Path $scriptDir "Invoke-CleanAll.ps1"

if (-not $SkipClean) {
    Invoke-Step -Name "步骤 1/4：集中清理（BuildOutput + bin/obj + .tsbuildinfo）" -Critical $true -ScriptBlock {
        if (Test-Path $cleanAllScript) {
            & $cleanAllScript -Force
        } else {
            Write-Warning "找不到 Invoke-CleanAll.ps1，回退到直接删除 BuildOutput"
            if (Test-Path $buildOutputRoot) {
                Remove-Item -Path $buildOutputRoot -Recurse -Force -ErrorAction Stop
                Write-Host "  BuildOutput 目录已删除" -ForegroundColor Gray
            } else {
                Write-Host "  BuildOutput 目录不存在，跳过" -ForegroundColor Gray
            }
        }
    }
} else {
    Write-Host ""
    Write-SubHeader "▶ 步骤 1：跳过清理（-SkipClean）"
}

# ============================================================
# 步骤 2：全量编译 C#
# ============================================================

Invoke-Step -Name "步骤 2/4：全量编译 C# ($Configuration)" -Critical $true -ScriptBlock {
    Write-Host "  解决方案: $slnFile" -ForegroundColor Gray
    Write-Host "  配置: $Configuration" -ForegroundColor Gray
    Write-Host ""

    $buildOutput = & dotnet build $slnFile -c $Configuration --nologo 2>&1
    $buildSucceeded = Format-BuildOutput -BuildOutput $buildOutput

    if (-not $buildSucceeded) {
        throw "C# 编译失败"
    }
}

# ============================================================
# 步骤 3：全量编译 TypeScript
# ============================================================

if (-not $SkipTypeScript) {
    Invoke-Step -Name "步骤 3/4：全量编译 TypeScript" -Critical $false -ScriptBlock {
        Write-Host "  项目根目录: $tsProjectRoot" -ForegroundColor Gray
        Write-Host "  配置文件: $tsConfigFile" -ForegroundColor Gray
        Write-Host ""

        Push-Location $tsProjectRoot
        $tsBuildOutput = & npx tsc --build $tsConfigFile 2>&1
        Pop-Location

        $tsBuildSucceeded = $LASTEXITCODE -eq 0

        if (-not $tsBuildSucceeded) {
            Write-Host "  TypeScript 编译输出:" -ForegroundColor DarkGray
            $tsBuildOutput | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
            throw "TypeScript 编译失败"
        }

        Write-Host "  TypeScript 编译成功" -ForegroundColor Green
    }
} else {
    Write-Host ""
    Write-SubHeader "▶ 步骤 3：跳过 TypeScript 编译（-SkipTypeScript）"
}

# ============================================================
# 步骤 4：验证输出完整性
# ============================================================

Invoke-Step -Name "步骤 4/4：验证输出完整性" -Critical $false -ScriptBlock {
    # 如果跳过了 TypeScript，从验证清单中移除 TS 项目
    $activeVerification = if ($SkipTypeScript) {
        $verificationFiles | Where-Object { $_.Label -notmatch '^TS:' }
    } else {
        $verificationFiles
    }

    Invoke-VerificationCheck -Files $activeVerification -ProjectRoot $projectRoot
}

# ============================================================
# 完成
# ============================================================

Write-Host ""
Write-Header "全量编译完成"
Write-Host ""
Write-Host "  编译摘要:" -ForegroundColor Cyan
Write-Host "  • C# ($Configuration): 已完成" -ForegroundColor Gray
if (-not $SkipTypeScript) {
    Write-Host "  • TypeScript: 已完成" -ForegroundColor Gray
}
Write-Host "  • 输出目录: $buildOutputRoot" -ForegroundColor Gray
Write-Host ""
Write-Host "  提示:" -ForegroundColor Yellow
Write-Host "  1. 如需单独编译 C#: dotnet build CsharpPrj/MainProj.sln -c $Configuration" -ForegroundColor Gray
Write-Host "  2. 如需单独编译 TS: cd TypeScriptPrj && npx tsc --build" -ForegroundColor Gray
Write-Host ""
