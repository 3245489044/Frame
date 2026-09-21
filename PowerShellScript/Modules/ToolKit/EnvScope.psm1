<#
.SYNOPSIS
    进程级环境变量作用域（通用）：快照 → 可选写入/清除 → 退出还原。

.DESCRIPTION
    `Run-CSharpTests.ps1` 的 FD-11 收口的**单一实现**（原为该脚本内手写的 `$envEntered` / `$envRestore` +
    `Exit-Script` 还原循环）。语义与 FD-11 实测口径逐条一致：

      ① **只快照、不写**：`Enter-EnvScope -Names` 仅记录进入时的值（写入时机由调用方决定 —— 实测
         `MSBUILDDISABLENODEREUSE` 只在构建前才置 `1`）；
      ② **写入即登记还原**：`Set-EnvScopeVariable` 写入并把该名登记为「退出时还原」（重复调用幂等）；
         原本未设 → 退出时删除（不复活为残留）；
      ③ **清除不算写入**：`Clear-EnvScopeVariable` 删除并**撤销**还原登记 —— 回填会把上一次的残留复活
         （实测 `SPEC_CASE_FILTER` 残留致守卫 84/9/1 → 81/12/1 假失败）。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本件零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

function Enter-EnvScope {
    <#
    .SYNOPSIS
        进入环境变量作用域：快照 `-Names` 的当前值（不写任何变量）。
    .PARAMETER Names
        本作用域**可能触碰**的变量名（写或不写由后续调用决定，故进入即全部快照）。
    .OUTPUTS
        pscustomobject：`{ Snapshot = ordered@{ 名称 = 进入前的值 }; Restore = List[string] }`
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    $snapshot = [ordered]@{}
    foreach ($name in $Names) {
        $snapshot[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    return [pscustomobject]@{
        Snapshot = $snapshot
        Restore  = [System.Collections.Generic.List[string]]::new()
    }
}

function Set-EnvScopeVariable {
    <#
    .SYNOPSIS
        在作用域内写入变量，并登记为「退出时还原」（幂等）；进入前未设 ⇒ 退出时删除。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Scope,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value
    )

    if (-not $Scope.Snapshot.Contains($Name)) {
        throw "[EnvScope] 变量 '$Name' 未在 Enter-EnvScope -Names 中登记（无法还原，禁静默写入）"
    }

    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
    if (-not $Scope.Restore.Contains($Name)) { $Scope.Restore.Add($Name) }
}

function Clear-EnvScopeVariable {
    <#
    .SYNOPSIS
        在作用域内清除变量（反泄漏语义：**退出时也不还原**）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Scope,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not $Scope.Snapshot.Contains($Name)) {
        throw "[EnvScope] 变量 '$Name' 未在 Enter-EnvScope -Names 中登记（禁越界清除）"
    }

    [Environment]::SetEnvironmentVariable($Name, $null, 'Process')
    [void]$Scope.Restore.Remove($Name)
}

function Exit-EnvScope {
    <#
    .SYNOPSIS
        退出作用域：还原**本次写入过**的变量（快照为 `$null` → 删除）；清除项保持未设。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Scope
    )

    foreach ($name in $Scope.Restore) {
        [Environment]::SetEnvironmentVariable($name, $Scope.Snapshot[$name], 'Process')
    }
    $Scope.Restore.Clear()
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

Export-ModuleMember -Function @(
    'Enter-EnvScope', 'Set-EnvScopeVariable', 'Clear-EnvScopeVariable', 'Exit-EnvScope', 'Test-EnvScopeResidue'
)
