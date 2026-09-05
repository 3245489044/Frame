# WorkflowUseage 前台使用模块架构分析

## 1. 概述

`WorkflowUseage` 是 `WorkFlowMethod` 项目的前台使用模块，位于 `WorkFlowMethod/WorkflowUseage.cs`。它基于 `BasicMethod/FunctionExecution` 的管线执行引擎，桥接 `WorkflowFunctions` 工作流函数注册表，提供统一的工作流执行、函数调用、文件驱动加载能力。

### 1.1 核心设计目标

1. **统一函数注册**：桥接 `Core.IFunctionRegistry`（通用委托注册）和 `WorkflowFunctions.IWorkflowRegistry`（工作流上下文注册）两个注册表
2. **管线化执行**：利用 `PipelineExecutor` 编排工作流步骤
3. **文件驱动**：从文件加载工作流定义并执行
4. **兼容现有接口**：保持对 `WorkFlowMethod` 旧有代码的兼容

---

## 2. 模块架构

```
┌─────────────────────────────────────────────────────────────────┐
│                    WorkflowUseage (前台使用模块)                   │
│  WorkFlowMethod/WorkflowUseage.cs                                │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  静态初始化 (Initialize / EnsureInitialized)              │    │
│  │  - IWorkflowRegistry _workflowRegistry                   │    │
│  │  - IFunctionRegistry _basicRegistry                      │    │
│  │  - BasicToWorkflowRegistryAdapter _bridgeAdapter         │    │
│  │  - PipelineExecutor _pipelineExecutor                    │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ 函数注册  │ │ 函数执行  │ │ 管线编排  │ │ 文件驱动  │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│              BasicMethod/FunctionExecution 管线引擎               │
│                                                                  │
│  ┌─────────────────────┐  ┌─────────────────────────────────┐   │
│  │ Core (核心抽象层)     │  │ WorkflowFunctions (工作流函数层) │   │
│  │                     │  │                                 │   │
│  │ IFunctionRegistry   │  │ IWorkflowRegistry               │   │
│  │ IFunctionUnit       │  │ WorkflowContext                 │   │
│  │ ExecutionContext    │  │ DefaultWorkflowRegistry         │   │
│  │ PipelineExecutor    │  │ OverridableWorkflowRegistry     │   │
│  │ FunctionExecutor    │  │ WorkflowRegistryExtensions      │   │
│  └─────────────────────┘  └─────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Bridge (桥接层)                                           │    │
│  │                                                          │    │
│  │ WorkflowFunctionUnitAdapter  (IWorkflowRegistry→IFunctionUnit)│
│  │ BasicToWorkflowRegistryAdapter (IFunctionRegistry→IWorkflowRegistry)│
│  │ WorkflowBridgeManager (统一管理双向桥接)                    │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. 核心组件详解

### 3.1 BasicMethod/FunctionExecution 管线引擎

位于 `BasicMethod/FunctionExecution/`，命名空间 `UniversalWorkflowFramework.FunctionExecution`。

#### 3.1.1 Core 核心抽象层 (`ExecutionContext.cs`)

| 类型 | 说明 |
|------|------|
| `IFunctionRegistry` | 通用函数注册表接口，支持委托和函数单元注册 |
| `IFunctionUnit` | 函数单元接口，封装可执行的函数逻辑 |
| `IFunctionExecutor` | 函数执行器接口 |
| `IPipelineExecutor` | 管线执行器接口 |
| `IPipelineBuilder` | 管线构建器接口 |
| `ExecutionContext` | 通用执行上下文 |
| `ExecutionResult` | 执行结果 |
| `PipelineDefinition` | 管线定义 |
| `PipelineStep` | 管线步骤 |

#### 3.1.2 WorkflowFunctions 工作流函数层 (`WorkflowFunctions/`)

| 类型 | 文件 | 说明 |
|------|------|------|
| `IWorkflowRegistry` | `IWorkflowRegistry.cs` | 工作流函数注册表接口，函数签名统一为 `Func<WorkflowContext, object>` |
| `WorkflowContext` | `WorkflowContext.cs` | 工作流函数执行上下文，承载参数、共享数据、执行标识 |
| `DefaultWorkflowRegistry` | `DefaultWorkflowRegistry.cs` | 默认实现，支持同步/异步函数注册与调用 |
| `OverridableWorkflowRegistry` | `OverridableWorkflowRegistry.cs` | 可覆盖的装饰器，实现函数级别的 AOP 或测试替身 |
| `WorkflowFunctionNotFoundException` | `WorkflowFunctionNotFoundException.cs` | 函数未找到异常 |
| `WorkflowRegistryExtensions` | `WorkflowRegistryExtensions.cs` | 扩展方法（带覆盖调用、批量注册、TryCall） |

#### 3.1.3 Bridge 桥接层 (`Bridge/WorkflowBridge.cs`)

| 类型 | 说明 |
|------|------|
| `WorkflowFunctionUnitAdapter` | 将 `IWorkflowRegistry` 适配为 `IFunctionUnit`，使其可在管线中执行 |
| `BasicToWorkflowRegistryAdapter` | 将 `IFunctionRegistry` 适配为 `IWorkflowRegistry`，使其可在工作流中调用 |
| `WorkflowBridgeManager` | 统一管理两个函数注册表之间的双向桥接 |

### 3.2 WorkFlowMethod 项目

位于 `WorkFlowMethod/`，命名空间 `WorkFlow`。

#### 3.2.1 WorkflowUseage 前台模块 (`WorkflowUseage.cs`)

静态类，提供以下功能区域：

**初始化：**
```csharp
WorkflowUseage.Initialize(workflowRegistry, basicRegistry);
```

**函数注册：**
```csharp
// 注册到 WorkflowFunctions 注册表
WorkflowUseage.RegisterFunction("MyFunc", ctx => { ... });
WorkflowUseage.RegisterAsyncFunction("MyAsyncFunc", async ctx => { ... });

