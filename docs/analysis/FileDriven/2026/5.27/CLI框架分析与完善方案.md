# CLI 框架分析与完善方案

## 1. 背景

CLI 模式是 FileDriven 框架的**无 UI 执行模式**，设计目的是：
- 不创建 WinForms 窗口和控件，快速验证 FunctionStorage 功能
- 适合 CI/CD 自动化测试
- 可在非交互环境运行

但当前 CLI 模式与 GUI 模式存在执行路径分叉，导致部分功能不可用。

---

## 2. 架构现状

### 2.1 整体分层

```
BasicMethod (基础框架层)
  ├── FunctionExecution/FunctionRegistry.cs   ← FunctionExecutionFramework（函数注册中心）
  ├── FrameMethod/SkeletonFlow.cs             ← SkeletonFlow 管线框架
  ├── Pipeline/                               ← 管线执行框架
  └── MetaCommand/                            ← 命令模式框架

FileDriven.CLI (CLI 适配层)
  ├── Adapters/
  │   ├── BaseAdapter.cs                      ← 适配器基类
  │   ├── BackendCommandAdapter.cs            ← CLI 适配器
  │   └── GuiCommandAdapter.cs                ← GUI 适配器
  ├── Services/
  │   ├── EventTriggerService.cs              ← 事件触发服务
  │   ├── EventTriggerServiceBase.cs          ← 事件触发基类
  │   ├── EventBindingResolver.cs             ← 事件绑定解析器
  │   ├── VirtualControlStore.cs              ← 虚拟控件存储
  │   └── ValueExtractionHelper.cs            ← 值提取工具
  └── CliModeRouter.cs                        ← 命令路由器

FileDriven.UI (GUI 层)
  ├── Commands/UICommandManager.cs            ← UI 命令管理器
  └── Core/MainForm.cs                        ← 主窗口

FunctionStorage (函数库层)
  ├── MathLibrary.cs                          ← 数学函数注册
  ├── WorkflowLibrary.cs                      ← 工作流函数注册
  ├── UIConnector.cs                          ← UI 连接器
  └── WorkflowExecutionLayer.cs               ← 工作流执行层
```

### 2.2 命令执行路径对比

#### GUI 模式路径

```
命令 → CliModeRouter
  → GuiCommandAdapter.DoClickAsync("executeButton")
    → UICommandManager.ClickControlAsync("executeButton")
      → FindControlByName("executeButton")     // 查找真实 Button 控件
      → btn.PerformClick()                      // 触发 WinForms Click 事件
        → WinFormsEventBinder 的 Click handler
          → 遍历 config.Click 中的函数名列表
          → IMediator.SendAsync("ExecuteWorkflow")  // 通过 IMediator 执行
```

#### CLI 模式路径

```
命令 → CliModeRouter
  → BackendCommandAdapter.DoClickAsync("executeButton")
    → EventTriggerService.TriggerEventAsync("executeButton", "click", "")
      → EventBindingResolver.FindEventBinding("executeButton", "click")
        → 找到函数名 "ExecuteWorkflow"
      → ExecuteFunctionAsync("ExecuteWorkflow", ...)
        → FunctionExecutionFramework.ExecuteAsync("ExecuteWorkflow", ...)  // ❌ 失败！
```

### 2.3 关键差异

| 环节 | GUI 模式 | CLI 模式 |
|------|---------|---------|
| 控件查找 | 真实 WinForms 控件 | 虚拟控件（`_loadedConfigs` 中的配置） |
| 事件触发 | `btn.PerformClick()` → WinForms 事件系统 | `EventTriggerService.TriggerEventAsync` |
| 函数执行 | `IMediator.SendAsync` / `IEventBus.PublishAsync` | `FunctionExecutionFramework.ExecuteAsync` |
| 函数注册 | 通过 `IMediator` 注册 | 需要注册到 `FunctionExecutionFramework.Registry` |

---

## 3. 问题清单

### 问题1：UI 操作函数未注册到 FunctionExecutionFramework（阻塞）

**状态**：❌ 未修复

