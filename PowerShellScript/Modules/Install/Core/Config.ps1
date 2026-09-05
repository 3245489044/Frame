# @requires: Path Framework, BasicMethod/Core/Json, BasicMethod/Core/Path
# @version: 1.1.0
# @author: WorkFlowPrj
# @category: install
# @alias: install-config
# @exports: Load-InstallManifest, Get-ManifestPath, Validate-Manifest, Get-DefaultManifests

<#
.SYNOPSIS
    Install Framework 配置加载与验证模块
.DESCRIPTION
    提供 manifest.json 的加载、验证、路径解析功能。
.NOTES
    依赖：Path Framework, BasicMethod/Core/Json, BasicMethod/Core/Path
#>

function Get-ManifestPath {
    <#
    .SYNOPSIS
        获取 manifest 文件的完整路径
    .PARAMETER ManifestName
        manifest 文件名（不含路径）
    .PARAMETER ProjectRoot
        项目根目录
    .EXAMPLE
        Get-ManifestPath -ManifestName "mcp-servers.json"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestName,

        [string]$ProjectRoot = ""
    )

    $manifestPath = Get-FrameworkDataPath -Framework "Install" -Directory "Manifests" -FileName $ManifestName
    if ($manifestPath) {
        return $manifestPath
    }

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    return Join-Path $ProjectRoot "PowerShellScript\Install\Manifests\$ManifestName"
}

function Load-InstallManifest {
    <#
    .SYNOPSIS
        加载安装 manifest 文件
    .PARAMETER ManifestName
        manifest 文件名或完整路径
    .PARAMETER ProjectRoot
        项目根目录
    .PARAMETER Validate
        是否验证 schema（默认：$true）
    .EXAMPLE
        $manifest = Load-InstallManifest -ManifestName "mcp-servers.json"
    .EXAMPLE
        $manifest = Load-InstallManifest -ManifestName "D:\custom\manifest.json"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestName,

        [string]$ProjectRoot = "",

        [switch]$Validate = $true
    )

    # 确定 manifest 路径
    $manifestPath = if ([System.IO.Path]::IsPathRooted($ManifestName)) {
        $ManifestName
    } else {
        Get-ManifestPath -ManifestName $ManifestName -ProjectRoot $ProjectRoot
    }

    if (-not (Test-Path $manifestPath)) {
        Write-Error "Manifest 文件不存在: $manifestPath"
        return $null
    }

    Write-Info "加载配置: $manifestPath"

    try {
        $content = Get-Content $manifestPath -Raw -Encoding UTF8
        $manifest = $content | ConvertFrom-Json
    } catch {
        Write-Error "配置文件解析失败: $($_.Exception.Message)"
        return $null
    }

    # 验证 schema
    if ($Validate) {
        $errors = Validate-Manifest -Manifest $manifest
        if ($errors.Count -gt 0) {
            Write-Error "Manifest 验证失败:"
            foreach ($err in $errors) {
                Write-Host "  - $err" -ForegroundColor Red
            }
            return $null
        }
    }

    return $manifest
}

function Validate-Manifest {
    <#
    .SYNOPSIS
        验证 manifest 对象的 schema
    .PARAMETER Manifest
        manifest 对象
    .RETURNS
        错误列表（为空表示验证通过）
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$Manifest
    )

    $errors = @()

    # 检查必需字段
    if (-not $Manifest.name) {
        $errors += "缺少必需字段: name"
    }

    if (-not $Manifest.items) {
        $errors += "缺少必需字段: items"
    } else {
        if ($Manifest.items.Count -eq 0) {
            $errors += "items 数组为空"
        }

        # 验证每个 item
        foreach ($item in $Manifest.items) {
            if (-not $item.name) {
                $errors += "item 缺少必需字段: name"
            }
            if (-not $item.type) {
                $errors += "item '$($item.name)' 缺少必需字段: type"
            } else {
                $validTypes = @("npm", "pip", "github-release", "git-clone", "zip-extract", "rustup", "msi", "local")
                if ($item.type -notin $validTypes) {
                    $errors += "item '$($item.name)' 的 type '$($item.type)' 不是有效值（有效值: $($validTypes -join ', ')）"
                }
            }
            if (-not $item.source) {
                $errors += "item '$($item.name)' 缺少必需字段: source"
            }
        }
    }

    # 检查目标字段
    if ($Manifest.target) {
        if ($Manifest.target -notin @("local", "docker")) {
            $errors += "target 不是有效值（有效值: local, docker）"
        }
    }

    return $errors
}

