# 消除 IOutputWriter 方案

> **目标**：EventBinding 不应定义输出类型，输出写入完全由调用方在外部处理。
> `HandleExecutionResult` 只负责通过 `onResult` 回调返回结果值。

---

## 当前架构

```
EventBindingStageBase.CreateEventHandler (Task.Run 内部)
  ├── _parameterBuilder(sender, bindings) → 参数字典
  ├── _eventExecutionService.ExecuteFunctionAsync(functionName, parameters) → ExecutionResult
  └── HandleExecutionResult(result)
        ├── 从 result.Value 提取 resolvedValue（含 outputKey 字典提取）
        ├── _outputWriter.WriteOutput(resolvedValue)    ← 问题：EventBinding 定义了输出类型
        └── onResult?.Invoke(functionName, "success", ...)
```

## 目标架构

```
EventBindingStageBase.CreateEventHandler (Task.Run 内部)
  ├── _parameterBuilder(sender, bindings) → 参数字典
  ├── _eventExecutionService.ExecuteFunctionAsync(functionName, parameters) → ExecutionResult
  └── HandleExecutionResult(result)
        ├── 从 result.Value 提取 resolvedValue（含 outputKey 字典提取）
        └── onResult?.Invoke(functionName, "success", { result: resolvedValue, ... })
                                                              ↑
                                                             调用方自行决定如何写入

调用方（如 WdUI.RenderPipeline）：
  └── 收到 onResult 回调 → 获取 resolvedValue → 调用 OutputPipelineRunner 写入
```

---

## 涉及文件

| # | 文件 | 操作 | 说明 |
|---|------|------|------|
| 1 | `CsharpPrj\Share\EventBindingOrchestrator\Abstractions\IOutputWriter.cs` | ❌ 删除 | 整个接口文件 |
| 2 | `CsharpPrj\Operation\WorkFlowMethod\Output\OutputPipelineWriterAdapter.cs` | ❌ 删除 | 适配器不再需要 |
| 3 | `CsharpPrj\Operation\WorkFlowMethod\EventBinding\EventBindingStageBase.cs` | ✏️ 改造 | 删除 `_outputWriter` 字段 + 改造 `HandleExecutionResult` |
| 4 | `CsharpPrj\Share\EventBindingOrchestrator\Abstractions\EventBindingOptions.cs` | ✏️ 改造 | 删除 `OutputWriter` 属性 |
| 5 | `CsharpPrj\Share\EventBindingOrchestrator\Transformers\EventBindingPipelineFactory.cs` | ✏️ 改造 | 删除 `OutputWriter` 传递 |
| 6 | `CsharpPrj\Operation\WdUI\WdUI.RenderPipeline.cs` | ✏️ 改造 | 删除 `IOutputWriter` 解析 |
| 7 | `CsharpPrj\Operation\WorkFlowMethod\EventBinding\EventBindingStageBase.cs` | ✏️ 注释更新 | `EventBindingStageOptions` 注释 |

---

## 详细步骤

### Step 1: 删除 `IOutputWriter.cs`

**文件**：`CsharpPrj\Share\EventBindingOrchestrator\Abstractions\IOutputWriter.cs`

直接删除整个文件。接口定义：

```csharp
public interface IOutputWriter
{
    void WriteOutput(object? value);
    Task WriteOutputAsync(object? value) { WriteOutput(value); return Task.CompletedTask; }
}
```

---

### Step 2: 删除 `OutputPipelineWriterAdapter.cs`

**文件**：`CsharpPrj\Operation\WorkFlowMethod\Output\OutputPipelineWriterAdapter.cs`

直接删除整个文件。此类实现 `IOutputWriter`，委托给 `OutputPipelineRunner`。

---

### Step 3: 改造 `EventBindingStageBase.cs`

**文件**：`CsharpPrj\Operation\WorkFlowMethod\EventBinding\EventBindingStageBase.cs`

#### 3.1 删除 `_outputWriter` 字段

```diff
  protected readonly Func<object, IReadOnlyList<ParameterBinding>?, Dictionary<string, object>>? _parameterBuilder;
- protected readonly IOutputWriter? _outputWriter;
  protected readonly IFunctionRegistry? _registry;
```

#### 3.2 删除构造函数中的赋值

```diff
  _parameterBuilder = options?.ParameterBuilder;
  _registry = options?.Registry;
- _outputWriter = options?.OutputWriter;
```

#### 3.3 改造 `HandleExecutionResult` 方法

当前代码（第 449-534 行）：

