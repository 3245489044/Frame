# CsprojNewProjectInit.psm1 - 新项目 .csproj 初始化模块
# 提供新项目的 .csproj 配置检测与修复功能
# 依赖：XmlHelpers.psm1, FileSystemHelpers.psm1, InteractiveHelpers.psm1
#
# 本模块是 CsprojFixEngine 的子模块，专注于新项目初始化

<#
.SYNOPSIS
    初始化新项目的 .csproj 配置，确保编译输出到根 BuildOutput\
.DESCRIPTION
    检测新建项目（或指定项目）的 .csproj 配置是否正确：
    1. 检查是否包含 OutputPath（应移除，让全局配置生效）
    2. 检查 TargetFramework 是否需要 windows 后缀（根据项目引用关系自动判断）
    3. 检查是否需要 UseWindowsForms
    4. 移除 VS 自动生成的冗余配置
    自动修复并备份原文件
.PARAMETER csprojFiles
    要检查的 csproj 文件列表（如果为 $null，则自动扫描新建项目）
.PARAMETER rootDir
    解决方案根目录
.PARAMETER backupDir
    备份目录路径
.PARAMETER globalTargetFramework
    全局 TargetFramework 值（默认 net10.0）
.PARAMETER winFormsDependentProjects
    需要 WinForms 的项目引用列表
.PARAMETER autoFix
    是否自动修复（跳过确认提示），默认 $false
.OUTPUTS
    [hashtable[]] 每个项目的修复结果列表
.EXAMPLE
    # 自动检测所有新建项目并修复
    Initialize-NewProject -rootDir $rootDir -backupDir $backupDir

    # 指定特定项目文件
    Initialize-NewProject -csprojFiles @(Get-Item "MyNewProject\MyNewProject.csproj") -backupDir $backupDir
#>
function Initialize-NewProject {
    param(
        [System.IO.FileInfo[]]$csprojFiles = $null,
        [string]$rootDir = "",
        [string]$backupDir = "",
        [string]$globalTargetFramework = 'net10.0',
        [string[]]$winFormsDependentProjects = @('WdUI.csproj', 'FileDriven.UI.csproj', 'BasicMethod.csproj'),
        [bool]$autoFix = $false
    )

    if ([string]::IsNullOrEmpty($rootDir)) {
        $rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
    }
    if ([string]::IsNullOrEmpty($backupDir)) {
        $backupDir = Join-Path $PSScriptRoot "..\backups"
    }

    # 如果没有指定文件，自动扫描所有 csproj
    if ($null -eq $csprojFiles) {
        $csprojFiles = Get-CsprojFiles -rootDir $rootDir -excludePaths @(Join-Path $rootDir "BuildOutput")
    }

    $results = @()

    foreach ($csproj in $csprojFiles) {
        $content = Get-Content -Path $csproj.FullName -Raw
        $issues = @()
        $fixes = @()

        # ----- 检查 1：是否有 OutputPath（应移除） -----
        if ($content -match '<OutputPath>') {
            $issues += "包含自定义 OutputPath（会覆盖全局配置，导致输出到项目本地目录）"
            $fixes += "RemoveOutputPath"
        }

        # ----- 检查 2：是否有显式的 TargetFramework -----
        $hasExplicitTf = ($content -match '<TargetFramework>')
        $hasWindowsTf = ($content -match '<TargetFramework>.*windows</TargetFramework>')
        $hasMultiTf = ($content -match '<TargetFrameworks>')

        # 判断项目是否需要 windows 后缀
        $needsWindows = $false
        if (-not $hasExplicitTf -and -not $hasMultiTf) {
            # 没有显式 TargetFramework，检查项目引用
            foreach ($dep in $winFormsDependentProjects) {
                if ($content -match [regex]::Escape($dep)) {
                    $needsWindows = $true
                    break
                }
            }
            # 检查 OutputType
            if (-not $needsWindows) {
                $needsWindows = ($content -match '<OutputType>WinExe</OutputType>') -or
                                ($content -match '<OutputType>Exe</OutputType>')
            }
            if ($needsWindows) {
                $issues += "缺少 TargetFramework（项目引用 WinForms 项目或为可执行文件，需要 net10.0-windows）"
                $fixes += "AddWindowsTargetFramework"
            }
        } elseif ($hasExplicitTf -and -not $hasWindowsTf -and -not $hasMultiTf) {
            # 有 TargetFramework 但不是 windows，检查是否需要
            foreach ($dep in $winFormsDependentProjects) {
                if ($content -match [regex]::Escape($dep)) {
                    $needsWindows = $true
                    break
                }
            }
            if ($needsWindows) {
                $issues += "TargetFramework 不是 windows 版本（项目引用 WinForms 项目，需要 net10.0-windows）"
                $fixes += "FixToWindowsTargetFramework"
            }
        }

        # ----- 检查 3：是否需要 UseWindowsForms -----
        $hasUseWinForms = ($content -match '<UseWindowsForms>')
        if (-not $hasUseWinForms -and ($hasWindowsTf -or $needsWindows)) {
            $issues += "缺少 UseWindowsForms 配置"
            $fixes += "AddUseWindowsForms"
        }

        # ----- 检查 4：是否有 VS 自动生成的冗余 AssemblyName/RootNamespace（与项目名相同可省略） -----
        $projectName = $csproj.BaseName
        if ($content -match "<AssemblyName>$projectName</AssemblyName>") {
            $issues += "AssemblyName 与项目名相同，可省略（VS 自动生成）"
            $fixes += "RemoveRedundantAssemblyName"
        }
        if ($content -match "<RootNamespace>$projectName</RootNamespace>") {
            $issues += "RootNamespace 与项目名相同，可省略（VS 自动生成）"
            $fixes += "RemoveRedundantRootNamespace"
        }

        # ----- 汇总结果 -----
        if ($issues.Count -gt 0) {
            $result = @{
                File = $csproj
                Issues = $issues
                Fixes = $fixes
                Fixed = $false
            }

            Write-Host "  [需要修复] $($csproj.Name)" -ForegroundColor Yellow
            foreach ($issue in $issues) {
                Write-Host "    - $issue" -ForegroundColor Magenta
            }

            if ($autoFix) {
                $result.Fixed = Initialize-NewProjectFix -csproj $csproj -content $content -fixes $fixes -backupDir $backupDir `
                    -globalTargetFramework $globalTargetFramework -needsWindows $needsWindows
            } else {
                $confirm = Read-Host "  确认修复 $($csproj.Name)？(Y/N)"
                if ($confirm -eq 'Y' -or $confirm -eq 'y') {
                    $result.Fixed = Initialize-NewProjectFix -csproj $csproj -content $content -fixes $fixes -backupDir $backupDir `
                        -globalTargetFramework $globalTargetFramework -needsWindows $needsWindows
                } else {
                    Write-Host "    [已跳过]" -ForegroundColor Gray
                }
            }

            $results += $result
            Write-Host ""
        } else {
            Write-Host "  [配置正确] $($csproj.Name)" -ForegroundColor Green
        }
    }

    if ($results.Count -eq 0) {
        Write-Host "  所有项目配置正确，无需修复。" -ForegroundColor Green
    } else {
        $fixedCount = ($results | Where-Object { $_.Fixed }).Count
        Write-Host "  检查完成：$($results.Count) 个项目需要修复，已修复 $fixedCount 个。" -ForegroundColor Cyan
    }

    return $results
}

