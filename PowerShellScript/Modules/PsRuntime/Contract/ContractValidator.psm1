# ContractValidator.psm1 — 参数与环境预校验（边界安全：null 契约容错，PS 5.1 兼容 os 判定）

function Test-PsContractParams {
    param([hashtable]$Contract, [hashtable]$Arguments)
    $errors = @()
    # null 契约边界
    if (-not $Contract) { return [PSCustomObject]@{ IsValid = $false; Errors = @("contract_null") } }
    # 无 params 视为无必填参数
    if (-not $Contract.ContainsKey("params") -or -not $Contract.params) {
        return [PSCustomObject]@{ IsValid = $true; Errors = @() }
    }
    if (-not $Arguments) { $Arguments = @{} }
    foreach ($p in $Contract.params) {
        if ($p.required -and -not $Arguments.ContainsKey($p.name)) { $errors += $p.name }
    }
    return [PSCustomObject]@{ IsValid = ($errors.Count -eq 0); Errors = $errors }
}

function Test-PsContractEnv {
    param([hashtable]$Contract)
    # null 契约边界
    if (-not $Contract) { return [PSCustomObject]@{ IsValid = $false; Errors = @("contract_null") } }
    $env = if ($Contract.ContainsKey("env_tags")) { $Contract.env_tags } else { $null }
    # 无 env_tags 视为无环境要求（通过）
    if (-not $env) { return [PSCustomObject]@{ IsValid = $true; Errors = @() } }

    $errors = @()

    # PS 5.1 无 $IsWindows 变量，Desktop 版只在 Windows 运行
    $onWindows = if (Get-Variable IsWindows -ErrorAction SilentlyContinue) { $IsWindows } else { $true }
    $currentOs = if ($onWindows) { "windows" } else { "linux" }
    # env_tags.os 是契约要求的 OS：若与当前不匹配则失败
    if ($env.os -and $env.os -ne $currentOs) { $errors += "os" }

    # 模块可用性检查
    $mods = if ($env.modules) { $env.modules } else { @() }
    foreach ($m in $mods) {
        if ($m -and -not (Get-Module -Name $m -ListAvailable -ErrorAction SilentlyContinue)) { $errors += "module:$m" }
    }

    return [PSCustomObject]@{ IsValid = ($errors.Count -eq 0); Errors = $errors }
}

Export-ModuleMember -Function Test-PsContractParams, Test-PsContractEnv
