# @requires: Core/EnvProviders, Core/PythonVerifier
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-provider-python-verify
# @exports: Get-PythonVerifyProvider

<#
.SYNOPSIS
    python-verify Provider —— 只做能力断言（如 import comfy_kitchen），Install 为无副作用。
.DESCRIPTION
    这类项无法“安装修复”，失败即为环境根因；Repair 时 apply 成功但 verify 失败，明确暴露问题。
#>

function Get-PythonVerifyProvider {
    return (New-EnvProvider -Type 'python-verify' -Name 'python-verify Provider' `
        -Test ${function:Test-PythonVerifyItem} -Install ${function:Install-PythonVerifyItem} -Verify ${function:Verify-PythonVerifyItem})
}

function Test-PythonVerifyItem {
    param($Item, $Manifest)

    $python = Get-EnvItemPython -Item $Item -Manifest $Manifest
    if (-not (Test-Path -LiteralPath $python)) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "python 不存在: $python" }
    }
    if (-not $Item.verify) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = '未声明 verify 断言' }
    }

    $check = Test-EnvPythonExpression -Python $python -Code ([string]$Item.verify) `
        -LogPath (Get-EnvItemLogPath -Item $Item -Manifest $Manifest -Tag 'verify') -Quiet

    return [pscustomobject]@{ Installed = $check.Ok; Outdated = $false; Version = $null; Reason = $check.Reason }
}

function Install-PythonVerifyItem {
    param($Item, $Manifest)
    Write-EnvWarn "$($Item.name): 断言项无安装动作，失败需人工修复依赖"
    return $true
}

function Verify-PythonVerifyItem {
    param($Item, $Manifest)
    $test = Test-PythonVerifyItem -Item $Item -Manifest $Manifest
    return [pscustomobject]@{ Ok = $test.Installed; Reason = $test.Reason; Output = '' }
}
