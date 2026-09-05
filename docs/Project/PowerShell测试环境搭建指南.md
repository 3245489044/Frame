# PowerShell 测试环境搭建指南

## 概述

本文档介绍如何搭建 PowerShell 测试环境，用于对 `Script\Modules\FileDriven\` 下的模块脚本（`ScriptRunner.ps1`、`SelectScript.ps1`、`FindLauncherDir.ps1`、`FileDrivenTestUtils.ps1`）以及 `CentralConsole\FileDriven.Tests\FileDriven\LogViewer.ps1` 进行单元测试。

## 环境要求

| 组件 | 最低版本 | 推荐版本 |
|------|---------|---------|
| PowerShell | 5.1 | **7.x**（PowerShell Core） |
| Pester | 3.4.0 | **5.x** |
| NuGet 提供程序 | 2.8.5.201 | 最新 |

## 第一步：升级 PowerShell

### 为什么需要 PowerShell 7？

- PowerShell 7 是跨平台版本，支持更多现代特性
- Pester 5.x 在 PowerShell 7 上运行更稳定
- 更好的性能和兼容性

### 安装 PowerShell 7

#### 方法一：winget 安装（推荐）

```powershell
# 搜索 PowerShell
winget search Microsoft.PowerShell

# 安装 PowerShell 7
winget install --id Microsoft.Powershell --source winget
```

#### 方法二：MSI 安装包

1. 访问 https://github.com/PowerShell/PowerShell/releases
2. 下载最新版本的 `PowerShell-7.x.x-win-x64.msi`
3. 双击安装，使用默认选项

#### 方法三：使用 .NET 工具安装

```powershell
# 安装 dotnet tool
dotnet tool install --global PowerShell
```

### 验证安装

```powershell
# 启动 PowerShell 7
pwsh

# 查看版本
$PSVersionTable.PSVersion
```

预期输出：
```
Major  Minor  Build  Revision
-----  -----  -----  --------
7      4      x      xxxx
```

## 第二步：安装 NuGet 提供程序

NuGet 提供程序是 PowerShell 包管理的基础组件，用于从 PSGallery 下载模块。

```powershell
# 以管理员身份运行 PowerShell

# 安装 NuGet 提供程序
Install-PackageProvider -Name NuGet -Force

# 验证安装
Get-PackageProvider -Name NuGet
```

如果遇到网络问题，可以手动下载：

1. 访问 https://www.nuget.org/api/v2/package/NuGet/
2. 下载 `.nupkg` 文件
3. 解压到 `C:\Program Files\PackageManagement\ProviderAssemblies\`
4. 重命名文件夹为 `nuget`，文件为 `nuget.exe`

## 第三步：安装/升级 Pester

### 安装 Pester 5.x

```powershell
# 以管理员身份运行 PowerShell 7

# 安装 Pester 5.x
Install-Module -Name Pester -Force -SkipPublisherCheck

# 验证安装
Get-Module -ListAvailable Pester | Select-Object Name, Version, Path
```

### 如果安装失败（常见问题处理）

#### 问题 1：执行策略限制

```powershell
# 查看当前执行策略
Get-ExecutionPolicy

# 设置为 RemoteSigned（允许本地脚本运行）
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
```

#### 问题 2：NuGet 提供程序版本过旧

```powershell
# 强制更新 NuGet 提供程序
Install-PackageProvider -Name NuGet -Force -RequiredVersion 2.8.5.201
```

#### 问题 3：PSGallery 未注册

```powershell
# 注册 PSGallery
Register-PSRepository -Default

# 查看已注册的源
Get-PSRepository
```

### 验证 Pester 安装

```powershell
# 导入 Pester
Import-Module Pester -PassThru

# 查看 Pester 版本
(Get-Module Pester).Version

