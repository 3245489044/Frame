# Script 目录使用介绍

## 概述

`Script/` 目录包含 WorkFlowPrj 项目的 PowerShell 脚本和模块，提供构建输出清理、项目配置修复、测试运行、文件操作等辅助功能。

## 快速开始

### 使用统一启动器（推荐）

```powershell
# 显示交互菜单
.\Script\ScriptLauncher.ps1

# 或双击 ScriptLauncher.bat
```

```powershell
# 快捷调用特定工具
.\Script\ScriptLauncher.ps1 -Tool clean          # 快速清理 bin/obj
.\Script\ScriptLauncher.ps1 -Tool kill-proc      # 清理编译/运行时冗余进程
.\Script\ScriptLauncher.ps1 -Tool analyze        # csproj 引用分析
.\Script\ScriptLauncher.ps1 -Tool env-config     # 更新环境配置
.\Script\ScriptLauncher.ps1 -Tool log            # 日志查看器
.\Script\ScriptLauncher.ps1 -Tool filedriven     # FileDriven 测试
.\Script\ScriptLauncher.ps1 -Tool sync-ext       # VS Code 扩展同步
.\Script\ScriptLauncher.ps1 -Tool test           # 运行 Pester 测试
.\Script\ScriptLauncher.ps1 -List                # 列出所有工具
```

> 💡 **ScriptLauncher** 覆盖了 Script/ 目录下所有工具，按功能分类组织，支持分层菜单导航和快捷参数调用。

## 目录结构

```
Script/
├── README.md                          # 本文档
├── ScriptLauncher.ps1                 # 统一启动器（主入口）
├── ScriptLauncher.bat                 # 统一启动器（双击入口）
├── launcher.config.json               # 工具配置文件
├── Build/                             # 🔧 构建与清理
│   └── CleanupExtraBuildOutput.ps1    # 构建输出清理与项目配置修复
├── Solution/                          # 📋 解决方案管理
│   ├── Clean-SolutionConfig.ps1       # 精简 .sln 配置
│   ├── Sync-SlnToDisk.ps1             # 同步 .sln ↔ 磁盘
│   └── Fix-CrossProjectAnalysis.ps1   # 跨项目分析修复
├── Analysis/                          # 🔍 代码分析
│   └── Analyze-CsprojReferences.ps1   # csproj 引用分析
├── Git/                               # 🌿 Git 工具
│   └── Remove-BranchesExceptMain.ps1  # 删除非 Main 分支
├── ConfigDrivenUIAutomationFramework.md # UI 自动化框架技术方案文档
├── ref_report.json                    # 引用分析报告输出
├── backups/                           # 文件备份目录
│   └── deprecated/                    # 废弃脚本归档
│       ├── ScriptTools.bat            # 旧启动器（已废弃）
│       ├── ScriptTools.ps1            # 旧启动器（已废弃）
│       ├── Remove-BranchesExceptMain.bat # 分支清理包装器（已废弃）
│       ├── Rebuild_With_Kill.bat      # 编译重建包装器（已废弃）
│       └── clean.bat                  # 清理包装器（已废弃）
├── Modules/                           # 模块目录
│   ├── Import-Modules.ps1             # 模块加载器
│   │   ├── OutputHelpers.psm1         # 统一输出辅助函数
│   │   ├── ProjectPaths.psm1          # 项目路径框架
│   │   ├── ProjectConfig.psm1         # 项目配置常量
│   │   ├── InteractiveHelpers.psm1    # 交互式确认工具模块
│   │   ├── FileSystemHelpers.psm1     # 文件系统工具模块
│   │   ├── XmlHelpers.psm1            # XML 操作工具模块
│   │   ├── BatToolConfig.psm1         # BatTool 路径别名模块（从 BatTool 迁移）
│   │   └── ReadProjectConfig.ps1      # 项目配置读取器
│   ├── Csproj/
│   │   ├── CsprojFixEngine.psm1       # .csproj 配置修复引擎
│   │   ├── CsprojDetection.psm1       # csproj 检测
│   │   ├── CsprojFixActions.psm1      # csproj 修复操作
│   │   └── CsprojNewProjectInit.psm1  # 新建项目初始化
│   ├── BuildOutput/
│   │   └── BuildOutputCleaner.psm1    # 构建输出清理引擎
│   ├── FileDriven/
│   │   ├── FileDrivenTestUtils.ps1    # FileDriven 测试工具函数
│   │   ├── ScriptRunner.ps1           # 通用脚本运行器框架
│   │   └── FindLauncherDir.ps1        # 启动器目录查找
│   └── SelectScript.ps1               # 通用脚本选择器
├── Tools/                             # 工具子目录
│   ├── clean.ps1                      # 快速清理 bin/obj
│   ├── Update-EnvironmentConfig.ps1   # 环境配置更新与文档生成
│   ├── Validate-ClineMCPConfig.ps1    # 验证 CLine MCP 配置
│   ├── Setup-PesterTestEnv.ps1        # 搭建 Pester 测试环境
│   ├── fix_coderunner_config.ps1      # 修复 VS Code Code Runner 配置
│   ├── register_entry.ps1             # 注册启动项到 entries.config.json
│   ├── sync_vscode_entries.ps1        # 同步 VS Code entries 配置
│   ├── Sync-VscodeExtensions.ps1      # VS Code 扩展同步（导出/安装/差异对比）
│   ├── Run-PesterTests.ps1            # Pester 测试运行器
│   ├── vscode-extensions.list         # VS Code 扩展列表
│   └── modules/                       # Tools 子模块
├── FileDriven/                        # FileDriven 测试脚本
│   └── FileDriven/
│       ├── 运行测试GUI模式.ps1        # GUI 模式测试运行器
│       └── LogViewer.ps1              # 分层交互式日志查看器
└── TEST/                              # 测试脚本目录
    ├── README.md
    ├── test_gui.bat / test_gui.ps1    # GUI 测试
    ├── ui-runner.bat / ui-runner.ps1  # UI 运行器
    ├── load_database.bat / .ps1       # 数据库加载
    ├── read_database.bat / .ps1 / .cmd / .sh  # 数据库读取
    ├── test_ui_operations.ps1         # UI 操作测试
    ├── advanced_query.cmd             # 高级查询
    ├── db_query_commands.txt          # 数据库查询命令
    └── GraphReference/                # 图数据库引用
```

