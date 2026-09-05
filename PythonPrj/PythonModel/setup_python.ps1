#requires -Version 7.0

<#
.SYNOPSIS
    Python 运行环境配置脚本 — 用于 Workflow_ 项目的 Python 依赖部署。
.DESCRIPTION
    自动检测/安装 Python 运行时，安装 pip 依赖包，配置 PYTHONHOME 环境变量。
    支持 Windows 10/11 + PowerShell 7+ 环境。
.NOTES
    作者: Workflow_ Team
    用法: .\setup_python.ps1 [-Force]
#>

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# ──────────────────────────────────────────────
# 配置区（可按项目需要修改）
# ──────────────────────────────────────────────
$ProjectRoot      = $PSScriptRoot
$RequirementsFile = Join-Path $ProjectRoot "pythonmodel\requirements.txt"
$PythonScriptDir  = Join-Path $ProjectRoot "pythonmodel"

# PYTHONHOME 的备选路径（脚本会依次尝试）
$PythonSearchPaths = @(
    "C:\Users\Administrator\AppData\Local\Programs\Python\Python313"
    "C:\Users\Administrator\AppData\Local\Programs\Python\Python312"
    "C:\Users\Administrator\AppData\Local\Programs\Python\Python311"
    "C:\Program Files\Python313"
    "C:\Program Files\Python312"
    "C:\Program Files\Python311"
)

# ──────────────────────────────────────────────
# 辅助函数
# ──────────────────────────────────────────────

function Write-Step([string]$Title) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-Info([string]$Msg) {
    Write-Host "[INFO] $Msg" -ForegroundColor Gray
}

function Write-Success([string]$Msg) {
    Write-Host "[OK]   $Msg" -ForegroundColor Green
}

function Write-Warn([string]$Msg) {
    Write-Host "[WARN] $Msg" -ForegroundColor Yellow
}

function Write-ErrorExit([string]$Msg) {
    Write-Host "[ERR]  $Msg" -ForegroundColor Red
    exit 1
}

# ──────────────────────────────────────────────
# 步骤 1：检测 Python 运行时
# ──────────────────────────────────────────────

Write-Step "1/5  检测 Python 运行时"

$PythonHome = $null

# 1a) 检查当前 PYTHONHOME 环境变量
$envHome = [Environment]::GetEnvironmentVariable("PYTHONHOME", "User")
if ([string]::IsNullOrEmpty($envHome)) {
    $envHome = [Environment]::GetEnvironmentVariable("PYTHONHOME", "Machine")
}
if (-not [string]::IsNullOrEmpty($envHome) -and (Test-Path (Join-Path $envHome "python3.dll"))) {
    Write-Success "PYTHONHOME 已配置: $envHome"
    $PythonHome = $envHome
}

# 1b) 尝试用 py 启动器查找最新 Python
if (-not $PythonHome) {
    try {
        $pyOutput = & py -0 2>$null
        if ($LASTEXITCODE -eq 0) {
            $latestTag = ($pyOutput | Select-String "-bit" | Select-Object -Last 1).ToString().Trim()
            if ($latestTag -match '^(-?\d+\.?\d*)') {
                $version = $matches[1]
                $pyPath = & py "-$version" -c "import sys; print(sys.exec_prefix)" 2>$null
                if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrEmpty($pyPath) -and (Test-Path (Join-Path $pyPath "python3.dll"))) {
                    Write-Success "通过 py 启动器找到 Python $version : $pyPath"
                    $PythonHome = $pyPath
                }
            }
        }
    } catch {
        # py 启动器不可用，继续
    }
}

# 1c) 从常见安装路径搜索
if (-not $PythonHome) {
    foreach ($path in $PythonSearchPaths) {
        if (Test-Path (Join-Path $path "python3.dll")) {
            Write-Success "在常见路径找到 Python: $path"
            $PythonHome = $path
            break
        }
    }
}

# 1d) 尝试 python 命令直接获取
if (-not $PythonHome) {
    try {
        $pyPath = & python -c "import sys; print(sys.exec_prefix)" 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrEmpty($pyPath) -and (Test-Path (Join-Path $pyPath "python3.dll"))) {
            Write-Success "通过 python 命令找到: $pyPath"
            $PythonHome = $pyPath
        }
    } catch {
        # python 命令不可用
    }
}

# 1e) 仍未找到，自动安装
if (-not $PythonHome) {
    Write-Warn "未检测到 Python 运行时"
    $installChoice = Read-Host "是否自动通过 winget 安装 Python 3.13？(Y/N, 默认 Y)"
    if ($installChoice -eq "" -or $installChoice -eq "Y" -or $installChoice -eq "y") {
        Write-Info "正在通过 winget 安装 Python 3.13..."

        $wingetResult = & winget install "Python.Python.3.13" --silent --accept-package-agreements 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-ErrorExit "winget 安装失败，请手动安装 Python 3.13 后重试。"
        }

        $PythonHome = "C:\Users\Administrator\AppData\Local\Programs\Python\Python313"
        if (-not (Test-Path (Join-Path $PythonHome "python3.dll"))) {
            # 等待安装完成，最多等待 30 秒
            $waitSeconds = 0
            while (-not (Test-Path (Join-Path $PythonHome "python3.dll")) -and $waitSeconds -lt 30) {
                Start-Sleep -Seconds 2
                $waitSeconds += 2
            }
            if (-not (Test-Path (Join-Path $PythonHome "python3.dll"))) {
                Write-ErrorExit "Python 已安装但路径不正确。请手动设置 PYTHONHOME 环境变量。"
            }
        }
        Write-Success "Python 3.13 安装完成"
    } else {
        Write-ErrorExit "请先安装 Python 3.11+，然后重新运行此脚本。"
    }
}