**描述**：以下 6 个 UI 操作函数在 GUI 模式下通过 `IMediator` 注册和执行，但未注册到 `FunctionExecutionFramework`，导致 CLI 模式无法调用：

| 函数名 | 绑定控件 | 功能 |
|--------|---------|------|
| `ClearOutput` | clearButton | 清空 outputTextBox |
| `ExecuteWorkflow` | executeButton | 执行工作流 |
| `ReloadWorkflows` | reloadWorkflowsButton | 重新加载工作流列表 |
| `SaveResultToFile` | saveResultButton | 保存结果到文件 |
| `LoadWorkflowDetails` | workflowComboBox | 加载工作流详情 |
| `LoadEnvironmentApiKey` | loadEnvKeyButton | 加载环境变量 API Key |

**影响**：CLI 模式下所有 click 命令失败，错误信息 `Function 'xxx' not found in registry`

### 问题2：BackendCommandAdapter.DoLoadAsync 未存储配置到 _loadedConfigs

**状态**：✅ 已修复

**描述**：`DoLoadAsync` 加载配置后未存储到 `_loadedConfigs` 字典，导致 `EventTriggerService` 无法查找事件绑定。

**修复**：在 `DoLoadAsync` 中添加：
```csharp
lock (_loadedConfigsLock)
{
    _loadedConfigs[windowName] = new LoadedConfigInfo(loadedConfig);
}
```

### 问题3：EventTriggerService.WriteOutputAsync 在 CLI 模式下为空实现

**状态**：⚠️ 待优化

**描述**：`EventTriggerService.WriteOutputAsync`（第212-218行）只记录日志，不实际写入虚拟控件。结果由调用方 `BackendCommandAdapter` 处理，逻辑分散。

```csharp
protected override Task WriteOutputAsync(string outputTarget, string value)
{
    _logger.LogInformation("WriteOutput (CLI mode): {Output} = {Value}", outputTarget, value);
    return Task.CompletedTask;
}
```

### 问题4：CLI 和 GUI 两套函数执行机制

**状态**：⚠️ 待优化

**描述**：
- GUI 模式：`IMediator.SendAsync` / `IEventBus.PublishAsync`
- CLI 模式：`FunctionExecutionFramework.ExecuteAsync`

两套机制导致函数需要注册两次，维护成本高。

### 问题5：参数传递类型不匹配

**状态**：⚠️ 待验证

**描述**：`EventTriggerService.ExecuteFunctionAsync` 传递 `List<object?>` 作为参数，但注册的函数可能期望具体类型（如 `string`），可能导致反序列化失败。

---

## 4. 完善方案

### 4.1 核心思路

**构建统一的函数注册和执行中心**，所有函数（包括 UI 操作函数）统一注册到 `FunctionExecutionFramework`，CLI 和 GUI 共享同一套执行路径。

```
                    ┌─ GUI模式: → GuiCommandAdapter → UICommandManager
命令 → CliModeRouter ┤
                    └─ CLI模式: → BackendCommandAdapter
                                      ↓
                          FunctionExecutionFramework (统一执行中心)
                            ├── MathLibrary (数学函数)
                            ├── WorkflowLibrary (工作流函数)
                            └── UIToolbox (UI 操作函数)  ← 新增
                                  ├── ClearOutput
                                  ├── ExecuteWorkflow
                                  ├── ReloadWorkflows
                                  ├── SaveResultToFile
                                  ├── LoadWorkflowDetails
                                  └── LoadEnvironmentApiKey
```

### 4.2 新增 UIToolbox 模块

在 `FunctionStorage` 中新增 `UIToolbox.cs`，统一注册 UI 操作函数：

