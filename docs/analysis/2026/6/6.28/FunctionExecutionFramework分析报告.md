# FunctionExecutionFramework 分析报告

> 分析日期：2026-06-25
> 分析范围：`CsharpPrj/Operation/WorkFlowMethod/FunctionExecutionFramework/` vs `CsharpPrj/BasicMethod/FunctionExecution/`

---

## 一、FunctionExecutionFramework 功能概述

**FunctionExecutionFramework**（Operation 层）是一个**函数执行框架**，提供以下核心能力：

| 功能 | 说明 |
|------|------|
| **函数注册** | 支持委托、函数单元（IFunctionUnit）、工作流函数（Func<WorkflowContext, object>）三种注册方式 |
| **函数执行** | 按名称查找执行、直接执行委托、直接执行函数单元，支持参数解析（数组/字典/单值） |
| **管线执行** | 步骤管线（条件/并行/子管线嵌套），支持链式传参（体系B）和显式参数（体系A） |
| **内置函数** | Plus/Int/Float/Double/String/Concat/GetPathValue/SetPathValue/ExecuteWorkflow 等 |
| **BasicMethod 桥接** | BasicMethodAdapter 实现双向同步 |
| **函数覆盖** | 支持测试/AOP 场景的函数覆盖机制 |

---

## 二、与 BasicMethod 层的对比分析

### 2.1 文件级对比

| FunctionExecutionFramework 文件 | BasicMethod 对应文件 | 差异分析 |
|------|------|------|
| `Core/FunctionRegistry.cs` | `FunctionRegistry.cs` | **几乎相同**。仅命名空间不同，FEF 版本使用 `object?[]` 而非 `object[]` |
| `Core/FunctionExecutor.cs` | `FunctionCall.cs` 中的 `FunctionExecutor` | **几乎相同**。FEF 版本更简洁，无超时控制、无参数处理器（IParameterProcessor） |
| `Core/PipelineExecutor.cs` | `FunctionRegistry.cs` 中的 `PipelineExecutor`+`PipelineBuilder` | **几乎相同**。FEF 版本将 PipelineBuilder 合并到同一文件 |
| `Core/DefaultWorkflowRegistry.cs` | `WorkflowFunctions/DefaultWorkflowRegistry.cs` | **几乎相同**。FEF 版本使用 `KeyNotFoundException` 而非自定义 `WorkflowFunctionNotFoundException` |
| `Abstractions/IFunctionRegistry.cs` | `ExecutionContext.cs` 中的 `IFunctionRegistry` | **几乎相同** |
| `Abstractions/IFunctionExecutor.cs` | `ExecutionContext.cs` 中的 `IFunctionExecutor` | **几乎相同** |
| `Abstractions/IFunctionUnit.cs` | `ExecutionContext.cs` 中的 `IFunctionUnit` | **几乎相同**。FEF 版本多了 `IFunctionUnit<TInput, TOutput>` 的 `ExecutionResult<TOutput>` 返回 |
| `Abstractions/IPipelineExecutor.cs` | `ExecutionContext.cs` 中的 `IPipelineExecutor`+`IPipelineBuilder` | **几乎相同** |
| `Abstractions/IWorkflowRegistry.cs` | `WorkflowFunctions/IWorkflowRegistry.cs` | **几乎相同** |
| `Models/FunctionDescriptor.cs` | `ExecutionContext.cs` 中的 `FunctionDescriptor` | **几乎相同** |
| `Models/WorkflowContext.cs` | `WorkflowFunctions/WorkflowContext.cs` | **几乎相同** |
| `Registrars/WorkflowFunctionRegistrar.cs` | `WorkflowFunctions/DefaultWorkflowRegistry` | **功能重复**，只是多了一层静态封装 |
| `Registrars/BuiltInFunctionRegistrar.cs` | `FunctionCall.cs` 中的 `BuiltInUnits` | **部分重叠**，FEF 有更多内置函数 |
| `Adapters/BasicMethodAdapter.cs` | **无对应** | **全新功能** |
| `Models/ExecutionRequest.cs` | **无对应** | **全新功能** |
| `FunctionExecutionFrameworkExtensions.cs` | `FunctionRegistry.cs` 中的 `FunctionRegistryExtensions` + `FunctionCall.cs` 中的 `FunctionExecutionExtensions` | **部分重叠**，FEF 有更多扩展方法 |

### 2.2 功能覆盖矩阵