---

## ScriptLauncher 统一启动器

### 功能分类

| 编号 | 分类 | 包含工具 |
|------|------|---------|
| 1 | 🔧 构建与清理 | CleanupExtraBuildOutput, clean, Clean-Processes, rebuild（内联） |
| 2 | 📋 解决方案管理 | Clean-SolutionConfig, Sync-SlnToDisk, Fix-CrossProjectAnalysis |
| 3 | 🔍 代码分析 | Analyze-CsprojReferences |
| 4 | 🌿 Git 工具 | Remove-BranchesExceptMain |
| 5 | ⚙️ 环境配置 | Update-EnvironmentConfig, Validate-ClineMCPConfig, Setup-PesterTestEnv |
| 6 | 🧪 FileDriven 测试 | 运行测试GUI模式, LogViewer |
| 7 | 💻 VS Code 工具 | Sync-VscodeExtensions, fix_coderunner_config, register_entry, sync_vscode_entries |
| 8 | 🧪 测试工具 | Run-PesterTests |

### 使用方式

**交互菜单：**
```powershell
.\Script\ScriptLauncher.ps1
# 或双击 Script\ScriptLauncher.bat
```

**快捷调用：**
```powershell
.\Script\ScriptLauncher.ps1 -Tool <别名>
```

| 别名 | 对应工具 |
|------|---------|
| `clean` | 快速清理 bin/obj |
| `cleanup` | 清理构建输出并修复项目配置 |
| `kill-proc` | 清理编译和 VS Code 运行时的冗余进程 |
| `rebuild` | 编译前清理进程并重新编译 |
| `sln-clean` | 精简 .sln 配置 |
| `sln-sync` | 同步 .sln ↔ 磁盘 |
| `fix-cross` | 跨项目分析修复 |
| `analyze` | csproj 引用分析 |
| `git-clean` | 删除非 Main 分支 |
| `env-config` | 更新环境配置 |
| `validate-mcp` | 验证 MCP 配置 |
| `setup-pester` | 搭建 Pester 环境 |
| `fix-coderunner` | 修复 Code Runner |
| `register-entry` | 注册启动项 |
| `sync-entries` | 同步 entries |
| `sync-ext` | VS Code 扩展同步 |
| `test` | 运行 Pester 测试 |
| `filedriven` | FileDriven 测试 |
| `log` | 日志查看器 |

