#requires -Version 5.1
<#
.SYNOPSIS
    声明面定位器（PowerShell 侧）—— 与 C# DeclarationLocator 同口径：角色 + basename → 物理路径。

.DESCRIPTION
    布局真源 = Config\declaration-layout.json（role / dirs / recursive / exclude / naming）。
    门禁脚本禁止硬编码 Config\nodes\x.ushar；一律经本模块按 role + basename 解析。
    解析顺序：角色目录集 → 递归 → basename → 排除项；未命中返回 $null（调用方显式判红）。
#>

function Get-DeclarationRepoRoot {
    $dir = $PSScriptRoot
    while ($dir -and -not ((Test-Path -LiteralPath (Join-Path $dir 'Frame')) -and (Test-Path -LiteralPath (Join-Path $dir 'Projects')))) {
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    if (-not $dir) { throw '声明定位器：仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
    return $dir
}

function Get-DeclarationConfigRoot {
    return (Join-Path (Get-DeclarationRepoRoot) 'Projects\Plug\Agent\Config')
}

function Get-DeclarationLayout {
    param([string]$ConfigRoot = '')
    if ([string]::IsNullOrWhiteSpace($ConfigRoot)) { $ConfigRoot = Get-DeclarationConfigRoot }
    $layoutPath = Join-Path $ConfigRoot 'declaration-layout.json'
    if (-not (Test-Path -LiteralPath $layoutPath)) { throw "声明定位器：布局真源缺失 $layoutPath" }
    return (Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Test-DeclarationExcluded {
    param([string]$FullPath, [string]$Root, [object[]]$Exclude)
    $relative = [IO.Path]::GetRelativePath($Root, $FullPath)
    $segments = $relative.Split([IO.Path]::DirectorySeparatorChar)
    foreach ($ex in $Exclude) {
        if ([string]::IsNullOrWhiteSpace($ex)) { continue }
        if ($segments -contains $ex) { return $true }
    }
    return $false
}

function Resolve-DeclarationFile {
    param(
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$ConfigRoot = '',
        [string]$Extension = '.ushar'
    )
    if ([string]::IsNullOrWhiteSpace($ConfigRoot)) { $ConfigRoot = Get-DeclarationConfigRoot }
    $layout = Get-DeclarationLayout -ConfigRoot $ConfigRoot
    $scope = $layout.roles | Where-Object { $_.role -eq $Role } | Select-Object -First 1
    if (-not $scope) { return $null }

    # 名称已带扩展名 ⇒ 保留；否则补 -Extension（使 ui-regions.json / AgentRuntimeUI.json 等 JSON 声明可按 role 解析）。
    if ([string]::IsNullOrWhiteSpace($Extension)) { $Extension = '.ushar' }
    if (-not $Extension.StartsWith('.')) { $Extension = '.' + $Extension }
    $fileName = if ($Name -match '\.[A-Za-z0-9]+$') { $Name } else { "$Name$Extension" }
    foreach ($dir in @($scope.dirs)) {
        $full = Join-Path $ConfigRoot $dir
        if (-not (Test-Path -LiteralPath $full)) { continue }
        $files = Get-ChildItem -LiteralPath $full -Filter $fileName -File -Recurse:([bool]$scope.recursive) -ErrorAction SilentlyContinue |
            Where-Object { -not (Test-DeclarationExcluded -FullPath $_.FullName -Root $ConfigRoot -Exclude @($scope.exclude)) } |
            Sort-Object -Property FullName
        $hit = $files | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

function Resolve-DeclarationFileAnyRole {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$ConfigRoot = '',
        [string]$Extension = '.ushar'
    )
    if ([string]::IsNullOrWhiteSpace($ConfigRoot)) { $ConfigRoot = Get-DeclarationConfigRoot }
    $layout = Get-DeclarationLayout -ConfigRoot $ConfigRoot
    foreach ($scope in @($layout.roles)) {
        $hit = Resolve-DeclarationFile -Role $scope.role -Name $Name -ConfigRoot $ConfigRoot -Extension $Extension
        if ($hit) { return $hit }
    }
    return $null
}

Export-ModuleMember -Function Get-DeclarationRepoRoot, Get-DeclarationConfigRoot, Get-DeclarationLayout, Resolve-DeclarationFile, Resolve-DeclarationFileAnyRole