```
Legend: ✅ = 完全覆盖  ◐ = 部分覆盖  ❌ = 未覆盖  N/A = 不适用

功能项                          BasicMethod 层    Share 层
──────────────────────────────────────────────────────────
函数注册（委托）                    ✅              ❌
函数注册（IFunctionUnit）          ✅              ❌
函数注册（工作流函数）              ✅              ❌
函数执行（按名称）                  ✅              ❌
函数执行（委托）                    ✅              ❌
函数执行（函数单元）                ✅              ❌
管线执行（链式传参）                ✅              ❌
管线执行（显式参数）                ✅              ❌
管线执行（条件步骤）                ✅              ❌
管线执行（并行步骤）                ✅              ❌
管线执行（子管线嵌套）              ✅              ❌
参数解析（数组/字典/单值）          ✅              ❌
超时控制                          ✅              ❌
表达式树编译执行器                  ✅              ❌
内置函数（字符串/类型转换）         ✅              ❌
内置函数（算术运算）               ◐（无）         ❌
内置函数（路径操作）               ◐（无）         ❌
内置函数（工作流执行）             ◐（无）         ❌
BasicMethod 双向同步               ❌              ❌
ExecutionRequest 封装              ❌              ❌
ExecutionResult<T> 强类型结果      ◐（无泛型版）   ❌
子上下文创建                       ◐（无）         ❌
扩展方法（注册/执行/管线）         ◐（部分）        ❌
工作流级执行（图数据库）            ❌              ✅
UI/AI 操作执行                     ❌              ✅
执行结果验证                       ❌              ✅
```

---

## 三、应提取到 BasicMethod 层的逻辑

### 3.1 BuiltInFunctionRegistrar — 内置工作流函数注册器

**现状：** BasicMethod 的 `BuiltInUnits` 只注册了 `StringJoin`、`StringSplit`、`ListCount`、`DictGet`、类型转换（ToString/ToInt/ToFloat/ToDouble/ToBool），缺少以下函数：

| 函数 | 签名 | 说明 |
|------|------|------|
| `Plus` | `(object a, object b) → object` | 支持 int/long/double/string 混合加法 |
| `Int` | `(object v) → int` | 转换为 int |
| `Float` | `(object v) → float` | 转换为 float |
| `Double` | `(object v) → double` | 转换为 double |
| `String` | `(object v) → string` | 转换为 string |
| `Concat` | `(params object[] values) → string` | 字符串拼接 |
| `MergePrefix` | `(string prefix, string value) → string` | 前缀合并 |
| `GetPathValue` | `(object root, string path) → object` | 基于 NestedDictionaryFramework 的路径取值 |
| `SetPathValue` | `(object root, string path, object value) → object` | 基于 NestedDictionaryFramework 的路径设值 |
| `ExecuteWorkflow` | `(WorkflowContext ctx) → object` | 执行子工作流 |
| `ExecuteWorkflowAsync` | `(WorkflowContext ctx) → Task<object>` | 异步执行子工作流 |

**提取建议：** 将 `BuiltInFunctionRegistrar` 合并到 BasicMethod 的 `BuiltInUnits.RegisterAll()` 中，统一注册所有内置函数。

### 3.2 BasicMethodAdapter — 双向同步适配器

**现状：** BasicMethod 的 `FunctionRegistry` 和 `DefaultWorkflowRegistry` 之间没有双向同步机制。当通过 `FunctionRegistry.Default` 注册函数时，`DefaultWorkflowRegistry` 无法感知；反之亦然。

**功能：**
- `SyncToBasicMethod()` — 将本框架的注册同步到 BasicMethod
- `SyncFromBasicMethod()` — 将 BasicMethod 的注册同步到本框架
- `SyncBoth()` — 双向同步

**提取建议：** 将 `BasicMethodAdapter` 提取到 BasicMethod 层，作为 `FunctionRegistry` 和 `DefaultWorkflowRegistry` 之间的标准桥接工具。

### 3.3 ExecutionRequest — 执行请求模型

**现状：** BasicMethod 的 `FunctionExecutor.ExecuteAsync` 直接接收 `object? parameters`，没有标准化的请求封装。

**功能：**
- `StartNode` — 起始节点名称
- `Parameters` — 执行参数（Dictionary<string, object>）
- `FunctionOverrides` — 函数覆盖（用于测试/AOP）
- `Timeout` — 超时设置
- `CancellationToken` — 取消令牌
- `ToWorkflowContext()` — 转换为 WorkflowContext

**提取建议：** 将 `ExecutionRequest` 提取到 BasicMethod 层，作为标准化的执行请求模型。

### 3.4 FunctionExecutionFrameworkExtensions — 扩展方法

**现状：** BasicMethod 的 `FunctionRegistryExtensions` 和 `FunctionExecutionExtensions` 只提供了部分扩展方法。

**缺失的扩展方法：**

| 扩展目标 | 方法 | 说明 |
|------|------|------|
| `ExecutionScope` | `WithData(key, value)` | 设置上下文数据 |
| `ExecutionScope` | `WithFunctionName(name)` | 设置函数名称 |
| `ExecutionScope` | `WithCancellationToken(token)` | 设置取消令牌 |
| `WorkflowContext` | `WithParameter(key, value)` | 添加参数 |
| `WorkflowContext` | `WithParameters(dict)` | 批量添加参数 |
| `WorkflowContext` | `WithFunctionOverride(name, func)` | 添加函数覆盖 |
| `WorkflowContext` | `WithAsyncFunctionOverride(name, func)` | 添加异步函数覆盖 |
| `IPipelineBuilder` | `Step(name, func)` | Lambda 语法糖 |
| `IPipelineBuilder` | `StepIf(name, condition, func)` | 条件步骤语法糖 |
| `IPipelineBuilder` | `Parallel(steps)` | 并行步骤语法糖 |
| `IPipelineBuilder` | `Call(name, params)` | 显式参数语法糖 |
| `IPipelineBuilder` | `SubPipeline(name, build)` | 子管线语法糖 |
| `ExecutionResult` | `GetValueOrDefault<T>(default)` | 安全取值 |
| `ExecutionResult` | `TryGetValue<T>(out value)` | 尝试取值 |
| `ExecutionResult` | `ThrowIfFailed()` | 失败时抛出异常 |
| `IFunctionRegistry` | `RegisterAll(pairs)` | 批量注册 |
| `IFunctionRegistry` | `TryUnregister(name)` | 安全注销 |

