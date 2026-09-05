# CanHandler.psm1 — 确定性边界判定（param 优先于 env 短路）
function Test-PsCanHandle {
    param([hashtable]$Contract, [hashtable]$Arguments)
    if (-not $Contract) { return [PSCustomObject]@{ CanHandle = $false; Reason = "no_contract" } }
    # Stage 1: 参数校验（短路优先）
    $p = Test-PsContractParams -Contract $Contract -Arguments $Arguments
    if (-not $p.IsValid) { return [PSCustomObject]@{ CanHandle = $false; Reason = "param_missing: $($p.Errors -join ',')" } }
    # Stage 2: 环境校验
    $e = Test-PsContractEnv -Contract $Contract
    if (-not $e.IsValid) { return [PSCustomObject]@{ CanHandle = $false; Reason = "env_unmet: $($e.Errors -join ',')" } }
    return [PSCustomObject]@{ CanHandle = $true; Reason = "ok" }
}
Export-ModuleMember -Function Test-PsCanHandle