```csharp
protected async Task HandleExecutionResult(ExecutionResult result)
{
    var ctx = EventContextScope.Current;
    // ... 获取 functionName, outputTarget, outputKey, onResult, eventName ...

    if (result.IsSuccess)
    {
        object? resolvedValue = result.Value;

        // outputKey 字典提取逻辑（保留）
        if (resolvedValue is System.Collections.IDictionary dict && ...)
        {
            resolvedValue = dict[outputKey];
        }

        // ★ 删除：outputTarget 空值检查
        // ★ 删除：_outputWriter.WriteOutput(resolvedValue)

        // 保留 onResult 回调，将 resolvedValue 传回
        onResult?.Invoke(functionName, "success", new
        {
            result = resolvedValue,
            resultType = resolvedValue?.GetType().Name,
            eventName,
            executionTime = result.ExecutionTime.TotalMilliseconds,
            status = result.Status.ToString()
        });
    }
    else
    {
        // 失败分支保持不变
        onResult?.Invoke(functionName, "failed", new { ... });
    }
}
```

改造后：

```csharp
protected async Task HandleExecutionResult(ExecutionResult result)
{
    var ctx = EventContextScope.Current;
    if (ctx == null)
    {
        EventBindingDiagnosticContext.Record("EventBinder.EventContextMissing", new
        {
            note = "EventContext.Current 为 null，无法处理执行结果"
        });
        return;
    }

    var functionName = ctx.FunctionName;
    var outputKey = ctx.OutputKey;
    var onResult = ctx.OnResult;
    var eventName = ctx.EventName;

    if (result.IsSuccess)
    {
        object? resolvedValue = result.Value;

        // outputKey 字典提取逻辑（保留）
        if (resolvedValue is System.Collections.IDictionary dict &&
            !string.IsNullOrEmpty(outputKey) &&
            dict.Contains(outputKey))
        {
            resolvedValue = dict[outputKey];
        }

        EventBindingDiagnosticContext.Record(EventNames.EventBinderFunctionSuccess, new
        {
            name = functionName,
            result = resolvedValue?.ToString(),
            resultType = resolvedValue?.GetType().Name,
            executionTimeMs = result.ExecutionTime.TotalMilliseconds
        });

        // ★ 2026-07-15 重构：删除 _outputWriter.WriteOutput 调用
        //   输出写入由调用方通过 onResult 回调处理

        onResult?.Invoke(functionName, "success", new
        {
            result = resolvedValue,
            resultType = resolvedValue?.GetType().Name,
            eventName,
            executionTime = result.ExecutionTime.TotalMilliseconds,
            status = result.Status.ToString()
        });
    }
    else
    {
        // 失败分支保持不变
        EventBindingDiagnosticContext.Record(EventNames.EventBinderFunctionException, new
        {
            name = functionName,
            error = result.ErrorMessage ?? "Unknown error",
            hasException = result.Exception != null,
            exceptionType = result.Exception?.GetType().Name,
            exceptionMessage = result.Exception?.Message,
            exceptionStackTrace = result.Exception?.StackTrace
        });

        onResult?.Invoke(functionName, "failed", new
        {
            error = result.ErrorMessage ?? "Unknown error",
            eventName
        });
    }
}
```

#### 3.4 删除 using 语句

```diff
- using Share.EventBindingOrchestrator.Abstractions;
```

如果 `EventBindingStageBase` 中不再使用 `Share.EventBindingOrchestrator.Abstractions` 命名空间中的其他类型，则删除此 using。

---

### Step 4: 删除 `EventBindingOptions.OutputWriter` 属性

**文件**：`CsharpPrj\Share\EventBindingOrchestrator\Abstractions\EventBindingOptions.cs`

```diff
-     /// <summary>
-     /// 输出写回服务（可选）
-     /// <para>★ 2026-07-15 重构：合并 OutputControlFinder + OutputWriter 两个委托，</para>
-     /// <para>   统一使用 IOutputWriter 接口。特殊控件适配下沉到 unified.json 配置层。</para>
-     /// <para>由上层（如 WorkFlowMethod.Output.OutputWriter）注入。</para>
-     /// <para>当为 null 时，输出写回会被跳过。</para>
-     /// </summary>
-     public IOutputWriter? OutputWriter { get; set; }
```

---

### Step 5: 更新 `EventBindingPipelineFactory.cs`

**文件**：`CsharpPrj\Share\EventBindingOrchestrator\Transformers\EventBindingPipelineFactory.cs`

```diff
  var stageOptions = new EventBindingStageOptions
  {
      EventExecutionService = eventExecutionService,
      OnEventResult = onEventResult,
      BinderStrategy = resolvedBinderStrategy,
      ParameterBuilder = options?.ParameterBuilder,
      Registry = options?.Registry,
-     // ★ 2026-07-15 重构：合并 OutputControlFinder + OutputWriter 为 OutputWriter（IOutputWriter）
-     OutputWriter = options?.OutputWriter
  };
```

