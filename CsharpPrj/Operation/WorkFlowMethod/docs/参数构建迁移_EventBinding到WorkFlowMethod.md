# 参数构建迁移：EventBinding → WorkFlowMethod

> **文档状态**：规划稿 · 2026-07-14  
> **目标**：将参数构建职责从 `Share.EventBindingOrchestrator` 迁移到 `Operation.WorkFlowMethod`，实现职责分离

---

## 1. 迁移背景与目标

### 1.1 当前问题

`Share.EventBindingOrchestrator` 承担了超出事件绑定范围的职责：

```
EventBinding（事件绑定层）
  ├─ 控件事件 → 函数名映射                    ✅ 核心职责
  ├─ ParameterBuilder.Build(sender, bindings)  ❌ 参数构建
  ├─ OutputHelper.WriteOutputToControl()        ❌ 结果写回
  └─ HandleExecutionResult()                   ❌ 执行结果处理
```

这导致：
- **职责混淆**：事件绑定层关心了"函数参数怎么构建"和"结果怎么写回"
- **`IEventBindingInfo` 接口缺失 Parameters**：因为参数构建不在 EventBinding 的职责范围内，所以 `ToFindBindingFunc` 返回 `null` 作为 Inputs 是合理的
- **Share 层膨胀**：约 980 行参数构建代码在 Share 层，本应属于 Operation 层

### 1.2 迁移目标

```
EventBinding（事件绑定层）
  └─ 控件事件 → 函数名映射
  └─ 事件触发时调用 WorkFlowMethod.ExecuteAsync(functionName, sender, parameterBindings, output)

WorkFlowMethod（函数执行层）
  ├─ ParameterBuilder.Build(sender, parameterBindings) → Dictionary<string, object>
  ├─ ExecuteFunctionAsync(functionName, parameters) → ExecutionResult
  └─ OutputHelper.WriteOutputToControl(output, result)  // 结果写回
```

---

## 2. 当前架构分析

### 2.1 EventBindingOrchestrator 中的参数构建逻辑全景