// 注册到 BasicMethod 注册表
WorkflowUseage.RegisterDelegate("MyDel", new Func<int, int>(x => x * 2));
WorkflowUseage.RegisterUnit(new MyFunctionUnit());

// 桥接注册
WorkflowUseage.RegisterWorkFlowFunctionToPipeline("MyFunc");
WorkflowUseage.RegisterAllWorkFlowFunctionsToPipeline();
```

**函数执行：**
```csharp
// 通过 WorkflowFunctions 注册表
var result = WorkflowUseage.CallFunction("MyFunc", context);
var asyncResult = await WorkflowUseage.CallFunctionAsync("MyAsyncFunc", context);

// 通过 BasicMethod 注册表
var execResult = await WorkflowUseage.ExecuteFunctionAsync("MyFunc", parameters);

// 通过桥接注册表（自动选择）
var bridgedResult = WorkflowUseage.CallBridgedFunction("MyFunc", context);
```

**管线编排：**
```csharp
// 创建管线
var pipeline = WorkflowUseage.CreatePipeline("MyPipeline")
    .AddStep("Step1", new Func<int, int>(x => x + 1))
    .AddStep("Step2", new Func<int, int>(x => x * 2))
    .Build();

// 执行管线
var result = await WorkflowUseage.ExecutePipelineAsync(pipeline);

// 快速单步执行
var stepResult = await WorkflowUseage.ExecuteStepAsync("Add", new Func<int, int, int>((a, b) => a + b), new object[] { 1, 2 });

// 快速多步执行
var multiResult = await WorkflowUseage.ExecuteStepsAsync(
    ("Step1", new Func<int, int>(x => x + 1)),
    ("Step2", new Func<int, int>(x => x * 2))
);
```

**文件驱动：**
```csharp
// 从文件加载管线
var pipeline = WorkflowUseage.LoadPipelineFromFile("pipeline.json");

// 从文件加载并执行
var result = await WorkflowUseage.ExecuteFromFileAsync("pipeline.json");

// 从 JSON 字符串加载并执行
var result = await WorkflowUseage.ExecuteFromJsonAsync(jsonString);

