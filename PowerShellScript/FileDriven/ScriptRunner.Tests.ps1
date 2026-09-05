<#
.SYNOPSIS
    Pester 测试：ScriptRunner.ps1

.DESCRIPTION
    测试 Script\Modules\FileDriven\ScriptRunner.ps1 中的所有函数：
    - New-ScriptRunner
    - Show-ScriptList
    - Show-ConfigInfo
    - Show-DirectoryStructure
    - Show-HelpText
    - Show-FileContent
    - Show-ActionList
#>

BeforeAll {
    # 获取项目根目录（从 PowerShellTests\FileDriven 上移 2 级）
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent


    # 直接点引入加载被测试脚本（ScriptRunner 依赖 FileDrivenTestUtils）
    $testUtilsPath = Join-Path $script:ProjectRoot "PowerShellScript\Modules\FileDriven\FileDrivenTestUtils.ps1"
    . $testUtilsPath
    $scriptRunnerPath = Join-Path $script:ProjectRoot "PowerShellScript\Modules\FileDriven\ScriptRunner.ps1"
    . $scriptRunnerPath

    # 创建测试配置
    $script:TestConfig = @{
        ProjectRoot   = $script:ProjectRoot
        FileDrivenDir = Join-Path $script:ProjectRoot "CsharpPrj\FileDriven"
        ScriptsDir    = Join-Path $script:ProjectRoot "CsharpPrj\FileDriven\DrivenConfig\Scripts"
        Aliases       = @{
            "Output" = Join-Path $script:ProjectRoot "BuildOutput\Debug"
            "Config" = Join-Path $script:ProjectRoot "CsharpPrj\FileDriven\DrivenConfig"
        }
    }

    # 创建测试运行动作
    $script:TestRunAction = {
        param([string]$Action, [string]$Arg = "")
        Write-Host "RunAction invoked: Action=$Action, Arg=$Arg"
    }
}

# ============================================
#  New-ScriptRunner 测试
# ============================================

Describe "New-ScriptRunner" {
    It "应创建包含 Invoke 方法的运行器对象" {
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction

        $runner | Should -Not -BeNullOrEmpty
        $runner.Config | Should -Not -BeNullOrEmpty
        $runner.RunAction | Should -Not -BeNullOrEmpty
        $runner | Should -HaveMember "Invoke"
    }

    It "应使用默认标题" {
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction
        $runner.Title | Should -Be "脚本运行器"
    }

    It "应支持自定义标题" {
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction -Title "自定义标题"
        $runner.Title | Should -Be "自定义标题"
    }

    It "应支持自定义 ExtraCommands" {
        $extraCommands = @{ "build" = "构建脚本"; "deploy" = "部署" }
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction -ExtraCommands $extraCommands

        $runner.ExtraCommands.Keys.Count | Should -Be 2
        $runner.ExtraCommands["build"] | Should -Be "构建脚本"
    }

    It "应支持自定义 FilePattern" {
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction -FilePattern ".ps1|.psm1"
        $runner.FilePattern | Should -Be ".ps1|.psm1"
    }

    It "应包含所有必需的方法" {
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction

        $runner | Should -HaveMember "ShowList"
        $runner | Should -HaveMember "ShowInfo"
        $runner | Should -HaveMember "ShowDirs"
        $runner | Should -HaveMember "ShowHelp"
        $runner | Should -HaveMember "ShowFile"
        $runner | Should -HaveMember "ShowActionableList"
        $runner | Should -HaveMember "ShowInteractive"
        $runner | Should -HaveMember "InvokeRunActionSafe"
    }
}

# ============================================
#  Show-ScriptList 测试
# ============================================

Describe "Show-ScriptList" {
    It "应列出指定目录中的脚本文件" {
        $scriptsDir = Join-Path $script:ProjectRoot "CsharpPrj\FileDriven\DrivenConfig\Scripts"
        if (Test-Path $scriptsDir) {
            $output = & { Show-ScriptList -ScriptsDir $scriptsDir } 6>&1
            $output.Count | Should -BeGreaterThan 0
            $output | Where-Object { $_ -match "可用脚本" } | Should -Not -BeNullOrEmpty
        }
    }

    It "应支持自定义 FilePattern" {
        New-Item -Path (Join-Path $TestDrive "test.json") -ItemType File -Force | Out-Null
        New-Item -Path (Join-Path $TestDrive "test.txt") -ItemType File -Force | Out-Null

        $output = & { Show-ScriptList -ScriptsDir $TestDrive -FilePattern ".json" } 6>&1
        $output.Count | Should -BeGreaterThan 0
    }

    It "目录为空时应只显示标题" {
        New-Item -Path (Join-Path $TestDrive "EmptyDir") -ItemType Directory -Force | Out-Null
        $output = & { Show-ScriptList -ScriptsDir (Join-Path $TestDrive "EmptyDir") } 6>&1
        $output.Count | Should -BeGreaterThan 0
    }
}

# ============================================
#  Show-ConfigInfo 测试
# ============================================

