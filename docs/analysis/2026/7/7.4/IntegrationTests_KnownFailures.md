# Integration.Tests 已知测试失败分析

> 最后更新：2026-07-04
> 测试文件：`CentralTest\CSharpTest\Integration.Tests\FunctionStorageRegistryUsageTests.cs`
> 测试总数：35 | 通过：33 | 失败：2

---

## 失败 1：E1_ExecuteSingleStepPipeline_ThroughWorkFlowMethodUsage

### 错误信息

```
Assert.AreEqual 失败。应为: <15>，实际为: <0>。
```

### 测试代码（第 722-733 行）

```csharp
[TestMethod]
public async Task E1_ExecuteSingleStepPipeline_ThroughWorkFlowMethodUsage()
{
    var result = await WorkFlowMethodUsage.ExecuteStepAsync<Func<int, int, int>>(
        "AddStep", (a, b) => a + b, new object[] { 7, 8 });

    Assert.IsTrue(result.IsSuccess, $"执行失败: {result.ErrorMessage}");
    Assert.AreEqual(15, Convert.ToInt32(result.Value));
}
```

### 根因分析

问题出在 `ExecutionPipelineRunner.ExecuteStepAsync()`（第 40-58 行）：

```csharp
public async Task<BmExecutionResult> ExecuteStepAsync<TDelegate>(
    string stepName, TDelegate function, object? input = null)
    where TDelegate : Delegate
{
    // 如果 input 是 ExecutionContext，通过管线执行
    if (input is BmExecutionContext execCtx) { ... }

    // 否则，构建单步管线执行委托，将 input 作为参数传递
    var pipeline2 = new BmPipelineBuilder()
        .AddStep(stepName, function)
        .Build();
    return await _executor.ExecutePipelineAsync(pipeline2, new BmExecutionContext());
    //                                                      ↑
    // 问题：input 参数（new object[] { 7, 8 }）被丢弃了！
    // 创建了一个空的 BmExecutionContext，没有将 input 作为参数传入。
}
```

**根本原因**：`ExecuteStepAsync` 在非 `BmExecutionContext` 分支中，创建了空的 `BmExecutionContext`，没有将 `input` 参数传递给管线执行器。因此管线中的委托 `(a, b) => a + b` 收到的参数为默认值（0, 0），结果为 0。

### 修复方向

将 `input` 参数包装到 `BmExecutionContext` 中传递：

```csharp
var pipeline2 = new BmPipelineBuilder()
    .AddStep(stepName, function)
    .Build();
var ctx = new BmExecutionContext();
if (input != null)
{
    ctx.Items["input"] = input;  // 或根据管线执行器的参数约定传递
}
return await _executor.ExecutePipelineAsync(pipeline2, ctx);
```

> **注意**：需要确认 `BmPipelineExecutor` 如何从 `BmExecutionContext` 提取委托参数，以确定正确的传递方式。

---

## 失败 2：F2_CallBridgedFunction_ShouldRouteToStorageFunction

### 错误信息

```
System.Reflection.TargetParameterCountException: Parameter count mismatch.
```

### 测试代码（第 783-798 行）

```csharp
[TestMethod]
public void F2_CallBridgedFunction_ShouldRouteToStorageFunction()
{
    FunctionStorageRegistry.Register("BridgedAdd", (int a, int b) => a + b);

    var context = new WorkflowContext(
        parameters: new Dictionary<string, object> { { "a", 15 }, { "b", 25 } },
        executionId: "bridge_002");

    var result = WorkFlowMethodUsage.CallBridgedFunction("BridgedAdd", context);

    Assert.AreEqual(40, Convert.ToInt32(result));
}
```

### 根因分析

问题出在 `WorkFlowMethodUsage.CallBridgedFunction()`（第 292-318 行）：

```csharp
// 从 WorkflowContext.Parameters 提取参数值列表作为函数参数
var parameters = context.Parameters.Count > 0
    ? context.Parameters.Values.ToArray()    // ← 提取为 object[]
    : null;

// ... 然后调用
await _functionExecutor!.ExecuteAsync(name, parameters, execContext);
```

`context.Parameters` 是一个 `Dictionary<string, object>`，其 `Values` 属性返回 `Dictionary<string, object>.ValueCollection`。当通过 `.ToArray()` 转换为 `object[]` 时，每个元素是 `object` 类型的值（15 和 25）。

但 `_functionExecutor.ExecuteAsync` 内部将 `parameters` 作为 `object[]` 传递给委托的 `DynamicInvoke`。问题在于：

1. **参数顺序**：`Dictionary.Values` 的枚举顺序**不保证**与委托参数顺序一致。测试中 `{ "a", 15 }, { "b", 25 }` 的 Values 顺序可能是 `[15, 25]`，但依赖字典插入顺序是不安全的。

2. **更深层的问题**：`CallBridgedFunction` 将 `Dictionary<string, object>.ValueCollection` 直接作为 `object[]` 传递，但 `_functionExecutor.ExecuteAsync` 内部可能将其视为**单个参数**（一个 `ValueCollection` 对象），而不是两个独立的参数 `int a, int b`。

**根本原因**：`CallBridgedFunction` 的参数提取逻辑与 `FunctionExecutor` 的参数传递约定不匹配。`FunctionExecutor` 期望参数按位置传递（`object[]` 中的每个元素对应委托的一个参数），但 `CallBridgedFunction` 从字典中提取值列表，丢失了参数名到位置的映射关系。

### 修复方向

有两种修复思路：

**方案 A**：在 `CallBridgedFunction` 中，将 `parameters` 包装为单元素数组，让 `FunctionExecutor` 自行解包：

```csharp
var parameters = context.Parameters.Count > 0
    ? new object[] { context.Parameters.Values.ToArray() }
    : null;
```

**方案 B**：修改 `FunctionExecutor.ExecuteAsync` 的参数处理逻辑，使其能正确处理 `object[]` 中的多个参数（即展开数组）。

> **注意**：需要确认 `FunctionExecutor.ExecuteAsync` 内部对 `parameters` 参数的处理方式，以确定正确的修复方案。

---

## 影响范围

| 测试 | 影响 | 优先级 |
|------|------|--------|
| E1 | 管线快速执行功能（`ExecuteStepAsync`） | 中 |
| F2 | 统一桥接入口（`CallBridgedFunction`） | 低（异步版本 F3 正常） |

两个失败均不影响核心功能：
- ✅ 函数注册/执行
- ✅ 工作流执行（B 组测试全部通过）
- ✅ 共享注册表（H 组测试全部通过）
- ✅ 并发执行（D 组测试全部通过）
- ✅ 异步桥接（F3 通过）