**列出所有工具：**
```powershell
.\Script\ScriptLauncher.ps1 -List
```

### 配置驱动

工具列表和分类由 `launcher.config.json` 定义，支持：
- 自定义分类和排序
- 工具描述和别名
- 工作目录指定
- 内联工具（如 rebuild）直接在启动器中实现，无需外部脚本

---

## 主入口脚本

### CleanupExtraBuildOutput.ps1

**构建输出清理与项目配置修复脚本**，是 Script 目录的核心入口。

**功能：**

| 步骤 | 功能 | 说明 |
|------|------|------|
| 0.1 | 修复无效 TargetFramework | 如 `net10.0-windows7.0` → `net10.0-windows` |
| 0.2 | 移除自定义 OutputPath | 让 `Directory.Build.props` 的全局配置生效 |
| 0.3 | 补充缺失的 UseWindowsForms | 当项目引用 WinForms 项目时自动补充 |
| 0.4 | 移除冗余 TargetFramework | 移除与全局一致的显式配置 |
| 0.5 | 修复 SolutionDir 引用 | 将 `$(SolutionDir)` 替换为 `$(MSBuildThisFileDirectory)` |
| 1 | 检查 TargetFramework 配置 | 分析新建项目是否缺少必要的配置 |
| 2 | 删除 bin\ 和 obj\ 目录 | 清理项目目录下多余的构建中间文件 |
| 3 | 清理 BuildOutput 旧目录 | 清理不符合 `$(Configuration)\$(MSBuildProjectName)\` 模式的旧目录 |
| 4 | 检查输出目录完整性 | 检查 Debug/Release 下所有项目的输出目录 |
| 5 | 检测项目本地 BuildOutput | 检测并修复项目目录下多余的 BuildOutput\ |

**用法：**

```powershell
# 直接运行（交互式，每一步都会询问确认）
.\Script\CleanupExtraBuildOutput.ps1
```

**新建项目注意事项：**
- 新建项目时，`.csproj` 中不要设置 `<OutputPath>`，让 `Directory.Build.props` 的全局配置生效
- 如果项目需要 WinForms/WPF 支持，必须在 `.csproj` 中显式设置 `<TargetFramework>net10.0-windows</TargetFramework>`
- 首次构建后，运行此脚本清理项目本地的 `bin\` 和 `obj\` 目录


### Clean-Processes.ps1 — 进程清理工具

**清理编译和 VS Code 运行时的冗余进程**，解决编译时 DLL 被占用导致的编译失败问题。

**功能（6 种检测策略）：**

| 策略 | 检测目标 | 说明 |
|------|---------|------|
| 1 | BuildOutput 目录下的 exe | 扫描 `BuildOutput\` 下所有 exe，检测哪些正在运行 |
| 2 | 已知项目进程 | 检测 `FileDriven.Launcher`、`CentralConsole` 等已知进程 |
| 3 | VS Code 子进程 | 通过 WMI 进程树分析，检测 Code Runner / 终端启动的进程 |
| 4 | dotnet 进程（按命令行分类） | MSBuild 编译节点、热重载/运行中、测试运行器、VS Code 扩展进程、项目相关进程 |
| 5 | 测试运行器 | 检测 `vstest.console`、`testhost` 等 |
| 6 | conhost 终端残留 | 检测 VS Code 终端关闭后残留的 conhost 进程 |

**参数：**

| 参数 | 说明 |
|------|------|
| `-Force` | 直接终止所有匹配进程，不询问确认 |
| `-List` | 仅列出匹配的进程，不执行终止操作 |
| `-BeforeBuild` | 编译前模式：仅清理可能锁定 BuildOutput 目录下 DLL 的进程 |
| `-ProcessName` | 指定要终止的进程名（可多个，用逗号分隔） |
| `-Interactive` | 交互模式（默认），逐项询问确认 |

**用法：**

```powershell
# 交互式清理（默认）
.\Tools\Clean-Processes.ps1

