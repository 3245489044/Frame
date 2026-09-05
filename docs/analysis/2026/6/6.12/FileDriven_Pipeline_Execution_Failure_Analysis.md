# FileDriven 管线执行失败分析报告

> **分析日期：** 2026-06-11  
> **分析范围：** FileDriven GUI 模式测试运行器  
> **测试脚本：** `math_functions_test.json`  
> **触发日志：** 两次独立运行（19:08:55 和 19:09:16）

---

## 目录

1. [问题概述](#1-问题概述)
2. [日志现象](#2-日志现象)
3. [架构背景](#3-架构背景)
4. [问题一：sideload 命令执行失败](#4-问题一sideload-命令执行失败)
5. [问题二：事件绑定查找失败](#5-问题二事件绑定查找失败)
6. [根因分析](#6-根因分析)
7. [影响范围](#7-影响范围)
8. [修复建议](#8-修复建议)
9. [附录：关键代码索引](#9-附录关键代码索引)

---

## 1. 问题概述

在 FileDriven GUI 模式下运行 `math_functions_test.json` 测试脚本时，管线执行出现两类错误：

| 错误类型 | 错误信息 | 出现次数 |
|----------|----------|----------|
| 非事件命令未知 | `sideload MathFunctions.json: Unknown non-event command: sideload` | 1 次 |
| 绑定查找失败 | `valueATextBox.textchanged: No binding found for 'valueATextBox.textchanged'` | 多次 |
| 绑定查找失败 | `addButton.click: No binding found for 'addButton.click'` | 多次 |
| 绑定查找失败 | `subtractButton.click: No binding found for 'subtractButton.click'` | 多次 |
| 绑定查找失败 | `multiplyButton.click: No binding found for 'multiplyButton.click'` | 多次 |
| 绑定查找失败 | `divideButton.click: No binding found for 'divideButton.click'` | 多次 |

**核心结论：** 两类错误存在**级联因果关系**——`sideload` 命令失败导致 UI 配置未加载到侧边容器，进而导致后续所有控件的事件绑定查找全部失败。

---

## 2. 日志现象

### 2.1 正常启动阶段

```
[19:08:55] [DEBUG] [MainForm] 控件引用获取完成
[19:08:55] [DEBUG] [MainForm] 开始调用 LoadConfigSelectorAsync
[19:08:55] [DEBUG] [MainForm] LoadConfigSelectorAsync 开始
[19:08:55] [DEBUG] [JsonConfigService] PreprocessConfig 开始
[19:08:55] [DEBUG] [ProcessJsonWithTemplate] 输入: sourceType=file, sourcePath=...ConfigSelector.json
[19:08:55] [DEBUG] [ProcessJsonWithTemplate] 输出: success=true, outputLength=4003
[19:08:55] [INFO]  [MainForm] OnConfigSelectorLoaded: success=true
[19:08:55] [INFO]  Config selector loaded.
[19:08:55] [DEBUG] [MainForm] MainLayout 加载完成
```

**结论：** 框架初始化、MainLayout 加载、ConfigSelector 加载均正常完成。

### 2.2 侧边容器加载阶段

```
[19:08:59] [INFO]  Loading config into side container: ...MathFunctions.json
[19:08:59] [DEBUG] [MainForm] LoadSideContainerAsync 开始
[19:08:59] [DEBUG] [JsonConfigService] PreprocessConfig 开始
[19:08:59] [DEBUG] [ProcessJsonWithTemplate] 输出: success=true, outputLength=12384
[19:08:59] [INFO]  [DIAG] Control: 'titleLabel' Type='label'
[19:08:59] [INFO]  [DIAG] Control: 'inputGroupBox' Type='groupbox'
[19:08:59] [INFO]  [DIAG] Control: 'functionGroupBox' Type='groupbox'
[19:08:59] [INFO]  [DIAG] Control: 'resultGroupBox' Type='groupbox'
[19:08:59] [INFO]  [DIAG] Control: 'statusLabel' Type='label'
[19:08:59] [SUCCESS] Config loaded into side container: ...MathFunctions.json
```

**结论：** UI 配置加载成功，控件已创建。但注意——**这是通过 `LoadSideContainerAsync` 直接加载的，不是通过 `sideload` 命令加载的**。

### 2.3 命令执行阶段（错误发生）

```
[19:09:01] [ERROR] valueATextBox.textchanged: No binding found for 'valueATextBox.textchanged'
[19:09:01] [ERROR] valueATextBox.textchanged: 管线执行失败
[19:09:02] [ERROR] valueBTextBox.textchanged: No binding found for 'valueBTextBox.textchanged'
[19:09:03] [ERROR] addButton.click: No binding found for 'addButton.click'
```

**结论：** 所有事件命令均因绑定查找失败而无法执行。

### 2.4 第二次运行（完整日志）

第二次运行提供了更完整的调用链：

```
[19:09:16.547] [INFO] Executing command file in GUI mode: ...script_math_functions_test_....txt
[19:09:16.575] [ERROR] sideload MathFunctions.json: Unknown non-event command: sideload
[19:09:16.578] [ERROR] set valueATextBox 10: No binding found for 'valueATextBox.textchanged'
...
```

**关键发现：** 临时命令文件的第一条命令是 `sideload MathFunctions.json`，该命令失败后，后续所有命令均失败。

---

## 3. 架构背景

### 3.1 管线架构

FileDriven 的执行管线分为两层：

```
第1层：CentralPipeline<TContext>     — 通用管线执行引擎（Share/CentralPipeline）
第2层：ExecutionPipelineEntry        — FileDriven 执行管线入口（FileDriven.Execution）
  ├── ScriptLoadSkeleton             — 加载 JSON 脚本 → 转换为命令文件
  └── ForEach(CommandLoop)           — 遍历每条命令
       └── CommandPipeline           — 命令执行子管线
            ├── ParseCommandStage    — 解析命令字符串
            ├── FindBindingStage     — 查找事件绑定
            ├── ExtractValuesStage   — 提取控件值
            ├── ExecuteFunctionStage — 执行绑定函数
            └── WriteResultStage     — 写回结果
```

### 3.2 命令分类

命令分为两类：

| 类型 | 说明 | 示例 | 处理方式 |
|------|------|------|----------|
| **事件命令** | 操作 UI 控件的命令 | `set valueATextBox 10`、`click addButton` | 经过完整管线（FindBinding → ExecuteFunction → WriteResult） |
| **非事件命令** | 系统操作命令 | `load config.json`、`sideload MathFunctions.json`、`sleep 1000` | 由 `NonEventCommandHandler` 直接处理，跳过事件管线阶段 |

### 3.3 命令路由机制

```
命令字符串
    │
    ▼
CommandParser.Parse()
    │
    ├── IsNonEventCommand == true  ──→ NonEventCommandHandler.HandleAsync()
    │                                      │
    │                                      └── 查找已注册的处理函数
    │                                              │
    │                                              ├── 找到 → 执行
    │                                              └── 未找到 → "Unknown non-event command"
    │
    └── IsNonEventCommand == false ──→ 事件管线阶段
                                             │
                                             ├── FindBindingStage
                                             │      │
                                             │      └── findBindingFunc(target, eventType)
                                             │              │
                                             │              ├── 找到绑定 → 继续执行
                                             │              └── 未找到 → "No binding found"
                                             │
                                             ├── ExtractValuesStage
                                             ├── ExecuteFunctionStage
                                             └── WriteResultStage
```

---

## 4. 问题一：sideload 命令执行失败

### 4.1 错误信息

```
[ERROR] sideload MathFunctions.json: Unknown non-event command: sideload
```

### 4.2 调用链追踪

```
ScriptLoadSkeleton.ExecuteCore()
    │  将 math_functions_test.json 转换为临时命令文件
    │  临时文件内容：
    │    sideload MathFunctions.json
    │    set valueATextBox 10
    │    set valueBTextBox 25
    │    click addButton
    │    ...
    ▼
ForEach(CommandLoop) — 遍历每条命令
    │
    ├── 第1条: "sideload MathFunctions.json"
    │       │
    │       ▼
    │   CommandParser.Parse("sideload MathFunctions.json")
    │       │  verb = "sideload", target = "MathFunctions.json"
    │       │  IsNonEventCommand = true  (sideload 在 DefaultNonEventCommands 中)
    │       ▼
    │   FindBindingStage.CanExecute() → false (IsNonEventCommand == true)
    │       │
    │       ▼
    │   NonEventCommandHandler.HandleAsync(parsedCommand)
    │       │
    │       ▼
    │   _handlers.TryGetValue("sideload", out handler) → false
    │       │
    │       ▼
    │   [ERROR] Unknown non-event command: sideload
    │
    ├── 第2条: "set valueATextBox 10"
    │       │  IsNonEventCommand = false → 进入事件管线
    │       │  FindBindingStage: findBindingFunc("valueATextBox", "textchanged") → null
    │       │  [ERROR] No binding found
    │
    ├── 第3条: "set valueBTextBox 25"
    │       │  同样失败
    │
    └── ... 全部失败
```

### 4.3 代码分析

**CommandParser.cs — 非事件命令注册：**

```csharp
// 第193-199行
public static readonly HashSet<string> DefaultNonEventCommands = new(StringComparer.OrdinalIgnoreCase)
{
    "load", "close", "echo", "sleep", "show",
    "look", "wait", "assert", "discover",
    "list", "validate", "exit",
    "sideload"  // ← sideload 已注册
};
```

**NonEventCommandHandler.cs — 处理函数注册（缺失 sideload）：**

`NonEventCommandHandler` 中注册了 `load`、`close`、`echo`、`sleep` 等处理函数，但**没有注册 `sideload` 的处理函数**。

### 4.4 根因

**`CommandParser` 和 `NonEventCommandHandler` 的注册不同步。** `sideload` 在 `CommandParser` 中被识别为非事件命令，但 `NonEventCommandHandler` 中没有对应的处理函数实现。

---

## 5. 问题二：事件绑定查找失败

### 5.1 错误信息

```
[ERROR] valueATextBox.textchanged: No binding found for 'valueATextBox.textchanged'
[ERROR] addButton.click: No binding found for 'addButton.click'
```

### 5.2 调用链追踪

```
命令: "set valueATextBox 10"
    │
    ▼
CommandParser.Parse("set valueATextBox 10")
    │  Normalize("set valueATextBox 10") → "do set valueATextBox 10"
    │  verb = "do", action = "set", target = "valueATextBox"
    │  IsNonEventCommand = false → 进入事件管线
    ▼
FindBindingStage.ExecuteCoreAsync(context)
    │
    ├── InferEventType("set", "valueATextBox", findBindingFunc)
    │       │
    │       ▼
    │   InferSetEventType("valueATextBox", findBindingFunc)
    │       │  尝试: findBindingFunc("valueATextBox", "textchanged") → null
    │       │  尝试: findBindingFunc("valueATextBox", "valuechanged") → null
    │       │  尝试: findBindingFunc("valueATextBox", "selectedindexchanged") → null
    │       │  回退: return "textchanged"
    │
    ├── findBindingFunc("valueATextBox", "textchanged")
    │       │
    │       ▼
    │   ExecutionPipelineEntry.IEventPipelineExecutor.Execute() 中包装的委托
    │       │
    │       ▼
    │   etfBindingResolver.FindEventBinding("valueATextBox", "textchanged")
    │       │
    │       ▼
    │   EventBindingResolver 在 LoadedConfigs 中查找
    │       │  查找 "valueATextBox" 控件 → 未找到事件绑定
    │       ▼
    │   return null
    │
    ├── binding.FunctionName == null
    │       │
    │       ▼
    │   context.SetStageResult("FindBinding", false,
    │       "No binding found for 'valueATextBox.textchanged'")
    │
    ▼
[ERROR] valueATextBox.textchanged: 管线执行失败
```

### 5.3 代码分析

**FindBindingStage.cs — 绑定查找逻辑：**

```csharp
// 第86-101行
var eventType = InferEventType(action, target, findBindingFunc);
var binding = findBindingFunc(target, eventType);
if (binding.FunctionName == null)
{
    // ★ 修复：未找到绑定时记录警告，不再静默通过
    context.SetStageResult(Name, false,
        $"No binding found for '{target}.{eventType}'");
    return Task.CompletedTask;
}
```

**ExecutionPipelineEntry.cs — FindBindingFunc 包装：**

```csharp
// 第256-266行
context.SharedData["FindBindingFunc"] = (Func<string, string, ...>)
    ((controlName, eventType) =>
    {
        var binding = etfBindingResolver.FindEventBinding(controlName, eventType);
        if (binding == null)
            return (null, null, null);
        return (binding.FunctionName, binding.Inputs?.ToList(), binding.Output);
    });
```

### 5.4 根因

**`MathFunctions.json` 的 UI 配置中，控件没有关联的事件绑定定义。** 具体来说：

1. `EventBindingResolver` 在已加载的配置中查找 `valueATextBox` 的 `textchanged` 事件绑定
2. 配置中可能只定义了控件的布局和样式，但没有定义 `events` 或 `bindings` 部分
3. 因此 `FindEventBinding` 返回 null

---

## 6. 根因分析

### 6.1 直接原因

| 问题 | 直接原因 |
|------|----------|
| `sideload` 失败 | `NonEventCommandHandler` 未注册 `sideload` 处理函数 |
| 绑定查找失败 | `MathFunctions.json` 配置中缺少事件绑定定义 |

### 6.2 根本原因

**`sideload` 命令失败是导致所有后续命令失败的级联根因。**

`sideload` 命令的预期行为是：将 `MathFunctions.json` 的 UI 配置加载到侧边容器中，同时**将配置中的事件绑定注册到 `EventBindingResolver`**。由于 `sideload` 失败：

1. `MathFunctions.json` 的 UI 配置虽然已通过 `LoadSideContainerAsync` 加载到界面
2. 但**事件绑定信息没有注册到 `EventBindingResolver`**
3. 因此 `FindBindingStage` 无法找到任何控件的事件绑定
4. 所有 `set`/`click` 命令全部失败

### 6.3 框架设计缺陷

| 缺陷 | 说明 |
|------|------|
| **注册不同步** | `CommandParser.DefaultNonEventCommands` 和 `NonEventCommandHandler` 的注册没有强制同步机制 |
| **缺少注册检查** | 没有在初始化时验证所有非事件命令都有对应的处理函数 |
| **级联失败无保护** | `sideload` 失败后，后续命令没有降级或重试机制 |
| **绑定注册路径不清晰** | UI 配置加载和事件绑定注册的路径不统一，存在两条路径（直接加载 vs sideload 命令） |

---

## 7. 影响范围

| 影响项 | 说明 |
|--------|------|
| **测试脚本** | `math_functions_test.json` 中的所有数学函数测试均无法执行 |
| **sideload 功能** | 所有使用 `sideload` 命令的脚本均会失败 |
| **事件绑定机制** | 通过命令文件加载的 UI 配置无法注册事件绑定 |
| **GUI 自动化测试** | 依赖事件绑定的自动化测试全部受阻 |

---

## 8. 修复建议

### 8.1 短期修复（优先级高）

**在 `NonEventCommandHandler` 中注册 `sideload` 处理函数：**

```csharp
// NonEventCommandHandler 初始化时注册
RegisterHandler("sideload", async (parsedCommand) =>
{
    var configPath = parsedCommand.Target;
    if (string.IsNullOrEmpty(configPath))
        return CommandExecutionResult<object?>.Failure("sideload: config path is required");

    // 调用侧边容器加载逻辑
    await _mainForm.LoadSideContainerAsync("sideContainer", configPath);
    return CommandExecutionResult<object?>.Success(null);
});
```

### 8.2 中期修复（优先级中）

**在 `MathFunctions.json` 中补充事件绑定定义：**

```json
{
  "form": { ... },
  "views": [
    {
      "name": "MathFunctionsView",
      "type": "Panel",
      "controls": [
        {
          "name": "valueATextBox",
          "type": "TextBox",
          "events": {
            "textchanged": {
              "function": "math.add",
              "inputs": ["valueATextBox.Text", "valueBTextBox.Text"],
              "output": "resultLabel.Text"
            }
          }
        },
        {
          "name": "addButton",
          "type": "Button",
          "events": {
            "click": {
              "function": "math.add",
              "inputs": ["valueATextBox.Text", "valueBTextBox.Text"],
              "output": "resultLabel.Text"
            }
          }
        }
      ]
    }
  ]
}
```

### 8.3 长期修复（框架层面）— ✅ 已实现

以下修复已于 **2026-06-11** 完成，涉及 4 个文件：

#### 8.3.1 新建文件：`NonEventCommandRegistry.cs`

**路径：** `FileDriven/FileDriven.Pipeline/Core/NonEventCommandRegistry.cs`

**解决缺陷：注册不同步、缺少注册检查、级联失败无保护、绑定注册路径不清晰**

```csharp
// 单例注册中心 — 所有非事件命令的单一真相来源
public class NonEventCommandRegistry
{
    // 全局单例
    public static NonEventCommandRegistry Instance => _instance.Value;

    // 统一注册入口：自动同步到 CommandParser 和 NonEventCommands
    public void Register(string commandName, Action<object?> handler,
        CommandCriticality criticality = CommandCriticality.Important,
        string? description = null)
    {
        _handlers[commandName] = entry;           // 1. 内部字典
        SyncToCommandParser(commandName);          // 2. CommandParser
        SyncToNonEventCommands(commandName);       // 3. NonEventCommands
    }

    // 启动时校验：检查所有命令都有处理函数
    public NonEventCommandValidationResult ValidateAll()
    {
        // 遍历 NonEventCommands.All，检查 _handlers 中是否存在
        // 关键命令缺失时抛出 InvalidOperationException
    }

    // 重试策略配置
    public static SideloadRetryPolicy RetryPolicy { get; set; }

    // 绑定注册统一入口
    public static class BindingRegistration
    {
        public enum BindingSource { DirectLoad, Sideload, GuiConfig, ExternalApi }
        public static void RecordRegistration(string configName, BindingSource source, bool isSuccess, ...);
        public static string GetStatistics();
    }
}
```

#### 8.3.2 修改文件：`PipelineFactory.cs`

**路径：** `FileDriven/FileDriven.Execution/Command/PipelineFactory.cs`

**新增 `RegisterBuiltInCommands()` 方法：**

```csharp
public static void RegisterBuiltInCommands()
{
    var registry = NonEventCommandRegistry.Instance;

    // 关键命令（Critical）— 缺失时阻止框架启动
    registry.Register("load",     ctx => HandleLoad((FileDrivenPipelineContext)ctx!),     CommandCriticality.Critical);
    registry.Register("sideload", ctx => HandleSideload((FileDrivenPipelineContext)ctx!), CommandCriticality.Critical);

    // 重要命令（Important）— 缺失时记录警告
    registry.Register("echo",    ctx => HandleEcho((FileDrivenPipelineContext)ctx!),    CommandCriticality.Important);
    registry.Register("sleep",   ctx => HandleSleep((FileDrivenPipelineContext)ctx!),   CommandCriticality.Important);
    registry.Register("close",   ctx => HandleClose((FileDrivenPipelineContext)ctx!),   CommandCriticality.Important);
    registry.Register("show",    ctx => HandleShow((FileDrivenPipelineContext)ctx!),    CommandCriticality.Important);
    registry.Register("look",    ctx => HandleLook((FileDrivenPipelineContext)ctx!),    CommandCriticality.Important);
    registry.Register("wait",    ctx => HandleWait((FileDrivenPipelineContext)ctx!),    CommandCriticality.Important);
    registry.Register("assert",  ctx => HandleAssert((FileDrivenPipelineContext)ctx!),  CommandCriticality.Important);

    // 辅助命令（Optional）— 缺失时仅记录信息
    registry.Register("discover", ctx => HandleDiscover((FileDrivenPipelineContext)ctx!), CommandCriticality.Optional);
    registry.Register("list",     ctx => HandleList((FileDrivenPipelineContext)ctx!),     CommandCriticality.Optional);
    registry.Register("validate", ctx => HandleListValidate((FileDrivenPipelineContext)ctx!, "validate"), CommandCriticality.Optional);
    registry.Register("exit",     ctx => HandleExit((FileDrivenPipelineContext)ctx!),     CommandCriticality.Optional);
}
```

#### 8.3.3 修改文件：`FileDrivenExecutor.cs`

**路径：** `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs`

**调整初始化顺序：先注册命令，再校验；关键命令缺失时阻止框架启动：**

```csharp
// InitializeFramework() 中：
// ★ 框架设计缺陷修复：先注册所有内置非事件命令，再执行校验
PipelineFactory.RegisterBuiltInCommands();    // 1. 注册
PerformNonEventCommandValidation();           // 2. 校验（关键缺失时抛出异常）
```

**`PerformNonEventCommandValidation()` 增强：**

```csharp
private static void PerformNonEventCommandValidation()
{
    var result = registry.ValidateAll();
    if (!result.IsValid && result.CriticalMissing.Length > 0)
    {
        // ★ 关键命令缺失：阻止框架启动
        throw new InvalidOperationException(
            $"关键非事件命令缺少处理函数，框架无法启动。缺失：{string.Join(", ", result.CriticalMissing)}");
    }
}
```

#### 8.3.4 修改文件：`NonEventCommandHandler.cs`

**路径：** `FileDriven/FileDriven.Execution/Command/Skeletons/NonEventCommandHandler.cs`

**`HandleSideload` 增加重试逻辑和降级标志：**

```csharp
internal static void HandleSideload(FileDrivenPipelineContext context)
{
    // ★ 级联失败保护：获取重试策略
    var retryPolicy = NonEventCommandRegistry.RetryPolicy;

    for (int retryCount = 0; retryCount <= retryPolicy.MaxRetries; retryCount++)
    {
        try
        {
            var result = pipelineEntry.LoadConfig(target, showForm: false, uiCommandManager: null);
            if (result.IsSuccess)
            {
                context.SideloadFailed = false;  // 成功时清除降级标志
                return;
            }
            // 失败时等待（指数退避）
            Thread.Sleep(retryPolicy.GetDelayMs(retryCount + 1));
        }
        catch (Exception ex) { /* 记录异常，继续重试 */ }
    }

    // ★ 所有重试均失败：设置降级标志
    context.SideloadFailed = true;
    context.FunctionErrorMessage = $"Sideload failed after {retryPolicy.MaxRetries + 1} attempts";
}
```

**`HandleLoad` 和 `HandleSideload` 共享绑定注册逻辑：**

```csharp
// HandleLoad 中：
NonEventCommandRegistry.BindingRegistration.RecordRegistration(
    configName: result.ConfigName ?? target,
    source: BindingSource.DirectLoad,
    isSuccess: result.IsSuccess);

// HandleSideload 中：
NonEventCommandRegistry.BindingRegistration.RecordRegistration(
    configName: result.ConfigName ?? target,
    source: BindingSource.Sideload,
    isSuccess: true);
```

#### 8.3.5 修改文件：`FileDrivenPipelineContext.cs`

**路径：** `FileDriven/FileDriven.Execution/Command/FileDrivenPipelineContext.cs`

**新增降级标志：**

```csharp
/// <summary>
/// ★ sideload 失败降级标志
/// 后续依赖 sideload 结果的命令可以检查此标志并降级处理。
/// </summary>
public bool SideloadFailed { get; set; }
```

### 8.4 修复效果总结

| 缺陷 | 修复方式 | 状态 |
|------|----------|------|
| **注册不同步** | `NonEventCommandRegistry.Register()` 自动同步到 `CommandParser` 和 `NonEventCommands` | ✅ 已修复 |
| **缺少注册检查** | `ValidateAll()` + `PerformNonEventCommandValidation()` 启动时校验，关键缺失时阻止启动 | ✅ 已修复 |
| **级联失败无保护** | `SideloadRetryPolicy` 重试策略 + `SideloadFailed` 降级标志 | ✅ 已修复 |
| **绑定注册路径不清晰** | `BindingRegistration` 统一入口，记录来源路径（DirectLoad/Sideload/GuiConfig/ExternalApi） | ✅ 已修复 |

---

## 9. 附录：关键代码索引

| 文件 | 行号 | 说明 |
|------|------|------|
| `Share/CommandFramework/CommandParser.cs` | 193-199 | `DefaultNonEventCommands` 定义（含 `sideload`） |
| `Share/CommandFramework/CommandParser.cs` | 231-307 | `Parse()` 方法，命令解析逻辑 |
| `Share/CommandFramework/CommandParser.cs` | 342-355 | `IsNonEventCommand()` 判断逻辑 |
| `Share/CommandFramework/Pipeline/PipelineStageBase.cs` | 86-123 | `CommandStageBase` 基类，`IsEventCommand()` 判断 |
| `Share/CommandFramework/Pipeline/Stages/FindBindingStage.cs` | 57-116 | `FindBindingStage` 绑定查找逻辑 |
| `Share/CommandFramework/Pipeline/Stages/FindBindingStage.cs` | 124-163 | `InferEventType()` 事件类型推断 |
| `FileDriven/FileDriven.Execution/Command/ExecutionPipelineEntry.cs` | 217-292 | `IEventPipelineExecutor.Execute()` 事件执行入口 |
| `FileDriven/FileDriven.Execution/Command/ExecutionPipelineEntry.cs` | 256-266 | `FindBindingFunc` 委托包装 |
| `FileDriven/FileDriven.Execution/Command/PipelineFactory.cs` | 57-142 | `CreateFileDrivenPipeline()` 管线构建 |
| `FileDriven/FileDriven.Execution/Command/PipelineFactory.cs` | 152-197 | `BuildCommandPipeline()` 命令管线构建 |
| `FileDriven/FileDriven.Execution/Command/PipelineFactory.cs` | 全文件 | `RegisterBuiltInCommands()` 注册所有内置非事件命令（✅ 新增） |
| `FileDriven/FileDriven.Execution/Command/Skeletons/ScriptLoadSkeleton.cs` | 30-105 | `ScriptLoadSkeleton` 脚本加载 |
| `FileDriven/FileDriven.Execution/Command/Skeletons/NonEventCommandHandler.cs` | 全文件 | 非事件命令处理器，含 `HandleSideload` 重试逻辑和降级标志（✅ 已修复） |
| `FileDriven/FileDriven.Execution/Command/FileDrivenPipelineContext.cs` | 全文件 | 新增 `SideloadFailed` 降级标志（✅ 新增） |
| `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs` | 692-723 | `PerformNonEventCommandValidation()` 关键缺失时阻止框架启动（✅ 已修复） |
| `FileDriven/FileDriven.Pipeline/Core/NonEventCommandRegistry.cs` | 全文件 | 非事件命令注册中心，含 `ValidateAll()`、`SideloadRetryPolicy`、`BindingRegistration`（✅ 新增） |
| `FileDriven/FileDriven.Pipeline/PipelineEntry.cs` | 68-420 | `PipelineEntry` 共享状态管理 |
| `FileDriven/FileDriven.Utilities/CLI/ScriptConverter.cs` | 33-143 | `ScriptConverter` JSON 脚本转换 |
| `FileDriven/FileDriven.Utilities/ScriptRunner/Core/Models/ScriptDefinition.cs` | 14-69 | `ScriptDefinition` 脚本定义模型 |
| `FileDriven/FileDriven.Utilities/ScriptRunner/Processors/ScriptToCommandConverter.cs` | 16-53 | `ScriptToCommandConverter` 命令文本转换 |

---

## 修订历史

| 版本 | 日期 | 修改内容 | 作者 |
|------|------|----------|------|
| v1.0 | 2026-06-11 | 初始版本 | Cline |
| v1.1 | 2026-06-11 | 补充 8.3 节"长期修复（框架层面）— ✅ 已实现"，记录 NonEventCommandRegistry、PipelineFactory、FileDrivenExecutor、NonEventCommandHandler、FileDrivenPipelineContext 的修复实现 | Cline |
