# FunctionExecution → Modules 迁移方案

> **核心原则**：`BasicMethod.Transformers` 是底层数学模型（`f: A → B`），保持不变。  
> **迁移路径**：将 `BasicMethod/FunctionExecution` 拆分下迁到 `Modules/Registry` 和 `Modules/ExecutionFramework`。  
> **数学基础**：一切皆是 `ITransformer` 的变体。

---

## 一、现状分析

### 1.1 当前依赖关系

```
ExecutionFramework ──→ BasicMethod.FunctionExecution.Core
       │                        │
       │                        ├── ExecutionContext.cs（Core 数据模型 + 接口）
       │                        ├── FunctionRegistry.cs（注册表实现）
       │                        ├── FunctionCall.cs（执行器 + 管线）
       │                        └── WorkflowFunctions/（装饰层）
       │
CommandFramework ──→ Modules.Registry（基类）
```

### 1.2 Transformers 的数学定位（不变层）

`BasicMethod.Transformers` 提供最基础的数学抽象：

| 抽象 | 数学模型 | 代码 |
|------|---------|------|
| `ITransformer<A, B>` | `f: A → B` | 核心接口 |
| `IAsyncTransformer<A, B>` | `f: A → Task<B>` | 异步版本 |
| `Then()` | `g ∘ f`（函数复合） | 扩展方法 |
| `Pipeline` | `f₁ ∘ f₂ ∘ ... ∘ fₙ` | 多步复合 |
| `Decorator` | 横切关注点 | 装饰器模式 |
| `GroupReducer` | 等价类划分 | 分组归约 |
| `TransformContext` | 隐式上下文传播 | AsyncLocal |

**Transformers 保持不变**，但需要扩展以下能力以支撑迁移：

1. **`IAsyncTransformer` 的 `Then()` 复合** — 当前只有同步版本的 `Then()`
2. **`ITransformer<object[], Task<object>>` 作为通用函数签名** — 对应 `Delegate` 的数学抽象

---

## 二、迁移目标架构

```
BasicMethod/                           Modules/
  Transformers/          ──不变──>      Registry/          ExecutionFramework/
    ITransformer<A,B>      (数学基础)      RegistryBase<TKey>     ExecutionEngine
    Pipeline                              MapRegistryBase         StepExecutor
    Decorator                              FunctionRegistry       SessionManager
    TransformContext                       FunctionDescriptor     ExecutionCenter
    GroupReducer                           ParameterDescriptor    ...
    Collections                            ExecutionContext
    ...                                    ExecutionResult
                                           IFunctionUnit
                                           IFunctionExecutor
                                           IPipelineExecutor
                                           PipelineDefinition
                                           PipelineStep
```

### 2.1 迁移后依赖关系

```
ExecutionFramework ──→ Modules.Registry（Core 模型）
                    ──→ BasicMethod.Transformers（数学基础，间接）

CommandFramework  ──→ Modules.Registry（基类 + 函数描述符）

BasicMethod        ──→ Modules.Registry（Core 模型，原 FunctionExecution.Core 迁出）
  WorkflowFunctions/   （保留装饰层：DefaultFunctionRegistry、OverridableFunctionRegistry）
```

---

## 三、详细迁移步骤

### 步骤 1：扩展 Transformers（BasicMethod 层）✅ 已完成

在 `BasicMethod.Transformers` 中补充缺失的数学抽象：

#### 1.1 异步函数复合

```csharp
// 新增：IAsyncTransformer 的 Then() 复合
public static IAsyncTransformer<TInput, TOutput> Then<TInput, TMiddle, TOutput>(
    this IAsyncTransformer<TInput, TMiddle> first,
    IAsyncTransformer<TMiddle, TOutput> second)
    => new ComposedAsyncTransformer<TInput, TMiddle, TOutput>(first, second);
```

#### 1.2 委托到 Transformer 的适配器

```csharp
// 新增：将任意 Delegate 适配为 ITransformer
public class DelegateTransformer : ITransformer<object[], object>
{
    private readonly Delegate _delegate;
    public string Name { get; }
    public object Transform(object[] input) => _delegate.DynamicInvoke(input);
}
```

#### 1.3 异步委托适配器

```csharp
// 新增：将异步 Delegate 适配为 IAsyncTransformer
public class AsyncDelegateTransformer : IAsyncTransformer<object[], object>
{
    private readonly Delegate _delegate;
    public string Name { get; }
    public async Task<object> TransformAsync(object[] input, CancellationToken ct)
    {
        var result = _delegate.DynamicInvoke(input);
        if (result is Task task)
        {
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }
        return result;
    }
}
```