# 运行内置测试验证
Get-Command -Module Pester
```

预期输出应包含：
```
CommandType     Name                                               Version
-----------     ----                                               -------
Function        Describe                                           5.x.x
Function        It                                                 5.x.x
Function        Should                                             5.x.x
Function        Mock                                               5.x.x
Function        InModuleScope                                      5.x.x
Function        BeforeAll                                          5.x.x
Function        AfterAll                                           5.x.x
Function        BeforeEach                                         5.x.x
Function        AfterEach                                          5.x.x
```

## 第四步：创建测试目录结构

在项目根目录下创建 PowerShell 测试目录：

```
CentralConsole/
  FileDriven.Tests/
    FileDriven/
      ScriptRunner.Tests.ps1        # ScriptRunner.ps1 的 Pester 测试
      SelectScript.Tests.ps1        # SelectScript.ps1 的 Pester 测试
      FindLauncherDir.Tests.ps1     # FindLauncherDir.ps1 的 Pester 测试
      FileDrivenTestUtils.Tests.ps1 # FileDrivenTestUtils.ps1 的 Pester 测试
      LogViewer.Tests.ps1           # LogViewer.ps1 的 Pester 测试
```

## 第五步：编写第一个测试

### 测试文件模板

```powershell
# LogViewer.Tests.ps1
# Pester 测试文件：LogViewer.ps1

BeforeAll {
    # 设置项目根目录
    $script:ProjectRoot = Resolve-Path "$PSScriptRoot\..\..\..\.."
    
    # 加载被测试脚本
    . "$PSScriptRoot\LogViewer.ps1"
    
    # 加载依赖模块
    . "$PSScriptRoot\..\..\..\Script\Modules\FileDriven\FileDrivenTestUtils.ps1"
}

Describe "Invoke-LogCommand" {
    It "启动器不存在时应返回空字符串" {
        $result = Invoke-LogCommand -LauncherDir "C:\NonExistent" `
            -ProjectRoot $script:ProjectRoot `
            -Command "count"
        $result | Should -Be ""
    }
}