// 保存管线到文件
WorkflowUseage.SavePipelineToFile(pipeline, "pipeline.json");
```

**工具方法：**
```csharp
var allNames = WorkflowUseage.GetAllFunctionNames();
var exists = WorkflowUseage.HasFunction("MyFunc");
WorkflowUseage.Reset(); // 重置模块状态
```

#### 3.2.2 ExecutionEngine 目录

| 文件 | 说明 |
|------|------|
| `ExecutionEngine.cs` | 引擎核心，1303 行，包含工作流执行逻辑 |
| `ExecutionRequest.cs` | 执行请求模型 |
| `WorkflowExecutor.cs` | 工作流执行器 |
| `External.cs` | 外部函数集合 |
| `Integration.cs` | 内置函数注册（1779 行） |
| `CallFrame.cs` | 调用框架 |
| `IWorkflowExecutor.cs` | 工作流执行器接口 |

#### 3.2.3 Functions 目录（旧代码兼容层）

`WorkFlowMethod/Functions/` 目录中的文件已迁移至 `BasicMethod/FunctionExecution/WorkflowFunctions/`，当前仅保留 `global using` 别名映射以保持向后兼容：

| 文件 | 映射目标 |
|------|----------|
| `IFunctionRegistry.cs` | `IWorkflowRegistry` |
| `FunctionContext.cs` | `WorkflowContext` |
| `DefaultFunctionRegistry.cs` | `DefaultWorkflowRegistry` |
| `FunctionNotFoundException.cs` | `WorkflowFunctionNotFoundException` |
| `OverridableFunctionRegistry.cs` | `OverridableWorkflowRegistry` |
| `FunctionRegistryExtensions.cs` | `WorkflowRegistryExtensions` |
| `BridgeAdapter.cs` | `WorkflowFunctionUnitAdapter`, `BasicToWorkflowRegistryAdapter`, `WorkflowBridgeManager` |

---

## 4. 桥接机制详解

### 4.1 为什么需要桥接？

存在两个独立的函数注册体系：

| 特性 | Core.IFunctionRegistry | WorkflowFunctions.IWorkflowRegistry |
|------|------------------------|-------------------------------------|
| 函数签名 | 任意委托 | `Func<WorkflowContext, object>` |
| 上下文 | `ExecutionContext` | `WorkflowContext` |
| 设计目标 | 通用函数执行 | 工作流编排 |
| 参数传递 | 自动参数匹配 | 显式上下文传递 |

### 4.2 双向桥接

```
┌──────────────────────┐         ┌──────────────────────────┐
│  Core.IFunctionRegistry │ ◄─────► │  IWorkflowRegistry       │
│  (通用委托注册)        │  桥接   │  (工作流上下文注册)       │
│                      │         │                          │
│  PipelineExecutor    │         │  DefaultWorkflowRegistry │
│  FunctionExecutor    │         │  OverridableWorkflowReg  │
└──────────────────────┘         └──────────────────────────┘
         ▲                                    ▲
         │                                    │
         │  WorkflowFunctionUnitAdapter       │
         │  (IWorkflowRegistry → IFunctionUnit)│
         │                                    │
         │  BasicToWorkflowRegistryAdapter    │
         │  (IFunctionRegistry → IWorkflowRegistry)│
         └────────────────────────────────────┘
