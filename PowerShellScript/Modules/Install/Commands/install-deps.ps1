<#
.SYNOPSIS
    安装 TypeScript 项目的 npm 依赖到 build 目录
.DESCRIPTION
    将 node_modules 安装到 TypeScriptPrj/build/<ProjectName>/node_modules/，
    避免在项目源码目录下产生 node_modules。
.PARAMETER ProjectName
    项目名称（如 MCPBridge、MCPFramework、CsprojContext）
.PARAMETER ProjectDir
    项目源码目录（相对于 TypeScriptPrj/）
.PARAMETER BuildOutputRoot
    build 根目录（默认：..\build）
.EXAMPLE
    .\install-deps.ps1 -ProjectName "MCPBridge"
    .\install-deps.ps1 -ProjectName "MCPFramework"
    .\install-deps.ps1 -ProjectName "CsprojContext"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectName,

    [string]$ProjectDir = $ProjectName,

    [string]$BuildOutputRoot = "..\build"
)

$ErrorActionPreference = "Stop"

# 项目根目录（WorkFlowPrj）
$WorkFlowRoot = "D:\Project\Program\WorkFlowPrj"

# 路径计算
$ProjectRoot = Join-Path $WorkFlowRoot "TypeScriptPrj\$ProjectDir"
$BuildOutputDir = Join-Path $WorkFlowRoot "TypeScriptPrj\build\$ProjectName"
$NodeModulesDir = Join-Path $BuildOutputDir "node_modules"
$PackageJson = Join-Path $ProjectRoot "package.json"
$TsconfigJson = Join-Path $ProjectRoot "tsconfig.json"

Write-Host "=" -ForegroundColor Cyan
Write-Host "  安装 $ProjectName 依赖到 BuildOutput" -ForegroundColor Cyan
Write-Host "=" -ForegroundColor Cyan
Write-Host ""

# 检查路径
if (-not (Test-Path $PackageJson)) {
    Write-Host "[✗] 未找到 package.json: $PackageJson" -ForegroundColor Red
    exit 1
}
Write-Host "[✓] 项目目录: $ProjectRoot" -ForegroundColor Green
Write-Host "[✓] 构建输出: $BuildOutputDir" -ForegroundColor Green
Write-Host "[✓] node_modules 目标: $NodeModulesDir" -ForegroundColor Green

# 1. 检查 Node.js
try {
    $nodeVersion = node --version
    $npmVersion = npm --version
    Write-Host "[✓] Node.js: $nodeVersion" -ForegroundColor Green
    Write-Host "[✓] npm:     $npmVersion" -ForegroundColor Green
} catch {
    Write-Host "[✗] Node.js 未找到" -ForegroundColor Red
    exit 1
}

# 2. 确保 BuildOutput 目录存在
Write-Host ""
Write-Host ">>> 步骤 1/4: 确保 BuildOutput 目录存在..." -ForegroundColor Yellow
if (-not (Test-Path $BuildOutputDir)) {
    New-Item -Path $BuildOutputDir -ItemType Directory -Force | Out-Null
    Write-Host "    [✓] 已创建: $BuildOutputDir" -ForegroundColor Green
} else {
    Write-Host "    [✓] 目录已存在" -ForegroundColor Green
}

# 3. 清理旧的 node_modules
Write-Host ""
Write-Host ">>> 步骤 2/4: 清理旧的 node_modules..." -ForegroundColor Yellow
if (Test-Path $NodeModulesDir) {
    Write-Host "    删除 $NodeModulesDir ..." -ForegroundColor Gray
    Remove-Item -Path $NodeModulesDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "    [✓] 已删除" -ForegroundColor Green
} else {
    Write-Host "    [✓] 无需清理" -ForegroundColor Green
}

# 4. 在 BuildOutput 目录下安装依赖
Write-Host ""
Write-Host ">>> 步骤 3/4: 安装 npm 依赖到 BuildOutput..." -ForegroundColor Yellow
Write-Host "    目录: $BuildOutputDir" -ForegroundColor Gray
Write-Host ""

Push-Location $BuildOutputDir
try {
    # 复制 package.json 到 BuildOutput 目录
    Copy-Item -Path $PackageJson -Destination $BuildOutputDir -Force
    Write-Host "    [✓] 已复制 package.json" -ForegroundColor Green

    # 如果有 tsup.config.ts 也复制（构建时需要）
    $TsupConfig = Join-Path $ProjectRoot "tsup.config.ts"
    if (Test-Path $TsupConfig) {
        Copy-Item -Path $TsupConfig -Destination $BuildOutputDir -Force
        Write-Host "    [✓] 已复制 tsup.config.ts" -ForegroundColor Green
    }

    # 执行 npm install
    npm install --no-package-lock 2>&1 | ForEach-Object {
        if ($_ -match "error") {
            Write-Host "    [✗] $_" -ForegroundColor Red
        } elseif ($_ -match "warn") {
            Write-Host "    [!] $_" -ForegroundColor Yellow
        } else {
            Write-Host "    $_" -ForegroundColor Gray
        }
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "[✗] npm install 失败 (exit code: $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
} finally {
    Pop-Location
}

# 5. 验证安装结果
Write-Host ""
Write-Host ">>> 步骤 4/4: 验证安装结果..." -ForegroundColor Yellow

$requiredPackages = @(
    "@modelcontextprotocol/sdk",
    "@types/node",
    "typescript"
)

$allOk = $true
foreach ($pkg in $requiredPackages) {
    $pkgPath = Join-Path $NodeModulesDir $pkg.Replace("/", "\")
    if (Test-Path $pkgPath) {
        Write-Host "    [✓] $pkg" -ForegroundColor Green
    } else {
        Write-Host "    [✗] $pkg — 未找到" -ForegroundColor Red
        $allOk = $false
    }
}

Write-Host ""
if ($allOk) {
    Write-Host "=" -ForegroundColor Cyan
    Write-Host "  所有依赖安装成功！" -ForegroundColor Green
    Write-Host "  位置: $NodeModulesDir" -ForegroundColor Green
    Write-Host "=" -ForegroundColor Cyan
} else {
    Write-Host "=" -ForegroundColor Red
    Write-Host "  部分依赖缺失，请检查错误信息" -ForegroundColor Red
    Write-Host "=" -ForegroundColor Red
}
