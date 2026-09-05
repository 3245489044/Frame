# @requires: BasicMethod/Core/Output, BasicMethod/Core/Json
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install
# @alias: install-pipeline
# @exports: Invoke-InstallPipeline, Test-InstallItem, Get-InstallItemVersion, Write-InstallSummary

<#
.SYNOPSIS
    Install Framework 安装管道编排引擎
.DESCRIPTION
    提供通用的安装管道编排逻辑：循环遍历安装项、检测已安装状态、跳过已安装项、
    支持强制安装、WhatIf 预览模式、输出安装摘要。

    设计模式：Pipeline + Provider
    Pipeline 负责流程编排，Provider 负责具体安装实现。
.NOTES
    依赖：BasicMethod/Core/Output, BasicMethod/Core/Json
#>

function Invoke-InstallPipeline {
    <#
    .SYNOPSIS
        执行安装管道
    .DESCRIPTION
        遍历安装项列表，对每个项执行：检测状态 → 决定是否安装 → 调用 Provider 安装 → 验证结果
    .PARAMETER Items
        安装项列表（从 manifest.json 加载的数组）
    .PARAMETER Provider
        Provider 脚本块，必须实现：Test-Installed, Get-Version, Invoke-Install
    .PARAMETER Target
        安装目标：local | docker
    .PARAMETER ContainerName
        Docker 容器名称（Target=docker 时必填）
    .PARAMETER Force
        强制重新安装（跳过已安装检查）
    .PARAMETER WhatIf
        仅预览，不实际安装
    .PARAMETER Executor
        执行器脚本块（用于 Docker 环境）
    .EXAMPLE
        $provider = Get-NpmProvider
        Invoke-InstallPipeline -Items $items -Provider $provider -Target local
    .EXAMPLE
        Invoke-InstallPipeline -Items $items -Provider $provider -Target docker -ContainerName "workflow-mcp-env" -Force
    #>
    param(
        [Parameter(Mandatory = $true)]
        [array]$Items,

        [Parameter(Mandatory = $true)]
        [hashtable]$Provider,

        [ValidateSet("local", "docker")]
        [string]$Target = "local",

        [string]$ContainerName = "",

        [switch]$Force,
        [switch]$WhatIf,

        [scriptblock]$Executor = $null
    )

    $installResult = @{
        Total      = $Items.Count
        Success    = 0
        Failed     = 0
        Skipped    = 0
        Items      = @()
    }

    Write-Banner -Title "安装管道" -Subtitle "目标: $Target" -Color "Cyan"

    if ($Target -eq "docker") {
        if (-not $ContainerName) {
            Write-Error "Docker 目标需要指定 ContainerName 参数"
            return $installResult
        }
        Write-Info "容器名称: $ContainerName"
    }

    Write-Info "将安装 $($Items.Count) 个项目"
    Write-Host ""

    foreach ($item in $Items) {
        $itemResult = @{
            Name    = $item.name
            Type    = $item.type
            Status  = "unknown"
            Message = ""
            Version = $null
        }

        Write-Section "安装 $($item.name)"

        $installed = Test-InstallItem -Item $item -Provider $Provider -Target $Target -ContainerName $ContainerName -Executor $Executor
        
        if ($installed -and -not $Force) {
            $version = Get-InstallItemVersion -Item $item -Provider $Provider -Target $Target -ContainerName $ContainerName -Executor $Executor
            $versionStr = if ($version) { " (v$version)" } else { "" }
            Write-Warn "$($item.name) 已安装$versionStr，跳过（使用 -Force 强制重新安装）"
            $itemResult.Status = "skipped"
            $itemResult.Message = "已安装$versionStr"
            $itemResult.Version = $version
            $installResult.Skipped++
            $installResult.Items += $itemResult
            continue
        }

        Write-Info "安装类型: $($item.type)"
        Write-Info "源: $($item.source)"

        if ($WhatIf) {
            Write-WhatIf "将安装 $($item.name) ($($item.source))"
            $itemResult.Status = "whatif"
            $itemResult.Message = "预览模式"
            $installResult.Skipped++
            $installResult.Items += $itemResult
            continue
        }

        $invokeResult = Invoke-ProviderInstall -Item $item -Provider $Provider -Target $Target -ContainerName $ContainerName -Executor $Executor

        if ($invokeResult) {
            $version = Get-InstallItemVersion -Item $item -Provider $Provider -Target $Target -ContainerName $ContainerName -Executor $Executor
            Write-Success "$($item.name) 安装成功"
            if ($version) {
                Write-Info "安装版本: $version"
            }
            $itemResult.Status = "success"
            $itemResult.Message = "安装成功"
            $itemResult.Version = $version
            $installResult.Success++
        } else {
            Write-Error "$($item.name) 安装失败"
            $itemResult.Status = "failed"
            $itemResult.Message = "安装失败"
            $installResult.Failed++
        }

        $installResult.Items += $itemResult

        Write-Host ""
    }

    Write-InstallSummary -Result $installResult

    return $installResult
}

