# OutputSchema.psm1 — 结构化输出 {status, message, data, errors}
function New-PsResult {
    param(
        [ValidateSet("success", "failure")][string]$Status,
        [string]$Message = "", $Data = $null, [array]$Errors = @()
    )
    [PSCustomObject]@{ status = $Status; message = $Message; data = $Data; errors = $Errors }
}

function ConvertTo-PsResult {
    param($InputObject)
    # 已结构化（含 status 字段）原样返回，避免双重包装
    if ($null -ne $InputObject -and $InputObject -is [pscustomobject] -and $InputObject.PSObject.Properties.Name -contains "status") { return $InputObject }
    return New-PsResult -Status success -Data $InputObject
}

Export-ModuleMember -Function New-PsResult, ConvertTo-PsResult
