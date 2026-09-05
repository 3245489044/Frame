# FileDriven UI & Launcher 基于 Core 重构方案

## 1. 当前架构问题

### 1.1 项目依赖关系（现状）

```
FileDrivenFramework.Abstractions  (抽象接口层)
    ↑ 引用
FileDrivenFramework.Core           (核心实现层)
    ↑ 未引用 ← 问题
FileDriven.UI                      (UI 层 - WinForms)
FileDriven.Launcher                (启动器 - 控制台/WinForms)
```

### 1.2 代码重复问题

| 重复概念 | UI/Launcher 中的定义 | Core 中的定义 |
|---------|-------------------|-------------|
| 命令执行引擎 | `UICommandManager` (UI/Commands/) | `CommandEngine` (Core/) |
| 命令执行结果 | `CommandExecutionResult` (UI/Commands/) | `ICommandExecutionResult` + `CommandExecutionResult` (Abstractions + Core) |
| JSON 处理器接口 | `IJsonProcessor` (Launcher/JsonProcessing/) | `IJsonProcessor` (Abstractions/) |
| JSON 处理服务 | `JsonProcessingService` (Launcher/JsonProcessing/) | `JsonProcessingService` (Core/) |
| JSON 处理结果 | `JsonProcessingResult` (Launcher/JsonProcessing/) | `IJsonProcessingResult` + `JsonProcessingResult` (Abstractions + Core) |
| JSON 处理选项 | `JsonProcessingOptions` (Launcher/JsonProcessing/) | `IJsonProcessingOptions` + `JsonProcessingOptions` (Abstractions + Core) |

### 1.3 重复代码量统计

- **UI 项目**：`UICommandManager`（333行）与 Core 的 `CommandEngine`（231行）功能高度重叠
- **Launcher 项目**：`JsonProcessing/` 目录下 5 个文件（约 300 行）与 Core 的 `JsonProcessingService` + `JsonProcessorAdapter` 功能重叠

---

## 2. 重构目标架构

```
FileDrivenFramework.Abstractions  (抽象接口层)
    ↑ 引用
FileDrivenFramework.Core           (核心实现层)
    ↑ 引用                          ↑ 引用
FileDriven.UI                      FileDriven.Launcher
    ↑ 引用
FileDriven.Launcher
```

### 2.1 依赖关系变更

| 项目 | 当前依赖 | 重构后依赖 |
|------|---------|-----------|
| FileDriven.UI | WdUI, PathManager, FunctionExecution, FunctionStorage | **+ FileDrivenFramework.Core** |
| FileDriven.Launcher | FileDriven.UI, PathManager, FunctionExecution, FunctionStorage, JsonUIBeautifier | **+ FileDrivenFramework.Core** |

---

## 3. 重构步骤

### 步骤 1：FileDriven.UI 添加 Core 引用

**文件**：`FileDriven/FileDriven.UI/FileDriven.UI.csproj`

```xml
<ItemGroup>
  <ProjectReference Include="..\FileDrivenFramework.Core\FileDrivenFramework.Core.csproj" />
  <!-- 保留原有引用 -->
  <ProjectReference Include="..\..\BasicMethod\PathManager\PathManager.csproj" />
  <ProjectReference Include="..\..\WdUI\WdUI.csproj" />
  <ProjectReference Include="..\..\BasicMethod\FunctionExecution\UniversalWorkflowFramework.FunctionExecution.csproj" />
  <ProjectReference Include="..\..\FunctionStorage\FunctionStorage.csproj" />
</ItemGroup>
```

### 步骤 2：重构 UICommandManager

**文件**：`FileDriven/FileDriven.UI/Commands/UICommandManager.cs`

**变更**：
1. 添加 `using FileDrivenFramework.Core;`
2. 内部持有 `CommandEngine` 实例，将命令执行委托给 `CommandEngine`
3. 保留 UI 特有的命令（load, show, close, list, set, click, get）
4. 将通用命令（echo, sleep, exit）委托给 `CommandEngine`

```csharp
public class UICommandManager
{
    private readonly CommandEngine _commandEngine;

    public UICommandManager()
    {
        _commandEngine = new CommandEngine();
        // ... 原有初始化代码
    }

    // UI 特有命令保留在本地
    public async Task<Form?> LoadFormConfigAsync(string configPath, string? formName = null) { ... }
    public void ShowForm(string formName) { ... }
    public void CloseForm(string formName) { ... }

    // 通用命令委托给 CommandEngine
    public bool ExecuteCommand(string command)
    {
        var result = _commandEngine.Execute(command);
        return result.Success;
    }

    public async Task<int> ExecuteFileAsync(string filePath, bool jsonOutput = false)
    {
        var result = await _commandEngine.ExecuteFileAsync(filePath);
        // ... 处理结果
    }
}
```