---

### Step 6: 更新 `WdUI.RenderPipeline.cs`

**文件**：`CsharpPrj\Operation\WdUI\WdUI.RenderPipeline.cs`

删除 fallback 路径中的 `IOutputWriter` 解析：

```diff
  var fallbackOptions = new EventBindingOptions
  {
      EventBus = eventBus,
      ParameterBuilder = (sender, bindings) =>
          new WorkFlowMethod.Parameters.ParameterBuilder().Build(
              sender, bindings as List<CSG.Unified.ParameterBinding>),
      Registry = FrameworkAbstractions.ComponentRegistry.AppServiceProvider.Resolve<TConfig.IFunctionRegistry>(),
-     // ★ 2026-07-15 重构：使用 IOutputWriter 替代两个委托
-     OutputWriter = FrameworkAbstractions.ComponentRegistry.AppServiceProvider
-         .Resolve<Share.EventBindingOrchestrator.Abstractions.IOutputWriter>()
  };
```

同时删除不再需要的 using：

```diff
- using Share.EventBindingOrchestrator.Abstractions;
```

---

### Step 7: 更新 `EventBindingStageOptions` 注释

**文件**：`CsharpPrj\Operation\WorkFlowMethod\EventBinding\EventBindingStageBase.cs`

```diff
-     // ★ 2026-07-15 重构：ParameterBuilder / Registry / OutputWriter 属性已由
-     //   EventBindingOptions 基类提供，此处不再重复定义。
-     //   参见 Share.EventBindingOrchestrator.Abstractions.EventBindingOptions。
+     // ★ 2026-07-15 重构：ParameterBuilder / Registry 属性已由
+     //   EventBindingOptions 基类提供，此处不再重复定义。
+     //   参见 Share.EventBindingOrchestrator.Abstractions.EventBindingOptions。
```

---

### Step 8: 编译验证

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln" 2>&1 | findstr /i "error CS"
```

---

## 影响分析

### 删除的文件（2 个）
- `IOutputWriter.cs` — 接口定义
- `OutputPipelineWriterAdapter.cs` — 适配器实现

### 修改的文件（4 个）
- `EventBindingStageBase.cs` — 删除字段 + 改造方法 + 更新注释
- `EventBindingOptions.cs` — 删除属性
- `EventBindingPipelineFactory.cs` — 删除传递
- `WdUI.RenderPipeline.cs` — 删除解析

### 不受影响
- `OutputPipelineRunner.cs` — 仍然存在，由调用方直接使用
- `Output 管线 Stages` — 5 个 Stage 不受影响
- `ParameterBuilder.cs` — 不受影响
- `EventContextScope.cs` — 不受影响

### 调用方变化

之前：`EventBindingStageBase` 内部自动调用 `_outputWriter.WriteOutput(resolvedValue)` 写入结果。

之后：`EventBindingStageBase` 只通过 `onResult` 回调返回 `resolvedValue`，调用方（如 `WdUI.RenderPipeline`）在 `onResult` 回调中自行调用 `OutputPipelineRunner.Execute(sender, outputTarget, resolvedValue)` 写入。

---

## 时序对比

### 改造前

```
EventBindingStageBase.CreateEventHandler
  └── Task.Run
        ├── ParameterBuilder.Build() → { a, b }
        ├── EventExecutionService.ExecuteFunctionAsync("Add", { a, b }) → 3.0
        └── HandleExecutionResult
              ├── 提取 resolvedValue = 3.0
              ├── _outputWriter.WriteOutput(3.0)          ← EventBinding 定义了输出
              │     └── OutputPipelineWriterAdapter
              │           └── OutputPipelineRunner.Execute(sender, "resultTextBox", 3.0)
              │                 └── 管线写入 resultTextBox.Text = "3"
              └── onResult?.Invoke("Add", "success", ...)
```

### 改造后

```
EventBindingStageBase.CreateEventHandler
  └── Task.Run
        ├── ParameterBuilder.Build() → { a, b }
        ├── EventExecutionService.ExecuteFunctionAsync("Add", { a, b }) → 3.0
        └── HandleExecutionResult
              ├── 提取 resolvedValue = 3.0
              └── onResult?.Invoke("Add", "success", { result: 3.0, ... })
                                                              ↑
调用方（WdUI.RenderPipeline 等）收到 onResult 回调：
  └── 从回调参数中获取 resolvedValue = 3.0
  └── OutputPipelineRunner.Execute(sender, "resultTextBox", 3.0)
        └── 管线写入 resultTextBox.Text = "3"
```
