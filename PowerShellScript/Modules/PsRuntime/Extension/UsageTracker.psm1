# UsageTracker.psm1 — 用量累计 + 升级阈值（PS 5.1 兼容：手动 hashtable 转换）
# 边界: Action 空 / 文件不存在 / 窗口外记录 / 未记录 Action

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

function _Load-Usage {
    param([string]$StorePath)
    if (Test-Path $StorePath) {
        try {
            $data = Get-Content $StorePath -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            $ht = script:ConvertTo-HashtableDeep $data
            if (-not $ht -or -not $ht.ContainsKey("records")) { return @{ records = @{} } }
            return $ht
        } catch {
            return @{ records = @{} }
        }
    }
    return @{ records = @{} }
}

function Update-PsContractUsage {
    param([string]$Action, [string]$StorePath)
    # 边界: Action 为空不记录
    if (-not $Action) { return 0 }
    $u = _Load-Usage -StorePath $StorePath
    if (-not $u.records.ContainsKey($Action)) { $u.records[$Action] = @() }
    # 强制数组，避免单元素展开
    $arr = @($u.records[$Action]) + (Get-Date).ToString("o")
    $u.records[$Action] = $arr
    @{ records = $u.records } | ConvertTo-Json -Depth 6 | Set-Content $StorePath -Encoding UTF8
    return $arr.Count
}

function Get-PsContractUsage {
    param([string]$Action, [string]$StorePath)
    $u = _Load-Usage -StorePath $StorePath
    # 边界: 未记录的 Action 返回空数组
    if (-not $Action -or -not $u.records.ContainsKey($Action)) { return @() }
    return @($u.records[$Action])
}

function Get-PsContractPromotion {
    param([string]$Action, [string]$StorePath, [int]$Threshold = 3, [int]$WindowDays = 30)
    $recs = Get-PsContractUsage -Action $Action -StorePath $StorePath
    $cutoff = (Get-Date).AddDays(-$WindowDays)
    $recent = @($recs | Where-Object { [datetime]$_ -ge $cutoff })
    return [PSCustomObject]@{
        ShouldPromote = ($recent.Count -ge $Threshold)
        RecentCount   = $recent.Count
        Threshold     = $Threshold
        WindowDays    = $WindowDays
    }
}

Export-ModuleMember -Function Update-PsContractUsage, Get-PsContractUsage, Get-PsContractPromotion
