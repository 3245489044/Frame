<#
.SYNOPSIS
    Pester 测试：FileDrivenTestUtils.ps1

.DESCRIPTION
    测试 Script\Modules\FileDriven\FileDrivenTestUtils.ps1 中的所有函数：
    - Get-FileDrivenConfig
    - Get-RunMode / Set-RunMode / Switch-RunMode
    - Write-Header / Write-Info
    - Set-AliasEnvironment
    - Get-ScriptFiles
    - Find-LauncherDir

.NOTES
    从 Script\FileDriven\ 迁移至 PowerShellTests\FileDriven\
#>

BeforeAll {
    # 获取项目根目录（从 PowerShellTests\FileDriven 上移 3 级）
    $script:ProjectRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent

    # 直接点引入加载被测试脚本（在 BeforeAll 中加载到全局作用域）
    $testUtilsPath = Join-Path $script:ProjectRoot "Script\Modules\FileDriven\FileDrivenTestUtils.ps1"
    . $testUtilsPath
}

# ============================================
#  Get-FileDrivenConfig 测试
# ============================================

Describe "Get-FileDrivenConfig" {
    It "应返回包含所有必需键的哈希表" {
        $config = Get-FileDrivenConfig -ScriptPath (Join-Path $script:ProjectRoot "CentralConsole\FileDriven.Tests\FileDriven\LogViewer.ps1")

        $config | Should -Not -BeNullOrEmpty
        $config.ContainsKey("ProjectRoot") | Should -Be $true
        $config.ContainsKey("FileDrivenDir") | Should -Be $true
        $config.ContainsKey("ScriptsDir") | Should -Be $true
        $config.ContainsKey("Aliases") | Should -Be $true
        $config.ContainsKey("BatToolConfigPath") | Should -Be $true
    }

    It "应正确计算项目根目录（默认上移 3 级）" {
        $config = Get-FileDrivenConfig -ScriptPath (Join-Path $script:ProjectRoot "CentralConsole\FileDriven.Tests\FileDriven\LogViewer.ps1")

        $config.ProjectRoot | Should -Be $script:ProjectRoot
    }

    It "应正确计算 FileDriven 目录" {
        $config = Get-FileDrivenConfig -ScriptPath (Join-Path $script:ProjectRoot "CentralConsole\FileDriven.Tests\FileDriven\LogViewer.ps1")

        $config.FileDrivenDir | Should -Be (Join-Path $script:ProjectRoot "FileDriven")
    }

    It "应正确计算 ScriptsDir 目录" {
        $config = Get-FileDrivenConfig -ScriptPath (Join-Path $script:ProjectRoot "CentralConsole\FileDriven.Tests\FileDriven\LogViewer.ps1")

        $config.ScriptsDir | Should -Be (Join-Path $script:ProjectRoot "FileDriven\DrivenConfig\Scripts")
    }

    It "应支持自定义上移级数" {
        $config = Get-FileDrivenConfig -ScriptPath (Join-Path $script:ProjectRoot "FileDriven\SomeScript.ps1") -ProjectRootUpLevels 1

        $config.ProjectRoot | Should -Be $script:ProjectRoot
    }
}

# ============================================
#  Get-RunMode 测试
# ============================================

Describe "Get-RunMode" {
    BeforeEach {
        Remove-Item -Path "env:FILEDRIVEN_RUN_MODE" -ErrorAction SilentlyContinue
    }

    It "未设置环境变量时应返回默认值 'auto'" {
        $mode = Get-RunMode
        $mode | Should -Be "auto"
    }

    It "应支持自定义默认值" {
        $mode = Get-RunMode -Default "manual"
        $mode | Should -Be "manual"
    }

    It "环境变量设置为 'manual' 时应返回 'manual'" {
        $env:FILEDRIVEN_RUN_MODE = "manual"
        $mode = Get-RunMode
        $mode | Should -Be "manual"
    }

    It "环境变量设置为 'auto' 时应返回 'auto'" {
        $env:FILEDRIVEN_RUN_MODE = "auto"
        $mode = Get-RunMode
        $mode | Should -Be "auto"
    }

    It "环境变量为空字符串时应返回默认值" {
        $env:FILEDRIVEN_RUN_MODE = ""
        $mode = Get-RunMode
        $mode | Should -Be "auto"
    }

    It "环境变量为空白字符串时应返回默认值" {
        $env:FILEDRIVEN_RUN_MODE = "   "
        $mode = Get-RunMode
        $mode | Should -Be "auto"
    }
}

# ============================================
#  Set-RunMode 测试
# ============================================

Describe "Set-RunMode" {
    AfterEach {
        Remove-Item -Path "env:FILEDRIVEN_RUN_MODE" -ErrorAction SilentlyContinue
    }

    It "应设置环境变量为 'auto'" {
        Set-RunMode -Mode "auto"
        $env:FILEDRIVEN_RUN_MODE | Should -Be "auto"
    }

    It "应设置环境变量为 'manual'" {
        Set-RunMode -Mode "manual"
        $env:FILEDRIVEN_RUN_MODE | Should -Be "manual"
    }

    It "应覆盖已存在的环境变量值" {
        $env:FILEDRIVEN_RUN_MODE = "auto"
        Set-RunMode -Mode "manual"
        $env:FILEDRIVEN_RUN_MODE | Should -Be "manual"
    }
}

# ============================================
#  Switch-RunMode 测试
# ============================================

