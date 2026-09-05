# AgentConfigUI.json InitCommands 执行问题分析与修复方案

## 问题现象

`AgentConfigUI.json` 加载后 UI 渲染成功（21 个控件全部创建），事件绑定成功（boundCount=1），但 `initCommands` 中的命令没有产生预期效果——工作流列表（funcListView）为空。

## 执行链路分析

### 当前执行路径

```
AgentConfigUI.json
  └─ initCommands: ["exec AgentConfigWorkflow _stepSelector=Init", "exec GetWorkflowNames"]
       │
       ▼
ContainerBinderPipeline.ExecuteInitCommandsAsync()
       │
       ├─ context.PipelineInstance != null? → YES（优先使用）
       │    │
       │    ▼
       │  TryExecuteWithPipelineInstanceAsync("exec GetWorkflowNames", pipelineInstance)
       │    │
       │    ▼  (反射调用)
       │  PipelineInstance.RunCommandAsync("exec GetWorkflowNames")
       │    │
       │    ▼  (CommandFramework 管线解析)
       │  Parser.Parse("exec GetWorkflowNames") → ParsedCommand { Name="exec", Args=["GetWorkflowNames"] }
       │    │
       │    ▼  (ExecuteCommandStage)
       │  exec 命令处理器 → 执行 GetWorkflowNames 函数
       │
       └─ PipelineInstance == null? → 回退到 CommandExecutor
            │
            ▼
          CommandFrameworkUsage.ExecuteCommandAsync("exec GetWorkflowNames")
            │
            ▼
          PipelineInstance.RunCommandAsync("exec GetWorkflowNames")
```

### 关键发现

1. **`PipelineInstance` 优先**（`MainForm.cs:620-623`）：
   ```csharp
   if (PipelineInstance != null)
       context.PipelineInstance = PipelineInstance;
   ```

2. **`PipelineInstance` 在 `CreateAndConfigureBlock.cs:129` 注入**：
   ```csharp
   mainForm.PipelineInstance = CommandFramework.CommandFrameworkUsage.PipelineInstance;
   ```

3. **`ContainerBinderPipeline.ExecuteInitCommandsAsync` 第 439-449 行**：
   ```csharp
   if (pipelineInstance != null)
   {
       var success = await TryExecuteWithPipelineInstanceAsync(cmd, pipelineInstance);
       if (!success)
       {
           // 回退到 CommandExecutor
           if (executorFunc != null)
               await ExecuteWithExecutorAsync(cmd, executorFunc);
       }
   }
   ```

4. **`TryExecuteWithPipelineInstanceAsync` 第 334-371 行**通过反射调用 `RunCommandAsync(string, object?)`。

### 根本原因

**`TryExecuteWithPipelineInstanceAsync` 反射调用失败**，且回退到 `CommandExecutor` 时 `executorFunc` 为 `null`。

原因分析：

- `PipelineInstance.RunCommandAsync` 的签名是 `RunCommandAsync(string command, IStagePipeline<CommandContext>? pipeline = null)`
- 反射调用时传入了 `new object?[] { command, null }`，但第二个参数类型是 `IStagePipeline<CommandContext>` 而非 `object`
- 反射的 `GetMethod("RunCommandAsync", ..., new[] { typeof(string), typeof(object) }, ...)` 找不到匹配的方法
- 回退到 `GetMethod("RunCommandAsync", ..., new[] { typeof(string) }, ...)` 找到单参数重载
- 但 `method.Invoke(pipelineInstance, new object?[] { command, null })` 传了 2 个参数给 1 参数方法 → **抛出异常**
- `TryExecuteWithPipelineInstanceAsync` 的 catch 块吞掉异常，返回 `false`
- 回退到 `executorFunc`，但 `executorFunc` 在 `pipelineInstance != null` 时被设为 `null`（第 416 行）
- **最终：命令未执行，静默失败**

## 修复方案

### 方案一：修复 TryExecuteWithPipelineInstanceAsync 的参数匹配（推荐）

**修改 `ContainerBinderPipeline.cs` 第 362 行**，根据找到的方法参数数量决定传参：

```csharp
// 执行 RunCommandAsync(command)
var methodParams = method.GetParameters();
object?[] invokeArgs;
if (methodParams.Length == 1)
    invokeArgs = new object?[] { command };
else
    invokeArgs = new object?[] { command, null };

var task = (Task)method.Invoke(pipelineInstance, invokeArgs)!;
await task.ConfigureAwait(false);
```

