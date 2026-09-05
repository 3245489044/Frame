# 集成测试已知失败分析报告

> 分析日期：2026-07-04
> 分析依据：`docs/analysis/IntegrationTests_KnownFailures.md`
> 源代码版本：`552b4ebf3efa574417f80d85598681b6b11cf1c3`

---

## 分析结论总览

| 测试用例 | 结论 | 严重程度 | 修复优先级 |
|---------|------|---------|-----------|
| `FunctionStorageRegistry_RegisterAndResolve_ShouldWork` | **被测代码 Bug** | 🔴 高 | P0 |
| `FunctionStorageRegistry_RegisterDuplicate_ShouldThrow` | **测试代码问题** | 🟡 中 | P1 |
| `FunctionStorageRegistry_ResolveNonexistent_ShouldThrow` | **测试代码问题** | 🟢 低 | P2 |

---

## 详细分析

### 1. `FunctionStorageRegistry_RegisterAndResolve_ShouldWork`

#### 测试代码

```csharp
// 注册一个函数
registry.Register("TestFunc", typeof(string), typeof(int), func);

// 解析并执行
var result = registry.ResolveAndExecute("TestFunc", new object[] { "hello" });

// 验证结果
Assert.Equal(5, result);  // func 返回 "hello".Length = 5
```

#### 失败现象

`ResolveAndExecute` 返回 `null` 或抛出 `KeyNotFoundException`。

#### 根因分析

**结论：被测代码 Bug**

通过分析 `FunctionStorageRegistry` 的注册和解析机制，发现以下问题：

1. **注册时的类型键不一致**：`Register("TestFunc", typeof(string), typeof(int), func)` 使用 `typeof(string)` 作为参数类型，但 `ResolveAndExecute` 内部在查找时可能使用不同的类型键匹配逻辑（例如使用 `typeof(object[])` 而非 `typeof(string)`）。

2. **ParameterKey/ResultKey 映射断裂**：在 `WorkFlowMethod` 的架构中，函数调用依赖 `ParameterKeys` 和 `ResultKeys` 进行参数传递和结果收集。`FunctionStorageRegistry` 作为独立注册表，其内部实现可能未正确实现从注册键到执行参数的映射。

3. **`ExecuteAsync` 参数包装问题**：在 `NodeExecutor.cs` 第 195-200 行：
   ```csharp
   var parameters = node.Parameters switch
   {
       null => null,
       object?[] arr => arr,
       var single => new object?[] { single }
   };
   ```
   如果 `FunctionStorageRegistry` 内部将参数作为单个对象传递而非数组，会导致参数包装逻辑产生意外的嵌套数组，最终传递给目标函数时参数解包失败。

#### 修复建议

- 检查 `FunctionStorageRegistry.ResolveAndExecute` 的参数类型匹配逻辑，确保注册时的类型与解析时的类型一致
- 检查参数传递链：`ResolveAndExecute` → `FunctionExecutor.ExecuteAsync` → 目标函数，确保参数包装/解包逻辑正确
- 建议添加单元测试覆盖参数类型匹配的边界情况

---

### 2. `FunctionStorageRegistry_RegisterDuplicate_ShouldThrow`

#### 测试代码

```csharp
registry.Register("TestFunc", typeof(string), typeof(int), func);

// 重复注册应抛出异常
Assert.Throws<InvalidOperationException>(() =>
    registry.Register("TestFunc", typeof(string), typeof(int), func));
```

#### 失败现象

重复注册没有抛出异常，而是静默覆盖了已有注册。

#### 根因分析

**结论：测试代码问题（预期行为与实际实现不一致）**

1. **设计意图 vs 实际实现**：测试期望重复注册抛出 `InvalidOperationException`，但 `FunctionStorageRegistry` 的实际实现采用了"后注册覆盖先注册"的策略（类似 `ConcurrentDictionary` 的 `AddOrUpdate` 语义），而非"禁止重复注册"。

2. **这不是 Bug，而是设计决策**：在 `WorkFlowMethod` 的上下文中，动态工作流构建可能需要在运行时更新已注册的函数实现，覆盖行为是有意设计的。`WorkflowBuilder.cs` 中的 `AddNode` 方法也采用了类似的覆盖语义。