**新增文件**：
- `BasicMethod/Transformers/AsyncComposition.cs` — `IAsyncTransformer` 的 `Then()` 复合
- `BasicMethod/Transformers/DelegateAdapter.cs` — `DelegateTransformer`/`AsyncDelegateTransformer`

**验证**：`BasicMethod.Transformers` 项目编译通过 ✅

---

### 步骤 2：Core 数据模型 → Modules/Registry ✅ 已完成

将 `BasicMethod/FunctionExecution/ExecutionContext.cs` 中的 Core 类型迁移到 `Modules/Registry`。

#### 2.1 迁移清单

| 类型 | 源文件 | 目标文件 | 状态 |
|------|--------|---------|------|
| `ExecutionStatus` 枚举 | `ExecutionContext.cs` | `Modules/Registry/11_ExecutionModels.cs` | ✅ |
| `ParameterDescriptor` | `ExecutionContext.cs` | **合并**到 `05_FunctionDescriptors.cs` | ✅ |
| `FunctionDescriptor` | `ExecutionContext.cs` | **合并**到 `05_FunctionDescriptors.cs` | ✅ |
| `ExecutionResult` | `ExecutionContext.cs` | `Modules/Registry/11_ExecutionModels.cs` | ✅ |
| `ExecutionContext` | `ExecutionContext.cs` | `Modules/Registry/11_ExecutionModels.cs` | ✅ |
| `PipelineDefinition` | `ExecutionContext.cs` | `Modules/Registry/12_PipelineModels.cs` | ✅ |
| `PipelineStep` | `ExecutionContext.cs` | `Modules/Registry/12_PipelineModels.cs` | ✅ |
| `IFunctionCall` | `ExecutionContext.cs` | `Modules/Registry/13_ExecutionInterfaces.cs` | ✅ |
| `IParameterProcessor` | `ExecutionContext.cs` | `Modules/Registry/13_ExecutionInterfaces.cs` | ✅ |
| `IFunctionUnit` | `ExecutionContext.cs` | `Modules/Registry/13_ExecutionInterfaces.cs` | ✅ |
| `IFunctionRegistry` | `ExecutionContext.cs` | **合并**到 `05_FunctionDescriptors.cs` | ✅ |
| `IFunctionExecutor` | `ExecutionContext.cs` | `Modules/Registry/13_ExecutionInterfaces.cs` | ✅ |
| `IPipelineExecutor` | `ExecutionContext.cs` | `Modules/Registry/13_ExecutionInterfaces.cs` | ✅ |
| `IPipelineBuilder` | `ExecutionContext.cs` | `Modules/Registry/13_ExecutionInterfaces.cs` | ✅ |

#### 2.2 IFunctionRegistry 接口合并

`BasicMethod` 的 `IFunctionRegistry` 比 `Modules.Registry.IFunctionRegistry` 多出：

```csharp
// BasicMethod 版本有：
void Register(IFunctionUnit unit);
bool TryGetUnit(string name, out IFunctionUnit unit);

// Modules.Registry 版本有：
bool TryGetDelegate(string name, out Delegate? function);
```

**合并后**的 `Modules.Registry.IFunctionRegistry`：

```csharp
public interface IFunctionRegistry
{
    // 来自 Modules.Registry
    void Register(string name, Delegate function, string? description = null);
    void Unregister(string name);
    bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor);
    IEnumerable<string> GetRegisteredNames();
    bool Contains(string name);
    void Clear();
    bool TryGetDelegate(string name, out Delegate? function);

    // 来自 BasicMethod
    void Register(IFunctionUnit unit);
    bool TryGetUnit(string name, out IFunctionUnit unit);
}
```

#### 2.3 FunctionDescriptor 合并

`BasicMethod.FunctionDescriptor` 比 `Modules.Registry.FunctionDescriptor` 多出：

```csharp
// BasicMethod 版本有：
public Delegate Delegate { get; set; }
public Func<object[], Task<object>> CompiledExecutor { get; set; }
public Dictionary<string, object> Metadata { get; set; }
```

**合并后**的 `Modules.Registry.FunctionDescriptor` 添加这些属性。

**新增文件**：
- `Modules/Registry/11_ExecutionModels.cs` — `ExecutionStatus`、`ExecutionResult`、`ExecutionContext`
- `Modules/Registry/12_PipelineModels.cs` — `PipelineDefinition`、`PipelineStep`
- `Modules/Registry/13_ExecutionInterfaces.cs` — 所有执行接口
- `Modules/Registry/14_RegistryExtensions.cs` — `FunctionRegistryHelper` 扩展方法

