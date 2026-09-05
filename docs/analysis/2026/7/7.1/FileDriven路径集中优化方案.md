# FileDriven 路径集中优化方案

> **日期**：2026-07-01  
> **背景**：FileDriven 从 `CsharpPrj/FileDriven` 分离为独立项目 `FileDriven/`  
> **目标**：消除所有硬编码路径，建立集中路径管理体系

---

## 一、问题清单

### 1.1 C# 代码中的硬编码路径

| 文件 | 问题 | 严重程度 |
|------|------|----------|
| `CsharpPrj/BasicMethod/PathManager/ProjectDirectories.cs` | `FileDriven` 属性指向 `CsharpProjectRoot/FileDriven`（旧路径） | P0 |
| `CsharpPrj/Share/Diagnostics/LoggingBootstrapper.cs` | 硬编码 `CsharpPrj/FileDriven/` 路径 | P0 |
| `CsharpPrj/BasicMethod/JsonProcess/ProcessJsonConfig/Program.cs` | 硬编码 `CsharpPrj/FileDriven/` 路径 | P1 |
| `CsharpPrj/BasicMethod/PathManager/PathResolver.cs` | 引用 `ProjectDirectories.FileDriven_*` 旧路径 | P1 |
| `FileDriven/FileDriven.Utilities/Path/PathAliasRegistry.cs` | 引用 `ProjectDirectories.FileDriven_*`（依赖 CsharpPrj） | P1 |
| `FileDriven/FileDriven.Utilities/Path/PathConstants.cs` | 引用 `ProjectDirectories.FileDriven_*`（依赖 CsharpPrj） | P1 |
| `FileDriven/FileDriven.Execution/ExecutionHelper.cs` | 引用 `ProjectDirectories.FileDriven_Scripts` | P1 |
| `FileDriven/FileDriven.Business/Workflow/WorkflowExecutionLayer.cs` | 引用 `ProjectDirectories.FileDriven_WorkFlowConfig` | P1 |
| `FileDriven/FileDriven.JsonRefer/JsonProcessing/JsonConfigService.cs` | 引用 `ProjectDirectories.FileDriven_JsonConfig` | P1 |
| `FileDriven/FileDriven.Execution/Lifecycle/LoggingInitializer.cs` | 引用 `ProjectDirectories.FileDriven`（旧路径） | P0 |
| `FileDriven/FileDriven.Logging/FileDrivenLogManager.cs` | 引用 `PathManager.ProjectDirectories.FileDriven`（旧路径） | P0 |

### 1.2 PowerShell 脚本

| 文件 | 状态 | 说明 |
|------|------|------|
| `PowerShellScript/Modules/Core/ReadProjectConfig.ps1` | ✅ 正确 | `Get-FileDrivenDir` 返回 `Join-Path $ProjectRoot "FileDriven"` |
| `PowerShellScript/Modules/Core/ProjectPaths.psm1` | ✅ 正确 | 委托给 `Get-FileDrivenDir` |

### 1.3 文档

| 文件 | 问题 | 严重程度 |
|------|------|----------|
| `docs/analysis/` 下多个分析报告 | 引用 `CsharpPrj/FileDriven/` 旧路径 | P3 |

---

## 二、优化方案

### 2.1 核心设计