```

**WorkflowFunctionUnitAdapter**：将 `IWorkflowRegistry` 中的函数包装为 `IFunctionUnit`，使其可在 `PipelineExecutor` 的管线中执行。当管线执行到该步骤时，自动将 `ExecutionContext` 转换为 `WorkflowContext` 并调用工作流函数。

**BasicToWorkflowRegistryAdapter**：将 `IFunctionRegistry` 中的函数暴露为 `IWorkflowRegistry` 接口，使其可在工作流编排中调用。当工作流调用函数时，自动将 `WorkflowContext` 转换为 `ExecutionContext` 并执行 BasicMethod 函数。

---

## 5. 文件驱动机制

`WorkflowUseage` 支持从 JSON 文件加载管线定义并执行：

```json
{
  "name": "MyPipeline",
  "steps": [
    {
      "name": "Step1",
      "useExplicitParameters": true,
      "parameters": [1, 2]
    },
    {
      "name": "Step2",
      "useExplicitParameters": false
    }
  ],
  "flattenSingleResult": true
}
```

文件驱动流程：
1. `LoadPipelineFromFile` → 读取 JSON 文件
2. `LoadPipelineFromJson` → 反序列化为 `PipelineDefinition`
3. `ExecutePipelineAsync` → 执行管线

---

## 6. 与 FunctionStorage 的关系

`FunctionStorage` 项目（`FunctionStorage/`）提供函数存储能力：

| 文件 | 说明 |
|------|------|
| `MathLibrary.cs` | 数学函数库 |
| `ModelLibrary.cs` | 模型函数库 |
| `UIConnector.cs` | UI 连接器 |

这些函数库可以通过 `WorkflowUseage.RegisterDelegate()` 或 `WorkflowUseage.RegisterUnit()` 注册到 BasicMethod 注册表中，然后通过桥接机制在工作流中使用。

---

## 7. 与 FileDriven 的关系

`FileDriven` 项目（`FileDriven/`）提供文件驱动框架：

| 目录 | 说明 |
|------|------|
| `DrivenConfig/` | 驱动配置 |
| `Dialogue/` | 对话系统 |
| `src/` | 核心源码 |

`WorkflowUseage` 的文件驱动能力（`ExecuteFromFileAsync`）可以与 `FileDriven` 框架配合使用，从文件加载工作流定义并执行。

---

## 8. 函数执行整合分析

### 8.1 当前状态

工作流函数执行已经整合到 `BasicMethod/FunctionExecution` 框架中：

1. **WorkflowFunctions 子模块**：作为 `FunctionExecution` 的一部分，提供工作流专用的函数注册表
2. **Bridge 桥接层**：实现两个注册表之间的双向适配
3. **WorkflowUseage 前台模块**：统一暴露两个注册表的能力

### 8.2 整合优势

- **统一执行引擎**：`PipelineExecutor` 和 `FunctionExecutor` 同时支持两种注册表
- **减少代码重复**：工作流函数注册表复用 Core 的执行器、管线编排等基础设施
- **渐进式迁移**：旧代码可以通过 `global using` 别名继续使用
- **灵活扩展**：新的函数类型只需实现 `IFunctionUnit` 或注册到 `IWorkflowRegistry`

### 8.3 整合建议

1. **新代码应直接引用** `UniversalWorkflowFramework.FunctionExecution.WorkflowFunctions` 命名空间
2. **旧代码兼容**：`WorkFlowMethod.Functions` 目录保留 `global using` 别名映射
3. **桥接使用场景**：
   - 需要将工作流函数放入管线执行 → 使用 `WorkflowFunctionUnitAdapter`
   - 需要将通用函数在工作流中调用 → 使用 `BasicToWorkflowRegistryAdapter`
   - 需要统一管理 → 使用 `WorkflowBridgeManager`

---

## 9. 使用示例

### 9.1 完整工作流示例

```csharp
using UniversalWorkflowFramework.FunctionExecution;
using UniversalWorkflowFramework.FunctionExecution.WorkflowFunctions;
using UniversalWorkflowFramework.FunctionExecution.Bridge;

// 1. 初始化
WorkflowUseage.Initialize();

// 2. 注册工作流函数
WorkflowUseage.RegisterFunction("Add", ctx =>
{
    var a = ctx.GetParameter<int>("0");
    var b = ctx.GetParameter<int>("1");
    return a + b;
});

WorkflowUseage.RegisterAsyncFunction("FetchData", async ctx =>
{
    var url = ctx.GetParameter<string>("url");
    using var http = new HttpClient();
    return await http.GetStringAsync(url);
});

// 3. 注册 BasicMethod 函数
WorkflowUseage.RegisterDelegate("Multiply", new Func<int, int, int>((a, b) => a * b));

// 4. 桥接到管线
WorkflowUseage.RegisterAllWorkFlowFunctionsToPipeline();

