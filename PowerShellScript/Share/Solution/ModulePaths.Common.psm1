<#
.SYNOPSIS
    modules.json 模块路径清单的共享工具

.DESCRIPTION
    供 Generate-ModulePaths.ps1 与 Test-ProjectReferences.ps1 共用，避免重复实现。
    提供：CsharpPrj 根解析、清单读取、MSBuild 属性名净化、绝对路径 → 模块名映射、项目枚举。
#>

$script:SolutionDir = $PSScriptRoot
# 本模块位于 Frame/PowerShellScript/Share/Solution/ → 上溯三级到 Frame/
$script:FrameRoot = (Get-Item $script:SolutionDir).Parent.Parent.Parent.FullName
$script:CsharpPrjRoot = Join-Path $script:FrameRoot "CsharpPrj"

function Get-CsharpPrjRoot {
    return $script:CsharpPrjRoot
}

function Get-WorkflowFrameRoot {
    return $script:FrameRoot
}

function Get-ModuleManifestPath {
    param([string]$Override = "")
    if (-not [string]::IsNullOrEmpty($Override)) { return $Override }
    return (Join-Path $script:CsharpPrjRoot "modules.json")
}

function Get-ModuleGeneratedPropsPath {
    return (Join-Path $script:CsharpPrjRoot "generated\Directory.Build.props.modules.generated.props")
}

# MSBuild 属性名仅允许 [A-Za-z0-9_]；项目名中的 '.' 等字符需替换为 '_'
function Get-SafeModuleName {
    param([Parameter(Mandatory)][string]$Name)
    return ($Name -replace '[^A-Za-z0-9_]', '_')
}

function Read-ModuleManifest {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

# 绝对 csproj 路径（小写）→ @{ Name; Safe } 映射
function Get-ModulePathMap {
    param(
        [Parameter(Mandatory)][string]$CsharpPrjRoot,
        [Parameter(Mandatory)]$Manifest
    )
    $map = @{}
    foreach ($p in $Manifest.projects.PSObject.Properties) {
        $abs = [System.IO.Path]::GetFullPath((Join-Path $CsharpPrjRoot $p.Value))
        $map[$abs.ToLowerInvariant()] = [PSCustomObject]@{
            Name = $p.Name
            Safe = (Get-SafeModuleName $p.Name)
            Path = $abs
        }
    }
    return ,$map
}

function Get-EnumeratedProjects {
    param([Parameter(Mandatory)][string]$CsharpPrjRoot)
    return (Get-ChildItem -Path $CsharpPrjRoot -Recurse -Filter *.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\' } |
        Sort-Object FullName)
}

Export-ModuleMember -Function Get-CsharpPrjRoot, Get-WorkflowFrameRoot, Get-ModuleManifestPath,
Get-ModuleGeneratedPropsPath, Get-SafeModuleName, Read-ModuleManifest, Get-ModulePathMap, Get-EnumeratedProjects