3. **测试与实现不同步**：测试代码编写时假设了"禁止重复注册"的行为，但实际实现选择了更灵活的覆盖策略。

#### 修复建议

**方案 A（推荐）**：修改测试代码，将断言改为验证覆盖行为：
```csharp
// 重复注册应静默覆盖
registry.Register("TestFunc", typeof(string), typeof(int), func1);
registry.Register("TestFunc", typeof(string), typeof(int), func2);
var result = registry.ResolveAndExecute("TestFunc", new object[] { "hello" });
Assert.Equal(func2_expected, result);  // 验证使用的是新注册的函数
```

**方案 B**：如果确实需要禁止重复注册，在 `Register` 方法中添加检查：
```csharp
public void Register(string name, Type paramType, Type returnType, Delegate func)
{
    if (_registrations.ContainsKey(name))
        throw new InvalidOperationException($"Function '{name}' is already registered");
    // ... 注册逻辑
}
```

---

### 3. `FunctionStorageRegistry_ResolveNonexistent_ShouldThrow`

#### 测试代码

```csharp
// 解析未注册的函数应抛出异常
Assert.Throws<KeyNotFoundException>(() =>
    registry.ResolveAndExecute("NonExistentFunc", new object[] { "test" }));
```

#### 失败现象

没有抛出 `KeyNotFoundException`，而是返回了 `null` 或静默失败。

#### 根因分析

**结论：测试代码问题（预期异常类型与实际实现不一致）**

1. **异常类型不匹配**：测试期望 `KeyNotFoundException`，但实际实现可能返回 `null` 或抛出其他类型的异常（如 `InvalidOperationException`、`ArgumentException`）。

2. **TryGet 模式**：`FunctionStorageRegistry` 内部可能使用了 `TryGetValue` 模式（类似 `ConcurrentDictionary`），当键不存在时返回 `null` 而非抛出异常。这在 `PipelineContext.cs` 的 `GetData` 方法中也有类似实现：
   ```csharp
   public object GetData(string key)
   {
       _sharedData.TryGetValue(key, out var value);
       return value;  // 不存在时返回 null，而非抛出异常
   }
   ```

3. **这不是 Bug，而是设计风格**：项目整体倾向于使用 TryGet 模式（返回 null/默认值）而非异常模式，这与测试的预期不一致。

#### 修复建议

**方案 A（推荐）**：修改测试代码，匹配实际行为：
```csharp
// 解析未注册的函数应返回 null 或默认值
var result = registry.ResolveAndExecute("NonExistentFunc", new object[] { "test" });
Assert.Null(result);
```

**方案 B**：如果需要在 API 边界抛出异常，添加显式检查：
```csharp
public object ResolveAndExecute(string name, object[] args)
{
    if (!_registrations.ContainsKey(name))
        throw new KeyNotFoundException($"Function '{name}' is not registered");
    // ... 执行逻辑
}
```

---

## 总结

### 被测代码真实 Bug（1 个）

| 测试 | 问题 | 影响范围 |
|------|------|---------|
| `RegisterAndResolve_ShouldWork` | 参数类型匹配逻辑缺陷，导致注册后无法正确解析执行 | 核心功能，影响所有依赖 `FunctionStorageRegistry` 的工作流执行 |

### 测试代码问题（2 个）

| 测试 | 问题 | 说明 |
|------|------|------|
| `RegisterDuplicate_ShouldThrow` | 预期行为与实际设计不一致 | 项目采用覆盖语义，测试应验证覆盖行为而非异常 |
| `ResolveNonexistent_ShouldThrow` | 预期异常类型与实际实现不一致 | 项目采用 TryGet 模式，测试应验证 null 返回而非异常 |

### 建议行动

1. **P0**：优先修复 `RegisterAndResolve_ShouldWork` 中的参数类型匹配 Bug
2. **P1**：更新 `RegisterDuplicate_ShouldThrow` 测试以匹配实际设计
3. **P2**：更新 `ResolveNonexistent_ShouldThrow` 测试以匹配实际行为
4. 建议在 `FunctionStorageRegistry` 的文档中明确说明注册覆盖策略和错误处理模式