```csharp
namespace FunctionStorage
{
    public static class UIToolbox
    {
        private static bool _registered;
        private static readonly Logger _log = new(moduleName: "UIToolbox");

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;

            _log.Info(LogCategory.FunctionStorage_General, "注册 UI 工具箱函数...");

            // 1. ClearOutput - 清空输出
            FunctionExecutionFramework.Register("ClearOutput",
                (Func<string>)(() =>
                {
                    _log.Info(LogCategory.FunctionStorage_General, "清空输出");
                    return string.Empty;
                }), "清空输出文本框");

            // 2. ExecuteWorkflow - 执行工作流
            // 参数：[workflowName, input]
            FunctionExecutionFramework.Register("ExecuteWorkflow",
                (Func<string, string, string>)((workflowName, input) =>
                {
                    _log.Info(LogCategory.FunctionStorage_General, "执行工作流: {0}", workflowName);
                    return UIConnector.ExecuteWorkflow(workflowName, input);
                }), "执行指定工作流");

            // 3. ReloadWorkflows - 重新加载工作流列表
            FunctionExecutionFramework.Register("ReloadWorkflows",
                (Func<string>)(() =>
                {
                    _log.Info(LogCategory.FunctionStorage_General, "重新加载工作流列表");
                    WorkflowLibrary.ReloadAll();
                    var names = string.Join(",", WorkflowExecutionLayer.GetAllWorkflowNames());
                    return names;
                }), "重新加载所有工作流");

            // 4. SaveResultToFile - 保存结果到文件
            FunctionExecutionFramework.Register("SaveResultToFile",
                (Func<string, string>)((content) =>
                {
                    var filePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"WorkflowResult_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllText(filePath, content);
                    _log.Info(LogCategory.FunctionStorage_General, "结果已保存: {0}", filePath);
                    return $"已保存到: {filePath}";
                }), "保存工作流执行结果到文件");

            // 5. LoadWorkflowDetails - 加载工作流详情
            FunctionExecutionFramework.Register("LoadWorkflowDetails",
                (Func<string, string>)((workflowName) =>
                {
                    _log.Debug(LogCategory.FunctionStorage_General, "加载工作流详情: {0}", workflowName);
                    return UIConnector.GetWorkflowDetails(workflowName);
                }), "获取工作流详情文本");

            // 6. LoadEnvironmentApiKey - 加载环境变量 API Key
            FunctionExecutionFramework.Register("LoadEnvironmentApiKey",
                (Func<string>)(() =>
                {
                    var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
                    _log.Debug(LogCategory.FunctionStorage_General, "加载 API Key: {0}",
                        string.IsNullOrEmpty(apiKey) ? "(未设置)" : $"(长度: {apiKey.Length})");
                    return apiKey;
                }), "从环境变量加载 API Key");

            _log.Info(LogCategory.FunctionStorage_General, "UI 工具箱函数注册完成");
        }
    }
}
```

### 4.3 修改 Program.cs

在 `InitializeFramework` 中，`MathLibrary.RegisterAll()` 之后添加 `UIToolbox.RegisterAll()`：

```csharp
private static void InitializeFramework()
{
    // ... 现有初始化代码 ...

    _logger.Info(LogCategory.Launcher_Init, "Initializing built-in functions...");
    MathLibrary.RegisterAll();
    UIToolbox.RegisterAll();  // ← 新增
    _logger.Info(LogCategory.Launcher_Init, "Built-in functions initialized");

    // ... 后续代码 ...
}
```

### 4.4 修复 EventTriggerService 参数传递

`EventTriggerService.ExecuteFunctionAsync` 当前传递 `List<object?>`，需要根据注册的函数签名调整参数传递方式：

```csharp
protected override async Task<IFunctionExecutionResult> ExecuteFunctionAsync(
    string functionName, IReadOnlyList<object?> parameters)
{
    object? execParam = null;
    if (parameters != null && parameters.Count > 0)
    {
        execParam = parameters.Count == 1 ? parameters[0] : parameters.ToList();
    }

    var execResult = await FunctionExecutionFramework.ExecuteAsync(functionName, execParam);

    return new FunctionExecutionResult
    {
        IsSuccess = execResult.IsSuccess,
        Value = execResult.Value,
        ErrorMessage = execResult.ErrorMessage,
        ExecutionTime = execResult.ExecutionTime
    };
}
```

### 4.5 修复 WriteOutputAsync

让 CLI 模式的 `WriteOutputAsync` 实际写入虚拟控件：

