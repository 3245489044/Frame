<#
.SYNOPSIS
    Rust 安装脚本 — 通过 Install Framework 安装 Rust 工具链
.DESCRIPTION
    使用 Install-Tool 框架安装 Rust 工具链到 D:\DevEnv\Rust
    安装路径：
    - RUSTUP_HOME: D:\DevEnv\Rust\.rustup
    - CARGO_HOME: D:\DevEnv\Rust\.cargo

    通过 PScriptManager 调用：
        Invoke-Function -Name 'Install-Rust' -Arguments @{DevEnvRoot='D:\DevEnv'}
.PARAMETER DevEnvRoot
    开发环境根目录，默认为 D:\DevEnv
.PARAMETER CheckOnly
    仅检查环境，不执行安装
.PARAMETER Force
    强制执行安装，不询问确认
.EXAMPLE
    Install-Rust
    Install-Rust -DevEnvRoot "E:\DevEnv"
    Install-Rust -CheckOnly
#>
# @requires: Install/Core/Config, Install/Core/Executor, Install/Core/Pipeline
# @version: 1.1.0
# @author: WorkFlowPrj
# @category: install
# @alias: rust-install
# @exports: Install-Rust

function Install-Rust {
    <#
    .SYNOPSIS
        安装 Rust 工具链
    .PARAMETER DevEnvRoot
        开发环境根目录，默认为 D:\DevEnv
    .PARAMETER CheckOnly
        仅检查环境，不执行安装
    .PARAMETER Force
        强制执行安装，不询问确认
    .EXAMPLE
        Install-Rust
        Install-Rust -DevEnvRoot "E:\DevEnv"
        Install-Rust -CheckOnly
    #>
    param(
        [string]$DevEnvRoot = "D:\DevEnv",
        [switch]$CheckOnly,
        [switch]$Force
    )

    # ============================================================
    # 初始化 — 确保 Install Core 模块已加载
    # ============================================================

    $installCoreLoaded = (Get-Command 'Load-InstallManifest' -ErrorAction SilentlyContinue) -ne $null
    if (-not $installCoreLoaded) {
        $installRoot = Join-Path $global:PScriptManagerProjectRoot "PowerShellScript\Install"
        $coreModules = @(
            'Core\Config.ps1',
            'Core\Executor.ps1',
            'Core\Pipeline.ps1'
        )
        foreach ($module in $coreModules) {
            $modulePath = Join-Path $installRoot $module
            if (Test-Path $modulePath) {
                . $modulePath
            }
        }
    }

    # ============================================================
    # 主逻辑
    # ============================================================

    Write-Banner -Title "🦀 Rust 安装工具" -Color "Green"

    $rustupHome = Join-Path $DevEnvRoot "Rust\.rustup"
    $cargoHome = Join-Path $DevEnvRoot "Rust\.cargo"
    $cargoBin = Join-Path $cargoHome "bin"

    if ($CheckOnly) {
        Write-Host "`n  [检查模式] 仅显示环境状态" -ForegroundColor Cyan
        Write-Host "`n  环境变量状态:" -ForegroundColor White
        Write-Host "    RUSTUP_HOME: $(if ($env:RUSTUP_HOME) { $env:RUSTUP_HOME } else { "未设置" })" -ForegroundColor Gray
        Write-Host "    CARGO_HOME: $(if ($env:CARGO_HOME) { $env:CARGO_HOME } else { "未设置" })" -ForegroundColor Gray
        Write-Host "    CARGO_HOME\bin in PATH: $(if ($env:PATH -like "*$cargoBin*") { "是" } else { "否" })" -ForegroundColor Gray
        Write-Host "`n  目标目录状态:" -ForegroundColor White
        Write-Host "    ${rustupHome}: $(if (Test-Path $rustupHome) { "已存在" } else { "不存在" })" -ForegroundColor Gray
        Write-Host "    ${cargoHome}: $(if (Test-Path $cargoHome) { "已存在" } else { "不存在" })" -ForegroundColor Gray
        
        if (Get-Command "rustc" -ErrorAction SilentlyContinue) {
            Write-Host "`n  ⚠️ 检测到已安装 Rust" -ForegroundColor Yellow
            Write-Host "    rustc version: $(rustc --version)" -ForegroundColor Gray
        }
        
        return
    }

    $manifest = Load-InstallManifest -ManifestName "rust.json"
    if (-not $manifest) {
        Write-Error "无法加载 rust.json 清单"
        return
    }

    $manifest.items[0].installDir = Join-Path $DevEnvRoot "Rust"

    $provider = Load-Provider -Type "rustup"
    if (-not $provider) {
        return
    }

    $executor = Get-Executor -Target "local"
    $result = Invoke-InstallPipeline -Items $manifest.items -Provider $provider -Target "local" -Force:$Force -Executor $executor

    if ($result.Failed -gt 0) {
        return $false
    }

    Write-Host ""
    Write-Host "══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "  🦀 Rust 安装完成！" -ForegroundColor Green
    Write-Host "══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Host "  安装路径:" -ForegroundColor White
    Write-Host "    RUSTUP_HOME: $rustupHome" -ForegroundColor Gray
    Write-Host "    CARGO_HOME:  $cargoHome" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  后续操作:" -ForegroundColor White
    Write-Host "    1. 重启终端以加载新环境变量" -ForegroundColor Gray
    Write-Host "    2. 在 VS Code 中安装 rust-analyzer 扩展" -ForegroundColor Gray
    Write-Host "    3. 创建测试项目: cargo new mdpp-test" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  验证命令:" -ForegroundColor White
    Write-Host "    rustc --version" -ForegroundColor Gray
    Write-Host "    cargo --version" -ForegroundColor Gray

    return $true
}

# 当直接执行脚本时（非 dot-source），调用函数
if ($MyInvocation.InvocationName -eq $null -or $MyInvocation.InvocationName -eq '') {
    $splat = @{}
    if ($args.Count -gt 0) {
        for ($i = 0; $i -lt $args.Count; $i += 2) {
            $key = $args[$i] -replace '^-', ''
            $value = $args[$i + 1]
            $splat[$key] = $value
        }
    }
    Install-Rust @splat
}
