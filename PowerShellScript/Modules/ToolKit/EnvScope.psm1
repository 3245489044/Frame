<#
.SYNOPSIS
    进程级环境变量作用域（通用）：快照 → 写入 → 还原；未指定即**清除**的反泄漏语义。

.DESCRIPTION
    消 D-3 的 3 处手写（`Run-CSharpTests.ps1` 的 FD-11 收口 · `Tools\Update-SpecGolden.ps1` 的
    `PORT_GOLDEN_UPDATE` finally 还原 · 同族工具），并保留 FD-11 实测踩坑的两条口径：

      ① **还原只针对「本次显式写入」的变量**：原本未设 → 退出时删除（不复活）；
      ② **「清除」不算写入**：`-Clear` 指定的变量在退出时**不回填**（回填会把上一次的残留复活 ——
         实测 `SPEC_CASE_FILTER` 残留致守卫 84/9/1 → 81/12/1 假失败）。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本件零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

function Enter-EnvScope {
    <#
    .SYNOPSIS
        进入环境变量作用域：先按 `-Clear` 清除、再按 `-Set` 写入；返回还原凭据。
    .PARAMETER Clear
        进入时清除（置空 = 删除）的变量名。
    .PARAMETER Set
        进入时写入的 `名称 → 值`；**仅这些项在退出时还原**。
    .OUTPUTS
        pscustomobject：`{ Restore = @{ 名称 = 进入前的值 } }`（原值 `$null` = 进入前未设）。
    #>
    [CmdletBinding()]
    param(
        [string[]]$Clear = @(),
        [hashtable]$Set = @{}
    )

    $restore = [ordered]@{}
    foreach ($name in $Set.Keys) {
        $restore[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    foreach ($name in $Clear) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    foreach ($name in $Set.Keys) { [Environment]::SetEnvironmentVariable($name, [string]$Set[$name], 'Process') }

    return [pscustomobject]@{ Restore = $restore }
}

function Exit-EnvScope {
    <#
    .SYNOPSIS
        退出环境变量作用域：还原**本次显式写入**的变量（原值 `$null` → 删除）。
    .DESCRIPTION
        `-Clear` 项不在还原集内（反泄漏语义，见模块说明 ②）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Scope
    )

    if ($null -eq $Scope -or $null -eq $Scope.Restore) { return }
    foreach ($name in $Scope.Restore.Keys) {
        [Environment]::SetEnvironmentVariable($name, $Scope.Restore[$name], 'Process')
    }
}

function Test-EnvScopeResidue {
    <#
    .SYNOPSIS
        判定变量是否仍有残留（跨次污染判据：退出作用域后应全为「未设」）。
    .OUTPUTS
        仍有值的变量名（空数组 = 无残留）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    $residue = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $Names) {
        $value = [Environment]::GetEnvironmentVariable($name, 'Process')
        if (-not [string]::IsNullOrEmpty($value)) { $residue.Add($name) }
    }
    return , @($residue)
}

Export-ModuleMember -Function @('Enter-EnvScope', 'Exit-EnvScope', 'Test-EnvScopeResidue')