```
┌─────────────────────────────────────────────────────────────────┐
│                   事件触发时（运行时）                            │
│                                                                 │
│  sender 触发事件                                                 │
│    ↓                                                             │
│  CreateEventHandler (EventBindingStageBase.cs:105)               │
│    ├─ _parameterBuilder.Build(sender, parameterBindings)         │
│    │   └─ ParameterBuilder.Build (ParameterBuilder.cs:49)        │
│    │       └─ StagePipeline 编排:                                │
│    │           ├─ ParentFormLookupStage                          │
│    │           ├─ ForEachParameterBindingStage                   │
│    │           │   └─ ParameterBindingResolveStage (per binding) │
│    │           │       ├─ sourceType="control" → 查找控件+取值   │
│    │           │       ├─ sourceType="literal" → 直接返回字符串  │
│    │           │       ├─ sourceType="expression" → 暂按literal  │
│    │           │       └─ 类型转换 (ITypeConversionService)      │
│    │           ├─ SenderValueAddStage                            │
│    │           └─ FallbackValueStage                             │
│    ├─ _executionEngine.ExecuteFunctionAsync(functionName, params)│
│    └─ HandleExecutionResult(result, output, sender)              │
│        └─ OutputHelper.WriteOutputToControl(target, value)       │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                   绑定管线时（初始化）                            │
│                                                                 │
│  BindingMetadataTransformer (BindingMetadataTransformer.cs)      │
│    ├─ ExtractFromDeclaration: declaration.Parameters →           │
│    │   input.CurrentParameterBindings                            │
│    └─ ExtractFromConfig: config.Events.Bindings.Parameters →     │
│        input.CurrentParameterBindings                            │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 调用链中的关键文件

| 文件 | 行数 | 角色 |
|------|------|------|
| `Invoke/Pipeline/ParameterBuilder.cs` | 83 | 参数构建器入口 |
| `Invoke/Pipeline/ParameterBuilderStages.cs` | 417 | 参数构建管线阶段（5 个 Stage） |
| `Invoke/Pipeline/EventBindingStageBase.cs` | 488 | 事件处理器基类（含参数构建+执行+写回） |
| `Invoke/Pipeline/OutputHelper.cs` | 221 | 输出写回辅助方法 |
| `Transformers/BindingMetadataTransformer.cs` | 169 | 绑定元数据提取（含参数提取） |
| `Contexts/EventBindingContext.cs` | — | 上下文（含 CurrentParameterBindings） |
| `Services/DefaultParameterBuilder.cs` | 52 | IParameterBuilder<string> 实现 |

---

## 3. 迁移范围清单

### 3.1 核心迁移对象

| # | 组件 | 文件路径 | 行数 | 迁移方式 |
|---|------|---------|------|---------|
| 1 | **ParameterBuilder** | `Invoke/Pipeline/ParameterBuilder.cs` | 83 | 整体复制到 WorkFlowMethod，删除原文件 |
| 2 | **ParameterBuilderStages**（5 个 Stage） | `Invoke/Pipeline/ParameterBuilderStages.cs` | 417 | 整体复制到 WorkFlowMethod，删除原文件 |
| 3 | **OutputHelper** | `Invoke/Pipeline/OutputHelper.cs` | 221 | 整体复制到 WorkFlowMethod，删除原文件 |
| 4 | **EventBindingStageBase** 中的参数构建+执行+写回 | `Invoke/Pipeline/EventBindingStageBase.cs` | ~176（105-281 行） | 简化，委托给 WorkFlowMethod |
| 5 | **BindingMetadataTransformer** 中的参数提取 | `Transformers/BindingMetadataTransformer.cs` | ~30（71-104 行） | 移除参数提取逻辑 |
| 6 | **EventBindingContext.CurrentParameterBindings** | `Contexts/EventBindingContext.cs` | 1 属性 | 移除 |
| 7 | **DefaultParameterBuilder** | `Services/DefaultParameterBuilder.cs` | 52 | 合并到 WorkFlowMethod 或移除 |

**总计约 980 行代码**应从 EventBindingOrchestrator 迁移到 WorkFlowMethod。

### 3.2 各组件详细分析

#### 3.2.1 ParameterBuilder（核心）

**当前文件**：`CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/ParameterBuilder.cs`

```csharp
public class ParameterBuilder
{
    public Dictionary<string, object> Build(object sender, List<ParameterBinding>? parameterBindings)
    {
        var ctx = new ParameterBuildContext { Sender = sender, ParameterBindings = parameterBindings };
        _buildPipeline.ExecuteAsync(ctx).GetAwaiter().GetResult();
        return ctx.Parameters;
    }
}
```

**迁移目标**：复制到 `CsharpPrj/Operation/WorkFlowMethod/Parameters/ParameterBuilder.cs`

**依赖分析**：
- `CSG.Unified`（ParameterBinding 类型）— WorkFlowMethod 已引用
- `Modules.Pipeline`（StagePipeline）— WorkFlowMethod 已引用（通过 Pipeline.csproj）
- `ParameterResolver.Abstractions.ITypeConversionService` — WorkFlowMethod 已引用（通过 ParameterResolver.csproj）
- `Share.EventBindingOrchestrator.Diagnostics.EventNames` — 需解耦，改为 WorkFlowMethod 自己的诊断

#### 3.2.2 ParameterBuilderStages（5 个 Stage）

**当前文件**：`CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/ParameterBuilderStages.cs`

包含：
- `ParameterBuildContext` — 参数构建上下文
- `ParentFormLookupStage` — 父窗体查找
- `ForEachParameterBindingStage` — 参数绑定遍历
- `ParameterBindingResolveStage` — 参数绑定解析（核心）
- `SenderValueAddStage` — 发送者值添加
- `FallbackValueStage` — 回退值处理

**迁移目标**：复制到 `CsharpPrj/Operation/WorkFlowMethod/Parameters/ParameterBuilderStages.cs`

**依赖分析**：
- `Modules.EventFramework.Adapters.WinFormsEventSourceNode` — 用于控件查找和取值
  - 当前 WorkFlowMethod 未引用 `EventFramework.csproj`，但 `WorkflowMethodExecutionService` 已引用
  - 需要确保 `EventFramework.csproj` 的引用在 WorkFlowMethod 中可用
- `ParameterResolver.Converter.TypeResolver` — 用于类型解析
  - WorkFlowMethod 已引用 `ParameterResolver.csproj`

#### 3.2.3 OutputHelper

**当前文件**：`CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/OutputHelper.cs`

包含：
- `WriteOutputToControl` — 将值写回控件
- `WriteToListView` — ListView 特殊处理
- `ParseListViewItems` — 列表项解析
- `FindOutputControl` — 查找输出控件

**迁移目标**：复制到 `CsharpPrj/Operation/WorkFlowMethod/Output/OutputHelper.cs`

**依赖分析**：
- `Modules.Threading.ReentryGuard` — 重入保护
- `Modules.EventFramework.Adapters.WinFormsThreadDispatcher` — 跨线程调度
- `ParameterResolver.Converter.StringToListConverter` — 列表解析

#### 3.2.4 EventBindingStageBase.CreateEventHandler（简化）

**当前逻辑**（第 105-281 行，约 176 行）：

```csharp
protected EventHandler CreateEventHandler(object control, string eventName, string functionName,
    List<ParameterBinding>? parameterBindings = null, string? output = null, ...)
{
    return (sender, args) =>
    {
        Task.Run(async () =>
        {
            // 1. 参数构建
            var parameters = _parameterBuilder.Build(sender, parameterBindings);

            // 2. 函数执行
            var result = await _executionEngine.ExecuteFunctionAsync(functionName, parameters);

            // 3. 结果写回
            await HandleExecutionResult(result, functionName, output, sender, ...);
        });
    };
}
```

**迁移后简化**（约 30 行）：

```csharp
protected EventHandler CreateEventHandler(object control, string eventName, string functionName,
    List<ParameterBinding>? parameterBindings = null, string? output = null, ...)
{
    return (sender, args) =>
    {
        Task.Run(async () =>
        {
            // 委托给 WorkFlowMethod，由它完成参数构建 + 执行 + 结果写回
            var result = await _executionEngine.ExecuteFunctionAsync(functionName,
                sender, parameterBindings, output);
        });
    };
}
```

#### 3.2.5 BindingMetadataTransformer 中的参数提取

**当前逻辑**（第 71-104 行）：

```csharp
// ExtractFromDeclaration
input.CurrentParameterBindings = parameterBindings;

