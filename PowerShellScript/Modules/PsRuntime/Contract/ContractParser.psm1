# ContractParser.psm1 — 动词优先标签解析 + 多契约 sidecar（PS 5.1 兼容：手动 hashtable 转换）

# PS 5.1 无 ConvertFrom-Json -AsHashtable，手动递归转换 PSCustomObject -> hashtable
function script:ConvertTo-HashtableDeep([object]$obj) {
    if ($null -eq $obj) { return $null }
    if ($obj -is [System.Array]) {
        $arr = [System.Collections.ArrayList]@()
        foreach ($e in $obj) { $null = $arr.Add((script:ConvertTo-HashtableDeep $e)) }
        return $arr.ToArray()
    }
    if ($obj -is [pscustomobject]) {
        $ht = @{}
        foreach ($p in $obj.PSObject.Properties) {
            $ht[$p.Name] = script:ConvertTo-HashtableDeep $p.Value
        }
        return $ht
    }
    return $obj
}

function Get-PsScriptTags {
    param([Parameter(Mandatory)][string]$Path)
    $tags = @{}
    foreach ($line in (Get-Content $Path -TotalCount 30 -Encoding UTF8 -ErrorAction SilentlyContinue)) {
        if ($line -match '^\s*#\s*@([\w-]+):\s*(.+?)\s*$') { $tags[$Matches[1]] = $Matches[2].Trim() }
    }
    return $tags
}

function Get-PsContracts {
    param([Parameter(Mandatory)][string]$ScriptPath)
    $fileName = Split-Path $ScriptPath -Leaf
    $sidecar = Join-Path (Split-Path $ScriptPath -Parent) ($fileName -replace '\.(ps1|psm1)$', '.contracts.json')
    if (Test-Path $sidecar) {
        # 畸形 JSON 会让 ConvertFrom-Json 抛异常（边界：让调用方感知）
        $data = Get-Content $sidecar -Raw -Encoding UTF8 | ConvertFrom-Json
        $ht = script:ConvertTo-HashtableDeep $data
        if ($ht -and $ht.ContainsKey("contracts")) {
            $cs = @($ht.contracts)
            # 保留空数组（PowerShell 默认会 unwrap 空 array 为 null）
            return , $cs
        }
        return , @()
    }
    # 无 sidecar：从标签派生单契约
    $t = Get-PsScriptTags -Path $ScriptPath
    if (-not $t.ContainsKey('ps-action')) { return , @() }
    $action = $t.'ps-action'
    $parts = $action -split '\.'
    $fn = if ($t.ContainsKey('exports')) { ($t.exports -split ',')[0].Trim() } else { "" }
    $derived = @{
        identity = @{
            action = $action
            verb = if ($t.ContainsKey('verb')) { $t.verb } else { $parts[0] }
            object = if ($t.ContainsKey('object')) { $t.object } else { ($parts | Select-Object -Skip 1) -join '.' }
            purpose = if ($t.ContainsKey('description')) { $t.description } else { "" }
            function = $fn; path = $fileName
        }
        params = @()
        output_schema = @{ status = "success|failure"; message = "string"; data = "object"; errors = "array" }
        env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() }
    }
    return , @($derived)
}

Export-ModuleMember -Function Get-PsScriptTags, Get-PsContracts
