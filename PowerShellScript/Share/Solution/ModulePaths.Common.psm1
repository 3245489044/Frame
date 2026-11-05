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

<#
.SYNOPSIS
    推导「根伞工程」集合 —— MainProj.sln 唯一允许直挂的工程。

.DESCRIPTION
    规则（与 modules.json roots 同源，不设第二份清单）：
      1 根目录直属 csproj：<root>\<root>.csproj 存在；
      2 该根目录内无独立 *.sln（如 Protocol\Protocol.sln 自带解决方案 ⇒ 不并入 MainProj）。
    叶子工程（如 Modules\Orchestration\Gather\Gather.csproj）只经伞工程 ProjectReference 引入，不入 .sln。
    消费方 = Sync-SlnToDisk.ps1（伞模式）∧ Check-SolutionUmbrella.ps1（门禁）。
#>
function Get-UmbrellaProjects {
    param(
        [Parameter(Mandatory)][string]$CsharpPrjRoot,
        [Parameter(Mandatory)]$Manifest
    )
    $out = [System.Collections.Generic.List[object]]::new()
    foreach ($r in $Manifest.roots.PSObject.Properties) {
        $rootDir = Join-Path $CsharpPrjRoot ([string]$r.Value.path)
        $proj = Join-Path $rootDir ($r.Name + '.csproj')
        if (-not (Test-Path -LiteralPath $proj)) { continue }
        # 自带解决方案的根（Protocol）由自己的 sln 承载，禁并入 MainProj
        $ownSln = @(Get-ChildItem -LiteralPath $rootDir -Filter '*.sln' -File -ErrorAction SilentlyContinue)
        if ($ownSln.Count -gt 0) { continue }
        $out.Add([PSCustomObject]@{
            Name     = $r.Name
            FullPath = [System.IO.Path]::GetFullPath($proj)
            RelPath  = [System.IO.Path]::GetFullPath($proj).Substring([System.IO.Path]::GetFullPath($CsharpPrjRoot).Length).TrimStart('\', '/')
        })
    }
    return ,$out
}

Export-ModuleMember -Function Get-CsharpPrjRoot, Get-WorkflowFrameRoot, Get-ModuleManifestPath,
Get-ModuleGeneratedPropsPath, Get-SafeModuleName, Read-ModuleManifest, Get-ModulePathMap, Get-EnumeratedProjects,
Get-UmbrellaProjects
