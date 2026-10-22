# Plain payload 归一（5.1 兼容）：把 PowerShell 提供程序对象/属性元数据从协议载荷剥离。
# 判据 = 结构事实：输出只含基元 / 字典 / 数组 / 纯对象；PS* 属性与 PSDriveInfo/PSProviderInfo/PSModuleInfo 一律丢弃。

function ConvertTo-PlainPayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [object]$Value,

        [int]$MaxDepth = 8,

        [int]$Depth = 0
    )

    if ($null -eq $Value) { return $null }
    if ($Depth -ge $MaxDepth) { return '<max-depth>' }

    if ($Value -is [string] -or $Value -is [char]) { return [string]$Value }
    if ($Value -is [bool]) { return [bool]$Value }
    if ($Value -is [datetime]) { return ([datetime]$Value).ToString('o') }
    if ($Value -is [datetimeoffset]) { return ([datetimeoffset]$Value).ToString('o') }
    if ($Value -is [guid]) { return ([guid]$Value).ToString() }
    if ($Value -is [timespan]) { return ([timespan]$Value).ToString() }
    if ($Value -is [decimal]) { return [decimal]$Value }
    if ($Value.GetType().IsPrimitive) { return $Value }

    # 提供程序元数据：不是业务载荷，禁止进入 JSON（用类型全名判定，兼容 5.1/7 类型差异）。
    $typeName = $Value.GetType().FullName
    if ($typeName -eq 'System.Management.Automation.PSDriveInfo' -or
        $typeName -eq 'System.Management.Automation.ProviderInfo' -or
        $typeName -eq 'System.Management.Automation.PSModuleInfo') {
        return $null
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $dictionary = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $name = [string]$key
            if ([string]::IsNullOrWhiteSpace($name) -or $name.StartsWith('PS', [System.StringComparison]::Ordinal)) { continue }
            $dictionary[$name] = ConvertTo-PlainPayload -Value $Value[$key] -MaxDepth $MaxDepth -Depth ($Depth + 1)
        }
        return $dictionary
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $items = New-Object System.Collections.ArrayList
        foreach ($item in $Value) {
            [void]$items.Add((ConvertTo-PlainPayload -Value $item -MaxDepth $MaxDepth -Depth ($Depth + 1)))
        }
        return ,$items.ToArray()
    }

    if ($Value -is [System.Management.Automation.PSObject]) {
        $plain = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.MemberType -ne 'NoteProperty' -and $property.MemberType -ne 'Property' -and $property.MemberType -ne 'AliasProperty') { continue }
            if ([string]::IsNullOrWhiteSpace($property.Name) -or $property.Name.StartsWith('PS', [System.StringComparison]::Ordinal)) { continue }
            try {
                $plain[$property.Name] = ConvertTo-PlainPayload -Value $property.Value -MaxDepth $MaxDepth -Depth ($Depth + 1)
            }
            catch {
                $plain[$property.Name] = $null
            }
        }
        return $plain
    }

    # 兜底：不把 .NET 对象图交给 ConvertTo-Json（其属性可能含环/元数据）。
    return [string]$Value
}