# 一键清理，不询问
.\Tools\Clean-Processes.ps1 -Force

# 仅列出不终止
.\Tools\Clean-Processes.ps1 -List

# 编译前模式（集成在 rebuild 中自动调用）
.\Tools\Clean-Processes.ps1 -BeforeBuild

# 指定进程名
.\Tools\Clean-Processes.ps1 -ProcessName "FileDriven.Launcher","dotnet"
```

**安全保护：**
- 不会终止系统关键进程（`svchost`、`explorer`、`services`、`lsass` 等）
- 不会终止 VS Code / IDE 自身（`Code`、`devenv`）
- 不会终止 PowerShell 宿主（`powershell`、`pwsh`、`cmd`）
- 编译前模式（`-BeforeBuild`）只清理与编译相关的进程（MSBuild 节点、项目 exe、dotnet watch/run/test）
- 交互模式支持 Y/N/S 逐项确认
- conhost 终端残留会通过父进程分析过滤，不会误杀系统 conhost

**集成：**
- 已集成到 `ScriptLauncher.ps1` 的 `rebuild` 命令中，编译前自动执行
- 可通过 `ScriptLauncher.ps1 -Tool kill-proc` 快捷调用


---

## 模块说明

### Import-Modules.ps1 — 模块加载器

所有模块的集中加载入口。按依赖顺序自动加载所有 `.psm1` 和 `.ps1` 模块文件。

**加载顺序：**
1. `XmlHelpers.psm1` — XML 操作（无依赖）
2. `FileSystemHelpers.psm1` — 文件系统操作（无依赖）
3. `InteractiveHelpers.psm1` — 交互确认（依赖 FileSystemHelpers）
4. `CsprojFixEngine.psm1` — .csproj 修复（依赖以上三个模块）
5. `BuildOutputCleaner.psm1` — 构建输出清理（依赖以上模块）
6. `FileDrivenTestUtils.ps1` — 测试工具函数
7. `ScriptRunner.ps1` — 脚本运行器

**用法：**

```powershell
# 在其他脚本中加载所有模块
. "$PSScriptRoot\Import-Modules.ps1"
```

---

### XmlHelpers.psm1 — XML 操作工具模块

提供 XML 文件的解析、检测、元素移除等通用功能，适用于 `.csproj` / `.config` 等 MSBuild XML 格式文件。

**导出函数：**

| 函数 | 说明 |
|------|------|
| `Read-XmlFile` | 读取 XML 文件，返回 `XmlDocument` 对象和原始内容 |
| `Test-XmlElementExists` | 检查 XML 内容中是否存在指定元素（排除注释） |
| `Test-ContentHasElement` | 在文本内容中检测 XML 元素（排除注释，逐行扫描） |
| `Remove-XmlElementLines` | 从文本内容中移除指定 XML 元素的行 |
| `Get-MsbuildNamespaceManager` | 获取 MSBuild XML 命名空间管理器 |
| `Select-MsbuildNodes` | 在 XML 文档中选择 MSBuild 命名空间下的节点 |

**示例：**

```powershell
# 读取 XML 文件
$xml, $content = Read-XmlFile -filePath "Project.csproj"

# 检查元素是否存在
if (Test-XmlElementExists -content $content -elementName "OutputPath") {
    Write-Host "存在自定义 OutputPath"
}