Describe "Show-CategorySelector" {
    It "分类名不包含 '/' 分隔符时不应抛出异常" {
        # Mock Invoke-LogCommand 返回分类列表
        Mock Invoke-LogCommand { return @"
[ 1] Command — FileDriven 命令执行
[ 2] General — FileDriven 通用日志
[ 3] Inspector — FileDriven UI 检查器
"@ }
        
        # 测试不会抛出 Substring 异常
        { Show-CategorySelector -LauncherDir "dummy" -ProjectRoot "dummy" } | Should -Not -Throw
    }
}
```

### Pester 5.x 常用语法

```powershell
# 测试组织
Describe "功能模块名" {
    Context "特定场景" {
        BeforeAll {
            # 所有 It 块之前执行一次
        }
        BeforeEach {
            # 每个 It 块之前执行
        }
        
        It "应返回正确结果" {
            # 测试逻辑
            $result = Get-Something
            $result | Should -Be "expected"
        }
        
        It "应抛出异常" {
            { Throw-Something } | Should -Throw
        }
        
        AfterEach {
            # 每个 It 块之后执行
        }
        AfterAll {
            # 所有 It 块之后执行一次
        }
    }
}

# Mock 外部依赖
Mock Get-ChildItem { return @() } -ParameterFilter { $Path -eq "C:\test" }

# 验证 Mock 被调用
Assert-MockCalled Get-ChildItem -Times 1 -Exactly

# 跳过测试
It "待实现的测试" -Skip {
    # 测试逻辑
}
```

## 第六步：运行测试

### 运行所有测试

```powershell
# 使用 PowerShell 7
pwsh -NoProfile -Command "Invoke-Pester -Path 'CentralConsole\FileDriven.Tests\FileDriven\*.Tests.ps1'"

# 使用 Windows PowerShell
powershell -NoProfile -Command "Invoke-Pester -Path 'CentralConsole\FileDriven.Tests\FileDriven\*.Tests.ps1'"
```

### 运行单个测试文件

```powershell
Invoke-Pester -Path "CentralConsole\FileDriven.Tests\FileDriven\LogViewer.Tests.ps1"
```

### 运行特定测试

```powershell
# 按 Describe 名称筛选
Invoke-Pester -Path "CentralConsole\FileDriven.Tests\FileDriven\LogViewer.Tests.ps1" `
    -Filter "Show-CategorySelector"
```

### 生成测试报告

```powershell
# NUnit 格式输出（用于 CI/CD）
Invoke-Pester -Path "CentralConsole\FileDriven.Tests\FileDriven\*.Tests.ps1" `
    -OutputFile "TestResults.xml" `
    -OutputFormat NUnitXml
```

## 常见问题

### Q: Pester 3.x 和 5.x 语法有什么区别？

| 功能 | Pester 3.x | Pester 5.x |
|------|-----------|-----------|
| 设置/清理 | `BeforeAll` 不支持 | `BeforeAll`/`AfterAll` |
| Mock | 不支持 | `Mock`/`Assert-MockCalled` |
| 断言 | `Should Be` | `Should -Be` |
| 测试筛选 | 不支持 `-Tag` | 支持 `-Tag` |
| Code Coverage | 不支持 | 支持 `-CodeCoverage` |

### Q: 如何测试需要用户交互的函数？

使用 Mock 模拟 `Read-Host`：

```powershell
Mock Read-Host { return "1" }  # 模拟用户输入 "1"
```

### Q: 如何测试写文件的函数？

使用 `TestDrive` 临时目录（Pester 5.x 自动创建）：

```powershell
It "应写入文件" {
    $testPath = Join-Path $TestDrive "output.txt"
    Write-Output "test" | Out-File $testPath
    Test-Path $testPath | Should -Be $true
}
```

### Q: 如何测试依赖外部 exe 的函数？

使用 Mock 模拟外部命令：

```powershell
Mock Invoke-Expression { return "mocked output" }
```

## 一键安装脚本

将以下内容保存为 `Setup-PesterTestEnv.ps1`：

```powershell
<#
.SYNOPSIS
    一键搭建 PowerShell Pester 测试环境
.DESCRIPTION
    安装 NuGet 提供程序、升级 Pester 到 5.x、创建测试目录结构
#>

# 检查管理员权限
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdmin) {
    Write-Host "[ERROR] 请以管理员身份运行此脚本" -ForegroundColor Red
    exit 1
}

Write-Host "[INFO] 开始搭建 Pester 测试环境..." -ForegroundColor Cyan

# 步骤 1：安装 NuGet 提供程序
Write-Host "[1/3] 安装 NuGet 提供程序..." -ForegroundColor Yellow
try {
    Install-PackageProvider -Name NuGet -Force -ErrorAction Stop
    Write-Host "[OK] NuGet 提供程序安装成功" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] NuGet 提供程序安装失败: $_" -ForegroundColor Red
    exit 1
}

# 步骤 2：安装/升级 Pester
Write-Host "[2/3] 安装 Pester 5.x..." -ForegroundColor Yellow
try {
    Install-Module -Name Pester -Force -SkipPublisherCheck -ErrorAction Stop
    Write-Host "[OK] Pester 安装成功" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] Pester 安装失败: $_" -ForegroundColor Red
    exit 1
}

# 步骤 3：验证安装
Write-Host "[3/3] 验证安装..." -ForegroundColor Yellow
$pesterVersion = (Get-Module -ListAvailable Pester | Sort-Object Version -Descending | Select-Object -First 1).Version
Write-Host "[OK] Pester 版本: $pesterVersion" -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Pester 测试环境搭建完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "运行测试命令:" -ForegroundColor White
Write-Host "  Invoke-Pester -Path 'CentralConsole\FileDriven.Tests\FileDriven\*.Tests.ps1'" -ForegroundColor Gray
```

## 测试编写规范

### 命名规范

- 测试文件：`<被测脚本名>.Tests.ps1`
- Describe 块：`<函数名>`（与函数名一致）
- It 块：`应<预期行为>`（中文描述）

### 目录规范

```
CentralConsole/
  FileDriven.Tests/
    FileDriven/
      LogViewer.Tests.ps1           # LogViewer.ps1 的测试
      ScriptRunner.Tests.ps1        # ScriptRunner.ps1 的测试
      SelectScript.Tests.ps1        # SelectScript.ps1 的测试
      FindLauncherDir.Tests.ps1     # FindLauncherDir.ps1 的测试
      FileDrivenTestUtils.Tests.ps1 # FileDrivenTestUtils.ps1 的测试
```

### 测试覆盖要求

每个函数至少包含以下测试场景：

1. **正常路径**：函数在正常输入下的行为
2. **边界条件**：空输入、最大值、最小值等
3. **错误处理**：异常输入、外部依赖失败等
4. **Mock 验证**：验证外部依赖被正确调用