```csharp
// 在 EventTriggerService 中注入 VirtualControlStore
private readonly VirtualControlStore? _virtualStore;

// 在构造函数中接收
public EventTriggerService(
    IDictionary<string, LoadedConfigInfo> loadedConfigs,
    object? lockObj = null,
    ILoggerFactory? loggerFactory = null,
    VirtualControlStore? virtualStore = null)  // ← 新增参数
    : base(new EventBindingResolver(loadedConfigs, lockObj))
{
    _virtualStore = virtualStore;
    // ...
}

// 修改 WriteOutputAsync
protected override Task WriteOutputAsync(string outputTarget, string value)
{
    _logger.LogInformation("WriteOutput: {Output} = {Value}", outputTarget, value);
    _virtualStore?.Set(outputTarget, value);  // ← 实际写入
    return Task.CompletedTask;
}
```

---

## 5. 后续优化方向

### 5.1 统一 IFunctionExecutor 接口

创建统一的函数执行接口，消除 CLI 和 GUI 的执行路径差异：

```csharp
public interface IFunctionExecutor
{
    Task<ExecutionResult> ExecuteAsync(string functionName, object? parameters);
}
```

- **CLI 模式**：`FunctionExecutionFrameworkFunctionExecutor` → 调用 `FunctionExecutionFramework.ExecuteAsync`
- **GUI 模式**：`MediatorFunctionExecutor` → 调用 `IMediator.SendAsync`

### 5.2 将 UIToolbox 提升为独立项目

如果 UI 操作函数持续增加，可将 `UIToolbox` 从 `FunctionStorage` 中提取为独立项目，作为通用 CLI 工具箱。

### 5.3 支持脚本热加载

CLI 模式可支持监听脚本文件变化，自动重新执行，适合开发调试场景。

---

## 6. 实施计划

| 步骤 | 内容 | 优先级 |
|------|------|--------|
| 1 | 创建 `UIToolbox.cs`，注册 6 个 UI 操作函数 | P0 |
| 2 | 在 `Program.InitializeFramework` 中调用 `UIToolbox.RegisterAll()` | P0 |
| 3 | 修复 `EventTriggerService` 参数传递方式 | P0 |
| 4 | 修复 `WriteOutputAsync` 写入虚拟控件 | P1 |
| 5 | 运行 `math_workflow_test.json` 测试验证 | P0 |
| 6 | 创建 `IFunctionExecutor` 接口统一执行路径 | P2 |
| 7 | 编写 CLI 模式单元测试 | P2 |

---

## 7. 附录：相关文件路径

| 文件 | 路径 |
|------|------|
| BackendCommandAdapter | `FileDriven/FileDriven.CLI/Adapters/BackendCommandAdapter.cs` |
| GuiCommandAdapter | `FileDriven/FileDriven.CLI/Adapters/GuiCommandAdapter.cs` |
| EventTriggerService | `FileDriven/FileDriven.CLI/Services/EventTriggerService.cs` |
| EventBindingResolver | `FileDriven/FileDriven.CLI/Services/EventBindingResolver.cs` |
| VirtualControlStore | `FileDriven/FileDriven.CLI/Services/VirtualControlStore.cs` |
| CliModeRouter | `FileDriven/FileDriven.CLI/CliModeRouter.cs` |
| Program.cs | `FileDriven/FileDriven.Launcher/Program.cs` |
| UICommandManager | `FileDriven/FileDriven.UI/Commands/UICommandManager.cs` |
| FunctionExecutionFramework | `BasicMethod/FunctionExecution/FunctionRegistry.cs` |
| MathLibrary | `FunctionStorage/MathLibrary.cs` |
| WorkflowLibrary | `FunctionStorage/WorkflowLibrary.cs` |
| UIConnector | `FunctionStorage/UIConnector.cs` |
| 测试脚本 | `FileDriven/DrivenConfig/Scripts/Math/math_workflow_test.json` |
| 测试启动脚本 | `CentralConsole/FileDriven.Tests/FileDriven/运行测试GUI模式.bat` |