**修改文件**：
- `Modules/Registry/05_FunctionDescriptors.cs` — 合并 `FunctionDescriptor`（添加 `Delegate`/`CompiledExecutor`/`Metadata`）
- `Modules/Registry/05_FunctionDescriptors.cs` — 扩展 `IFunctionRegistry` 接口
- `Modules/Registry/06_ConcreteRegistries.cs` — 扩展 `FunctionRegistry`（添加 `_units` 字典）

**验证**：`Modules.Registry` 项目编译通过 ✅

---

### 步骤 3：注册表实现 → Modules/Registry ✅ 已完成

将 `BasicMethod/FunctionExecution/FunctionRegistry.cs` 中的注册表实现迁移到 `Modules/Registry`。

#### 3.1 迁移清单

| 类型 | 源文件 | 目标文件 | 状态 |
|------|--------|---------|------|
| `FunctionRegistry` | `FunctionRegistry.cs` | **合并**到 `06_ConcreteRegistries.cs` | ✅ |
| `FunctionRegistryHelper` | `FunctionRegistry.cs` | `Modules/Registry/14_RegistryExtensions.cs` | ✅ |
| `FunctionNotFoundException` | `FunctionNotFoundException.cs` | `Modules/Registry/11_ExecutionModels.cs` | ✅ |

#### 3.2 FunctionRegistry 合并

`BasicMethod.FunctionRegistry` 使用双字典存储（`_descriptors` + `_units`），而 `Modules.Registry.FunctionRegistry` 基于 `MetaMapRegistryBase<string, Delegate, FunctionDescriptor>`。

**合并策略**：扩展 `Modules.Registry.FunctionRegistry`，添加 `IFunctionUnit` 注册能力：

```csharp
public class FunctionRegistry : MetaMapRegistryBase<string, Delegate, FunctionDescriptor>,
    IFunctionRegistry
{
    // 新增：函数单元存储
    private readonly ConcurrentDictionary<string, IFunctionUnit> _units = new();

    public void Register(IFunctionUnit unit)
    {
        _units[unit.Name] = unit;
    }

    public bool TryGetUnit(string name, out IFunctionUnit unit)
        => _units.TryGetValue(name, out unit);

    // 重写 Unregister 和 Clear 以同步清理 _units
    public override void Unregister(string key)
    {
        base.Unregister(key);
        _units.TryRemove(key, out _);
    }

    public override void Clear()
    {
        base.Clear();
        _units.Clear();
    }
}
```

**验证**：`Modules.Registry` 项目编译通过 ✅

---

### 步骤 4：执行引擎 → Modules/ExecutionFramework ✅ 已完成

将 `FunctionExecutor`、`PipelineExecutor`、`PipelineBuilder`、`FunctionCall`、`DefaultParameterProcessor` 迁移到 `Modules/ExecutionFramework`。

#### 4.1 迁移清单

| 类型 | 源文件 | 目标文件 | 状态 |
|------|--------|---------|------|
| `FunctionExecutor` | `FunctionCall.cs` | `ExecutionFramework/Engine/FunctionExecutor.cs` | ✅ |
| `PipelineExecutor` | `FunctionRegistry.cs` | `ExecutionFramework/Engine/PipelineExecutor.cs` | ✅ |
| `PipelineBuilder` | `FunctionRegistry.cs` | `ExecutionFramework/Engine/PipelineBuilder.cs` | ✅ |
| `FunctionCall` | `FunctionCall.cs` | `ExecutionFramework/Models/FunctionCall.cs` | ✅ |
| `DefaultParameterProcessor` | `FunctionCall.cs` | `ExecutionFramework/Engine/ParameterProcessor.cs` | ✅ |
| `FunctionUnitBase` 系列 | `FunctionCall.cs` | `ExecutionFramework/Abstractions/FunctionUnitBase.cs` | ✅ |
| `BuiltInUnits` | `FunctionCall.cs` | `ExecutionFramework/BuiltIn/BuiltInUnits.cs` | ✅ |

#### 4.2 依赖关系变化

迁移后 `StepExecutor.cs` 不再直接依赖 `BasicMethod`：

```csharp
// 迁移前
using BasicExecutionResult = UniversalWorkflowFramework.FunctionExecution.Core.ExecutionResult;
using BmFunctionExecutor = UniversalWorkflowFramework.FunctionExecution.Core.IFunctionExecutor;

// 迁移后
using Modules.Registry;
```