# 移除元素
$newContent = Remove-XmlElementLines -content $content -elementName "OutputPath"
```

---

### FileSystemHelpers.psm1 — 文件系统工具模块

提供文件备份、目录大小计算、目录删除等通用功能。

**导出函数：**

| 函数 | 说明 |
|------|------|
| `Backup-File` | 备份文件到指定备份目录（带时间戳） |
| `Get-DirectorySizeMB` | 计算目录大小（MB） |
| `Remove-DirectoryWithLog` | 删除目录并返回是否成功 |
| `Get-CsprojFiles` | 获取指定根目录下所有 `.csproj` 文件 |
| `Get-BinObjDirectories` | 获取项目目录下的 `bin\` 和 `obj\` 目录列表 |

**示例：**

```powershell
# 备份文件
$backupPath = Backup-File -filePath "Project.csproj" -backupDir "backups"

# 获取所有 csproj 文件
$csprojFiles = Get-CsprojFiles -rootDir "D:\Project" -excludePaths @("D:\Project\BuildOutput")

# 获取 bin 和 obj 目录
$dirs = Get-BinObjDirectories -csprojFiles $csprojFiles

# 计算目录大小
$sizeMB = Get-DirectorySizeMB -dirPath "D:\Project\BuildOutput"
```

---

### InteractiveHelpers.psm1 — 交互式确认工具模块

提供 Y/N/S 交互确认模式，适用于需要用户确认的脚本场景。

**导出函数：**

| 函数 | 说明 |
|------|------|
| `Confirm-And-DeleteDirectories` | 通用确认删除函数，支持 Y（全部删除）/ N（跳过）/ S（逐项确认） |
| `Confirm-And-FixProjects` | 通用确认修复函数，列出项目并询问是否修复 |
| `Show-FixSummary` | 显示修复摘要 |

**交互模式说明：**

| 输入 | 行为 |
|------|------|
| `Y` / `y` | 全部确认，执行操作 |
| `N` / `n` | 全部跳过，不执行操作 |
| `S` / `s` | 逐项确认，每项单独询问 |

**示例：**

```powershell
# 确认并删除目录
$result = Confirm-And-DeleteDirectories -dirList @("dir1", "dir2") -promptMessage "确认删除？(Y/N/S)"

# 确认并修复项目
$fixed = Confirm-And-FixProjects -projectList @("proj1", "proj2") `
    -displayLabel "有问题的项目" `
    -confirmMessage "确认修复？(Y/N)" `
    -fixAction { Write-Host "修复中..." }
```

---

### CsprojFixEngine.psm1 — .csproj 配置修复引擎

提供 `.csproj` 文件的自动检测与修复功能。依赖 `XmlHelpers.psm1`、`FileSystemHelpers.psm1`、`InteractiveHelpers.psm1`。

**导出函数：**

| 函数 | 说明 |
|------|------|
| `Get-ProjectLocalBuildOutput` | 检测项目目录下多余的 `BuildOutput\` 目录 |
| `Get-TargetFrameworkConfiguration` | 检查 TargetFramework 配置情况 |
| `Get-ProjectReferenceIssues` | 检查项目引用路径是否正确 |
| `Fix-InvalidTargetFramework` | 修复无效的 TargetFramework（如 `net10.0-windows7.0` → `net10.0-windows`） |
| `Fix-CustomOutputPath` | 移除自定义的 OutputPath |
| `Fix-MissingUseWindowsForms` | 补充缺失的 UseWindowsForms |
| `Fix-RedundantTargetFramework` | 移除与全局一致的冗余 TargetFramework 配置 |
| `Fix-SolutionDirInDirectoryBuildProps` | 修复 `Directory.Build.props` 中的 `$(SolutionDir)` 问题 |
| `Invoke-CsprojRepair` | 一键检测并修复项目本地 BuildOutput 问题 |

**工作流：**
1. 检测项目目录下多余的 `BuildOutput\`（非根 `BuildOutput\` 的输出）
2. 分析 `.csproj` 配置问题（OutputPath、TargetFramework、UseWindowsForms 等）
3. 执行修复（自动备份原文件）
4. 清理多余的 `BuildOutput\` 目录

**示例：**

```powershell
# 一键修复
$result = Invoke-CsprojRepair -rootDir "D:\Project" -backupDir "D:\Project\Script\backups"

