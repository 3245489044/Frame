<#
.SYNOPSIS
    Owner-aware 逻辑加载/调用层：函数一律按 (owner 文件, function) 限定。
.DESCRIPTION
    读取 Build-FunctionIndex.ps1 产出的 ps-function-index.json。
      Get-FrameworkOwners      -Name X        -> 返回所有属主（同名多属主不丢弃、不静默覆盖）
      Invoke-FrameworkFunction -Name X [-Owner rel|-Owner abs] [-Arguments @{}]
        单属主: 自动选定；多属主且未给 -Owner: 抛错并列属主（强制消歧，禁止自动挑一个）。
        加载隔离: 目标文件在独立作用域 dot-source/Import-Module 后调用，不污染调用方全局，
        同名不同文件的实现因此互不覆盖。
.NOTES
    索引/仓库根自锚定（.git 哨兵）。
#>

$script:__idx = $null
$script:__repo = $null
$script:__idxPath = Join-Path $PSScriptRoot 'ps-function-index.json'

function Get-FrameworkRepoRoot {
    if ($script:__repo) { return $script:__repo }
    $p = $PSScriptRoot
    while ($p -and -not (Test-Path -LiteralPath (Join-Path $p '.git'))) { $p = Split-Path -Parent $p }
    if (-not $p) { throw '找不到仓库根（.git）' }
    $script:__repo = $p; return $p
}

function Get-FrameworkIndex {
    if ($script:__idx) { return $script:__idx }
    if (-not (Test-Path -LiteralPath $script:__idxPath)) { throw "索引不存在: $script:__idxPath （先运行 Build-FunctionIndex.ps1）" }
    $script:__idx = Get-Content -LiteralPath $script:__idxPath -Raw -Encoding UTF8 | ConvertFrom-Json
    return $script:__idx
}

function Resolve-FrameworkOwnerPath {
    param([string]$Owner)
    $repo = Get-FrameworkRepoRoot
    if ([System.IO.Path]::IsPathRooted($Owner)) { return $Owner }
    return Join-Path $repo ($Owner.Replace('/','\'))
}

function Get-FrameworkOwners {
    <# 返回函数的所有属主（文件 rel/abs 列表） #>
    param([Parameter(Mandatory=$true)][string]$Name)
    $idx = Get-FrameworkIndex
    $o = $idx.owners.PSObject.Properties[$Name]
    if (-not $o) { return @() }
    return @($o.Value)
}

function Get-FrameworkModuleResult {
    <# 判断 owner 文件是否为 .psm1 并提供模块名 #>
    param([string]$FileAbs)
    return ([System.IO.Path]::GetExtension($FileAbs) -eq '.psm1')
}

function Invoke-FrameworkFunction {
    <#
    .SYNOPSIS
        按 (owner,function) 逻辑调用；单属主自动选，多属主必须显式 -Owner。
    #>
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [string]$Owner = '',
        [hashtable]$Arguments = @{}
    )
    $idx = Get-FrameworkIndex
    $owners = @(Get-FrameworkOwners -Name $Name)
    if ($owners.Count -eq 0) { throw "函数未注册: $Name" }
    if (-not $Owner) {
        if ($owners.Count -gt 1) {
            throw ("函数 '$Name' 有多属主，须显式 -Owner：" + ($owners -join ' | '))
        }
        $Owner = $owners[0]
    }
    $norm = $Owner.Replace('\','/')
    if ($norm -notin $owners) { throw "Owner 不在属主列表内: $Owner（属主：$(($owners -join ' | '))）" }
    $fileAbs = Resolve-FrameworkOwnerPath -Owner $Owner
    if (-not (Test-Path -LiteralPath $fileAbs)) { throw "Owner 文件不存在: $fileAbs" }

    if (Get-FrameworkModuleResult -FileAbs $fileAbs) {
        # .psm1：模块天然隔离，Import-Module 后模块限定调用
        $mod = Import-Module -Name $fileAbs -Force -DisableNameChecking -PassThru -ErrorAction Stop
        $cmd = Get-Command -Name $Name -Module $mod.Name -ErrorAction Stop
        return & $cmd @Arguments
    }

    # .ps1：在独立作用域 dot-source 后调用（不泄漏、不覆盖调用方全局）
    $code = ". '" + $fileAbs.Replace("'", "''") + "'`r`n& '" + $Name + "' @args"
    $sb = [scriptblock]::Create($code)
    return (& $sb $Arguments)
}

function Import-FrameworkRequires {
    <#
    .SYNOPSIS
        按需求装载函数所属文件（供脚本以普通函数调用）。
        解析 owner 索引：单属主自动选；多属主须 -Owner 键（禁止静默挑一个）。
        装载隔离/去重：每个文件只装一次。现有函数不改。
    #>
    param(
        [Parameter(Mandatory=$true)][string[]]$Name,
        [hashtable]$Owner = @{},
        [switch]$LoadAllOwners,
        [switch]$Quiet
    )
    $idx = Get-FrameworkIndex
    $loaded = @{}
    foreach ($n in $Name) {
        $owners = @(Get-FrameworkOwners -Name $n)
        if ($owners.Count -eq 0) { throw "函数未注册: $n" }
        $pick = @()
        if ($Owner.ContainsKey($n)) { $pick = [string[]]@($Owner[$n]) }
        elseif ($owners.Count -eq 1) { $pick = [string[]]@($owners) }
        elseif ($LoadAllOwners) { $pick = @($owners) }
        else { throw ("函数 '$n' 多属主须指定 -Owner 键：" + ($owners -join ' | ')) }
        foreach ($o in $pick) {
            $norm = $o.Replace('\','/')
            if ($norm -notin $owners) { throw "Owner 不在属主列表内: $o" }
            if ($loaded.ContainsKey($norm)) { continue }
            $abs = Resolve-FrameworkOwnerPath -Owner $o
            if (-not (Test-Path -LiteralPath $abs)) { throw "Owner 文件不存在: $abs" }
            if ([System.IO.Path]::GetExtension($abs) -eq '.psm1') { Import-Module -Name $abs -Force -DisableNameChecking -ErrorAction Stop }
            else { . $abs }
            $loaded[$norm] = $true
            if (-not $Quiet) { Write-Host ("  [框架加载] {0}  (提供 {1})" -f $norm,$n) }
        }
    }
    return @($loaded.Keys)
}
Export-ModuleMember -Function Get-FrameworkRepoRoot, Get-FrameworkOwners, Invoke-FrameworkFunction, Import-FrameworkRequires




