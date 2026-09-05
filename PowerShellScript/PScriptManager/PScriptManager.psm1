<#
.SYNOPSIS
    PScriptManager — PowerShell 脚本管理框架
.DESCRIPTION
    统一管理 WorkFlowPrj 项目中所有 PowerShell 脚本的工具框架。
    
    核心能力：
    1. 自动扫描 — 递归扫描 .ps1/.psm1 文件，解析注释元数据
    2. 自动注册 — 将扫描结果自动更新到 launcher.config.json
    3. 清单生成 — 自动生成 .psd1 模块清单
    4. 依赖解析 — 声明式依赖管理
    5. 脚手架 — 新建模块/脚本模板

    使用方式：
        # 加载框架
        . .\PScriptManager\_init.ps1

        # 扫描脚本
        Invoke-ScriptScan -Path ".\PowerShellScript"

        # 注册到配置
        Register-ScriptsToConfig -Scripts $scripts -ProjectRoot (Get-Location)

.NOTES
    版本: 1.0.0
    作者: WorkFlowPrj
    依赖: ScriptScanner, ConfigGenerator, ManifestManager
#>

# ============================================================
# 加载子模块（dot-source 方式确保函数在当前作用域可用）
# ============================================================

$moduleDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# 按依赖顺序加载核心模块
$coreModules = @(
    'Core\ScriptScanner.psm1'
    'Core\ConfigGenerator.psm1'
    'Core\ManifestManager.psm1'
    'Core\ScriptRegistry.ps1'
)

foreach ($module in $coreModules) {
    $modulePath = Join-Path $moduleDir $module
    if (Test-Path $modulePath) {
        # 使用 dot-source 确保函数在当前 session 可用
        . $modulePath
    }
}


# ============================================================
# 框架版本信息
# ============================================================

$script:PScriptManagerVersion = "1.0.0"

function Get-PScriptManagerVersion {
    <#
    .SYNOPSIS
        获取 PScriptManager 框架版本
    #>
    return $script:PScriptManagerVersion
}

function Get-PScriptManagerInfo {
    <#
    .SYNOPSIS
        获取 PScriptManager 框架信息
    #>
    $info = [PSCustomObject]@{
        Name        = "PScriptManager"
        Version     = $script:PScriptManagerVersion
        Description = "PowerShell 脚本管理框架"
        Modules     = @('ScriptScanner', 'ConfigGenerator', 'ManifestManager')
        Commands    = @(
            'Scan-Scripts.ps1',
            'Register-Script.ps1'
        )
    }
    return $info
}

# 导出公开函数
Export-ModuleMember -Function @(
    'Get-PScriptManagerVersion',
    'Get-PScriptManagerInfo'
)