**提取建议：** 将这些扩展方法合并到 BasicMethod 的 `FunctionExecutionExtensions` 中。

### 3.5 ExecutionResult<T> — 强类型执行结果（可选优化）

**现状：** BasicMethod 只有非泛型 `ExecutionResult`，没有 `ExecutionResult<T>`。

**功能：**
- `TypedValue` — 强类型的返回值
- `From(ExecutionResult)` — 从非泛型结果转换
- 隐式转换支持

**提取建议：** 将 `ExecutionResult<T>` 添加到 BasicMethod 的 `ExecutionResult` 中。

### 3.6 ExecutionScope 子上下文创建（可选优化）

**现状：** BasicMethod 的 `ExecutionContext` 有 `Items` 字典但没有 `CreateSubContext` 方法，没有 `ParentContext` 属性。

**功能：**
- `CreateSubContext()` — 创建子上下文（继承当前上下文的部分数据）
- `ParentContext` — 父上下文引用

**提取建议：** 将 `CreateSubContext` 和 `ParentContext` 添加到 BasicMethod 的 `ExecutionContext` 中。

---

## 四、与 Share 层的对比分析

**Share 层的 `WorkflowExecution` 是工作流级执行引擎**，关注生命周期管理、拓扑遍历、弹性策略，与 FunctionExecutionFramework 的**函数级执行框架**处于不同抽象层次，**不能互相覆盖**。

| 维度 | FunctionExecutionFramework | Share.WorkflowExecution |
|------|--------------------------|------------------------|
| **抽象层次** | 函数级 | 工作流级 |
| **核心关注** | 函数注册、执行、管线编排 | 工作流生命周期、拓扑遍历、弹性策略 |
| **执行单元** | 函数/委托/IFunctionUnit | UI 操作 / AI 操作 |
| **数据源** | 内存注册表 | 图数据库 |
| **验证能力** | 无 | 有（WorkflowExecutionValidator） |
| **适用场景** | 函数调用、管线编排 | 工作流执行、UI/AI 操作管理 |

---

## 五、结论与建议

### 5.1 核心结论

**FunctionExecutionFramework 的功能在 BasicMethod 层已有完整的通用框架覆盖。** 12 个核心文件与 BasicMethod 层几乎完全重复，应直接删除，改为引用 BasicMethod 层的对应类型。

### 5.2 提取清单

| 优先级 | 逻辑 | 类型 | 工作量 |
|--------|------|------|--------|
| P0 | `BuiltInFunctionRegistrar` → 合并到 `BuiltInUnits.RegisterAll()` | 补充 | 小 |
| P0 | `BasicMethodAdapter` → 提取到 BasicMethod 层 | 新增 | 中 |
| P1 | `ExecutionRequest` → 提取到 BasicMethod 层 | 新增 | 小 |
| P1 | `FunctionExecutionFrameworkExtensions` → 合并到 BasicMethod 扩展 | 补充 | 中 |
| P2 | `ExecutionResult<T>` → 添加到 BasicMethod | 优化 | 小 |
| P2 | `ExecutionContext.CreateSubContext` → 添加到 BasicMethod | 优化 | 小 |

### 5.3 重构后架构

```
BasicMethod/FunctionExecution/
├── FunctionRegistry.cs          ← 保留（已有）
├── FunctionCall.cs              ← 保留（已有，含 FunctionExecutor/BuiltInUnits）
├── ExecutionContext.cs          ← 保留（已有，含所有接口/模型）
├── WorkflowFunctions/
│   ├── IWorkflowRegistry.cs     ← 保留（已有）
│   ├── DefaultWorkflowRegistry.cs ← 保留（已有）
│   ├── WorkflowContext.cs       ← 保留（已有）
│   ├── WorkflowRegistryExtensions.cs ← 保留（已有）
│   ├── OverridableWorkflowRegistry.cs ← 保留（已有）
│   └── WorkflowFunctionNotFoundException.cs ← 保留（已有）
├── Adapters/
│   └── BasicMethodAdapter.cs    ← 新增（从 FEF 提取）
├── Models/
│   └── ExecutionRequest.cs      ← 新增（从 FEF 提取）
└── Extensions/
    └── FunctionExecutionFrameworkExtensions.cs ← 新增（从 FEF 提取，合并现有扩展）

Operation/WorkFlowMethod/FunctionExecutionFramework/
├── FunctionExecutionFramework.cs ← 精简为对 BasicMethod 的引用
└── FunctionExecutionFramework.csproj ← 添加 BasicMethod 项目引用
```
