<#
.SYNOPSIS
    包管理器通用操作模块 — 提供 npm/pip 包的安装、检查、版本查询等通用函数

.DESCRIPTION
    从 Install-McpServer.ps1 中提取的纯通用包管理函数。
    不依赖任何项目特定常量（容器名、路径等由调用方传入）。
    支持在本地或远程（Docker 容器）环境中执行操作。

    包含函数:
        Test-CommandExists          — 检查命令是否存在
        Invoke-NpmInstall           — npm 安装包
        Invoke-PipInstall           — pip 安装包
        Test-NpmPackageInstalled    — 检查 npm 包是否已安装
        Test-PipPackageInstalled    — 检查 pip 包是否已安装
        Get-NpmPackageVersion       — 获取 npm 包版本
        Get-PipPackageVersion       — 获取 pip 包版本
        Invoke-NpmUninstall         — npm 卸载包
        Invoke-PipUninstall         — pip 卸载包
        Invoke-NpmUpdate            — npm 更新包
        Invoke-PipUpdate            — pip 更新包

    用法:
        Import-Module .\BasicMethod-PackageManager.psm1
        if (Test-NpmPackageInstalled -Package "@anthropic/mcp-sequentialthinking-tools" -Global) { ... }
        Invoke-NpmInstall -Package "@context7/context7-mcp" -Global
        $version = Get-PipPackageVersion -Package "token-savior-recall"
#>

# ============================================
# 环境检查
# ============================================

