# MathFunctions 完全走工作流引擎方案

## 1. 现状分析

### 当前执行路径（直接函数分发）

```
MathFunctions.json → UIConfiguration
  → EventBinderTransformer（绑定事件）
    → 用户点击按钮 → CreateEventHandler 触发
      → BuildParametersFromBindings 从 Tag 读取参数绑定
      → AppServiceProvider.Resolve<IEventExecutionService>()
        → WorkflowMethodExecutionService
          → WorkFlowMethodUsage.Executor.ExecuteAsync()
            → FunctionExecutor.ExecuteAsync()
              → TrConfigRegistry.TryGetConfig("Add")    ← 字典查找
              → ParameterTransformer.FillFromDict()      ← 参数转换
              → DynamicInvoke(MathLibrary.Add, 5.0, 3.0) ← 直接反射调用
```

**本质**：`TrConfigRegistry` 就是一个 `Dictionary<string, ITrConfig>`，找到函数后直接用 `DynamicInvoke` 调用。没有工作流上下文、没有节点处理、没有弹性策略。

### 函数注册方式（裸委托）

```csharp
// MathLibrary.cs — 注册为裸 Func<double,double,double>
registry.Register("Add", (Func<double, double, double>)Add, "Addition: a + b");
```

```csharp
// BuiltInFunctionRegistrar.cs — 工作流函数注册方式（对比）
registry.Register("Plus", (Delegate)((FunctionContext ctx) => {
    var a = ctx.GetParameter<object>("0");  // 从 FunctionContext 取参数
    return BmArithmetics.Plus(new List<object> { a, b });
}));
```

---

## 2. 目标路径（完全走工作流引擎）

### 完整执行路径

```
MathFunctions.json → UIConfiguration
  → EventBinderTransformer（绑定事件）
    → 用户点击按钮 → CreateEventHandler 触发
      → BuildParametersFromBindings
      → AppServiceProvider.Resolve<IEventExecutionService>()
        → WorkflowMethodExecutionService
          → 步骤 A：构建 WorkflowContext（含参数、执行ID、节点Key）
          → 步骤 B：构建 NodeDefinition（节点类型、函数名、超时配置）
          → 步骤 C：NodeExecutor.ExecuteNodeAsync(node, context)
            ├─ 函数节点 → 通过 FunctionExecutor 执行
            ├─ 条件节点 → 条件判断后分支执行
            ├─ 循环节点 → 重复执行直到条件满足
            └─ 并行节点 → 多路并行执行
          → 步骤 D：结果写回输出控件
```

### 关键变化

| 维度 | 当前 | 目标 |
|------|------|------|
| 注册方式 | `Func<double,double,double>` 裸委托 | `FunctionContext` 工作流函数 |
| 执行入口 | `FunctionExecutor.DynamicInvoke` | `NodeExecutor.ExecuteNodeAsync` |
| 上下文 | 无 | `WorkflowContext` |
| 参数传递 | `Dictionary<string,object>` → 类型转换 | `FunctionContext.Parameters` + 自动类型推断 |
| 弹性策略 | 无 | Polly 重试 + 超时 |
| 节点类型 | 无 | Function / Condition / Loop / Parallel |
| 调用栈追踪 | 无 | CallFrame |
| 日志 | Debug.WriteLine | `IWorkflowLogger` |

---

## 3. 实施方案

### 方案 A（推荐）：改造 MathLibrary 注册为工作流函数

**改动量最小，让 MathLibrary 的函数注册方式对齐 BuiltInFunctionRegistrar 的工作流函数风格。**

#### 步骤 1：修改 MathLibrary.cs

将裸委托注册改为 `FunctionContext` 工作流函数注册：

```csharp
public static void RegisterAll(IFunctionRegistry registry)
{
    // 当前：
    registry.Register("Add", (Func<double, double, double>)Add, "Addition: a + b");

    // 改为工作流函数风格（接收 FunctionContext）：
    registry.Register("Add", (Delegate)((FunctionContext ctx) =>
    {
        // 从 FunctionContext.Parameters (Dictionary<string,object>) 取值
        var a = Convert.ToDouble(ctx.GetParameter<object>("a") ?? "0");
        var b = Convert.ToDouble(ctx.GetParameter<object>("b") ?? "0");
        var result = Add(a, b);
        return result;
    }), "Addition: a + b");

    // 同理改造 Subtract, Multiply, Divide...
}
```

**注册表的兼容性**：`IFunctionRegistry` 内部存储的是 `Delegate` 类型，两种注册方式都存储为 `Delegate`。区别在于：
- 裸委托：`Func<double,double,double>` → 参数已转换好，直接调用
- 工作流函数：`Func<FunctionContext, object>` → 从上下文取参数，更灵活

#### 步骤 2：在 EventBinderTransformer 中判断是否需要走 NodeExecutor

在 `CreateEventHandler` 中增加一条分支路径：

