# CsprojFixActions.psm1 - .csproj 修复操作模块
# 提供 .csproj 文件的具体修复操作
# 依赖：XmlHelpers.psm1, FileSystemHelpers.psm1, InteractiveHelpers.psm1
#
# 本模块是 CsprojFixEngine 的子模块，专注于修复功能
# ★ XML 命名空间常量已由 XmlHelpers.psm1 统一提供，此处不再重复定义

<#
.SYNOPSIS
    修复无效的 TargetFramework（如 net10.0-windows7.0 → net10.0-windows）
.DESCRIPTION
    检查所有 .csproj 文件中是否存在无效的 TargetFramework 值，并修复
.PARAMETER csprojFiles
    csproj 文件列表
.PARAMETER invalidTfmMap
    无效 TFM 映射表（旧值→新值）
.PARAMETER backupDir
    备份目录路径
.OUTPUTS
    [int] 修复的项目数量
#>
function Fix-InvalidTargetFramework {
    param(
        [System.IO.FileInfo[]]$csprojFiles,
        [hashtable]$invalidTfmMap,
        [string]$backupDir
    )
    $invalidTfProjects = @()
    foreach ($csproj in $csprojFiles) {
        $content = Get-Content -Path $csproj.FullName -Raw
        foreach ($invalidTfm in $invalidTfmMap.Keys) {
            $escapedInvalid = [regex]::Escape($invalidTfm)
            if ($content -match "<TargetFramework>$escapedInvalid</TargetFramework>") {
                $invalidTfProjects += @{
                    File = $csproj
                    OldValue = $invalidTfm
                    NewValue = $invalidTfmMap[$invalidTfm]
                }
                break
            }
        }
    }

    $fixed = Confirm-And-FixProjects -projectList ($invalidTfProjects | ForEach-Object { "$($_.File.Name): $($_.OldValue) → $($_.NewValue)" }) `
        -displayLabel "使用了无效 TargetFramework 的项目" `
        -confirmMessage "确认修复以上 TargetFramework 配置？(Y/N)" `
        -fixAction {
            foreach ($item in $invalidTfProjects) {
                $backupPath = Backup-File -filePath $item.File.FullName -backupDir $backupDir
                $content = Get-Content -Path $item.File.FullName -Raw
                $escapedOld = [regex]::Escape($item.OldValue)
                $newContent = $content -replace "<TargetFramework>$escapedOld</TargetFramework>",
                                                "<TargetFramework>$($item.NewValue)</TargetFramework>"
                Set-Content -Path $item.File.FullName -Value $newContent -NoNewline
                Write-Host "  [已修复] $($item.File.Name) (备份: $backupPath)" -ForegroundColor Green
            }
        }
    if ($fixed) { return $invalidTfProjects.Count } else { return 0 }
}

<#
.SYNOPSIS
    移除自定义的 OutputPath
.DESCRIPTION
    检查并移除 .csproj 中自定义的 OutputPath 配置，让全局配置生效
.PARAMETER csprojFiles
    csproj 文件列表
.PARAMETER backupDir
    备份目录路径
.OUTPUTS
    [int] 修复的项目数量
#>
function Fix-CustomOutputPath {
    param(
        [System.IO.FileInfo[]]$csprojFiles,
        [string]$backupDir
    )
    $customOutputProjects = @()
    foreach ($csproj in $csprojFiles) {
        $content = Get-Content -Path $csproj.FullName -Raw
        if (Test-XmlElementExists -content $content -elementName 'OutputPath') {
            $customOutputProjects += $csproj
        }
    }

    $fixed = Confirm-And-FixProjects -projectList ($customOutputProjects | ForEach-Object { $_.FullName }) `
        -displayLabel "有自定义 OutputPath 的项目" `
        -confirmMessage "确认移除以上项目的自定义 OutputPath？(Y/N)" `
        -fixAction {
            foreach ($csproj in $customOutputProjects) {
                $backupPath = Backup-File -filePath $csproj.FullName -backupDir $backupDir
                $xml, $content = Read-XmlFile -filePath $csproj.FullName
                if ($xml -ne $null) {
                    $nsmgr = Get-MsbuildNamespaceManager -xml $xml
                    $outputPathNodes = $xml.SelectNodes('//default:OutputPath', $nsmgr)
                    foreach ($node in $outputPathNodes) {
                        [void]$node.ParentNode.RemoveChild($node)
                    }
                    $xml.Save($csproj.FullName)
                } else {
                    $newContent = Remove-XmlElementLines -content $content -elementName 'OutputPath'
                    Set-Content -Path $csproj.FullName -Value $newContent -NoNewline
                }
                Write-Host "  [已修复] $($csproj.Name) (备份: $backupPath)" -ForegroundColor Green
            }
        }
    if ($fixed) { return $customOutputProjects.Count } else { return 0 }
}

