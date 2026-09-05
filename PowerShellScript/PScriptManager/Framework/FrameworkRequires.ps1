<#
.SYNOPSIS
    点源(dot-source)入口：在调用方作用域定义 Import-FrameworkRequires 并把属主函数装进调用方作用域。
.DESCRIPTION
    与 FrameworkInvoke.psm1 的区别：本文件被脚本 dot-source 后，函数定义在调用方作用域；
    其内部 Import-Module 导入到调用方会话、dot-source 的 .ps1 函数落在调用方作用域，
    因此调用方脚本随后能以普通函数名直接调用装载的函数。
.NOTES
    用法（在消费脚本中，仓库根自锚定后）：
        . (Join-Path $repo 'PowerShellScript\PScriptManager\Framework\FrameworkRequires.ps1')
        Import-FrameworkRequires -Name @('Get-ProjectRoot',...) -Owner @{ 'Get-ProjectRoot' = '...' }
#>
$script:FR_PkgDir = $PSScriptRoot
$script:FR_IdxPath = Join-Path $PSScriptRoot 'ps-function-index.json'
$script:FR_Repo = ''

function Get-FRRepo {
    if ($script:FR_Repo) { return $script:FR_Repo }
    $p = $script:FR_PkgDir
    while ($p -and -not (Test-Path -LiteralPath (Join-Path $p '.git'))) { $p = Split-Path -Parent $p }
    if (-not $p) { throw '找不到仓库根（.git）' }
    $script:FR_Repo = $p; return $p
}

function Import-FrameworkRequires {
    <#
    按需求装载函数所属文件到调用方作用域。单属主自动选；多属主须 -Owner 键（禁止静默挑一个）。
    #>
    param(
        [Parameter(Mandatory=$true)][string[]]$Name,
        [hashtable]$Owner = @{},
        [switch]$LoadAllOwners,
        [switch]$Quiet
    )
    $idxPath = $script:FR_IdxPath
    if (-not (Test-Path -LiteralPath $idxPath)) { throw "索引不存在: $idxPath （先运行 Build-FunctionIndex.ps1）" }
    $idx = Get-Content -LiteralPath $idxPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $repo = Get-FRRepo
    $loaded = @{}
    foreach ($n in $Name) {
        $prop = $idx.owners.PSObject.Properties[$n]
        $owners = if ($prop) { @($prop.Value) } else { @() }
        if ($owners.Count -eq 0) { throw "函数未注册: $n" }
        $pick = @()
        if ($Owner.ContainsKey($n)) { $pick = [string[]]@($Owner[$n]) }
        elseif ($owners.Count -eq 1) { $pick = [string[]]$owners }
        elseif ($LoadAllOwners) { $pick = [string[]]$owners }
        else { throw ("函数 '$n' 多属主须指定 -Owner 键：" + ($owners -join ' | ')) }
        foreach ($o in $pick) {
            $norm = $o.Replace('\','/')
            if ($norm -notin $owners) { throw "Owner 不在属主列表内: $o" }
            if ($loaded.ContainsKey($norm)) { continue }
            $abs = if ([System.IO.Path]::IsPathRooted($o)) { $o } else { Join-Path $repo ($o.Replace('/','\')) }
            if (-not (Test-Path -LiteralPath $abs)) { throw "Owner 文件不存在: $abs" }
            if ([System.IO.Path]::GetExtension($abs) -eq '.psm1') { Import-Module -Name $abs -Force -DisableNameChecking -ErrorAction Stop }
            else { . $abs }
            $loaded[$norm] = $true
            if (-not $Quiet) { Write-Host ("  [框架加载] {0}  (提供 {1})" -f $norm,$n) }
        }
    }
}