// ExtractFromConfig
input.CurrentParameterBindings = new List<ParameterBinding>(config.Events.Bindings.Parameters);
```

**迁移后**：移除参数提取逻辑。`CurrentParameterBindings` 不再需要，因为参数映射由 WorkFlowMethod 管理。

#### 3.2.6 EventBindingContext.CurrentParameterBindings

**当前**：`EventBindingContext` 包含 `CurrentParameterBindings` 属性，用于在管线阶段间传递参数绑定列表。

**迁移后**：移除该属性。参数绑定列表直接由 `CreateEventHandler` 从配置读取并传给 WorkFlowMethod。

#### 3.2.7 DefaultParameterBuilder

**当前文件**：`Services/DefaultParameterBuilder.cs`

实现了 `IParameterBuilder<string>` 接口，但逻辑过于简单（只是将 sourceName 作为 key），与 `ParameterBuilder` 功能重叠。

**迁移后**：移除或合并到 WorkFlowMethod 的 ParameterBuilder 中。

---

## 4. 迁移后架构设计

### 4.1 新的接口定义

在 WorkFlowMethod 中新增接口：

```csharp
// WorkFlowMethod/Abstractions/IParameterBuilder.cs
public interface IParameterBuilder
{
    Dictionary<string, object> Build(object sender, IReadOnlyList<ParameterBinding>? bindings);
}
```

```csharp
// WorkFlowMethod/Abstractions/IOutputWriter.cs
public interface IOutputWriter
{
    void WriteOutput(object sender, string? outputTarget, object? value);
}
```

### 4.2 IExecutionEngine 接口扩展

当前 `IExecutionEngine` 接口：

```csharp
Task<ExecutionResult> ExecuteFunctionAsync(string functionName, Dictionary<string, object> parameters);
```

迁移后新增重载：

```csharp
// 新增：接收 sender + parameterBindings + output，内部完成参数构建和执行
Task<ExecutionResult> ExecuteFunctionAsync(string functionName,
    object sender,
    IReadOnlyList<ParameterBinding>? parameterBindings = null,
    string? output = null);
