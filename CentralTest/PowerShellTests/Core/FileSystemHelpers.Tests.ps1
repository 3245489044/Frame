<#
.SYNOPSIS
    Pester 测试：FileSystemHelpers.psm1

.DESCRIPTION
    测试 Script\Modules\Core\FileSystemHelpers.psm1 中的所有函数：
    - Backup-File
    - Get-DirectorySizeMB
    - Remove-DirectoryWithLog
    - Get-CsprojFiles
    - Get-BinObjDirectories
    - Test-ProjectReferences
    - Get-ChildDirectoriesByName
#>

BeforeAll {
    $script:ModulePath = Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) "Script\Modules\Core\FileSystemHelpers.psm1"
    Import-Module $script:ModulePath -Force
}

# ============================================
#  Backup-File 测试
# ============================================

Describe "Backup-File" {
    It "应创建带时间戳的备份文件" {
        $testFile = Join-Path $TestDrive "test.txt"
        Set-Content -Path $testFile -Value "test content" -Encoding UTF8

        $backupPath = Backup-File -filePath $testFile -backupDir (Join-Path $TestDrive "backups")
        $backupPath | Should -Not -BeNullOrEmpty
        Test-Path $backupPath | Should -Be $true
        Get-Content $backupPath -Raw | Should -Be "test content"
    }

    It "备份目录不存在时应自动创建" {
        $testFile = Join-Path $TestDrive "test2.txt"
        Set-Content -Path $testFile -Value "content" -Encoding UTF8

        $backupPath = Backup-File -filePath $testFile -backupDir (Join-Path $TestDrive "new_backups")
        Test-Path $backupPath | Should -Be $true
    }

    It "备份文件名应包含时间戳" {
        $testFile = Join-Path $TestDrive "test3.txt"
        Set-Content -Path $testFile -Value "content" -Encoding UTF8

        $backupPath = Backup-File -filePath $testFile -backupDir (Join-Path $TestDrive "backups3")
        $backupFileName = Split-Path $backupPath -Leaf
        $backupFileName | Should -Match '^\d{8}_\d{6}_'
    }
}

# ============================================
#  Get-DirectorySizeMB 测试
# ============================================

Describe "Get-DirectorySizeMB" {
    It "目录不存在时应返回 0" {
        Get-DirectorySizeMB -dirPath "C:\NonExistentDir_12345" | Should -Be 0
    }

    It "空目录时应返回 0" {
        $emptyDir = Join-Path $TestDrive "EmptyDir"
        New-Item -Path $emptyDir -ItemType Directory -Force | Out-Null
        Get-DirectorySizeMB -dirPath $emptyDir | Should -Be 0
    }

    It "应正确计算目录大小" {
        $dir = Join-Path $TestDrive "SizeTest"
        New-Item -Path $dir -ItemType Directory -Force | Out-Null
        # 创建 1KB 文件
        $bytes = [byte[]]::new(1024)
        [System.IO.File]::WriteAllBytes((Join-Path $dir "file1.bin"), $bytes)

        $size = Get-DirectorySizeMB -dirPath $dir
        $size | Should -BeGreaterThan 0
    }
}

# ============================================
#  Remove-DirectoryWithLog 测试
# ============================================

Describe "Remove-DirectoryWithLog" {
    It "目录存在时应成功删除并返回 $true" {
        $dir = Join-Path $TestDrive "ToDelete"
        New-Item -Path $dir -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $dir "file.txt") -ItemType File -Force | Out-Null

        $result = Remove-DirectoryWithLog -dirPath $dir
        $result | Should -Be $true
        Test-Path $dir | Should -Be $false
    }

    It "目录不存在时应返回 $false" {
        $result = Remove-DirectoryWithLog -dirPath "C:\NonExistentDir_12345"
        $result | Should -Be $false
    }
}

# ============================================
#  Get-CsprojFiles 测试
# ============================================

Describe "Get-CsprojFiles" {
    It "应返回指定目录下的 .csproj 文件" {
        $projectRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
        $files = Get-CsprojFiles -rootDir $projectRoot
        $files | Should -Not -BeNullOrEmpty
        $files[0].Extension | Should -Be ".csproj"
    }

    It "应支持排除指定路径" {
        $projectRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
        $files = Get-CsprojFiles -rootDir $projectRoot -excludePaths @("$projectRoot\Destoried")
        $files | Where-Object { $_.FullName -like "$projectRoot\Destoried\*" } | Should -BeNullOrEmpty
    }
}

# ============================================
#  Get-BinObjDirectories 测试
# ============================================

Describe "Get-BinObjDirectories" {
    It "应返回 csproj 所在目录的 bin/obj 路径" {
        $projectRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
        $csprojFiles = Get-CsprojFiles -rootDir $projectRoot
        $dirs = Get-BinObjDirectories -csprojFiles $csprojFiles
        $dirs | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  Test-ProjectReferences 测试
# ============================================

Describe "Test-ProjectReferences" {
    It "应检测不存在的 ProjectReference" {
        $projectRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
        $csprojFiles = Get-CsprojFiles -rootDir $projectRoot
        $issues = Test-ProjectReferences -csprojFiles $csprojFiles
        # 只验证函数能正常执行
        $issues | Should -Not -Be $null
    }
}

# ============================================
#  Get-ChildDirectoriesByName 测试
# ============================================

Describe "Get-ChildDirectoriesByName" {
    It "应查找指定名称的子目录" {
        $parent1 = Join-Path $TestDrive "Parent1"
        $parent2 = Join-Path $TestDrive "Parent2"
        New-Item -Path $parent1 -ItemType Directory -Force | Out-Null
        New-Item -Path $parent2 -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $parent1 "BuildOutput") -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $parent2 "BuildOutput") -ItemType Directory -Force | Out-Null

        $result = Get-ChildDirectoriesByName -parentDirs @($parent1, $parent2) -dirName "BuildOutput"
        $result.Count | Should -Be 2
    }

    It "应支持排除指定路径" {
        $parent1 = Join-Path $TestDrive "ExcludeParent"
        New-Item -Path $parent1 -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $parent1 "BuildOutput") -ItemType Directory -Force | Out-Null

        $result = Get-ChildDirectoriesByName -parentDirs @($parent1) -dirName "BuildOutput" -excludePath (Join-Path $parent1 "BuildOutput")
        $result.Count | Should -Be 0
    }

    It "目录不存在时应返回空数组" {
        $result = Get-ChildDirectoriesByName -parentDirs @("C:\NonExistentDir_12345") -dirName "BuildOutput"
        $result | Should -BeNullOrEmpty
    }
}