function Test-CommandExists {
    <#
    .SYNOPSIS
        检查指定命令是否存在于 PATH 中
    .PARAMETER Command
        命令名称
    .OUTPUTS
        [bool] 存在返回 $true，否则 $false
    .EXAMPLE
        if (Test-CommandExists "node") { "Node.js 已安装" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command
    )
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

# ============================================
# npm 操作函数
# ============================================

function Invoke-NpmInstall {
    <#
    .SYNOPSIS
        安装 npm 包
    .PARAMETER Package
        包名（可含版本，如 "@scope/pkg@1.0.0"）
    .PARAMETER Global
        全局安装（默认 $false）
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令。
        格式: { param($cmd) & docker exec $ContainerName sh -c $cmd }
        不传则在本地执行。
    .PARAMETER WhatIf
        仅预览，不实际执行
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Invoke-NpmInstall -Package "@anthropic/mcp-sequentialthinking-tools" -Global
    .EXAMPLE
        Invoke-NpmInstall -Package "express" -Executor { param($cmd) docker exec mycontainer sh -c $cmd }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [switch]$Global,

        [scriptblock]$Executor = $null,

        [switch]$WhatIf
    )

    $installCmd = if ($Global) {
        "npm install -g $Package 2>&1"
    } else {
        "npm install $Package 2>&1"
    }

    if ($WhatIf) {
        Write-Host "  [WhatIf] npm install: $Package" -ForegroundColor Magenta
        return $true
    }

    try {
        if ($Executor) {
            $result = & $Executor $installCmd
        } else {
            $result = Invoke-Expression $installCmd
        }
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Test-NpmPackageInstalled {
    <#
    .SYNOPSIS
        检查 npm 包是否已安装
    .PARAMETER Package
        包名
    .PARAMETER Global
        是否全局安装
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .OUTPUTS
        [bool] 已安装返回 $true，否则 $false
    .EXAMPLE
        if (Test-NpmPackageInstalled -Package "@anthropic/mcp-sequentialthinking-tools" -Global) { "已安装" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [switch]$Global,

        [scriptblock]$Executor = $null
    )

    $checkCmd = if ($Global) {
        "npm list -g --depth=0 2>/dev/null"
    } else {
        "npm list --depth=0 2>/dev/null"
    }

    try {
        if ($Executor) {
            $result = & $Executor $checkCmd
        } else {
            $result = Invoke-Expression $checkCmd
        }
        return ($LASTEXITCODE -eq 0 -and $result -match [regex]::Escape($Package))
    } catch {
        return $false
    }
}

function Get-NpmPackageVersion {
    <#
    .SYNOPSIS
        获取已安装的 npm 包版本号
    .PARAMETER Package
        包名
    .PARAMETER Global
        是否全局安装
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .OUTPUTS
        [string] 版本号，失败返回 $null
    .EXAMPLE
        $ver = Get-NpmPackageVersion -Package "@anthropic/mcp-sequentialthinking-tools" -Global
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [switch]$Global,

        [scriptblock]$Executor = $null
    )

    $checkCmd = if ($Global) {
        "npm list -g $Package --depth=0 2>/dev/null"
    } else {
        "npm list $Package --depth=0 2>/dev/null"
    }

    try {
        if ($Executor) {
            $result = & $Executor $checkCmd
        } else {
            $result = Invoke-Expression $checkCmd
        }
        if ($result -match '@([\d.]+)') {
            return $matches[1]
        }
    } catch {}
    return $null
}

function Invoke-NpmUninstall {
    <#
    .SYNOPSIS
        卸载 npm 包
    .PARAMETER Package
        包名
    .PARAMETER Global
        是否全局安装
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Invoke-NpmUninstall -Package "@anthropic/mcp-sequentialthinking-tools" -Global
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [switch]$Global,

        [scriptblock]$Executor = $null
    )

    $uninstallCmd = if ($Global) {
        "npm uninstall -g $Package 2>&1"
    } else {
        "npm uninstall $Package 2>&1"
    }

    try {
        if ($Executor) {
            $result = & $Executor $uninstallCmd
        } else {
            $result = Invoke-Expression $uninstallCmd
        }
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Invoke-NpmUpdate {
    <#
    .SYNOPSIS
        更新 npm 包
    .PARAMETER Package
        包名
    .PARAMETER Global
        是否全局安装
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Invoke-NpmUpdate -Package "@anthropic/mcp-sequentialthinking-tools" -Global
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [switch]$Global,

        [scriptblock]$Executor = $null
    )

    $updateCmd = if ($Global) {
        "npm update -g $Package 2>&1"
    } else {
        "npm update $Package 2>&1"
    }

    try {
        if ($Executor) {
            $result = & $Executor $updateCmd
        } else {
            $result = Invoke-Expression $updateCmd
        }
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

# ============================================
# pip 操作函数
# ============================================

function Invoke-PipInstall {
    <#
    .SYNOPSIS
        安装 pip 包
    .PARAMETER Package
        包名（可含版本，如 "package==1.0.0"）
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .PARAMETER WhatIf
        仅预览，不实际执行
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Invoke-PipInstall -Package "token-savior-recall"
    .EXAMPLE
        Invoke-PipInstall -Package "requests" -Executor { param($cmd) docker exec mycontainer sh -c $cmd }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [scriptblock]$Executor = $null,

        [switch]$WhatIf
    )

    $pipCmd = if (Test-CommandExists "pip3") { "pip3" } else { "pip" }
    $installCmd = "$pipCmd install $Package 2>&1"

    if ($WhatIf) {
        Write-Host "  [WhatIf] pip install: $Package" -ForegroundColor Magenta
        return $true
    }

    try {
        if ($Executor) {
            $result = & $Executor $installCmd
        } else {
            $result = Invoke-Expression $installCmd
        }
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Test-PipPackageInstalled {
    <#
    .SYNOPSIS
        检查 pip 包是否已安装
    .PARAMETER Package
        包名
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .OUTPUTS
        [bool] 已安装返回 $true，否则 $false
    .EXAMPLE
        if (Test-PipPackageInstalled -Package "token-savior-recall") { "已安装" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [scriptblock]$Executor = $null
    )

    $pipCmd = if (Test-CommandExists "pip3") { "pip3" } else { "pip" }
    $checkCmd = "$pipCmd list 2>/dev/null"

    try {
        if ($Executor) {
            $result = & $Executor $checkCmd
        } else {
            $result = Invoke-Expression $checkCmd
        }
        return ($result -match [regex]::Escape($Package))
    } catch {
        return $false
    }
}

function Get-PipPackageVersion {
    <#
    .SYNOPSIS
        获取已安装的 pip 包版本号
    .PARAMETER Package
        包名
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .OUTPUTS
        [string] 版本号，失败返回 $null
    .EXAMPLE
        $ver = Get-PipPackageVersion -Package "token-savior-recall"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [scriptblock]$Executor = $null
    )

    $pipCmd = if (Test-CommandExists "pip3") { "pip3" } else { "pip" }
    $checkCmd = "$pipCmd show $Package 2>/dev/null | grep Version"

    try {
        if ($Executor) {
            $result = & $Executor $checkCmd
        } else {
            $result = Invoke-Expression $checkCmd
        }
        if ($result -match 'Version:\s*([\d.]+)') {
            return $matches[1]
        }
    } catch {}
    return $null
}

function Invoke-PipUninstall {
    <#
    .SYNOPSIS
        卸载 pip 包
    .PARAMETER Package
        包名
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Invoke-PipUninstall -Package "token-savior-recall"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [scriptblock]$Executor = $null
    )

    $pipCmd = if (Test-CommandExists "pip3") { "pip3" } else { "pip" }
    $uninstallCmd = "$pipCmd uninstall -y $Package 2>&1"

    try {
        if ($Executor) {
            $result = & $Executor $uninstallCmd
        } else {
            $result = Invoke-Expression $uninstallCmd
        }
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Invoke-PipUpdate {
    <#
    .SYNOPSIS
        更新 pip 包
    .PARAMETER Package
        包名
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Invoke-PipUpdate -Package "token-savior-recall"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Package,

        [scriptblock]$Executor = $null
    )

    $pipCmd = if (Test-CommandExists "pip3") { "pip3" } else { "pip" }
    $updateCmd = "$pipCmd install --upgrade $Package 2>&1"

    try {
        if ($Executor) {
            $result = & $Executor $updateCmd
        } else {
            $result = Invoke-Expression $updateCmd
        }
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

# ============================================
# 模块导出
# ============================================

Export-ModuleMember -Function @(
    'Test-CommandExists',
    'Invoke-NpmInstall',
    'Invoke-PipInstall',
    'Test-NpmPackageInstalled',
    'Test-PipPackageInstalled',
    'Get-NpmPackageVersion',
    'Get-PipPackageVersion',
    'Invoke-NpmUninstall',
    'Invoke-PipUninstall',
    'Invoke-NpmUpdate',
    'Invoke-PipUpdate'
)
