<#
.SYNOPSIS
    Git 克隆构建通用模块 — 提供从 Git 仓库克隆、构建的通用函数

.DESCRIPTION
    从 Install-McpServer.ps1 中提取的纯通用 Git 克隆操作函数。
    不依赖任何项目特定常量（路径等由调用方传入）。
    支持在本地或远程（Docker 容器）环境中执行操作。

    包含函数:
        Invoke-GitCloneAndBuild     — 克隆 Git 仓库并执行构建命令

    用法:
        Import-Module .\BasicMethod-GitClone.psm1
        Invoke-GitCloneAndBuild -Repository "https://github.com/owner/repo.git" -InstallDir "/opt/mcp-servers/my-server"
        Invoke-GitCloneAndBuild -Repository "https://github.com/owner/repo.git" -InstallDir "/tmp/repo" `
            -Branch "develop" -BuildCommand "npm install && npm run build" `
            -Executor { param($cmd) docker exec mycontainer sh -c $cmd }
#>

# ============================================
# Git 克隆构建函数
# ============================================

function Invoke-GitCloneAndBuild {
    <#
    .SYNOPSIS
        克隆 Git 仓库并执行构建命令
    .PARAMETER Repository
        仓库 URL
    .PARAMETER InstallDir
        安装目标目录
    .PARAMETER Branch
        分支名称（默认 "main"）
    .PARAMETER BuildCommand
        构建命令（默认 "npm install && npm run build"）
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令。
        格式: { param($cmd) & docker exec $ContainerName sh -c $cmd }
        不传则在本地执行。
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Invoke-GitCloneAndBuild -Repository "https://github.com/owner/repo.git" -InstallDir "/opt/mcp-servers/my-server"
    .EXAMPLE
        Invoke-GitCloneAndBuild -Repository "https://github.com/owner/repo.git" -InstallDir "C:\Users\me\.mcp-servers\my-server" `
            -Branch "develop" -BuildCommand "npm install && npm run build"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string]$InstallDir,

        [string]$Branch = "main",

        [string]$BuildCommand = "npm install && npm run build",

        [scriptblock]$Executor = $null
    )

    Write-Host "  ℹ️  从 Git 仓库克隆: $Repository (分支: $Branch)" -ForegroundColor Gray

    try {
        if ($Executor) {
            # 在远程环境执行
            $commands = @(
                "rm -rf $InstallDir",
                "git clone --depth 1 -b $Branch '$Repository' $InstallDir 2>&1",
                "cd $InstallDir && $BuildCommand 2>&1"
            )

            foreach ($cmd in $commands) {
                $result = & $Executor $cmd
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "  ❌ 命令执行失败: $cmd" -ForegroundColor Red
                    Write-Host "  ❌ 输出: $result" -ForegroundColor Red
                    return $false
                }
            }
        } else {
            # 在本地执行
            if (Test-Path $InstallDir) {
                Remove-Item -Path $InstallDir -Recurse -Force
            }

            git clone --depth 1 -b $Branch $Repository $InstallDir 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  ❌ Git 克隆失败" -ForegroundColor Red
                return $false
            }

            Push-Location $InstallDir
            Invoke-Expression $BuildCommand 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  ⚠️  构建可能有警告" -ForegroundColor Yellow
            }
            Pop-Location
        }

        Write-Host "  ✅ 克隆构建成功到 $InstallDir" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "  ❌ Git 克隆安装失败: $_" -ForegroundColor Red
        return $false
    }
}

# ============================================
# 模块导出
# ============================================

Export-ModuleMember -Function @(
    'Invoke-GitCloneAndBuild'
)