```

### 4.3 WorkFlowMethod 新增目录结构

```
CsharpPrj/Operation/WorkFlowMethod/
  ├─ Parameters/                          # ★ 新增
  │   ├─ ParameterBuilder.cs              # 从 EventBinding 迁移
  │   ├─ ParameterBuilderStages.cs        # 从 EventBinding 迁移
  │   └─ Abstractions/
  │       └─ IParameterBuilder.cs         # ★ 新增接口
  ├─ Output/                              # ★ 新增
  │   ├─ OutputHelper.cs                  # 从 EventBinding 迁移
  │   └─ Abstractions/
  │       └─ IOutputWriter.cs             # ★ 新增接口
  └─ WorkflowMethodExecutionService.cs    # 修改：实现新重载
```

### 4.4 迁移后的调用链

```
事件触发
  ↓
EventBindingStageBase.CreateEventHandler
  ↓ (简化)
_executionEngine.ExecuteFunctionAsync(functionName, sender, parameterBindings, output)
  ↓
WorkflowMethodExecutionService.ExecuteFunctionAsync (新重载)
  ├─ _parameterBuilder.Build(sender, parameterBindings) → Dictionary<string, object>
  ├─ _nodeExecutor.ExecuteNodeAsync(node, context) → ExecutionResult
  └─ _outputWriter.WriteOutput(sender, output, result.Value)
```

---

## 5. 分步迁移计划

### 步骤 1：WorkFlowMethod 新增目录和接口（安全，无破坏性）

1. 在 WorkFlowMethod 中创建 `Parameters/` 和 `Output/` 目录
2. 创建 `IParameterBuilder` 和 `IOutputWriter` 接口
3. 从 EventBinding 复制 `ParameterBuilder.cs`、`ParameterBuilderStages.cs`、`OutputHelper.cs`
4. 调整命名空间为 `WorkFlowMethod.Parameters` 和 `WorkFlowMethod.Output`
5. 解耦对 `Share.EventBindingOrchestrator.Diagnostics` 的依赖，改为 WorkFlowMethod 自己的诊断

**验证**：编译通过，不影响现有功能

### 步骤 2：扩展 IExecutionEngine 接口

1. 在 `IExecutionEngine` 接口中新增重载方法
2. 在 `WorkflowMethodExecutionService` 中实现新重载
3. 新重载内部调用 ParameterBuilder + NodeExecutor + OutputHelper

**验证**：编译通过，新重载可用

### 步骤 3：简化 EventBindingStageBase

1. 修改 `CreateEventHandler`，将参数构建+执行+写回委托给 `IExecutionEngine` 的新重载
2. 移除 `_parameterBuilder` 字段（不再需要）
3. 移除 `HandleExecutionResult` 方法（不再需要）

**验证**：功能测试通过，事件触发后函数正常执行

### 步骤 4：清理 EventBindingOrchestrator

1. 删除 `ParameterBuilder.cs`、`ParameterBuilderStages.cs`、`OutputHelper.cs`
2. 删除 `DefaultParameterBuilder.cs`
3. 从 `BindingMetadataTransformer` 中移除参数提取逻辑
4. 从 `EventBindingContext` 中移除 `CurrentParameterBindings` 属性
5. 更新 `EventBindingOrchestrator.csproj`，移除不再需要的依赖

**验证**：编译通过，所有引用已更新

### 步骤 5：最终清理

1. 删除 `EventBindingOrchestrator/Invoke/Pipeline/` 中已迁移的文件
2. 更新 `EventBinding_CallChain.md` 等文档
3. 运行完整测试套件

---

## 6. 风险与注意事项

### 6.1 兼容性风险

- **`ParameterBuilder` 的 `Build` 方法签名不变**：`Build(object sender, List<ParameterBinding>? parameterBindings)` — 保持向后兼容
- **`OutputHelper` 的静态方法签名不变**：`WriteOutputToControl(object control, object? value)` — 保持向后兼容
- **`IExecutionEngine` 新增重载，不修改现有方法**：不影响现有调用方

### 6.2 依赖风险

- **WinForms 依赖**：`ParameterBuilderStages` 和 `OutputHelper` 依赖 `WinFormsEventSourceNode` 和 `WinFormsThreadDispatcher`，这些在 `Modules.EventFramework.Adapters` 中
  - WorkFlowMethod 已引用 `EventFramework.csproj`（第 101-103 行），所以可用
  - 但需要确认 `EventFramework.csproj` 是否已正确暴露这些适配器

- **诊断依赖**：当前代码使用 `EventBindingDiagnosticContext.Record` 记录诊断
  - 迁移后需要改为 WorkFlowMethod 自己的诊断机制（如 `WorkFlowMethodLog`）
  - 或者保留对 `EventBindingDiagnosticContext` 的引用（不推荐，会造成跨层依赖）

### 6.3 测试策略

1. **步骤 1 后**：验证 WorkFlowMethod 中的 ParameterBuilder 单元测试通过
2. **步骤 3 后**：验证事件触发 → 函数执行的集成测试通过
3. **步骤 4 后**：验证 EventBindingOrchestrator 编译通过，无残留引用

### 6.4 回退策略

如果迁移后出现问题，可以：
1. 保留 EventBinding 中的原代码（标记为 `[Obsolete]`）
2. 通过配置开关控制使用旧路径还是新路径
3. 逐步切换，先灰度再全量

---

## 7. 附录

### 7.1 当前文件引用关系

```
EventBindingStageBase.cs
  ├─ using ParameterBuilder = Share.EventBindingOrchestrator.Parameters.ParameterBuilder
  ├─ using OutputHelper = Share.EventBindingOrchestrator.Parameters.Output.OutputHelper
  └─ using IExecutionEngine = ExecutionFramework.Abstractions.IExecutionEngine

