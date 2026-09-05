<#
.SYNOPSIS
    VS Code 扩展同步工具（从 BatTool 迁移）

.DESCRIPTION
    提供 VS Code 扩展的导出、安装和差异对比功能。
    替代 BatTool/Tools/Utils/同步VSCode扩展.bat。

    功能:
    [1] 导出扩展列表（从本机导出到 vscode-extensions.list）
    [2] 安装扩展（根据 vscode-extensions.list 安装）
    [3] 查看差异（对比本机已安装与列表中的扩展）

    用法:
        .\Sync-VscodeExtensions.ps1          交互菜单
        .\Sync-VscodeExtensions.ps1 -Export  直接导出
        .\Sync-VscodeExtensions.ps1 -Install 直接安装
        .\Sync-VscodeExtensions.ps1 -Diff    直接查看差异
#>

[CmdletBinding()]
param(
    [switch]$Export,
    [switch]$Install,
    [switch]$Diff
)

# ============================================
#  初始化
# ============================================

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$extListFile = Join-Path $scriptPath "vscode-extensions.list"

# ============================================
#  VS Code 路径查找
# ============================================

function Find-VscodeCmd {
    <#
    .SYNOPSIS
        查找 VS Code 的 code.cmd 路径
    #>

    # 先尝试 PATH 中的 code
    $codeCmd = Get-Command "code" -ErrorAction SilentlyContinue
    if ($codeCmd) { return "code" }

    # 遍历常见安装目录
    $searchPaths = @(
        "${env:ProgramFiles}\Microsoft VS Code\bin\code.cmd",
        "${env:ProgramFiles(x86)}\Microsoft VS Code\bin\code.cmd",
        "${env:LOCALAPPDATA}\Programs\Microsoft VS Code\bin\code.cmd",
        "D:\File\VSCode\Microsoft VS Code\bin\code.cmd",
        "${env:USERPROFILE}\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd"
    )

    foreach ($path in $searchPaths) {
        if (Test-Path $path) { return $path }
    }

    return $null
}

# ============================================
#  核心功能
# ============================================

function Export-VscodeExtensions {
    <#
    .SYNOPSIS
        从本机导出 VS Code 扩展列表
    #>

    $codeCmd = Find-VscodeCmd
    if (-not $codeCmd) {
        Write-Host "✗ 未找到 VS Code，请确保已安装" -ForegroundColor Red
        return $false
    }

    Write-Host "正在从本机导出 VS Code 扩展列表..." -ForegroundColor Cyan
    Write-Host ""

    try {
        $extensions = & $codeCmd --list-extensions 2>&1
        if ($LASTEXITCODE -eq 0) {
            $extensions | Set-Content $extListFile -Encoding UTF8
            $count = ($extensions | Measure-Object).Count
            Write-Host "✓ 导出成功！" -ForegroundColor Green
            Write-Host "已保存到: $extListFile" -ForegroundColor Gray
            Write-Host "共导出 $count 个扩展。" -ForegroundColor Gray
            return $true
        }
        else {
            Write-Host "✗ 导出失败，请检查 VS Code 是否正常运行。" -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "✗ 导出失败: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Install-VscodeExtensions {
    <#
    .SYNOPSIS
        根据 vscode-extensions.list 安装扩展
    #>

    if (-not (Test-Path $extListFile)) {
        Write-Host "✗ 未找到扩展列表文件！" -ForegroundColor Red
        Write-Host "请先运行导出功能，或将 vscode-extensions.list 文件放到 Tools 目录下。" -ForegroundColor Yellow
        return
    }

    $codeCmd = Find-VscodeCmd
    if (-not $codeCmd) {
        Write-Host "✗ 未找到 VS Code，请确保已安装" -ForegroundColor Red
        return
    }

    Write-Host "正在根据列表安装 VS Code 扩展..." -ForegroundColor Cyan
    Write-Host "列表文件: $extListFile" -ForegroundColor Gray
    Write-Host "提示：安装过程中请勿关闭此窗口。" -ForegroundColor Yellow
    Write-Host ""

    # 获取本机已安装的扩展
    $installedExtensions = @(& $codeCmd --list-extensions 2>$null) | ForEach-Object { $_.Trim() }
    $extList = Get-Content $extListFile -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }

    $total = 0
    $installed = 0
    $skipped = 0
    $failed = 0

    foreach ($ext in $extList) {
        $total++

        if ($ext -in $installedExtensions) {
            Write-Host "  [跳过] $ext（已安装）" -ForegroundColor Yellow
            $skipped++
        }
        else {
            Write-Host "  [安装] $ext ..." -ForegroundColor Gray
            try {
                $result = & $codeCmd --install-extension $ext 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "  ✓ 安装成功" -ForegroundColor Green
                    $installed++
                }
                else {
                    Write-Host "  ✗ 安装失败" -ForegroundColor Red
                    $failed++
                }
            }
            catch {
                Write-Host "  ✗ 安装失败: $($_.Exception.Message)" -ForegroundColor Red
                $failed++
            }
        }
    }

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host " 安装完成！" -ForegroundColor White
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  总计: $total" -ForegroundColor Gray
    Write-Host "  已安装（跳过）: $skipped" -ForegroundColor Yellow
    Write-Host "  新安装: $installed" -ForegroundColor Green
    Write-Host "  失败: $failed" -ForegroundColor Red
    Write-Host ""
    Write-Host "提示：部分扩展可能需要重启 VS Code 才能生效。" -ForegroundColor Gray
}