Describe "Switch-RunMode" {
    AfterEach {
        Remove-Item -Path "env:FILEDRIVEN_RUN_MODE" -ErrorAction SilentlyContinue
    }

    It "从 auto 切换到 manual" {
        $env:FILEDRIVEN_RUN_MODE = "auto"
        $result = Switch-RunMode
        $result | Should -Be "manual"
        $env:FILEDRIVEN_RUN_MODE | Should -Be "manual"
    }

    It "从 manual 切换到 auto" {
        $env:FILEDRIVEN_RUN_MODE = "manual"
        $result = Switch-RunMode
        $result | Should -Be "auto"
        $env:FILEDRIVEN_RUN_MODE | Should -Be "auto"
    }

    It "未设置时默认从 auto 切换到 manual" {
        $result = Switch-RunMode
        $result | Should -Be "manual"
    }
}

# ============================================
#  Write-Header / Write-Info 测试
# ============================================

Describe "Write-Header" {
    It "应输出带等号线框的标题" {
        # Write-Host 输出到信息流（6），需要捕获
        $output = & { Write-Header "测试标题" } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output[0] | Should -Be ""
        $output[1] | Should -Match "===="
        $output[2] | Should -Match "测试标题"
    }

    It "应处理空标题" {
        $output = & { Write-Header "" } 6>&1
        $output.Count | Should -BeGreaterThan 0
    }
}

Describe "Write-Info" {
    It "应输出标签-值格式的信息" {
        $output = & { Write-Info -Label "名称" -Value "测试值" } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output[0] | Should -Match "名称"
        $output[0] | Should -Match "测试值"
    }

    It "应处理空标签和空值" {
        $output = & { Write-Info -Label "" -Value "" } 6>&1
        $output.Count | Should -BeGreaterThan 0
    }
}

# ============================================
#  Set-AliasEnvironment 测试
# ============================================

Describe "Set-AliasEnvironment" {
    BeforeEach {
        Get-ChildItem "env:FILEDRIVEN_ALIAS_*" -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item "env:$($_.Name)" -ErrorAction SilentlyContinue
        }
    }

    It "应将别名注入到环境变量" {
        $aliases = @{
            "Output" = "D:\output"
            "Config" = "D:\config"
        }
        Set-AliasEnvironment -Aliases $aliases

        $env:FILEDRIVEN_ALIAS_Output | Should -Be "D:\output"
        $env:FILEDRIVEN_ALIAS_Config | Should -Be "D:\config"
    }

    It "应支持覆盖别名值" {
        $aliases = @{
            "Output" = "D:\original"
        }
        $overrides = @{
            "Output" = "D:\overridden"
        }
        Set-AliasEnvironment -Aliases $aliases -Overrides $overrides

        $env:FILEDRIVEN_ALIAS_Output | Should -Be "D:\overridden"
    }

    It "应处理空别名哈希表" {
        Set-AliasEnvironment -Aliases @{}
    }
}

# ============================================
#  Get-ScriptFiles 测试
# ============================================

Describe "Get-ScriptFiles" {
    It "应返回指定目录中的脚本文件" {
        $scriptsDir = Join-Path $script:ProjectRoot "FileDriven\DrivenConfig\Scripts"
        if (Test-Path $scriptsDir) {
            $files = Get-ScriptFiles -ScriptsDir $scriptsDir
            $files | Should -Not -BeNullOrEmpty
        }
    }

    It "应支持自定义文件扩展名过滤" {
        New-Item -Path (Join-Path $TestDrive "test.json") -ItemType File -Force | Out-Null
        New-Item -Path (Join-Path $TestDrive "test.txt") -ItemType File -Force | Out-Null
        New-Item -Path (Join-Path $TestDrive "test.xml") -ItemType File -Force | Out-Null

        $files = Get-ScriptFiles -ScriptsDir $TestDrive -FilePattern ".json|.txt"
        $files.Count | Should -Be 2
    }

    It "应支持递归扫描子目录" {
        New-Item -Path (Join-Path $TestDrive "SubDir") -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $TestDrive "SubDir\nested.json") -ItemType File -Force | Out-Null

        $files = Get-ScriptFiles -ScriptsDir $TestDrive -FilePattern ".json" -Recurse
        $files.Count | Should -BeGreaterThan 0
    }

    It "应返回按名称排序的文件" {
        New-Item -Path (Join-Path $TestDrive "b_file.json") -ItemType File -Force | Out-Null
        New-Item -Path (Join-Path $TestDrive "a_file.json") -ItemType File -Force | Out-Null

        $files = Get-ScriptFiles -ScriptsDir $TestDrive -FilePattern ".json"
        $files[0].Name | Should -Be "a_file.json"
        $files[1].Name | Should -Be "b_file.json"
    }

    It "目录不存在时应返回空数组" {
        $files = Get-ScriptFiles -ScriptsDir "C:\NonExistentDir_12345"
        $files | Should -BeNullOrEmpty
    }
}

# ============================================
#  Find-LauncherDir 测试
# ============================================

Describe "Find-LauncherDir" {
    It "应委托给 FindLauncherDir.ps1 并返回路径" {
        $findScriptPath = Join-Path $script:ProjectRoot "Script\Modules\FileDriven\FindLauncherDir.ps1"
        $fileDrivenDir = Join-Path $script:ProjectRoot "FileDriven"

        if (Test-Path $findScriptPath) {
            $result = Find-LauncherDir -FindScriptPath $findScriptPath -FileDrivenDir $fileDrivenDir
            $result | Should -Not -Be $null
        }
    }

    It "FindScriptPath 不存在时应返回空" {
        $result = Find-LauncherDir -FindScriptPath "C:\NonExistent\FindLauncherDir.ps1" -FileDrivenDir "D:\test"
        $result | Should -BeNullOrEmpty
    }
}