**新增文件**：
- `ExecutionFramework/Engine/FunctionExecutor.cs` — 函数执行器
- `ExecutionFramework/Engine/PipelineExecutor.cs` — 管线执行器
- `ExecutionFramework/Engine/PipelineBuilder.cs` — 管线构建器
- `ExecutionFramework/Engine/ParameterProcessor.cs` — 参数处理器
- `ExecutionFramework/Abstractions/FunctionUnitBase.cs` — 函数单元抽象基类
- `ExecutionFramework/Models/FunctionCall.cs` — 函数调用模型
- `ExecutionFramework/BuiltIn/BuiltInUnits.cs` — 内置函数单元

**修改文件**：
- `ExecutionFramework/ExecutionFramework.csproj` — 添加 `Registry` 项目引用

**验证**：`Modules.ExecutionFramework` 项目编译通过（0 警告 0 错误）✅

---

### 步骤 5：保留层（BasicMethod 中的装饰封装）⏳ 待处理

以下类型保留在 `BasicMethod/FunctionExecution/WorkflowFunctions/` 中，作为对 `Modules.Registry` 的装饰封装：

| 类型 | 说明 |
|------|------|
| `IWorkflowRegistry` | 工作流函数注册表接口 |
| `DefaultFunctionRegistry` | 默认实现，包装 `Modules.Registry.FunctionRegistry` |
| `OverridableFunctionRegistry` | 可覆盖的装饰器 |
| `FunctionContext` | 工作流函数上下文（继承 `Modules.Registry.ExecutionContext`） |
| `FunctionNotFoundException` | 迁移到 `Modules.Registry` 后，保留 using 别名 |

这些类型在迁移后修改继承关系：

```csharp
// 迁移前
public class DefaultFunctionRegistry : IWorkflowRegistry
{
    protected readonly FunctionRegistry _innerRegistry;  // BasicMethod.FunctionRegistry
}

// 迁移后
public class DefaultFunctionRegistry : IWorkflowRegistry
{
    protected readonly Modules.Registry.FunctionRegistry _innerRegistry;  // 指向 Modules
}
```

---

## 四、文件变更清单

### 4.1 新增文件（Modules/Registry）✅ 已完成

| 文件 | 内容 | 状态 |
|------|------|------|
| `11_ExecutionModels.cs` | `ExecutionStatus`、`ExecutionResult`、`ExecutionContext` | ✅ |
| `12_PipelineModels.cs` | `PipelineDefinition`、`PipelineStep` | ✅ |
| `13_ExecutionInterfaces.cs` | `IFunctionUnit`、`IFunctionCall`、`IParameterProcessor`、`IFunctionExecutor`、`IPipelineExecutor`、`IPipelineBuilder` | ✅ |
| `14_RegistryExtensions.cs` | `FunctionRegistryHelper` 扩展方法 | ✅ |

### 4.2 修改文件（Modules/Registry）✅ 已完成

| 文件 | 变更 | 状态 |
|------|------|------|
| `05_FunctionDescriptors.cs` | 合并 `BasicMethod` 的 `FunctionDescriptor`（添加 `Delegate`/`CompiledExecutor`/`Metadata`） | ✅ |
| `05_FunctionDescriptors.cs` | 扩展 `IFunctionRegistry` 接口（添加 `Register(IFunctionUnit)`/`TryGetUnit`） | ✅ |
| `06_ConcreteRegistries.cs` | 扩展 `FunctionRegistry`（添加 `_units` 字典和 `IFunctionUnit` 注册） | ✅ |

### 4.3 新增文件（Modules/ExecutionFramework）✅ 已完成

| 文件 | 内容 | 状态 |
|------|------|------|
| `Engine/FunctionExecutor.cs` | 从 `FunctionCall.cs` 迁移 | ✅ |
| `Engine/PipelineExecutor.cs` | 从 `FunctionRegistry.cs` 迁移 | ✅ |
| `Engine/PipelineBuilder.cs` | 从 `FunctionRegistry.cs` 迁移 | ✅ |
| `Engine/ParameterProcessor.cs` | `DefaultParameterProcessor` 从 `FunctionCall.cs` 迁移 | ✅ |
| `Abstractions/FunctionUnitBase.cs` | `FunctionUnitBase`/`AsyncFunctionUnitBase`/`DelegateFunctionUnit` 等 | ✅ |
| `Models/FunctionCall.cs` | `FunctionCall` 模型 | ✅ |
| `BuiltIn/BuiltInUnits.cs` | `BuiltInUnits.RegisterAll` 等 | ✅ |

### 4.4 修改文件（BasicMethod/FunctionExecution）⏳ 待处理