# 单独修复无效 TargetFramework
$fixed = Fix-InvalidTargetFramework -csprojFiles $csprojFiles `
    -invalidTfmMap @{ 'net10.0-windows7.0' = 'net10.0-windows' } `
    -backupDir "backups"

# 检测项目本地 BuildOutput
$localBuildOutputs = Get-ProjectLocalBuildOutput -csprojFiles $csprojFiles -rootDir "D:\Project"
```

---

### BuildOutputCleaner.psm1 — 构建输出清理引擎

提供构建输出目录的清理和检查功能。依赖 `FileSystemHelpers.psm1`、`InteractiveHelpers.psm1`。

**导出函数：**

| 函数 | 说明 |
|------|------|
| `Get-ObsoleteBuildOutputDirectories` | 获取 BuildOutput 根目录下不符合 `$(Configuration)\$(MSBuildProjectName)\` 模式的旧目录 |
| `Check-OutputDirectoryIntegrity` | 检查项目输出目录完整性 |
| `Check-TargetFrameworkSubdirectories` | 检查 TargetFramework 子目录完整性 |
| `Show-IntegrityCheckResult` | 显示输出目录完整性检查结果 |

**示例：**

```powershell
# 获取需要清理的旧目录
$obsoleteDirs = Get-ObsoleteBuildOutputDirectories -rootBuildOutput "BuildOutput" `
    -knownConfigurations @('Debug', 'Release')

# 检查输出目录完整性
$integrity = Check-OutputDirectoryIntegrity -rootBuildOutput "BuildOutput" `
    -csprojFiles $csprojFiles -configurations @('Debug', 'Release')
```

---

### FileDrivenTestUtils.ps1 — FileDriven 测试工具函数

提供 FileDriven 测试脚本所需的通用功能：获取项目配置、运行模式管理、辅助输出函数等。

**导出函数：**

| 函数 | 说明 |
|------|------|
| `Get-FileDrivenConfig` | 获取 FileDriven 项目配置信息（路径、别名等） |
| `Get-RunMode` | 获取当前运行模式（auto/manual） |
| `Set-RunMode` | 设置运行模式到环境变量 |
| `Switch-RunMode` | 切换运行模式（auto ↔ manual） |
| `Write-Header` | 输出带等号线框的标题 |
| `Write-Info` | 输出标签-值格式的信息 |
| `Set-AliasEnvironment` | 将路径别名注入到环境变量 |
| `Get-ScriptFiles` | 获取指定目录中的脚本文件列表 |
| `Find-LauncherDir` | 查找 FileDriven.Launcher.exe 的输出目录 |

**示例：**

```powershell
# 获取配置
$config = Get-FileDrivenConfig -ScriptPath $MyInvocation.MyCommand.Path

# 切换运行模式
Switch-RunMode

# 输出标题
Write-Header "测试开始"
```

---

### ScriptRunner.ps1 — 通用脚本运行器框架

封装了脚本运行器的通用模式：参数解析、操作分发、交互选择等。通过 `New-ScriptRunner` 创建运行器实例。

**导出函数：**

| 函数 | 说明 |
|------|------|
| `New-ScriptRunner` | 创建脚本运行器实例 |
| `Show-ScriptList` | 列出可用脚本 |
| `Show-ConfigInfo` | 显示配置信息 |
| `Show-DirectoryStructure` | 显示目录结构 |
| `Show-HelpText` | 显示帮助文本 |
| `Show-FileContent` | 显示指定文件内容 |
| `Show-ActionList` | 显示可操作列表 |

**示例：**

```powershell
# 创建脚本运行器
$runner = New-ScriptRunner -Config @{
    ProjectRoot = "D:\Project"
    ScriptsDir = "D:\Project\Scripts"
    FileDrivenDir = "D:\Project\FileDriven"
} -RunAction { param($scriptName) Write-Host "运行: $scriptName" }