function Get-DefaultManifests {
    <#
    .SYNOPSIS
        获取所有可用的 manifest 文件列表
    .PARAMETER ProjectRoot
        项目根目录
    .EXAMPLE
        $manifests = Get-DefaultManifests
    #>
    param(
        [string]$ProjectRoot = ""
    )

    $manifestsDir = Get-FrameworkPath -Framework "Install" -Directory "Manifests"
    if (-not $manifestsDir) {
        if (-not $ProjectRoot) {
            $ProjectRoot = Get-ProjectRoot
        }
        $manifestsDir = Join-Path $ProjectRoot "PowerShellScript\Install\Manifests"
    }

    if (-not (Test-Path $manifestsDir)) {
        return @()
    }

    $files = Get-ChildItem -Path $manifestsDir -Filter "*.json" -File
    $result = @()

    foreach ($file in $files) {
        $manifest = $null
        try {
            $content = Get-Content $file.FullName -Raw -Encoding UTF8
            $manifest = $content | ConvertFrom-Json
        } catch {}

        $result += [PSCustomObject]@{
            Name        = $file.Name
            FullPath    = $file.FullName
            DisplayName = if ($manifest -and $manifest.name) { $manifest.name } else { $file.BaseName }
            ItemCount   = if ($manifest -and $manifest.items) { $manifest.items.Count } else { 0 }
            Target      = if ($manifest -and $manifest.target) { $manifest.target } else { "local" }
        }
    }

    return $result | Sort-Object Name
}

function Show-ManifestList {
    <#
    .SYNOPSIS
        显示所有可用的 manifest 列表
    .PARAMETER Manifests
        Get-DefaultManifests 返回的列表（可选，自动获取）
    .EXAMPLE
        Show-ManifestList
    #>
    param(
        [array]$Manifests = $null
    )

    if (-not $Manifests) {
        $Manifests = Get-DefaultManifests
    }

    Write-Banner -Title "可用安装清单" -Color "Cyan"

    if ($Manifests.Count -eq 0) {
        Write-Warning "未找到任何 manifest 文件"
        Write-Hint "请在 Install/Manifests/ 目录下创建 JSON 配置文件"
        return
    }

    Write-Host "  $('名称'.PadRight(25)) $('目标'.PadRight(10)) $('项目数'.PadRight(8)) 描述" -ForegroundColor White
    Write-Host "  $('-' * 25) $('-' * 10) $('-' * 8) $('-' * 30)" -ForegroundColor DarkGray

    foreach ($m in $Manifests) {
        Write-Host "  $($m.Name.PadRight(25)) $($m.Target.PadRight(10)) $($m.ItemCount.ToString().PadRight(8)) $($m.DisplayName)" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Hint "使用: Install-Tool.ps1 -Manifest <文件名> [-Name <项目名>] [-All]"
}

function Load-Provider {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Type
    )
    
    $typeToFunc = @{
        'npm'            = 'Get-NpmProvider'
        'pip'            = 'Get-PipProvider'
        'github-release' = 'Get-GitHubReleaseProvider'
        'git-clone'      = 'Get-GitCloneProvider'
        'zip-extract'    = 'Get-ZipExtractProvider'
        'rustup'         = 'Get-RustupProvider'
        'msi'            = 'Get-MsiProvider'
        'local'          = 'Get-LocalProvider'
    }
    
    $providersDir = Get-FrameworkPath -Framework "Install" -Directory "Providers"
    if (-not $providersDir) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
        $providersDir = Join-Path $scriptDir "..\Providers"
    }
    
    $providerPath = Join-Path $providersDir "$Type.ps1"
    if (Test-Path $providerPath) {
        . $providerPath
        $providerFunc = $typeToFunc[$Type]
        if (-not $providerFunc) {
            Write-Warning "不支持的 Provider 类型: $Type"
            return $null
        }
        if (Get-Command $providerFunc -ErrorAction SilentlyContinue) {
            return & $providerFunc
        }
    }
    Write-Warning "Provider 不存在或加载失败: $Type"
    return $null
}

if ($MyInvocation.MyCommand.Source -eq $null) {
    Export-ModuleMember -Function @('Load-InstallManifest', 'Get-ManifestPath', 'Validate-Manifest', 'Get-DefaultManifests', 'Show-ManifestList', 'Load-Provider')
}