### 步骤 3：删除 UI 中的 CommandExecutionResult

**文件**：`FileDriven/FileDriven.UI/Commands/CommandExecutionResult.cs`

**操作**：删除此文件，改用 Core 中的 `ICommandExecutionResult` / `CommandExecutionResult`。

**影响分析**：
- `UICommandManager.ExecuteCommandWithResultAsync()` 返回 `CommandExecutionResult`（UI 版本）
- 改为返回 `ICommandExecutionResult`（Core 版本）
- `CommandExecutionResult.ToJson()` 方法需要迁移或保留为扩展方法

### 步骤 4：FileDriven.Launcher 添加 Core 引用

**文件**：`FileDriven/FileDriven.Launcher/FileDriven.Launcher.csproj`

```xml
<ItemGroup>
  <ProjectReference Include="..\FileDrivenFramework.Core\FileDrivenFramework.Core.csproj" />
  <!-- 保留原有引用 -->
  <ProjectReference Include="..\FileDriven.UI\FileDriven.UI.csproj" />
  <ProjectReference Include="..\..\BasicMethod\PathManager\PathManager.csproj" />
  <ProjectReference Include="..\..\BasicMethod\FunctionExecution\UniversalWorkflowFramework.FunctionExecution.csproj" />
  <ProjectReference Include="..\..\FunctionStorage\FunctionStorage.csproj" />
  <ProjectReference Include="..\..\JsonProcess\JsonUIBeautifier\JsonUIBeautifier.csproj" />
</ItemGroup>
```

### 步骤 5：重构 Launcher 的 JsonProcessing 目录

**文件**：`FileDriven/FileDriven.Launcher/JsonProcessing/`

**变更**：

#### 5a. 删除 IJsonProcessor.cs
- Launcher 的 `IJsonProcessor` 接口与 Abstractions 的 `IJsonProcessor` 重复
- 改用 `FileDrivenFramework.Abstractions.IJsonProcessor`

#### 5b. 删除 JsonProcessingResult.cs
- Launcher 的 `JsonProcessingResult` 与 Core 的 `JsonProcessingResult` 重复
- 改用 `FileDrivenFramework.Core.JsonProcessingResult`

#### 5c. 删除 JsonProcessingOptions.cs
- Launcher 的 `JsonProcessingOptions` 与 Core 的 `JsonProcessingOptions` 重复
- 改用 `FileDrivenFramework.Core.JsonProcessingOptions`
- `ProcessorType` 枚举保留在 Launcher 中（仅 Launcher 使用）

#### 5d. 重构 JsonProcessingService.cs
- 内部改用 Core 的 `JsonProcessingService` 或 `JsonProcessorAdapter`
- 保留配置加载逻辑（`LoadFromConfig`）

```csharp
public class JsonProcessingService
{
    private readonly FileDrivenFramework.Core.JsonProcessingService _coreService;
    private readonly JsonProcessingOptions _options;

    public JsonProcessingService(JsonProcessingOptions options)
    {
        _options = options;
        // 根据配置创建 Core 的 JsonProcessingService
        var adapter = new JsonProcessorAdapter(
            CreatePipeline(options), 
            new JsonPipelineConfig());
        _coreService = new FileDrivenFramework.Core.JsonProcessingService(adapter);
    }

    public async Task<JsonProcessingResult> ProcessAsync(string json)
    {
        // 委托给 Core 服务
        var result = await _coreService.ProcessAsync(json, MapOptions(_options));
        return MapResult(result);
    }
}
```

#### 5e. 保留 JsonUIBeautifierAdapter.cs
- 此文件是 Launcher 特有的适配器，将 `JsonBeautifierPipeline` 包装为 `IJsonProcessor`
- 只需将实现的接口从 Launcher 的 `IJsonProcessor` 改为 Abstractions 的 `IJsonProcessor`
- 返回类型从 Launcher 的 `JsonProcessingResult` 改为 Core 的 `IJsonProcessingResult`

```csharp
// 修改前
public class JsonUIBeautifierAdapter : IJsonProcessor  // Launcher 的 IJsonProcessor
{
    public Task<JsonProcessingResult> ProcessAsync(string json)  // Launcher 的 JsonProcessingResult
}

// 修改后
public class JsonUIBeautifierAdapter : FileDrivenFramework.Abstractions.IJsonProcessor
{
    public async Task<IJsonProcessingResult> ProcessAsync(string json)
    {
        // ... 原有逻辑不变，返回类型改为 IJsonProcessingResult
    }
}
```

### 步骤 6：重构 Launcher 的 Program.cs

