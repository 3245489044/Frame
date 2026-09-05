# FileDriven GUI 模式 `sideload` 命令修复文档

## 一、问题概述

从运行日志分析，GUI 模式下执行 `math_functions_test.json` 测试脚本时，所有命令全部失败。根本原因是：

**`sideload MathFunctions.json` 命令执行失败 → 配置未加载到事件绑定解析器 → 后续所有 `set`/`click` 命令找不到控件的事件绑定 → 级联失败**

## 二、错误链分析

```
sideload MathFunctions.json 失败
    ↓  [ERROR] Unknown non-event command: sideload
MathFunctions.json 配置未加载到 _loadedConfigs 字典
    ↓
EventBindingResolver 在空字典中查找控件绑定 → 找不到
    ↓
FindBindingStage 返回 "No binding found for 'valueATextBox.textchanged'"
    ↓
所有 set/click 命令全部失败（共 20+ 条命令）
```

### 日志关键片段

```
[22:51:06.031] [ERROR] sideload MathFunctions.json: Unknown non-event command: sideload
[22:51:06.034] [ERROR] set valueATextBox 10: No binding found for 'valueATextBox.textchanged'
[22:51:06.036] [ERROR] click addButton: No binding found for 'addButton.click'
[22:51:06.036] [ERROR] set valueBTextBox 25: No binding found for 'valueBTextBox.textchanged'
...（后续所有命令均失败）
```

## 三、根因定位

### 3.1 第一层失败：`sideload` 命令未注册

**日志**：`[ERROR] sideload MathFunctions.json: Unknown non-event command: sideload`

**代码分析**：

`NonEventCommandHandler.Handle()` 方法（`NonEventCommandHandler.cs` 第 77-111 行）通过 `NonEventCommandRegistry.Instance.TryGetHandler(action, ...)` 查找命令处理函数。

查看 `PipelineFactory.RegisterBuiltInCommands()`（`PipelineFactory.cs` 第 158-180 行）：

```csharp
public static void RegisterBuiltInCommands()
{
    var registry = NonEventCommandRegistry.Instance;

    // 关键命令（Critical）
    registry.Register("load",     ctx => NonEventCommandHandler.HandleLoad(...),     CommandCriticality.Critical, ...);
    registry.Register("sideload", ctx => NonEventCommandHandler.HandleSideload(...), CommandCriticality.Critical, ...);
    // ...
}
```

`sideload` **确实已注册**。但日志中 `Executor_CommandFrameworkUsage_initialized` 和 `Executor_NonEventCommandValidation_Passed` 两条日志显示**注册校验通过了**：

```
[22:51:05.635] [ERROR] Log message 'Executor_CommandFrameworkUsage_initialized' not found for keyPrefix 'filedriven:'.
[22:51:05.640] [ERROR] Log message 'Executor_NonEventCommandValidation_Passed' not found for keyPrefix 'filedriven:'.
```

> **注意**：这两条日志显示 `[ERROR]` 是因为日志消息键（key）在 `filedriven_log_messages.json` 中未找到，属于日志配置问题，**不是实际错误**。注册本身是成功的。

### 3.2 第二层失败：GUI 模式下 `sideload` 处理路径错误

**真实原因**：虽然 `sideload` 已注册，但 `HandleSideload()` 方法在 GUI 模式下的处理逻辑有缺陷。

查看 `NonEventCommandHandler.HandleSideload()`（`NonEventCommandHandler.cs` 第 495-575 行）：

```csharp
internal static void HandleSideload(FileDrivenPipelineContext context)
{
    // ...
    for (int retryCount = 0; retryCount <= retryPolicy.MaxRetries; retryCount++)
    {
        try
        {
            // CLI 模式下，sideload 与 load 行为一致：加载配置到共享状态
            var result = pipelineEntry.LoadConfig(
                target,
                showForm: false,           // ← 问题：GUI 模式下应为 true
                uiCommandManager: null);   // ← 问题：GUI 模式下应传入 context.UICommandManager
            // ...
        }
    }
}
```

