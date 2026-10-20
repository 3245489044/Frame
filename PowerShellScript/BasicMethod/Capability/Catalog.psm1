# Catalog.psm1 — 能力目录（契约集投影；LLM 唯一能力发现入口）
# 单一职责：把已扫描契约投影为 {action, purpose, params, sourceFile}；不做执行、不做写盘。

function Get-CapabilityCatalog {
    param(
        [AllowNull()][array]$Contracts,
        [string]$Filter = ''
    )

    $items = @()
    foreach ($c in @($Contracts)) {
        if ($null -eq $c -or $null -eq $c.identity) { continue }
        $action = [string]$c.identity.action
        if ([string]::IsNullOrWhiteSpace($action)) { continue }
        if (-not [string]::IsNullOrWhiteSpace($Filter) -and $action -notmatch $Filter) { continue }

        $params = @()
        foreach ($p in @($c.params)) {
            if ($null -eq $p) { continue }
            $params += [PSCustomObject]@{
                name     = [string]$p.name
                type     = [string]$p.type
                required = [bool]$p.required
            }
        }

        $items += [PSCustomObject]@{
            action     = $action
            verb       = [string]$c.identity.verb
            object     = [string]$c.identity.object
            purpose    = [string]$c.identity.purpose
            function   = [string]$c.identity.function
            sourceFile = [string]$c.identity.path
            requiresIdempotency = [bool]$c.requires_idempotency
            params     = $params
        }
    }

    return New-PsResult -Status success -Message 'catalog' -Data @{
        count     = $items.Count
        contracts = @($items | Sort-Object action)
    }
}

Export-ModuleMember -Function Get-CapabilityCatalog