```csharp
// 在 WorkflowMethodExecutionService.ExecuteFunctionAsync 中
public async Task<ExecutionResult> ExecuteFunctionAsync(string functionName, Dictionary<string, object> namedParameters)
{
    // 1. 构建 WorkflowContext
    var wfContext = BuildWorkflowContext(functionName, namedParameters);

    // 2. 构建 NodeDefinition
    var node = BuildNodeDefinition(functionName, wfContext);

    // 3. 通过 NodeExecutor 执行
    var nodeExecutor = new NodeExecutor(WorkFlowMethodUsage.Executor);
    var result = await nodeExecutor.ExecuteNodeAsync(node, wfContext);

    // 4. 转换结果
    return result.IsSuccess
        ? ExecutionResult.Ok(result.Value)
        : ExecutionResult.Fail(result.ErrorMessage);
}

private static WorkflowContext BuildWorkflowContext(string functionName, Dictionary<string, object> parameters)
{
    return new WorkflowContext(
        parameters: parameters,
        data: new Dictionary<string, object> { ["FunctionName"] = functionName },
        executionId: Guid.NewGuid().ToString("N"),
        nodeKey: functionName,
        workflowId: "MathFunctionWorkflow"
    );
}

private static NodeDefinition BuildNodeDefinition(string functionName, IWorkflowContext context)
{
    return new NodeDefinition
    {
        Id = functionName,
        Name = functionName,
        NodeType = "Function",
        IsEnabled = true,
        TimeoutMs = 30000,  // 30秒超时
        // 函数名和参数放在 NodeDefinition 中供 NodeExecutor 调用
    };
}
```

#### 步骤 3：修改 WorkflowMethodExecutionService

当前 `WorkflowMethodExecutionService` 直接调用 `WorkFlowMethodUsage.Executor.ExecuteAsync()`，需要改为：

```csharp
// 当前路径：直接函数分发
var result = await WorkFlowMethodUsage.Executor.ExecuteAsync(functionName, namedParameters, execContext);

// 改为：通过 NodeExecutor 走工作流引擎
// （保留 FunctionContext 参数传递，但经过 NodeExecutor 做节点处理）
var nodeExecutor = new NodeExecutor(WorkFlowMethodUsage.Executor);
var wfContext = ConvertToWorkflowContext(functionName, namedParameters);
var nodeDef = new NodeDefinition { ... };
var result = await nodeExecutor.ExecuteNodeAsync(nodeDef, wfContext);
```

---

### 方案 B：定义 .workflow 文件 + WorkflowBuilder

**通过工作流定义文件驱动，更贴近 WorkFlowMethod 的设计意图。**

#### 步骤 1：创建 MathAdd.workflow 文件

```json
{
  "id": "math-add",
  "nodes": [
    {
      "symbol": "function",
      "nodeId": "add",
      "funcName": "MathAdd",
      "parameterKeys": ["a", "b"],
      "resultKeys": ["result"]
    }
  ]
}
```

#### 步骤 2：修改 WorkflowMethodExecutionService

```csharp
// 从 .workflow 文件加载工作流定义
var workflowDef = WorkflowBuilder.LoadFromFile("MathAdd.workflow");
var nodes = WorkflowBuilder.ParseFromJson(workflowDef);

// 通过 WorkFlowMethod 管线执行
var result = await WorkFlowMethodUsage.CreateRefactoredExecutor()
    .ExecuteWorkflowAsync("math-add");
```

---

### 方案 C：使用 PipelineExecutor 多步骤管线

**适合需要多步骤编排的场景。**

```csharp
var pipeline = WorkFlowMethodUsage.CreatePipeline("MathPipeline")
    .AddStep("readA", step => step.Function = ...)
    .AddStep("readB", step => step.Function = ...)
    .AddStep("executeAdd", step => step.Function = MathLibrary.Add)
    .AddStep("writeResult", step => step.Transformer = ...)
    .Build();

var result = await WorkFlowMethodUsage.ExecutePipelineAsync(pipeline);
```

---

## 4. 推荐方案：方案 A

### 理由

| 因素 | 方案 A | 方案 B | 方案 C |
|------|--------|--------|--------|
| 改动量 | 小 | 中 | 大 |
| 对现有代码影响 | 低 | 中 | 高 |
| 工作流引擎特性覆盖 | FunctionNode | 全部节点类型 | Pipeline 编排 |
| 与 EventBinder 集成 | 直接 | 间接 | 间接 |
| 可回滚性 | 高 | 高 | 低 |

方案 A 在**最小改动**的前提下让 MathFunctions 走通工作流引擎的核心路径（FunctionContext + NodeExecutor），后续可根据需求逐步扩展 Condition/Loop/Parallel 节点支持。

### 具体实施步骤

```
1. 改造 MathLibrary.cs
   └── 注册方式改为 FunctionContext 工作流函数
2. 改造 WorkflowMethodExecutionService.cs
   ├── 新增 BuildWorkflowContext() 方法
   ├── 新增 BuildNodeDefinition() 方法
   └── Executor 改为 NodeExecutor 包装
3. 验证
   ├── 运行 FileDriven GUI
   ├── 点击 Add 按钮
   └── 验证结果正确 + 日志输出工作流上下文
```

### 验证方法

在 `WorkflowMethodExecutionService` 中添加唯一标记日志，确认经过工作流引擎路径：

```csharp
// 添加在工作流引擎路径入口
Debug.WriteLine($"[WORKFLOW_ENGINE] Executing '{functionName}' via NodeExecutor");
Debug.WriteLine($"[WORKFLOW_ENGINE] WorkflowContext: Id={wfContext.ExecutionId}, NodeKey={wfContext.NodeKey}");
Debug.WriteLine($"[WORKFLOW_ENGINE] NodeDefinition: Type={nodeDef.NodeType}, Timeout={nodeDef.TimeoutMs}ms");
```

预期输出示例：
```
[WORKFLOW_ENGINE] Executing 'Add' via NodeExecutor
[WORKFLOW_ENGINE] WorkflowContext: Id=a1b2c3d4e5f6, NodeKey=Add
[WORKFLOW_ENGINE] NodeDefinition: Type=Function, Timeout=30000ms
```