function Test-InstallItem {
    <#
    .SYNOPSIS
        检测安装项是否已安装
    .PARAMETER Item
        安装项对象
    .PARAMETER Provider
        Provider 哈希表
    .PARAMETER Target
        安装目标
    .PARAMETER ContainerName
        Docker 容器名称
    .PARAMETER Executor
        执行器脚本块
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,

        [Parameter(Mandatory = $true)]
        [hashtable]$Provider,

        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    if (-not $Provider['Test-Installed']) {
        Write-Warn "Provider 未实现 Test-Installed 方法"
        return $false
    }

    try {
        $result = & $Provider['Test-Installed'] -Item $Item -Target $Target -ContainerName $ContainerName -Executor $Executor
        return $result
    } catch {
        Write-Warn "检测已安装状态失败: $_"
        return $false
    }
}

function Get-InstallItemVersion {
    <#
    .SYNOPSIS
        获取已安装版本
    .PARAMETER Item
        安装项对象
    .PARAMETER Provider
        Provider 哈希表
    .PARAMETER Target
        安装目标
    .PARAMETER ContainerName
        Docker 容器名称
    .PARAMETER Executor
        执行器脚本块
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,

        [Parameter(Mandatory = $true)]
        [hashtable]$Provider,

        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    if (-not $Provider['Get-Version']) {
        return $null
    }

    try {
        $result = & $Provider['Get-Version'] -Item $Item -Target $Target -ContainerName $ContainerName -Executor $Executor
        return $result
    } catch {
        return $null
    }
}

function Invoke-ProviderInstall {
    <#
    .SYNOPSIS
        调用 Provider 执行安装
    .PARAMETER Item
        安装项对象
    .PARAMETER Provider
        Provider 哈希表
    .PARAMETER Target
        安装目标
    .PARAMETER ContainerName
        Docker 容器名称
    .PARAMETER Executor
        执行器脚本块
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,

        [Parameter(Mandatory = $true)]
        [hashtable]$Provider,

        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    if (-not $Provider['Invoke-Install']) {
        Write-Error "Provider 未实现 Invoke-Install 方法"
        return $false
    }

    try {
        $result = & $Provider['Invoke-Install'] -Item $Item -Target $Target -ContainerName $ContainerName -Executor $Executor
        return $result
    } catch {
        Write-Error "Provider 安装执行失败: $_"
        return $false
    }
}

function Write-InstallSummary {
    <#
    .SYNOPSIS
        输出安装摘要
    .PARAMETER Result
        安装结果对象（来自 Invoke-InstallPipeline 的返回值）
    #>
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Result
    )

    Write-Host ""
    Write-Banner -Title "安装摘要" -Color "Cyan"

    Write-Host "  总计: $($Result.Total)" -ForegroundColor White
    Write-Host "  成功: $($Result.Success)" -ForegroundColor Green
    if ($Result.Failed -gt 0) {
        Write-Host "  失败: $($Result.Failed)" -ForegroundColor Red
    }
    if ($Result.Skipped -gt 0) {
        Write-Host "  跳过: $($Result.Skipped)" -ForegroundColor Yellow
    }

    if ($Result.Items.Count -gt 0) {
        Write-Host ""
        Write-Host "  详细结果:" -ForegroundColor Yellow
        foreach ($item in $Result.Items) {
            $statusColor = switch ($item.Status) {
                "success" { "Green" }
                "failed"  { "Red" }
                "skipped" { "Yellow" }
                "whatif"  { "Magenta" }
                default   { "Gray" }
            }
            $statusIcon = switch ($item.Status) {
                "success" { "✅" }
                "failed"  { "❌" }
                "skipped" { "⏭️" }
                "whatif"  { "📋" }
                default   { "❓" }
            }
            $versionStr = if ($item.Version) { " v$($item.Version)" } else { "" }
            Write-Host "    $statusIcon $($item.Name)$versionStr" -ForegroundColor $statusColor
        }
    }

    Write-Host ""
}

if ($MyInvocation.MyCommand.Source -eq $null) {
    Export-ModuleMember -Function @('Invoke-InstallPipeline', 'Test-InstallItem', 'Get-InstallItemVersion', 'Write-InstallSummary')
}