<#
.SYNOPSIS
    补充缺失的 UseWindowsForms
.DESCRIPTION
    检查引用 WinForms 项目或可执行文件项目是否缺少 UseWindowsForms 配置
.PARAMETER csprojFiles
    csproj 文件列表
.PARAMETER winFormsDependentProjects
    需要 WinForms 的项目引用列表
.PARAMETER backupDir
    备份目录路径
.OUTPUTS
    [int] 修复的项目数量
#>
function Fix-MissingUseWindowsForms {
    param(
        [System.IO.FileInfo[]]$csprojFiles,
        [string[]]$winFormsDependentProjects,
        [string]$backupDir
    )
    $missingUseWinForms = @()
    foreach ($csproj in $csprojFiles) {
        $content = Get-Content -Path $csproj.FullName -Raw

        if (Test-XmlElementExists -content $content -elementName 'UseWindowsForms') { continue }

        $hasWindowsTf = ($content -match '<TargetFramework>.*windows</TargetFramework>') -or
                        ($content -match '<TargetFrameworks>.*windows.*</TargetFrameworks>')

        if (-not $hasWindowsTf) { continue }

        $referencesWinForms = $false
        foreach ($dep in $winFormsDependentProjects) {
            if ($content -match [regex]::Escape($dep)) {
                $referencesWinForms = $true
                break
            }
        }

        $isExe = ($content -match '<OutputType>WinExe</OutputType>') -or
                 ($content -match '<OutputType>Exe</OutputType>')

        if ($referencesWinForms -or $isExe) {
            $missingUseWinForms += $csproj
        }
    }

    $fixed = Confirm-And-FixProjects -projectList ($missingUseWinForms | ForEach-Object { "$($_.Name) ($($_.DirectoryName))" }) `
        -displayLabel "可能缺少 UseWindowsForms 配置的项目" `
        -confirmMessage "确认为以上项目添加 UseWindowsForms=true？(Y/N)" `
        -fixAction {
            foreach ($csproj in $missingUseWinForms) {
                $backupPath = Backup-File -filePath $csproj.FullName -backupDir $backupDir
                $content = Get-Content -Path $csproj.FullName -Raw

                $xml, $null = Read-XmlFile -filePath $csproj.FullName
                $inserted = $false
                if ($xml -ne $null) {
                    $nsmgr = Get-MsbuildNamespaceManager -xml $xml
                    # ★ Bug 修复：同时尝试带命名空间和不带命名空间的 XPath 查询
                    #   因为有些 .csproj 文件（如 Share.Diagnostics.csproj）没有 xmlns 属性
                    $propertyGroups = $xml.SelectNodes("//default:PropertyGroup", $nsmgr)
                    if ($propertyGroups.Count -eq 0) {
                        # 没有命名空间时，使用无前缀的 XPath
                        $propertyGroups = $xml.SelectNodes("//PropertyGroup")
                    }
                    foreach ($pg in $propertyGroups) {
                        $tfNode = $pg.SelectSingleNode("default:TargetFramework", $nsmgr)
                        if ($tfNode -eq $null) {
                            # 也尝试不带命名空间的查询
                            $tfNode = $pg.SelectSingleNode("TargetFramework")
                        }
                        if ($tfNode -ne $null) {
                            # ★ 根据文件是否有默认命名空间动态决定是否使用命名空间
                            #   有些 .csproj 文件（如 Share.Diagnostics.csproj）没有 xmlns 属性
                            $hasDefaultNs = $xml.DocumentElement.NamespaceURI -ne ''
                            $ns = if ($hasDefaultNs) { "http://schemas.microsoft.com/developer/msbuild/2003" } else { "" }
                            $useWinFormsNode = $xml.CreateElement("UseWindowsForms", $ns)
                            $useWinFormsNode.InnerText = "true"
                            [void]$pg.InsertAfter($useWinFormsNode, $tfNode)
                            $inserted = $true
                            break
                        }
                    }
                    if ($inserted) {
                        $xml.Save($csproj.FullName)
                    }
                }

                if (-not $inserted) {
                    # ★ 改进：匹配包含 TargetFramework 的 PropertyGroup，避免插入到错误的 PropertyGroup 中
                    #   例如条件编译的 PropertyGroup（Condition="'$(Configuration)'=='Release'"）
                    $newContent = [regex]::Replace($content, 
                        '(<TargetFramework>[^<]+</TargetFramework>\s*</PropertyGroup>)',
                        "`$1`r`n    <UseWindowsForms>true</UseWindowsForms>", 1)
                    Set-Content -Path $csproj.FullName -Value $newContent -NoNewline
                }

                Write-Host "  [已修复] $($csproj.Name) (备份: $backupPath)" -ForegroundColor Green
            }
        }
    if ($fixed) { return $missingUseWinForms.Count } else { return 0 }
}

