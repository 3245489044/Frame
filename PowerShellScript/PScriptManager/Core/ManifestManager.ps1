# .SYNOPSIS
# PScriptManager 清单管理器 — 自动生成/更新 .psd1 模块清单
# .DESCRIPTION
# 根据脚本扫描结果，自动生成 PowerShell 模块清单（.psd1）文件。
# 支持：
# - 自动生成 FunctionsToExport
# - 自动检测 RootModule
# - 版本管理
# - 依赖声明
# .NOTES
# 此模块是 PScriptManager 框架的辅助组件。

function New-ModuleManifest {
    # .SYNOPSIS
# 为指定目录生成 .psd1 模块清单
# .PARAMETER ModulePath
# 模块目录路径
# .PARAMETER Scripts
# 该目录下的脚本元数据列表
# .PARAMETER ModuleName
# 模块名称（默认使用目录名）
# .PARAMETER Version
# 模块版本（默认 1.0.0）
# .PARAMETER DryRun
# 预览模式，只显示不写入
# .EXAMPLE
# New-ModuleManifest -ModulePath ".\PowerShellScript\BasicMethod" -Scripts $scripts
    param(
        [Parameter(Mandatory = $true)]
        [string]$ModulePath,

        [Parameter(Mandatory = $true)]
        [object[]]$Scripts,

        [string]$ModuleName = "",

        [string]$Version = "1.0.0",

        [switch]$DryRun
    )

    if (-not $ModuleName) {
        $ModuleName = Split-Path $ModulePath -Leaf
    }

    # 检测根模块文件
    $rootModule = $null
    $psm1Files = $Scripts | Where-Object { $_.Extension -eq '.psm1' -and $_.RelativePath -match "$ModuleName\\.+\.psm1$" }
    if ($psm1Files) {
        $rootModule = $psm1Files[0].FileName
    }

    # 收集导出的函数
    $functionsToExport = @()
    foreach ($script in $Scripts) {
        if ($script.FileName -match '^Invoke-|^Get-|^Set-|^Remove-|^New-|^Update-|^Add-') {
            $funcName = [System.IO.Path]::GetFileNameWithoutExtension($script.FileName)
            $functionsToExport += $funcName
        }
    }

    # 生成 .psd1 内容
    $psd1Content = @"
@{
    RootModule        = '$rootModule'
    ModuleVersion     = '$Version'
    GUID              = '$([Guid]::NewGuid().ToString())'
    Author            = 'WorkFlowPrj'
    CompanyName       = 'WorkFlowPrj'
    Copyright         = '(c) $(Get-Date -Format yyyy) WorkFlowPrj. All rights reserved.'
    Description       = '$ModuleName - PowerShell 模块'
    PowerShellVersion = '5.1'
    FunctionsToExport = @('$($functionsToExport -join "', '")')
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
}
"@

    $psd1Path = Join-Path $ModulePath "$ModuleName.psd1"

    if ($DryRun) {
        Write-Host ""
        Write-Host "  📋 [预览] 将生成模块清单: $psd1Path" -ForegroundColor Yellow
        Write-Host ""
        Write-Host $psd1Content -ForegroundColor Gray
        Write-Host ""
        Write-Host "  使用 -DryRun:\$false 以实际写入" -ForegroundColor Gray
    }
    else {
        $psd1Content | Set-Content $psd1Path -Encoding UTF8
        Write-Host "  ✅ 模块清单已生成: $psd1Path" -ForegroundColor Green
    }

    return $psd1Path
}