<#
.SYNOPSIS
    执行新项目 .csproj 的修复操作（内部函数）
.DESCRIPTION
    根据检测到的问题列表，执行对应的修复操作
#>
function Initialize-NewProjectFix {
    param(
        [System.IO.FileInfo]$csproj,
        [string]$content,
        [string[]]$fixes,
        [string]$backupDir,
        [string]$globalTargetFramework,
        [bool]$needsWindows
    )

    $backupPath = Backup-File -filePath $csproj.FullName -backupDir $backupDir
    $newContent = $content

    foreach ($fix in $fixes) {
        switch ($fix) {
            'RemoveOutputPath' {
                # 移除 OutputPath 行及其所在 PropertyGroup（如果只有 OutputPath）
                $newContent = Remove-XmlElementLines -content $newContent -elementName 'OutputPath'
                Write-Host "    [已修复] 移除 OutputPath" -ForegroundColor Green
            }
            'AddWindowsTargetFramework' {
                # 在第一个 PropertyGroup 中添加 TargetFramework
                $targetFramework = "$globalTargetFramework-windows"
                $newContent = $newContent -replace '(</PropertyGroup>)', "    <TargetFramework>$targetFramework</TargetFramework>`r`n  `$1", 1
                Write-Host "    [已修复] 添加 TargetFramework=$targetFramework" -ForegroundColor Green
            }
            'FixToWindowsTargetFramework' {
                $targetFramework = "$globalTargetFramework-windows"
                $newContent = $newContent -replace '<TargetFramework>[^<]+</TargetFramework>', "<TargetFramework>$targetFramework</TargetFramework>"
                Write-Host "    [已修复] 更新 TargetFramework 为 $targetFramework" -ForegroundColor Green
            }
            'AddUseWindowsForms' {
                $newContent = $newContent -replace '(</PropertyGroup>)', "    <UseWindowsForms>true</UseWindowsForms>`r`n  `$1", 1
                Write-Host "    [已修复] 添加 UseWindowsForms=true" -ForegroundColor Green
            }
            'RemoveRedundantAssemblyName' {
                $newContent = $newContent -replace '<AssemblyName>[^<]+</AssemblyName>\r?\n\s*', ''
                Write-Host "    [已修复] 移除冗余 AssemblyName" -ForegroundColor Green
            }
            'RemoveRedundantRootNamespace' {
                $newContent = $newContent -replace '<RootNamespace>[^<]+</RootNamespace>\r?\n\s*', ''
                Write-Host "    [已修复] 移除冗余 RootNamespace" -ForegroundColor Green
            }
        }
    }

    Set-Content -Path $csproj.FullName -Value $newContent -NoNewline
    Write-Host "    [备份] $backupPath" -ForegroundColor Gray
    return $true
}

# 导出公共函数
Export-ModuleMember -Function Initialize-NewProject