**关键问题**：

1. `showForm: false` — GUI 模式下需要 `showForm: true` 来实际创建 UI 控件
2. `uiCommandManager: null` — GUI 模式下需要传入 `context.UICommandManager` 来创建 Form

虽然 `LoadConfig` 会将配置信息存入 `_loadedConfigs` 字典，但**UI 控件没有被实际创建**，因此 `EventBindingResolver` 无法找到控件的事件绑定。

### 3.3 第三层失败：事件绑定查找失败

**日志**：`[ERROR] set valueATextBox 10: No binding found for 'valueATextBox.textchanged'`

查看 `FindBindingStage.cs`（第 86-101 行）：

```csharp
// 查找事件绑定
var binding = findBindingFunc(target, eventType);
if (binding.FunctionName == null)
{
    context.SetStageResult(Name, false,
        $"No binding found for '{target}.{eventType}'");
    return Task.CompletedTask;
}
```

`findBindingFunc` 委托包装了 `bindingResolver.FindEventBinding(controlName, eventType)`。由于 `MathFunctions.json` 配置未正确加载到 `EventBindingResolver` 的 `_loadedConfigs` 字典中，查找必然失败。

## 四、修复方案

### 修复1：`HandleSideload()` — GUI 模式下正确传递 UI 上下文

**文件**：`FileDriven/FileDriven.Execution/Command/Skeletons/NonEventCommandHandler.cs`

**修改位置**：第 521-525 行

**原代码**：

```csharp
// CLI 模式下，sideload 与 load 行为一致：加载配置到共享状态
var result = pipelineEntry.LoadConfig(
    target,
    showForm: false,
    uiCommandManager: null);
```

**修复后**：

```csharp
// GUI 模式下需要创建 UI 控件，CLI 模式下仅加载配置到共享状态
var isGuiMode = context.UICommandManager != null;
var result = pipelineEntry.LoadConfig(
    target,
    showForm: isGuiMode,                    // GUI 模式下为 true
    uiCommandManager: context.UICommandManager);  // GUI 模式下传入 UICommandManager
```

### 修复2：`LoadConfigCore()` — 添加控件加载日志

**文件**：`FileDriven/FileDriven.Pipeline/PipelineEntry.cs`

**修改位置**：第 248 行之前（`return LoadResult.Success(...)` 之前）

**添加代码**：

```csharp
// 记录已加载的控件名称和事件绑定信息，便于调试
if (loadedConfig.Views != null)
{
    var controlNames = CollectControlNames(loadedConfig);
    FileDrivenLogPlaceholderHandler.Log("PipelineEntry_LoadConfig_controls",
        new
        {
            configPath = resolvedPath,
            windowName = title,
            controlCount = controlNames.Count,
            controls = string.Join(", ", controlNames)
        });
}
```

需要添加 `CollectControlNames` 辅助方法（参考 `NonEventCommandHandler.cs` 第 237 行）：

```csharp
private static List<string> CollectControlNames(UIConfiguration configInfo)
    => ControlTreeWalker.CollectNames(configInfo);
```

### 修复3：`EventBindingResolver.FindEventBinding()` — 添加调试日志

**文件**：`Operation/EventTriggerFramework/Core/EventBindingResolver.cs`（需确认实际路径）

**修改位置**：`FindEventBinding` 方法入口

**添加代码**：

```csharp
System.Diagnostics.Debug.WriteLine(
    $"[EventBindingResolver] FindEventBinding: controlName={controlName}, eventType={eventType}, loadedConfigsCount={_loadedConfigs.Count}");

// 遍历 _loadedConfigs 查找匹配的控件
foreach (var kvp in _loadedConfigs)
{
    System.Diagnostics.Debug.WriteLine(
        $"[EventBindingResolver]   config: {kvp.Key}, type: {kvp.Value?.GetType().Name}");
}
```

## 五、修复前后对比