<#
.SYNOPSIS
    移除与全局一致的冗余 TargetFramework 配置
.DESCRIPTION
    检查 .csproj 中显式设置的 TargetFramework 是否与全局一致，一致则移除
.PARAMETER csprojFiles
    csproj 文件列表
.PARAMETER globalTargetFramework
    全局 TargetFramework 值
.PARAMETER backupDir
    备份目录路径
.OUTPUTS
    [int] 修复的项目数量
#>
function Fix-RedundantTargetFramework {
    param(
        [System.IO.FileInfo[]]$csprojFiles,
        [string]$globalTargetFramework,
        [string]$backupDir
    )
    $redundantTfProjects = @()
    foreach ($csproj in $csprojFiles) {
        $content = Get-Content -Path $csproj.FullName -Raw

        if ($content -match "<TargetFramework>$globalTargetFramework</TargetFramework>") {
            $xml, $null = Read-XmlFile -filePath $csproj.FullName
            if ($xml -ne $null) {
                $nsmgr = Get-MsbuildNamespaceManager -xml $xml
                $tfNodes = Select-Xml -Xml $xml -XPath "//default:TargetFramework[text()='$globalTargetFramework']" -Namespace $nsmgr | ForEach-Object { $_.Node }
                foreach ($node in $tfNodes) {
                    if ($node.Attributes['Condition'] -eq $null) {
                        $redundantTfProjects += @{
                            File = $csproj
                            Node = $node
                            Xml = $xml
                        }
                    }
                }
            }
        }
    }

    # 按文件分组处理，避免同一文件多次操作导致 XML 状态不一致
    $redundantByFile = @{}
    foreach ($item in $redundantTfProjects) {
        $filePath = $item.File.FullName
        if (-not $redundantByFile.ContainsKey($filePath)) {
            $redundantByFile[$filePath] = @{
                File = $item.File
                Xml = $item.Xml
                Nodes = @()
            }
        }
        $redundantByFile[$filePath].Nodes += $item.Node
    }

    # 按文件去重后生成显示列表，避免同一文件重复列出
    $displayList = $redundantByFile.Keys | ForEach-Object {
        $entry = $redundantByFile[$_]
        "$($entry.File.Name): TargetFramework=$globalTargetFramework ($($entry.Nodes.Count) 个节点)"
    }

    $fixed = Confirm-And-FixProjects -projectList $displayList `
        -displayLabel "TargetFramework 与全局一致（可移除）的项目" `
        -confirmMessage "确认移除以上冗余的 TargetFramework 配置？(Y/N)" `
        -fixAction {
            foreach ($key in $redundantByFile.Keys) {
                $entry = $redundantByFile[$key]
                $backupPath = Backup-File -filePath $entry.File.FullName -backupDir $backupDir
                foreach ($node in $entry.Nodes) {
                    [void]$node.ParentNode.RemoveChild($node)
                }
                $entry.Xml.Save($entry.File.FullName)
                Write-Host "  [已修复] $($entry.File.Name) (备份: $backupPath)" -ForegroundColor Green
            }
        }
    if ($fixed) { return $redundantByFile.Count } else { return 0 }
}

<#
.SYNOPSIS
    修复 Directory.Build.props 中的 $(SolutionDir) 问题
.DESCRIPTION
    将 Directory.Build.props 中的 $(SolutionDir) 替换为 $(MSBuildThisFileDirectory)
.PARAMETER directoryBuildPropsPath
    Directory.Build.props 文件路径
.PARAMETER backupDir
    备份目录路径
.OUTPUTS
    [bool] 执行了修复返回 $true，否则 $false
#>
function Fix-SolutionDirInDirectoryBuildProps {
    param(
        [string]$directoryBuildPropsPath,
        [string]$backupDir
    )
    if (-not (Test-Path $directoryBuildPropsPath)) {
        Write-Host "  Directory.Build.props 文件不存在: $directoryBuildPropsPath" -ForegroundColor Yellow
        return $false
    }

    $dbpContent = Get-Content -Path $directoryBuildPropsPath -Raw

    if ($dbpContent -match '\$\(SolutionDir\)') {
        Write-Host "  发现 Directory.Build.props 使用了 SolutionDir 变量：" -ForegroundColor Yellow
        Write-Host "    - 使用了 SolutionDir 变量（非 .sln 构建时未定义，会导致输出路径异常）" -ForegroundColor Magenta
        Write-Host ""
        Write-Host "  建议将 $(SolutionDir) 替换为 $(MSBuildThisFileDirectory)" -ForegroundColor Gray
        Write-Host "  $(MSBuildThisFileDirectory) 始终指向 Directory.Build.props 所在目录，不依赖 .sln 文件" -ForegroundColor Gray
        Write-Host ""
        $confirm = Read-Host "确认修复 Directory.Build.props 中的 SolutionDir 引用？(Y/N)"
        if ($confirm -eq 'Y' -or $confirm -eq 'y') {
            $backupPath = Backup-File -filePath $directoryBuildPropsPath -backupDir $backupDir
            $newContent = $dbpContent -replace '\$\(SolutionDir\)', '$(MSBuildThisFileDirectory)'
            Set-Content -Path $directoryBuildPropsPath -Value $newContent -NoNewline
            Write-Host "  [已修复] Directory.Build.props (备份: $backupPath)" -ForegroundColor Green
            Write-Host "  已将 $(SolutionDir) 替换为 $(MSBuildThisFileDirectory)" -ForegroundColor Green
            return $true
        }
        Write-Host "  已跳过 Directory.Build.props 修复。" -ForegroundColor Gray
        return $false
    }
    Write-Host "  Directory.Build.props 未使用 SolutionDir 变量，配置正确。" -ForegroundColor Green
    return $false
}

# 导出公共函数
Export-ModuleMember -Function Fix-InvalidTargetFramework,
                               Fix-CustomOutputPath,
                               Fix-MissingUseWindowsForms,
                               Fix-RedundantTargetFramework,
                               Fix-SolutionDirInDirectoryBuildProps