# 调用运行器
$runner.Invoke($args)
```

**支持的命令行参数：**

| 参数 | 说明 |
|------|------|
| `--list` | 列出可用脚本 |
| `--info` | 显示配置信息 |
| `--show <文件名>` | 显示指定脚本内容 |
| `--dirs` | 显示目录结构 |
| `--help` | 显示帮助 |
| `--log` | 查看日志 |
| 不传参 | 进入交互选择界面 |

---

### SelectScript.ps1 — 通用脚本选择器

交互式选择脚本文件，支持递归扫描子目录。输出格式供 bat 的 `for /f` 解析使用。

**参数：**

| 参数 | 说明 |
|------|------|
| `-ScriptsDir` | 脚本目录路径（必填） |
| `-RunMode` | 当前运行模式 auto/manual（必填） |
| `-Title` | 显示标题（可选） |
| `-FilePattern` | 文件扩展名过滤（可选，默认 `.json\|.txt`） |
| `-ShowDirInfo` | 是否显示目录结构信息（可选） |
| `-DirInfoPath` | 目录结构信息的根路径（可选） |

**示例：**

```powershell
powershell -File SelectScript.ps1 -ScriptsDir "D:\Scripts" -RunMode "auto"
```

---

### FindLauncherDir.ps1 — 启动器目录查找

查找 `FileDriven.Launcher.exe` 的输出目录，未找到时自动构建。

**查找顺序：**
1. 优先查找 Debug 版本
2. 其次查找 Release 版本
3. 都未找到时自动执行 `dotnet build` 构建 Debug 版本

**示例：**

```powershell
$launcherDir = powershell -File FindLauncherDir.ps1 -FileDrivenDir "D:\Project\FileDriven"
```

---

## 模块依赖关系

```
XmlHelpers.psm1          (无依赖)
FileSystemHelpers.psm1   (无依赖)
InteractiveHelpers.psm1  (依赖 FileSystemHelpers.psm1)
CsprojFixEngine.psm1     (依赖 XmlHelpers + FileSystemHelpers + InteractiveHelpers)
BuildOutputCleaner.psm1  (依赖 FileSystemHelpers + InteractiveHelpers)
FileDrivenTestUtils.ps1  (无依赖)
ScriptRunner.ps1         (依赖 FileDrivenTestUtils.ps1)
SelectScript.ps1         (独立脚本，无依赖)
FindLauncherDir.ps1      (独立脚本，无依赖)
```

---

## 常见使用场景

### 场景一：完整构建输出清理

```powershell
# 直接运行主脚本
.\Script\CleanupExtraBuildOutput.ps1
```

### 场景二：仅修复 .csproj 配置

```powershell
# 加载模块
. .\Script\Modules\Import-Modules.ps1

# 获取 csproj 文件
$csprojFiles = Get-CsprojFiles -rootDir "." -excludePaths @(".\BuildOutput")

# 修复无效 TargetFramework
Fix-InvalidTargetFramework -csprojFiles $csprojFiles `
    -invalidTfmMap @{ 'net10.0-windows7.0' = 'net10.0-windows' } `
    -backupDir ".\Script\backups"

# 移除自定义 OutputPath
Fix-CustomOutputPath -csprojFiles $csprojFiles -backupDir ".\Script\backups"
```

### 场景三：运行 FileDriven 测试

```powershell
# 使用脚本运行器
. .\Script\Modules\Import-Modules.ps1

$config = Get-FileDrivenConfig -ScriptPath $MyInvocation.MyCommand.Path
$runner = New-ScriptRunner -Config $config -RunAction { param($scriptName) ... }
$runner.Invoke($args)
```

### 场景四：查找并启动 FileDriven.Launcher

```powershell
$launcherDir = & powershell -File .\Script\Modules\FindLauncherDir.ps1 -FileDrivenDir ".\FileDriven"
if ($LASTEXITCODE -eq 0) {
    Start-Process (Join-Path $launcherDir "FileDriven.Launcher.exe")
}
```

代码仓库地址：`https://gitee.com/Ljy_111/ZhiLianStorage.git`