| 文件 | 变更 | 状态 |
|------|------|------|
| `ExecutionContext.cs` | 删除 Core 类型（迁移到 Modules/Registry），保留 using 别名兼容 | ⏳ |
| `FunctionRegistry.cs` | 删除注册表实现（迁移到 Modules/Registry），保留扩展方法 | ⏳ |
| `FunctionCall.cs` | 删除执行器/管线/函数单元（迁移到 Modules/ExecutionFramework） | ⏳ |
| `FunctionNotFoundException.cs` | 迁移到 Modules/Registry，保留 using 别名 | ⏳ |
| `DefaultFunctionRegistry.cs` | 修改 `_innerRegistry` 类型指向 `Modules.Registry.FunctionRegistry` | ⏳ |
| `OverridableFunctionRegistry.cs` | 同上 | ⏳ |

### 4.5 新增文件（BasicMethod/Transformers）✅ 已完成

| 文件 | 内容 | 状态 |
|------|------|------|
| `AsyncComposition.cs` | `IAsyncTransformer` 的 `Then()` 复合 | ✅ |
| `DelegateAdapter.cs` | `DelegateTransformer`/`AsyncDelegateTransformer` | ✅ |

---

## 五、迁移顺序与当前进度

```
Phase 1: 扩展 Transformers ✅
  └── 新增 AsyncComposition.cs + DelegateAdapter.cs
  └── 验证：BasicMethod.Transformers 项目编译通过 ✅

Phase 2: Core 模型 → Modules/Registry ✅
  └── 新增 11_ExecutionModels.cs ~ 14_RegistryExtensions.cs
  └── 修改 05_FunctionDescriptors.cs + 06_ConcreteRegistries.cs
  └── 验证：Modules.Registry 项目编译通过 ✅

Phase 3: 执行引擎 → Modules/ExecutionFramework ✅
  └── 新增 FunctionExecutor.cs / PipelineExecutor.cs / PipelineBuilder.cs 等 7 个文件
  └── 修改 ExecutionFramework.csproj（添加 Registry 引用）
  └── 验证：Modules.ExecutionFramework 项目编译通过（0 警告 0 错误）✅

Phase 4: BasicMethod 兼容层 ⏳ 待处理
  └── 修改 ExecutionContext.cs / FunctionRegistry.cs / FunctionCall.cs 为 using 别名
  └── 修改 DefaultFunctionRegistry.cs / OverridableFunctionRegistry.cs
  └── 验证：BasicMethod.FunctionExecution 项目编译通过

Phase 5: 全局验证 ⏳ 待处理
  └── dotnet build MainProj.sln
  └── 运行测试
```

### 当前进度总览

| Phase | 状态 | 完成度 |
|-------|------|--------|
| Phase 1: 扩展 Transformers | ✅ 已完成 | 100% |
| Phase 2: Core 模型 → Modules/Registry | ✅ 已完成 | 100% |
| Phase 3: 执行引擎 → Modules/ExecutionFramework | ✅ 已完成 | 100% |
| Phase 4: BasicMethod 兼容层 | ⏳ 待处理 | 0% |
| Phase 5: 全局验证 | ⏳ 待处理 | 0% |

---

## 六、风险与兼容性

### 6.1 命名空间兼容

`BasicMethod` 使用 `UniversalWorkflowFramework.FunctionExecution.Core` 命名空间，迁移后需要在 `Modules/Registry` 中添加：

```csharp
// 兼容性 using 别名（可选）
namespace UniversalWorkflowFramework.FunctionExecution.Core
{
    using ExecutionStatus = Modules.Registry.ExecutionStatus;
    using ExecutionResult = Modules.Registry.ExecutionResult;
    using ExecutionContext = Modules.Registry.ExecutionContext;
    // ...
}
```

### 6.2 CompiledExecutor 的归属

`FunctionDescriptor.CompiledExecutor`（表达式树编译优化）是 `BasicMethod.FunctionRegistry` 独有的高性能特性。迁移后：

- `Modules.Registry.FunctionDescriptor` 添加 `CompiledExecutor` 属性（可选）
- `Modules.Registry.FunctionRegistry` 不强制编译，仅在需要时通过扩展方法编译
- `BasicMethod.FunctionRegistryHelper` 保留 `RegisterClass<T>()` 等编译优化扩展

### 6.3 向后兼容

所有迁移操作应保证现有代码无需修改即可编译通过。策略：

1. 先在目标位置创建新文件
2. 修改源文件为 using 别名/转发
3. 验证编译通过后，再逐步清理源文件

### 6.4 已知问题

- `ExecutionContext` 与 `System.Threading.ExecutionContext` 存在命名冲突，需要使用 `EXContext` 别名
- `StepExecutor.cs` 和 `ExecutionContextAdapter.cs` 仍引用旧 `BasicMethod` 命名空间，需在 Phase 4 处理