| 阶段 | 修复前 | 修复后 |
|------|--------|--------|
| `sideload MathFunctions.json` | `showForm: false, uiCommandManager: null` → 配置存入字典但 UI 控件未创建 | `showForm: true, uiCommandManager: context.UICommandManager` → 配置加载 + UI 控件创建 |
| `set valueATextBox 10` | `EventBindingResolver` 在空字典中查找 → 找不到绑定 | `EventBindingResolver` 在已加载的配置中查找 → 找到 `textchanged` 绑定 |
| `click addButton` | 找不到 `addButton.click` 绑定 | 找到 `addButton.click` 绑定 → 执行 `Plus` 函数 |
| 后续所有命令 | 全部失败 | 正常执行 |

## 六、验证步骤

### 6.1 构建项目

```bash
cd d:\Project\Program\WorkFlowPrj
dotnet build BuildOutput/Debug/FileDriven.Launcher.csproj
```

### 6.2 运行测试

```bash
.\运行测试GUI模式.ps1 Math\math_functions_test.json
```

### 6.3 检查日志

验证以下关键日志输出：

```
[INFO] sideload MathFunctions.json: OK
[INFO] set valueATextBox 10: OK
[INFO] click addButton: OK
[INFO] set valueBTextBox 25: OK
...
```

### 6.4 验证 UI 窗口

确认 GUI 窗口正确显示，控件（文本框、按钮）正常创建，点击按钮后计算结果正确显示。

## 七、预防措施

### 7.1 添加 GUI/CLI 模式检测

在 `HandleSideload()` 中根据 `context.UICommandManager` 是否存在自动切换行为，避免类似问题再次发生。

### 7.2 增强日志

在 `LoadConfigCore()` 和 `EventBindingResolver.FindEventBinding()` 中添加详细日志，便于快速定位配置加载和绑定查找问题。

### 7.3 添加单元测试

| 测试用例 | 描述 |
|---------|------|
| GUI 模式 sideload | 验证 GUI 模式下 `sideload` 命令能正确创建 UI 控件 |
| CLI 模式 sideload | 验证 CLI 模式下 `sideload` 命令能正确加载配置 |
| 事件绑定查找 | 验证配置加载后 `EventBindingResolver` 能正确查找控件绑定 |
| 级联失败保护 | 验证 `sideload` 失败后重试机制和降级标志的正确性 |

## 八、相关代码文件索引

| 文件 | 关键行 | 说明 |
|------|--------|------|
| `FileDriven/FileDriven.Execution/Command/Skeletons/NonEventCommandHandler.cs` | 第 77-111 行 | `Handle()` 方法 — 命令分发入口 |
| `FileDriven/FileDriven.Execution/Command/Skeletons/NonEventCommandHandler.cs` | 第 495-575 行 | `HandleSideload()` 方法 — 需要修复 |
| `FileDriven/FileDriven.Execution/Command/PipelineFactory.cs` | 第 158-180 行 | `RegisterBuiltInCommands()` — 命令注册 |
| `FileDriven/FileDriven.Pipeline/PipelineEntry.cs` | 第 194-249 行 | `LoadConfigCore()` — 配置加载核心 |
| `FileDriven/FileDriven.Pipeline/Core/NonEventCommandRegistry.cs` | 第 140-170 行 | `Register()` — 命令注册方法 |
| `FileDriven/FileDriven.Pipeline/Core/NonEventCommands.cs` | 第 30-36 行 | 非事件命令列表定义 |
| `Share/CommandFramework/Pipeline/Stages/FindBindingStage.cs` | 第 86-101 行 | 事件绑定查找阶段 |
| `Share/CommandFramework/Abstractions/IEventBindingResolver.cs` | 第 40-57 行 | 事件绑定解析器接口 |
| `FileDriven/FileDriven.Execution/Command/ExecutionPipelineEntry.cs` | 第 134-163 行 | 上下文创建方法 |
| `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs` | 第 822-926 行 | GUI 模式启动流程 |