# ──────────────────────────────────────────────
# 步骤 2：安装 pip 依赖
# ──────────────────────────────────────────────

Write-Step "2/5  安装 pip 依赖"

$pip = Join-Path $PythonHome "Scripts\pip.exe"
if (-not (Test-Path $pip)) {
    $pip = Join-Path $PythonHome "pip.exe"
}
if (-not (Test-Path $pip)) {
    Write-Warn "pip.exe 未找到，尝试通过 python -m pip"
    $pip = "pip"
}

# 升级 pip
& "$PythonHome\python.exe" -m pip install --upgrade pip --quiet 2>$null
Write-Success "pip 已更新"

# 安装 requirements
if (Test-Path $RequirementsFile) {
    Write-Info "安装依赖: $(Get-Content $RequirementsFile -Raw)"
    & "$PythonHome\python.exe" -m pip install -r $RequirementsFile --quiet
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorExit "pip 安装失败，请检查 $RequirementsFile"
    }
    Write-Success "依赖包安装完成"
} else {
    Write-Warn "未找到 $RequirementsFile，跳过依赖安装"
}

# 额外确保 pythonnet 和 openai 已安装
& "$PythonHome\python.exe" -m pip install openai --quiet
Write-Success "python 依赖就绪"

# ──────────────────────────────────────────────
# 步骤 3：验证 python3.dll 和 pythonnet 兼容性
# ──────────────────────────────────────────────

Write-Step "3/5  验证 pythonnet 兼容性"

$dllPath = Join-Path $PythonHome "python3.dll"
if (-not (Test-Path $dllPath)) {
    Write-Warn "python3.dll 未找到（某些嵌入式安装可能不含此文件）"
    Write-Warn "尝试搜索 python3*.dll..."
    $altDll = Get-ChildItem -Path $PythonHome -Filter "python3*.dll" | Select-Object -First 1
    if ($altDll) {
        Write-Success "找到替代 DLL: $($altDll.Name)"
    } else {
        Write-ErrorExit "找不到 python3*.dll。pythonnet 运行时需要此文件。"
    }
} else {
    Write-Success "python3.dll 已确认"
}

# 尝试导入 pythonnet
$testResult = & "$PythonHome\python.exe" -c "import clr; print('pythonnet OK')" 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Success "pythonnet (clr) 可正常导入"
} else {
    Write-Info "pythonnet 不是必需的（仅 C# 端需要通过 NuGet 引用 pythonnet）"
}

# ──────────────────────────────────────────────
# 步骤 4：配置环境变量
# ──────────────────────────────────────────────

Write-Step "4/5  配置环境变量"

# PYTHONHOME — pythonnet 运行时需要此变量定位 python3.dll
[Environment]::SetEnvironmentVariable("PYTHONHOME", $PythonHome, "User")
$env:PYTHONHOME = $PythonHome
Write-Success "已设置用户环境变量 PYTHONHOME = $PythonHome"

# DeepSeekAPI — 检查是否已有配置
$existingApiKey = [Environment]::GetEnvironmentVariable("DeepSeekAPI", "User")
if ([string]::IsNullOrEmpty($existingApiKey)) {
    $existingApiKey = [Environment]::GetEnvironmentVariable("DeepSeekAPI", "Machine")
}

if ([string]::IsNullOrEmpty($existingApiKey)) {
    Write-Warn "环境变量 DeepSeekAPI 未设置"
    $apiKey = Read-Host "请输入 DeepSeek API Key（留空可稍后通过 set_api_key 设置）"
    if (-not [string]::IsNullOrEmpty($apiKey)) {
        [Environment]::SetEnvironmentVariable("DeepSeekAPI", $apiKey, "User")
        $env:DeepSeekAPI = $apiKey
        Write-Success "已设置用户环境变量 DeepSeekAPI"
    } else {
        Write-Warn "DeepSeekAPI 未设置。可通过以下方式设置："
        Write-Warn "  1. 重新运行此脚本"
        Write-Warn "  2. 在应用中调用 SetApiKey()"
        Write-Warn "  3. 手动设置: `$env:DeepSeekAPI = 'sk-your-key'"
    }
} else {
    $maskedKey = if ($existingApiKey.Length -gt 8) { "$($existingApiKey.Substring(0,4))****$($existingApiKey.Substring($existingApiKey.Length-4))" } else { "****" }
    Write-Success "DeepSeekAPI 已配置: $maskedKey"
}

# ──────────────────────────────────────────────
# 步骤 5：验证完整链路
# ──────────────────────────────────────────────

Write-Step "5/5  验证完整链路"

Write-Info "Python 版本:"
& "$PythonHome\python.exe" --version

Write-Info "Python 安装路径: $PythonHome"

Write-Info "检查 openai 模块:"
& "$PythonHome\python.exe" -c "import openai; print(f'openai {openai.__version__}')" 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Success "openai 模块可用"
}

# ──────────────────────────────────────────────
# 完成
# ──────────────────────────────────────────────

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Python 环境配置完成" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  PYTHONHOME  : $PythonHome" -ForegroundColor White
Write-Host "  Python 版本 : $(& "$PythonHome\python.exe" -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}')")"
Write-Host ""
Write-Host "  next step: 重新构建并运行即可" -ForegroundColor Yellow
Write-Host "    dotnet build .\FileDrivenFramework\src\FileDrivenFramework.Launcher\FileDrivenFramework.Launcher.csproj" -ForegroundColor Gray
Write-Host "    dotnet run --project .\FileDrivenFramework\src\FileDrivenFramework.Launcher -- --file DrivenConfig/Scripts/test_deepseek_chat.txt" -ForegroundColor Gray
Write-Host ""
