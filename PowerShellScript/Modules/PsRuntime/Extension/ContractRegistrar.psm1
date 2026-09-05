# ContractRegistrar.psm1 — 注册入库（幂等，PS 5.1 兼容：手动 hashtable 转换）
# 边界: null contract / 缺 identity / 缺 function / 文件不存在自动创建 / 畸形 JSON

function script:ConvertTo-HashtableDeep([object]$obj) {
    if ($null -eq $obj) { return $null }
    if ($obj -is [System.Array]) {
        $arr = [System.Collections.ArrayList]@()
        foreach ($e in $obj) { $null = $arr.Add((script:ConvertTo-HashtableDeep $e)) }
        return $arr.ToArray()
    }
    if ($obj -is [pscustomobject]) {
        $ht = @{}
        foreach ($p in $obj.PSObject.Properties) { $ht[$p.Name] = script:ConvertTo-HashtableDeep $p.Value }
        return $ht
    }
    return $obj
}

function Register-PsContract {
    param([hashtable]$Contract, [string]$RegistryPath)
    try {
        # 边界: contract 为 null
        if (-not $Contract) { return New-PsResult -Status failure -Message "contract null" -Errors @("contract_null") }
        # 边界: identity 缺失
        if (-not $Contract.ContainsKey("identity") -or -not $Contract.identity) {
            return New-PsResult -Status failure -Message "identity missing" -Errors @("identity_missing")
        }
        $id = $Contract.identity
        # 边界: function 缺失
        if (-not $id.ContainsKey("function") -or -not $id.function) {
            return New-PsResult -Status failure -Message "function missing" -Errors @("function_missing")
        }

        # 加载注册表（文件不存在自动创建空 hashtable）
        if (Test-Path $RegistryPath) {
            $data = Get-Content $RegistryPath -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            $reg = script:ConvertTo-HashtableDeep $data
        } else {
            $reg = @{}
        }
        if (-not $reg -or -not $reg.ContainsKey("Functions")) { $reg["Functions"] = @{} }

        $fn = $id.function
        # 幂等：同键覆盖
        $reg["Functions"][$fn] = @{
            Version = ""
            Description = if ($id.ContainsKey("purpose")) { $id.purpose } else { "" }
            Alias = if ($id.ContainsKey("action")) { $id.action } else { "" }
            Path = if ($id.ContainsKey("path")) { $id.path } else { "" }
            Author = "PsRuntime"
            Category = if ($id.ContainsKey("object")) { $id.object } else { "" }
        }

        $reg | ConvertTo-Json -Depth 6 | Set-Content -Path $RegistryPath -Encoding UTF8 -ErrorAction Stop
        return New-PsResult -Status success -Message "registered" -Data @{ function = $fn }
    } catch {
        return New-PsResult -Status failure -Message "register error" -Errors @($_.Exception.Message)
    }
}

Export-ModuleMember -Function Register-PsContract