**文件**：`FileDriven/FileDriven.Launcher/Program.cs`

**变更**：
- `InitializeFramework()` 中可以使用 Core 的 `CommandEngine` 替代直接创建 `UICommandManager`
- 或者保持 `UICommandManager` 不变（它内部已委托给 `CommandEngine`）

```csharp
private static void InitializeFramework()
{
    // ... 原有初始化代码
    
    // 使用 Core 的 CommandEngine
    _commandManager = new UICommandManager();  // 内部已使用 CommandEngine
    // 或直接使用 CommandEngine
    // _commandEngine = new CommandEngine();
}
```

---

## 4. 删除文件清单

重构后可以删除的重复文件：

| 文件 | 原因 | 替代 |
|------|------|------|
| `FileDriven.UI/Commands/CommandExecutionResult.cs` | 与 Core 重复 | `FileDrivenFramework.Core.CommandExecutionResult` |
| `FileDriven.Launcher/JsonProcessing/IJsonProcessor.cs` | 与 Abstractions 重复 | `FileDrivenFramework.Abstractions.IJsonProcessor` |
| `FileDriven.Launcher/JsonProcessing/JsonProcessingResult.cs` | 与 Core 重复 | `FileDrivenFramework.Core.JsonProcessingResult` |
| `FileDriven.Launcher/JsonProcessing/JsonProcessingOptions.cs` | 与 Core 重复 | `FileDrivenFramework.Core.JsonProcessingOptions` |

---

## 5. 保留文件清单

以下文件是各项目特有的，需要保留：

| 文件 | 保留原因 |
|------|---------|
| `FileDriven.UI/Commands/UICommandManager.cs` | UI 特有的命令管理（load, show, close, set, click, get） |
| `FileDriven.UI/Commands/ControlValueHelper.cs` | WinForms 控件值读写辅助方法 |
| `FileDriven.UI/Core/MainForm.cs` | 主窗体，WdUI 布局加载 |
| `FileDriven.Launcher/Program.cs` | 启动入口 |
| `FileDriven.Launcher/JsonProcessing/JsonUIBeautifierAdapter.cs` | JsonUIBeautifier 管线适配器（Launcher 特有） |
| `FileDriven.Launcher/JsonProcessing/JsonProcessingService.cs` | 配置加载逻辑（可简化，委托给 Core） |

---

## 6. 重构后的架构图

```
┌─────────────────────────────────────────────────────────┐
│                 FileDrivenFramework.Abstractions          │
│  ICommandEngine  IWorkflowEngine  IFunctionRegistry      │
│  IJsonProcessor  IJsonProcessingResult  IJsonProcessor   │
└──────────────────────┬──────────────────────────────────┘
                       │ 引用
┌──────────────────────▼──────────────────────────────────┐
│                 FileDrivenFramework.Core                  │
│  CommandEngine  WorkflowEngine  FunctionRegistry         │
│  FunctionExecutor  JsonProcessingService                 │
│  PipelineAdapter  JsonProcessorAdapter  MetaCommandAdapter│
└──────┬─────────────────────────────────────┬────────────┘
       │ 引用                               │ 引用
┌──────▼──────────────┐    ┌────────────────▼──────────────┐
│   FileDriven.UI      │    │   FileDriven.Launcher         │
│  MainForm            │    │  Program.cs                   │
│  UICommandManager    │    │  JsonUIBeautifierAdapter      │
│  ControlValueHelper  │    │  JsonProcessingService(简化)  │
│  (WinForms 特有)     │    │  (启动/配置/CLI 特有)         │
└──────────────────────┘    └──────────────────────────────┘
```

---

## 7. 实施优先级与建议

### 第一阶段（高优先级）- 消除编译错误
1. UI 项目添加 Core 引用
2. Launcher 项目添加 Core 引用
3. 解决命名冲突（using 别名或完全限定名）

### 第二阶段（中优先级）- 消除重复代码
4. UICommandManager 委托给 CommandEngine
5. 删除 UI 的 CommandExecutionResult
6. Launcher JsonProcessing 改用 Core 类型

### 第三阶段（低优先级）- 优化与清理
7. 删除 Launcher 中重复的 IJsonProcessor / JsonProcessingResult / JsonProcessingOptions
8. 统一异常处理
9. 统一日志输出

### 风险提示
- `UICommandManager` 的 `ExecuteCommand` 方法签名与 `CommandEngine.Execute` 不完全一致，需要适配
- `CommandExecutionResult` 在 UI 中有 `ToJson()` 方法，Core 版本没有，需要迁移
- Launcher 的 `JsonProcessingOptions` 包含 `ProcessorType` 枚举和美化相关选项，Core 版本更通用，需要映射