Describe "Show-ConfigInfo" {
    It "应显示配置信息" {
        $output = & { Show-ConfigInfo -Config $script:TestConfig -RunMode "auto" -Title "测试配置" } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output | Where-Object { $_ -match "测试配置" } | Should -Not -BeNullOrEmpty
        $output | Where-Object { $_ -match "项目根目录" } | Should -Not -BeNullOrEmpty
    }

    It "应显示运行模式" {
        $output = & { Show-ConfigInfo -Config $script:TestConfig -RunMode "manual" } 6>&1
        $output | Where-Object { $_ -match "manual" } | Should -Not -BeNullOrEmpty
    }

    It "应显示路径别名" {
        $output = & { Show-ConfigInfo -Config $script:TestConfig } 6>&1
        $output | Where-Object { $_ -match "FILEDRIVEN_ALIAS_" } | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  Show-DirectoryStructure 测试
# ============================================

Describe "Show-DirectoryStructure" {
    It "应显示目录结构" {
        $testDir = Join-Path $TestDrive "ShowDirsTest"
        New-Item -Path $testDir -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $testDir "SubDir1") -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $testDir "SubDir2") -ItemType Directory -Force | Out-Null

        $output = & { Show-DirectoryStructure -RootPath $testDir } 6>&1
        $output.Count | Should -BeGreaterThan 0
    }

    It "路径不存在时应显示标题" {
        $output = & { Show-DirectoryStructure -RootPath "C:\NonExistentPath_12345" } 6>&1
        $output.Count | Should -BeGreaterThan 0
    }
}

# ============================================
#  Show-HelpText 测试
# ============================================

Describe "Show-HelpText" {
    It "应显示帮助文本" {
        $output = & { Show-HelpText -ScriptName "test.ps1" -Title "测试运行器" } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output | Where-Object { $_ -match "test.ps1" } | Should -Not -BeNullOrEmpty
        $output | Where-Object { $_ -match "测试运行器" } | Should -Not -BeNullOrEmpty
    }

    It "应显示 ExtraCommands" {
        $extraCommands = @{ "log" = "查看日志" }
        $output = & { Show-HelpText -ScriptName "test.ps1" -ExtraCommands $extraCommands } 6>&1
        $output | Where-Object { $_ -match "log" } | Should -Not -BeNullOrEmpty
        $output | Where-Object { $_ -match "查看日志" } | Should -Not -BeNullOrEmpty
    }

    It "应显示用法说明" {
        $output = & { Show-HelpText -ScriptName "test.ps1" } 6>&1
        $output | Where-Object { $_ -match "用法" } | Should -Not -BeNullOrEmpty
        $output | Where-Object { $_ -match "--list" } | Should -Not -BeNullOrEmpty
        $output | Where-Object { $_ -match "--help" } | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  Show-FileContent 测试
# ============================================

Describe "Show-FileContent" {
    It "文件存在时应显示文件内容" {
        New-Item -Path (Join-Path $TestDrive "test.json") -ItemType File -Force | Out-Null
        Set-Content -Path (Join-Path $TestDrive "test.json") -Value '{ "name": "test" }' -Encoding UTF8

        $output = & { Show-FileContent -FileName "test.json" -ScriptsDir $TestDrive -FilePattern ".json" } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output | Where-Object { $_ -match "test.json" } | Should -Not -BeNullOrEmpty
    }

    It "文件不存在时应显示错误并列出可用文件" {
        New-Item -Path (Join-Path $TestDrive "available.json") -ItemType File -Force | Out-Null

        $output = & { Show-FileContent -FileName "nonexistent.json" -ScriptsDir $TestDrive -FilePattern ".json" } 6>&1
        $output | Where-Object { $_ -match "不存在" } | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  Show-ActionList 测试
# ============================================

Describe "Show-ActionList" {
    It "应显示可操作列表" {
        $output = & { Show-ActionList -Title "测试运行器" -ScriptName "test.ps1" -RunMode "auto" } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output | Where-Object { $_ -match "测试运行器" } | Should -Not -BeNullOrEmpty
        $output | Where-Object { $_ -match "运行脚本" } | Should -Not -BeNullOrEmpty
    }

    It "应显示当前模式" {
        $output = & { Show-ActionList -RunMode "manual" } 6>&1
        $output | Where-Object { $_ -match "manual" } | Should -Not -BeNullOrEmpty
    }

    It "应显示 ExtraCommands" {
        $extraCommands = @{ "build" = "构建脚本" }
        $output = & { Show-ActionList -ExtraCommands $extraCommands } 6>&1
        $output | Where-Object { $_ -match "构建脚本" } | Should -Not -BeNullOrEmpty
    }

    It "应显示脚本目录" {
        $output = & { Show-ActionList -ScriptsDir "D:\test\scripts" } 6>&1
        $output | Where-Object { $_ -match "D:\\test\\scripts" } | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  Invoke 方法测试
# ============================================

Describe "ScriptRunner.Invoke 方法" {
    It "--list 参数应调用 ShowList" {
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction
        & $runner.Invoke -List
    }

    It "--info 参数应调用 ShowInfo" {
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction
        & $runner.Invoke -Info
    }

    It "--help 参数应调用 ShowHelp" {
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction
        & $runner.Invoke -Help
    }



    It "不传参时应进入交互模式" {
        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $script:TestRunAction
        # 交互模式会调用 SelectScript，这里只验证不抛出异常
    }
}

# ============================================
#  InvokeRunActionSafe 测试
# ============================================

Describe "InvokeRunActionSafe" {
    It "应安全调用 RunAction" {
        $invokedAction = $null
        $invokedArg = $null
        $runAction = {
            param([string]$Action, [string]$Arg = "")
            $script:invokedAction = $Action
            $script:invokedArg = $Arg
        }

        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $runAction
        $runner.InvokeRunActionSafe("run", "test.json")

        $script:invokedAction | Should -Be "run"
        $script:invokedArg | Should -Be "test.json"
    }

    It "RunAction 抛出异常时应捕获而不中断" {
        $runAction = {
            param([string]$Action, [string]$Arg = "")
            throw "测试异常"
        }

        $runner = New-ScriptRunner -Config $script:TestConfig -RunAction $runAction
        $runner.InvokeRunActionSafe("run", "test.json")
    }
}
