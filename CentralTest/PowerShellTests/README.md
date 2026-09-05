# PowerShell 测试框架

## 概述

本项目使用 [Pester 5.x](https://pester.dev/) 作为 PowerShell 单元测试框架，对 `Script\Modules\FileDriven\` 下的模块脚本和 `CentralConsole\FileDriven.Tests\FileDriven\LogViewer.ps1` 进行单元测试。

## 环境要求

| 组件 | 版本 | 安装方式 |
|------|------|---------|
| PowerShell | 7.x 或 5.1 | [安装指南](../docs/Project/PowerShell测试环境搭建指南.md) |
| Pester | 5.x | `Install-Module -Name Pester -Force -SkipPublisherCheck` |
| NuGet 提供程序 | 最新 | `Install-PackageProvider -Name NuGet -Force` |

一键安装脚本：`Tools\Setup-PesterTestEnv.ps1`（需管理员身份运行）

## 目录结构

```
PowerShellTests/
├── README.md                              # 本文件
├── Run-AllTests.ps1                       # 一键运行所有测试
├── TestHelper/
│   ├── TestHelper.psm1                    # 通用测试辅助模块
│   └── (MockHelper.ps1)                   # Mock 辅助工具（预留）
├── BatTool/                               # BatTool 脚本测试
│   ├── RegisterEntry.Tests.ps1            # register_entry.ps1 测试
│   ├── SyncVscodeEntries.Tests.ps1        # sync_vscode_entries.ps1 测试
│   ├── FixCodeRunnerConfig.Tests.ps1      # fix_coderunner_config.ps1 测试
│   └── BatToolConfig.Tests.ps1            # BatToolConfig.psm1 测试
├── FileDriven/                            # FileDriven 模块测试（从 Script/FileDriven 迁移）
│   ├── FileDrivenTestUtils.Tests.ps1      # FileDrivenTestUtils.ps1 测试
│   ├── ScriptRunner.Tests.ps1             # ScriptRunner.ps1 测试
│   ├── SelectScript.Tests.ps1             # SelectScript.ps1 测试
│   └── FindLauncherDir.Tests.ps1          # FindLauncherDir.ps1 测试
├── LogViewer/                             # LogViewer 测试
│   └── LogViewer.Tests.ps1                # LogViewer.ps1 测试
├── Core/                                  # 核心模块测试（新增）
│   ├── FileSystemHelpers.Tests.ps1        # FileSystemHelpers.psm1 测试
│   ├── InteractiveHelpers.Tests.ps1       # InteractiveHelpers.psm1 测试
│   ├── OutputHelpers.Tests.ps1            # OutputHelpers.psm1 测试
│   ├── ProjectConfig.Tests.ps1            # ProjectConfig.psm1 测试
│   ├── ProjectPaths.Tests.ps1             # ProjectPaths.psm1 测试
│   └── XmlHelpers.Tests.ps1               # XmlHelpers.psm1 测试
├── BuildOutput/                           # 构建输出模块测试（新增）
│   └── BuildOutputCleaner.Tests.ps1       # BuildOutputCleaner.psm1 测试
├── Build/                                 # 构建工具测试（新增）
│   ├── CleanupExtraBuildOutput.Tests.ps1  # CleanupExtraBuildOutput.ps1 测试
│   └── clean.Tests.ps1                    # clean.ps1 测试
├── Solution/                              # 解决方案管理测试（新增）
│   ├── Clean-SolutionConfig.Tests.ps1     # Clean-SolutionConfig.ps1 测试
│   ├── Sync-SlnToDisk.Tests.ps1           # Sync-SlnToDisk.ps1 测试
│   └── Fix-CrossProjectAnalysis.Tests.ps1 # Fix-CrossProjectAnalysis.ps1 测试
├── Analysis/                              # 代码分析测试（新增）
│   └── Analyze-CsprojReferences.Tests.ps1 # Analyze-CsprojReferences.ps1 测试
├── Git/                                   # Git 工具测试（新增）
│   └── Remove-BranchesExceptMain.Tests.ps1 # Remove-BranchesExceptMain.ps1 测试
├── EnvConfig/                             # 环境配置测试（新增）
│   ├── Update-EnvironmentConfig.Tests.ps1 # Update-EnvironmentConfig.ps1 测试
│   ├── Validate-ClineMCPConfig.Tests.ps1  # Validate-ClineMCPConfig.ps1 测试
│   └── Setup-PesterTestEnv.Tests.ps1      # Setup-PesterTestEnv.ps1 测试
├── VsCode/                                # VS Code 工具测试（新增）
│   └── Sync-VscodeExtensions.Tests.ps1    # Sync-VscodeExtensions.ps1 测试
├── TestRunner/                            # 测试运行器测试（新增）
│   └── Run-PesterTests.Tests.ps1          # Run-PesterTests.ps1 测试
└── Launcher/                              # 启动器测试（新增）
    ├── ScriptLauncher.Tests.ps1           # ScriptLauncher.ps1 测试
    └── ScriptLauncher.Bat.Tests.ps1       # ScriptLauncher.bat 测试
```


## 测试覆盖

### FileDrivenTestUtils.ps1（7 个函数，30+ 测试用例）

| 函数 | 测试场景 |
|------|---------|
| `Get-FileDrivenConfig` | 路径计算、不存在的路径、自定义上移级数 |
| `Get-RunMode` | 默认值、环境变量、空值处理 |
| `Set-RunMode` | 设置 auto/manual、覆盖已有值 |
| `Switch-RunMode` | auto↔manual 切换 |
| `Write-Header` / `Write-Info` | 输出格式验证 |
| `Set-AliasEnvironment` | 别名注入、覆盖值、空哈希表 |
| `Get-ScriptFiles` | 文件过滤、递归扫描、排序、空目录 |
| `Find-LauncherDir` | 委托调用、路径解析 |

### ScriptRunner.ps1（8 个函数，20+ 测试用例）

| 函数 | 测试场景 |
|------|---------|
| `New-ScriptRunner` | 创建实例、默认/自定义参数、方法存在性 |
| `Show-ScriptList` | 列表展示、自定义 FilePattern、空目录 |
| `Show-ConfigInfo` | 配置展示、运行模式、路径别名 |
| `Show-DirectoryStructure` | 目录结构展示、路径不存在 |
| `Show-HelpText` | 帮助文本、ExtraCommands、用法说明 |
| `Show-FileContent` | 文件存在/不存在处理 |
| `Show-ActionList` | 可操作列表、模式、ExtraCommands |
| `Invoke` 方法 | 各操作模式分发 |
| `InvokeRunActionSafe` | 安全调用、异常捕获 |

### SelectScript.ps1（10+ 测试用例）

| 测试场景 | 说明 |
|---------|------|
| 参数解析 | ScriptsDir、RunMode、Title、FilePattern |
| 脚本列表展示 | 文件列表、递归扫描、JSON 描述、运行模式 |
| 目录结构信息 | ShowDirInfo 开关 |
| 错误处理 | 目录不存在、空目录 |

### FindLauncherDir.ps1（8+ 测试用例）

| 测试场景 | 说明 |
|---------|------|
| 基本功能 | 脚本存在、参数接受、stdout 输出 |
| 路径计算 | Debug/Release 目录路径 |
| 退出码 | 正常/异常退出码 |
| 输出格式 | stdout/stderr 分离 |

### LogViewer.ps1（7 个函数，15+ 测试用例）

| 函数 | 测试场景 |
|------|---------|
| `Invoke-LogCommand` | 启动器不存在、$null 输出、JSON 模式、过滤逻辑 |
| `Show-LogViewer` | 有/无 ExecCommand 分发 |
| `Show-LogOverview` | 统计解析、Launcher 不可用 |
| `Show-LevelSelector` | 菜单显示、有效/无效选择 |
| `Show-ModuleSelector` | 菜单显示、有效选择 |
| `Show-CategorySelector` | 分类解析、无可用分类、全部显示 |
| `Show-LogDetail` | 无条目、分页导航、搜索、显示全部、级别着色 |

### BatTool 脚本测试（4 个测试文件，50+ 测试用例）

#### RegisterEntry.Tests.ps1（register_entry.ps1）

| 测试场景 | 说明 |
|---------|------|
| 参数解析 | 验证 EntriesFile、EntryName、EntryTarget、WorkDir 参数 |
| JSON 条目添加 | 新条目添加到 entries 数组、保留现有条目、description 默认值 |
| 异常处理 | 文件不存在时的错误处理 |
| 文件编码 | UTF8 编码写入、中文内容支持 |

#### SyncVscodeEntries.Tests.ps1（sync_vscode_entries.ps1）

| 测试场景 | 说明 |
|---------|------|
| 参数解析 | 验证 EntriesFile、OldWorkDir、NewWorkDir 参数 |
| 路径更新 | BatTool 下 .bat 文件的 workDir 更新 |
| 过滤逻辑 | 非 BatTool 条目（vscode-plugin-framework/ 开头）不更新 |
| 类型过滤 | 非 .bat 类型条目不更新 |
| 路径匹配 | workDir 不匹配 OldWorkDir 时不更新 |
| 无需更新 | 没有匹配条目时文件内容不变 |

#### FixCodeRunnerConfig.Tests.ps1（fix_coderunner_config.ps1）

| 测试场景 | 说明 |
|---------|------|
| 参数解析 | 验证 SettingsFile 参数 |
| executorMap 创建 | 不存在时创建、已有时保留其他语言配置 |
| bat 执行器配置 | start 命令包装、cmd /c、$fullFileName |
| runInTerminal 设置 | 设置为 false、不存在时添加 |
| 保留现有配置 | 其他 VS Code 配置项不受影响 |

#### BatToolConfig.Tests.ps1（BatToolConfig.bat）

| 测试场景 | 说明 |
|---------|------|
| 文件存在性 | 文件存在、包含所有参数处理入口 |
| 目录变量定义 | PROJECT_ROOT、DIR_BASICMETHOD、DIR_BATTOOL 等 |
| 路径计算逻辑 | 上移级数、目录指向正确性 |
| 别名导出格式 | FILEDRIVEN_ALIAS_ 格式、5 个别名全部导出 |
| 别名路径正确性 | 各别名指向正确的子目录 |
| 参数分发逻辑 | 5 个参数跳转到对应标签、未知参数退出 |
| 脚本调用链 | 各参数调用对应的 .ps1 脚本 |

## 运行测试


### 运行所有测试

```powershell
# 使用 PowerShell 7（推荐）
pwsh -NoProfile -File "PowerShellTests\Run-AllTests.ps1"

# 使用 Windows PowerShell
powershell -NoProfile -File "PowerShellTests\Run-AllTests.ps1"
```

### 运行单个测试文件

```powershell
Invoke-Pester -Path "PowerShellTests\FileDriven\FileDrivenTestUtils.Tests.ps1"
Invoke-Pester -Path "PowerShellTests\FileDriven\ScriptRunner.Tests.ps1"
Invoke-Pester -Path "PowerShellTests\FileDriven\SelectScript.Tests.ps1"
Invoke-Pester -Path "PowerShellTests\FileDriven\FindLauncherDir.Tests.ps1"
Invoke-Pester -Path "PowerShellTests\LogViewer\LogViewer.Tests.ps1"
```

### 运行特定测试

```powershell
# 按 Describe 名称筛选
Invoke-Pester -Path "PowerShellTests\FileDriven\FileDrivenTestUtils.Tests.ps1" `
    -Filter "Get-FileDrivenConfig"

# 按 Tag 筛选（如果测试定义了 Tag）
Invoke-Pester -Path "PowerShellTests\*.Tests.ps1" -Recurse -Tag "core"
```

### 生成测试报告

```powershell
# 运行所有测试并生成 NUnit 格式报告
.\PowerShellTests\Run-AllTests.ps1

# 手动生成报告
Invoke-Pester -Path "PowerShellTests\*.Tests.ps1" -Recurse `
    -OutputFile "PowerShellTests\TestResults\Results.xml" `
    -OutputFormat NUnitXml
```

## 测试辅助模块

`TestHelper\TestHelper.psm1` 提供以下辅助函数：

| 函数 | 说明 |
|------|------|
| `Import-TestScript` | 通过点引入加载被测试脚本 |
| `Get-ProjectRoot` | 自动检测项目根目录 |
| `New-TestConfig` | 创建测试用配置哈希表 |
| `New-TestFile` | 在 TestDrive 中创建测试文件 |
| `New-TestDirectory` | 在 TestDrive 中创建测试目录 |
| `Invoke-WithOutputCapture` | 执行脚本块并捕获所有输出流 |
| `Get-MockParameterFilter` | 生成 Mock 的 ParameterFilter 脚本块 |

## 测试编写规范

### 命名规范

- 测试文件：`<被测脚本名>.Tests.ps1`
- Describe 块：`<函数名>`（与函数名一致）
- It 块：`应<预期行为>`（中文描述）

### 测试结构

```powershell
BeforeAll {
    # 加载测试辅助模块和被测脚本
}

Describe "函数名" {
    BeforeEach {
        # 每个测试前的准备工作
    }

    It "应返回正确结果" {
        # 测试逻辑
        $result = Get-Something
        $result | Should -Be "expected"
    }

    It "应处理异常情况" {
        { Throw-Something } | Should -Throw
    }
}
```

### Mock 使用

```powershell
# Mock 外部命令
Mock Invoke-LogCommand { return "mocked output" } -ParameterFilter { $Command -eq "count" }

# Mock Read-Host（模拟用户输入）
Mock Read-Host { return "1" }

# 验证 Mock 被调用
Assert-MockCalled Invoke-LogCommand -Times 1 -Exactly
```

## 常见问题

### Q: 测试运行时报错 "无法加载模块 Pester"

确保已安装 Pester 5.x：
```powershell
Install-Module -Name Pester -Force -SkipPublisherCheck
```

### Q: 测试中使用了 TestDrive 但文件未创建

TestDrive 是 Pester 自动创建的临时目录，仅在 It 块中可用。
在 BeforeAll 中无法使用 TestDrive。

### Q: Mock 不生效

确保 Mock 在调用目标函数之前定义，并且使用正确的 `-ModuleName` 参数（如果需要）。

### Q: 如何调试测试

```powershell
# 使用 -Show 参数查看详细输出
Invoke-Pester -Path "PowerShellTests\FileDriven\FileDrivenTestUtils.Tests.ps1" -Show All
```