// 5. 创建并执行管线
var pipeline = WorkflowUseage.CreatePipeline("CalcPipeline")
    .AddStep("Add", new Func<int, int, int>((a, b) => a + b))
    .AddStep("Multiply", new Func<int, int>((x) => x * 2))
    .Build();

var result = await WorkflowUseage.ExecutePipelineAsync(pipeline,
    new ExecutionContext { FunctionName = "CalcPipeline" });

// 6. 文件驱动
await WorkflowUseage.ExecuteFromFileAsync("workflow.json");
```

### 9.2 桥接使用示例

```csharp
// 将 BasicMethod 函数暴露为工作流函数
var basicRegistry = new FunctionRegistry();
basicRegistry.Register("Hello", new Func<string, string>(name => $"Hello, {name}!"));

var workflowAdapter = WorkflowBridgeManager.BridgeToWorkflow(basicRegistry);
var result = workflowAdapter.Call("Hello", new WorkflowContext(
    parameters: new Dictionary<string, object> { { "0", "World" } }
));
// result = "Hello, World!"

// 将工作流函数注册到 BasicMethod 管线
var workflowRegistry = new DefaultWorkflowRegistry();
workflowRegistry.Register("Greet", ctx => $"Hello, {ctx.GetParameter<string>("name")}!");

WorkflowBridgeManager.RegisterWorkflowFunctionToBasic(workflowRegistry, basicRegistry, "Greet");
var execResult = await basicRegistry.ExecuteAsync("Greet", new Dictionary<string, object> { { "name", "World" } });
```

---

## 10. 文件清单

### BasicMethod/FunctionExecution/

| 文件 | 行数 | 说明 |
|------|------|------|
| `ExecutionContext.cs` | 340 | 核心抽象层（接口、类型定义） |
| `FunctionRegistry.cs` | 616 | 函数注册表、管线构建器、执行器、静态框架入口 |
| `FunctionCall.cs` | 662 | 函数调用模型、参数处理、内置函数单元 |
| `Bridge/WorkflowBridge.cs` | 286 | 桥接适配器 |
| `WorkflowFunctions/IWorkflowRegistry.cs` | 29 | 工作流函数注册表接口 |
| `WorkflowFunctions/WorkflowContext.cs` | 126 | 工作流执行上下文 |
| `WorkflowFunctions/DefaultWorkflowRegistry.cs` | 89 | 默认工作流注册表 |
| `WorkflowFunctions/OverridableWorkflowRegistry.cs` | 88 | 可覆盖工作流注册表 |
| `WorkflowFunctions/WorkflowFunctionNotFoundException.cs` | 30 | 函数未找到异常 |
| `WorkflowFunctions/WorkflowRegistryExtensions.cs` | 103 | 扩展方法 |

### WorkFlowMethod/

| 文件 | 行数 | 说明 |
|------|------|------|
| `WorkflowUseage.cs` | 336 | 前台使用模块 |
| `ExecutionEngine/ExecutionEngine.cs` | 1303 | 引擎核心 |
| `ExecutionEngine/Integration.cs` | 1779 | 内置函数注册 |
| `ExecutionEngine/External.cs` | 179 | 外部函数集合 |
| `ExecutionEngine/WorkflowExecutor.cs` | 45 | 工作流执行器 |
| `ExecutionEngine/ExecutionRequest.cs` | 20 | 执行请求模型 |
| `Functions/*.cs` | - | 旧代码兼容层（global using 别名） |

---

## 11. 总结

`WorkflowUseage` 前台使用模块通过桥接机制统一了 `BasicMethod/FunctionExecution` 的两个函数注册体系，提供了：

1. **统一的函数注册和执行接口**
2. **管线化的工作流编排能力**
3. **文件驱动的工作流加载执行**
4. **向后兼容的旧代码支持**

工作流函数执行已经成功整合到 `BasicMethod/FunctionExecution` 框架中，通过 `WorkflowFunctions` 子模块和 `Bridge` 桥接层实现，避免了代码重复，提高了可维护性和扩展性。