function Compare-VscodeExtensions {
    <#
    .SYNOPSIS
        对比本机已安装与列表中的扩展差异
    #>

    if (-not (Test-Path $extListFile)) {
        Write-Host "✗ 未找到扩展列表文件！" -ForegroundColor Red
        Write-Host "请先运行导出功能。" -ForegroundColor Yellow
        return
    }

    $codeCmd = Find-VscodeCmd
    if (-not $codeCmd) {
        Write-Host "✗ 未找到 VS Code，请确保已安装" -ForegroundColor Red
        return
    }

    Write-Host "正在对比本机已安装扩展与列表中的差异..." -ForegroundColor Cyan
    Write-Host ""

    # 获取本机已安装的扩展
    $installedExtensions = @(& $codeCmd --list-extensions 2>$null) | ForEach-Object { $_.Trim() }
    $extList = Get-Content $extListFile -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }

    Write-Host "--- 列表中存在但本机未安装（需要安装）---" -ForegroundColor Yellow
    Write-Host ""

    $hasMissing = $false
    foreach ($ext in $extList) {
        if ($ext -notin $installedExtensions) {
            Write-Host "  [+] $ext" -ForegroundColor Green
            $hasMissing = $true
        }
    }
    if (-not $hasMissing) {
        Write-Host "  （无，所有列表中的扩展均已安装）" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "--- 本机已安装但列表中不存在（多余扩展）---" -ForegroundColor Yellow
    Write-Host ""

    $hasExtra = $false
    foreach ($ext in $installedExtensions) {
        if ($ext -notin $extList) {
            Write-Host "  [-] $ext" -ForegroundColor Red
            $hasExtra = $true
        }
    }
    if (-not $hasExtra) {
        Write-Host "  （无，本机扩展全部在列表中）" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "提示：" -ForegroundColor Gray
    Write-Host "  [+] 表示需要安装的扩展" -ForegroundColor Green
    Write-Host "  [-] 表示本机独有的扩展（不在列表中）" -ForegroundColor Red
}

# ============================================
#  菜单显示
# ============================================

function Show-Menu {
    <#
    .SYNOPSIS
        显示交互菜单
    #>

    $codeCmd = Find-VscodeCmd
    if (-not $codeCmd) {
        Write-Host ""
        Write-Host "============================================" -ForegroundColor Red
        Write-Host "  ✗ 未找到 VS Code" -ForegroundColor Red
        Write-Host "============================================" -ForegroundColor Red
        Write-Host ""
        Write-Host "未能在以下位置找到 code.cmd：" -ForegroundColor Yellow
        Write-Host "  - PATH 环境变量" -ForegroundColor Gray
        Write-Host "  - Program Files" -ForegroundColor Gray
        Write-Host "  - Local AppData" -ForegroundColor Gray
        Write-Host "  - D:\File\VSCode\" -ForegroundColor Gray
        Write-Host ""
        Write-Host "请确保 VS Code 已安装，或将 code.cmd 所在目录添加到 PATH。" -ForegroundColor Yellow
        Write-Host ""
        Pause-Message
        return
    }

    while ($true) {
        Clear-Host
        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host "   同步 VS Code 扩展" -ForegroundColor White
        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "  VS Code: $codeCmd" -ForegroundColor Gray
        Write-Host "  列表文件: $extListFile" -ForegroundColor Gray
        Write-Host ""
        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "  [1] 导出扩展列表（从本机导出）" -ForegroundColor White
        Write-Host "  [2] 安装扩展（根据列表安装）" -ForegroundColor White
        Write-Host "  [3] 查看差异（对比本机与列表）" -ForegroundColor White
        Write-Host "  [4] 退出" -ForegroundColor Red
        Write-Host ""
        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host ""

        $choice = Read-Host "请输入数字 (1-4)"

        switch ($choice) {
            "1" {
                Clear-Host
                Write-Host "============================================" -ForegroundColor Cyan
                Write-Host "   [1] 导出扩展列表" -ForegroundColor White
                Write-Host "============================================" -ForegroundColor Cyan
                Write-Host ""
                Export-VscodeExtensions
                Write-Host ""
                Pause-Message
            }
            "2" {
                Clear-Host
                Write-Host "============================================" -ForegroundColor Cyan
                Write-Host "   [2] 安装扩展" -ForegroundColor White
                Write-Host "============================================" -ForegroundColor Cyan
                Write-Host ""
                Install-VscodeExtensions
                Write-Host ""
                Pause-Message
            }
            "3" {
                Clear-Host
                Write-Host "============================================" -ForegroundColor Cyan
                Write-Host "   [3] 查看差异" -ForegroundColor White
                Write-Host "============================================" -ForegroundColor Cyan
                Write-Host ""
                Compare-VscodeExtensions
                Write-Host ""
                Pause-Message
            }
            "4" {
                Write-Host "再见！" -ForegroundColor Green
                return
            }
            default {
                Write-Host "无效输入，请重新选择..." -ForegroundColor Red
                Start-Sleep -Milliseconds 1500
            }
        }
    }
}

function Pause-Message {
    if ($host.Name -eq "ConsoleHost") {
        Write-Host "按回车键继续..." -ForegroundColor Gray
        $null = Read-Host
    }
}

# ============================================
#  主入口
# ============================================

if ($Export) {
    Export-VscodeExtensions
}
elseif ($Install) {
    Install-VscodeExtensions
    Write-Host ""
    Pause-Message
}
elseif ($Diff) {
    Compare-VscodeExtensions
    Write-Host ""
    Pause-Message
}
else {
    Show-Menu
}
