<#
.SYNOPSIS
    格式转换模块 — 提供统一的大小格式化、路径解析等工具函数
.DESCRIPTION
    包括文件大小格式化、路径模板解析、相对路径获取等。

    用法:
        Import-Module .\Convert-Format.psm1 -Force
        Format-Size -Bytes 1234567
        Get-HumanSize -Bytes 1234567
        Resolve-PathTemplate -PathTemplate "{LOCALAPPDATA}\Docker"
        Get-RelativePath -Path "C:\a\b\c.txt" -BasePath "C:\a"
#>

function Format-Size {
    <#
    .SYNOPSIS
        将字节数格式化为人类可读的大小字符串
    .PARAMETER Bytes
        字节数
    .EXAMPLE
        Format-Size -Bytes 1234567  # 返回 "1.18 MB"
    #>
    param([long]$Bytes)

    if ($Bytes -ge 1TB) { return "{0:N2} TB" -f ($Bytes / 1TB) }
    if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Get-HumanSize {
    <#
    .SYNOPSIS
        将字节数格式化为人类可读的大小字符串（别名）
    .PARAMETER Bytes
        字节数
    #>
    param([long]$Bytes)
    return Format-Size -Bytes $Bytes
}

function Get-DirSize {
    <#
    .SYNOPSIS
        递归计算目录大小
    .PARAMETER Path
        目录路径
    .RETURNS
        目录总大小（字节）
    #>
    param([string]$Path)

    if (-not (Test-Path $Path)) { return 0 }
    try {
        $size = (Get-ChildItem -Path $Path -Recurse -Force -ErrorAction SilentlyContinue |
                 Measure-Object -Property Length -Sum -ErrorAction SilentlyContinue).Sum
        return [math]::Max(0, $size)
    } catch { return 0 }
}

function Resolve-PathTemplate {
    <#
    .SYNOPSIS
        解析路径模板中的环境变量占位符
    .DESCRIPTION
        支持 {APPDATA}, {LOCALAPPDATA}, {USERPROFILE}, {TEMP}, {TMP}, {WINDIR}
    .PARAMETER PathTemplate
        包含占位符的路径模板
    .EXAMPLE
        Resolve-PathTemplate -PathTemplate "{LOCALAPPDATA}\Docker"
    #>
    param([string]$PathTemplate)

    $result = $PathTemplate
    $result = $result -replace '{APPDATA}', $env:APPDATA
    $result = $result -replace '{LOCALAPPDATA}', $env:LOCALAPPDATA
    $result = $result -replace '{USERPROFILE}', $env:USERPROFILE
    $result = $result -replace '{TEMP}', $env:TEMP
    $result = $result -replace '{TMP}', $env:TMP
    $result = $result -replace '{WINDIR}', $env:WINDIR
    return $result
}

function Resolve-EnvPath {
    <#
    .SYNOPSIS
        解析路径中的 %ENV% 环境变量占位符
    .PARAMETER Path
        包含 %ENV% 占位符的路径
    .EXAMPLE
        Resolve-EnvPath -Path "%LOCALAPPDATA%\Docker"
    #>
    param([string]$Path)

    if ([string]::IsNullOrEmpty($Path)) { return "" }
    $maxIter = 10; $iter = 0
    while ($Path -match '%([^%]+)%' -and $iter -lt $maxIter) {
        $iter++
        $v = [Environment]::GetEnvironmentVariable($Matches[1], "Process")
        if ([string]::IsNullOrEmpty($v)) { $v = [Environment]::GetEnvironmentVariable($Matches[1], "User") }
        if ([string]::IsNullOrEmpty($v)) { $v = [Environment]::GetEnvironmentVariable($Matches[1], "Machine") }
        if ([string]::IsNullOrEmpty($v)) { return $Path }
        $Path = $Path -replace [regex]::Escape("%$($Matches[1])%"), $v
    }
    return $Path
}

function Get-RelativePath {
    <#
    .SYNOPSIS
        获取相对路径
    .PARAMETER Path
        完整路径
    .PARAMETER BasePath
        基准路径
    .EXAMPLE
        Get-RelativePath -Path "C:\a\b\c.txt" -BasePath "C:\a"  # 返回 "b\c.txt"
    #>
    param([string]$Path, [string]$BasePath)

    if (-not $BasePath.EndsWith('\')) { $BasePath += '\' }
    if ($Path -like "$BasePath*") {
        return $Path.Substring($BasePath.Length)
    }
    return $Path
}

function ConvertTo-Hashtable {
    <#
    .SYNOPSIS
        将 PSCustomObject 递归转换为 Hashtable（用于 JSON 序列化）
    .PARAMETER InputObject
        输入对象
    #>
    param($InputObject)

    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [PSCustomObject]) {
        $hash = @{}
        $InputObject.PSObject.Properties | ForEach-Object {
            $hash[$_.Name] = ConvertTo-Hashtable $_.Value
        }
        return $hash
    }
    if ($InputObject -is [System.Collections.IList]) {
        $list = @()
        foreach ($item in $InputObject) {
            $list += ConvertTo-Hashtable $item
        }
        return $list
    }
    return $InputObject
}

function ConvertTo-PSCustomObject {
    <#
    .SYNOPSIS
        将 Hashtable 递归转换为 PSCustomObject
    .PARAMETER InputObject
        输入对象
    #>
    param($InputObject)

    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        $obj = [PSCustomObject]@{}
        $InputObject.Keys | ForEach-Object {
            $obj | Add-Member -NotePropertyName $_ -NotePropertyValue (ConvertTo-PSCustomObject $InputObject[$_])
        }
        return $obj
    }
    if ($InputObject -is [System.Collections.IList]) {
        $list = @()
        foreach ($item in $InputObject) {
            $list += ConvertTo-PSCustomObject $item
        }
        return [PSCustomObject[]]$list
    }
    return $InputObject
}

# 导出所有函数
Export-ModuleMember -Function Format-Size, Get-HumanSize, Get-DirSize,
    Resolve-PathTemplate, Resolve-EnvPath, Get-RelativePath,
    ConvertTo-Hashtable, ConvertTo-PSCustomObject