**优点**：改动最小，修复反射参数不匹配问题。

### 方案二：JSON 声明式控制（用户想要的通用方案）

在 `AgentConfigUI.json` 中添加 `initCommandMode` 字段，声明命令执行模式：

```json
{
  "description": "Agent 智能体控制面板",
  "initCommandMode": "function",
  "initCommands": [
    "AgentConfigWorkflow _stepSelector=Init",
    "GetWorkflowNames"
  ]
}
```

**支持的模式**：

| 模式 | 值 | 说明 |
|------|-----|------|
| 命令模式 | `"command"`（默认） | 走完整命令解析管线，支持 `exec` 前缀 |
| 函数模式 | `"function"` | 直接通过 `FunctionExecutor` 调用函数，跳过命令解析 |

**函数模式执行路径**：
```
ExecuteInitCommandsAsync()
  → config.InitCommandMode == "function"
  → ExecuteInitCommandsAsFunctionsAsync(commands, functionExecutor)
  → functionExecutor("GetWorkflowNames")
  → CommandFrameworkUsage.ExecuteCommandAsync("GetWorkflowNames")
  → PipelineInstance.RunCommandAsync("GetWorkflowNames")
```

**注意**：函数模式下，`initCommands` 中的条目作为纯函数名传递，**不需要 `exec` 前缀**。

### 方案三：同时修复反射 + 保留 CommandExecutor 回退

在 `ExecuteInitCommandsAsync` 中，即使 `pipelineInstance != null`，也保留 `executorFunc` 作为回退：

```csharp
// 第 416 行修改
var executorFunc = ResolveCommandExecutor(context);  // 不再判断 pipelineInstance
```

这样当 `TryExecuteWithPipelineInstanceAsync` 失败时，`executorFunc` 可用作回退。

## 推荐方案

**组合方案一 + 方案二**：

1. 修复 `TryExecuteWithPipelineInstanceAsync` 的参数匹配（方案一）
2. 在 `AgentConfigUI.json` 中使用 `initCommandMode: "function"` 声明函数模式（方案二）

这样既修复了反射调用的 bug，又提供了 JSON 声明式的控制方式。

## 实施步骤

### Step 1：修复 ContainerBinderPipeline.cs

修改 `TryExecuteWithPipelineInstanceAsync` 方法，根据方法参数数量动态传参。

### Step 2：修改 AgentConfigUI.json

```json
{
  "description": "Agent 智能体控制面板",
  "initCommandMode": "function",
  "initCommands": [
    "AgentConfigWorkflow _stepSelector=Init",
    "GetWorkflowNames"
  ]
}
```

### Step 3：验证

重新编译并运行，检查：
1. 日志中是否出现 `UIToolbox_GetWorkflowNames` 和 `UIToolbox_GetWorkflowNames_filled`
2. funcListView 是否填充了工作流列表
3. 按钮事件是否正常触发

## 附录：关键代码位置

| 文件 | 行号 | 说明 |
|------|------|------|
| `CsharpPrj/Share/EventBindingOrchestrator/ContainerBinderPipeline.cs` | 334-371 | `TryExecuteWithPipelineInstanceAsync` 反射调用 |
| `CsharpPrj/Share/EventBindingOrchestrator/ContainerBinderPipeline.cs` | 388-467 | `ExecuteInitCommandsAsync` 主逻辑 |
| `CsharpPrj/Share/EventBindingOrchestrator/ContainerBinderPipeline.cs` | 397-411 | `InitCommandMode` 判断逻辑 |
| `CsharpPrj/Share/EventBindingOrchestrator/ContainerBinderPipeline.cs` | 478-539 | `ExecuteInitCommandsAsFunctionsAsync` 函数模式 |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | 619-637 | PipelineInstance/CommandExecutor 注入 |
| `FileDriven/FileDriven.Execution/Gui/Blocks/CreateAndConfigureBlock.cs` | 126-149 | PipelineInstance/CommandExecutor 创建 |
| `CsharpPrj/Share/UniversalMiddleware/ConfigurationModels/Models/UIConfiguration.cs` | 29-37 | `InitCommandMode` 属性定义 |
| `CsharpPrj/Share/EventBindingOrchestrator/ContainerBinder/IContainerBinderContext.cs` | 108-120 | `PipelineInstance`/`FunctionExecutor` 接口定义 |