ParameterBuilder.cs
  ├─ using CSG.Unified (ParameterBinding)
  ├─ using Modules.Pipeline (StagePipeline)
  └─ using ParameterResolver.Abstractions.ITypeConversionService

ParameterBuilderStages.cs
  ├─ using CSG.Unified (ParameterBinding)
  ├─ using Modules.Pipeline (StagePipeline)
  ├─ using Modules.EventFramework.Adapters.WinFormsEventSourceNode
  ├─ using ParameterResolver.Converter.TypeResolver
  └─ using ParameterResolver.Abstractions.ITypeConversionService

OutputHelper.cs
  ├─ using Modules.Threading.ReentryGuard
  ├─ using Modules.EventFramework.Adapters.WinFormsThreadDispatcher
  └─ using ParameterResolver.Converter.StringToListConverter
```

### 7.2 WorkFlowMethod 已引用的相关项目

| 项目引用 | 用途 | 状态 |
|---------|------|------|
| `CSG.Unified.csproj` | ParameterBinding 类型 | ✅ 已引用 |
| `Pipeline.csproj` | StagePipeline | ✅ 已引用 |
| `ParameterResolver.csproj` | ITypeConversionService, TypeResolver | ✅ 已引用 |
| `EventFramework.csproj` | WinFormsEventSourceNode, WinFormsThreadDispatcher | ✅ 已引用 |
| `ExecutionFramework.csproj` | IExecutionEngine | ✅ 已引用 |
| `PlaceholderFramework.csproj` | 日志体系 | ✅ 已引用 |

### 7.3 迁移前后文件对比

| 迁移前（EventBindingOrchestrator） | 迁移后（WorkFlowMethod） |
|-----------------------------------|--------------------------|
| `Invoke/Pipeline/ParameterBuilder.cs` | `Parameters/ParameterBuilder.cs` |
| `Invoke/Pipeline/ParameterBuilderStages.cs` | `Parameters/ParameterBuilderStages.cs` |
| `Invoke/Pipeline/OutputHelper.cs` | `Output/OutputHelper.cs` |
| `Services/DefaultParameterBuilder.cs` | （删除） |
| `Invoke/Pipeline/EventBindingStageBase.cs`（176 行） | `Invoke/Pipeline/EventBindingStageBase.cs`（30 行） |
| `Transformers/BindingMetadataTransformer.cs`（含参数提取） | `Transformers/BindingMetadataTransformer.cs`（不含参数提取） |
| `Contexts/EventBindingContext.cs`（含 CurrentParameterBindings） | `Contexts/EventBindingContext.cs`（不含 CurrentParameterBindings） |