```
┌─────────────────────────────────────────────────────────────┐
│                   路径管理体系架构                            │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  FileDriven.Abstractions/PathMananger.cs                    │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ ★ 自包含路径管理类                                   │   │
│  │ ★ 不依赖 CsharpPrj/BasicMethod/PathManager           │   │
│  │ ★ 从程序集位置向上查找 .git 标记                      │   │
│  │ ★ 函数式接口：GetJsonConfigDir() / GetLogFilePath()  │   │
│  └─────────────────────────────────────────────────────┘   │
│                           ↕                                 │
│  FileDriven/PathMananger.cs (聚合项目转发)                  │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ ★ 转发到 FileDriven.Abstractions.PathMananger       │   │
│  │ ★ 兼容 using static FileDriven.PathMananger 语法    │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  CsharpPrj/BasicMethod/PathManager/ProjectDirectories.cs    │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ ★ FileDriven 属性标记 [Obsolete]                     │   │
│  │ ★ 保留旧路径兼容旧代码                                │   │
│  │ ★ 新代码提示使用 FileDriven.PathMananger             │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 迁移策略

#### 阶段一：FileDriven 项目内部（已完成）

| 文件 | 修改内容 |
|------|----------|
| `FileDriven/FileDriven.Abstractions/PathMananger.cs` | **新建**：自包含路径管理类 |
| `FileDriven/PathMananger.cs` | **重写**：转发到 Abstractions |
| `FileDriven/FileDriven.Execution/Lifecycle/LoggingInitializer.cs` | `using static FileDriven.Abstractions.PathMananger` |
| `FileDriven/FileDriven.Logging/FileDrivenLogManager.cs` | `using static FileDriven.Abstractions.PathMananger` |

#### 阶段二：CsharpPrj 兼容层（已完成）

| 文件 | 修改内容 |
|------|----------|
| `CsharpPrj/BasicMethod/PathManager/ProjectDirectories.cs` | `FileDriven` 属性标记 `[Obsolete]` |
| `CsharpPrj/Share/Diagnostics/LoggingBootstrapper.cs` | 反射调用 `PathMananger`，回退旧路径 |
| `CsharpPrj/BasicMethod/JsonProcess/ProcessJsonConfig/Program.cs` | 反射调用 `PathMananger`，回退旧路径 |

#### 阶段三：FileDriven 子项目迁移（待完成）

FileDriven 子项目（如 `FileDriven.Utilities`、`FileDriven.Execution` 等）当前引用 `CsharpPrj/BasicMethod` 中的 `ProjectDirectories`。迁移方案：

**方案 A（推荐）**：子项目通过 `FileDriven.Abstractions` 获取路径

- 所有子项目已引用 `FileDriven.Abstractions`
- 在子项目中添加 `using FileDriven.Abstractions;` 即可使用 `PathMananger`
- 逐步替换 `ProjectDirectories.FileDriven_JsonConfig` → `PathMananger.GetJsonConfigDir()`

**方案 B**：子项目继续使用 `ProjectDirectories`（通过 BasicMethod 引用）

- 保留 `ProjectDirectories.FileDriven_*` 属性
- 但需要确保这些属性指向正确的独立项目路径
- 需要修改 `ProjectDirectories.cs` 中的路径计算逻辑

**推荐方案 A**，因为：
1. `PathMananger` 自包含，不依赖 CsharpPrj
2. 函数式接口更清晰
3. 路径变更只需修改一处

---

## 三、PathMananger API 参考

```csharp
// 项目根目录
string GetProjectRoot()
void ResetProjectRootCache()

// FileDriven 目录
string GetFileDrivenRoot()
string GetDrivenConfigDir()
string GetJsonConfigDir()
string GetScriptsDir()
string GetWorkFlowDir()
string GetTempletConfigDir()
string GetConfigsDir()
string GetUsingsDir()

// 日志文件
string GetLogMessagesFilePath()
string GetLogFilterConfigPath()

// 构建输出
string GetBuildOutputRoot()
string GetProjectBuildOutput(string projectName, string configuration)
string GetLauncherOutputDir()
string GetLauncherExePath()

// 子项目目录
string GetAbstractionsDir()
string GetBusinessDir()
string GetExecutionDir()
string GetJsonReferDir()
string GetLauncherDir()
string GetLoggingDir()
string GetPipelineDir()
string GetUIDir()
string GetUtilitiesDir()

// 配置文件
string GetFrameworkConfigPath()
```

---

## 四、使用示例

### C# 代码

```csharp
// 推荐：使用 PathMananger 函数式接口
using static FileDriven.Abstractions.PathMananger;

var jsonDir = GetJsonConfigDir();
var logFile = GetLogMessagesFilePath();
var launcherExe = GetLauncherExePath();
```

### PowerShell 脚本

```powershell
# 使用 Get-FileDrivenDir 统一函数
$fileDrivenDir = Get-FileDrivenDir -ProjectRoot $ProjectRoot
$jsonConfigDir = Join-Path $fileDrivenDir "DrivenConfig\JsonConfig"
```

---

## 五、验证清单

- [x] `FileDriven.Abstractions/PathMananger.cs` 自包含路径检测
- [x] `LoggingInitializer.cs` 使用 `PathMananger` 获取日志文件路径
- [x] `FileDrivenLogManager.cs` 使用 `PathMananger` 获取日志文件路径
- [x] `ProjectDirectories.cs` 标记 `FileDriven` 属性为 `[Obsolete]`
- [x] `LoggingBootstrapper.cs` 反射调用 `PathMananger`，回退旧路径
- [x] `ProcessJsonConfig/Program.cs` 反射调用 `PathMananger`，回退旧路径
- [ ] `PathAliasRegistry.cs` 迁移到 `PathMananger`
- [ ] `PathConstants.cs` 迁移到 `PathMananger`
- [ ] `ExecutionHelper.cs` 迁移到 `PathMananger`
- [ ] `WorkflowExecutionLayer.cs` 迁移到 `PathMananger`
- [ ] `JsonConfigService.cs` 迁移到 `PathMananger`
- [ ] `PathResolver.cs` 迁移到 `PathMananger`
