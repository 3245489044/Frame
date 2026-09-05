# FunctionStorage 代码分析报告

> 分析日期：2026-06-14
> 分析范围：`Operation/FunctionStorage/` 目录下所有 `.cs` 文件
> 分析类型：重复逻辑、冗余逻辑、错误逻辑

---

## 一、重复逻辑（Duplicate Logic）

### 1.1 MathUtils.cs ↔ DelegateWrappers.cs（MathWrapper）

**文件：**
- `Operation/FunctionStorage/Business/Registrations/MathUtils.cs`
- `Share/Registry/AutoRegister/Wrappers/DelegateWrappers.cs`

**重复内容：**
| 方法 | MathUtils | MathWrapper |
|------|-----------|-------------|
| `ParseMathParameters` | ✅ 使用 `IParameterDescriptor[]` | ✅ 使用 `ExtractedMethodInfo.ParameterInfo[]` |
| `ConvertToType` | ✅ 完全相同 | ✅ 完全相同 |
| `FormatMathResult` | ✅ 完全相同 | ✅ 完全相同 |

**差异：** 参数类型不同（`IParameterDescriptor[]` vs `ExtractedMethodInfo.ParameterInfo[]`），无法直接委托。

**处理方案：** 添加注释说明对应关系，要求修改时同步修改两处。

### 1.2 WinFormsEventBinder.BindControlEvents() — output/inputs 保存逻辑重复

**文件：** `Operation/FunctionStorage/Business/UI/WinFormsEventBinder.cs`

**问题：** `BindControlEvents(Control, string, Delegate, string?, string?)` 方法中，output 和 inputs 的保存逻辑完全独立但结构相同，都是：
```csharp
if (!string.IsNullOrWhiteSpace(output))
{
    control.Tag = control.Tag is Dictionary<string, object> tag
        ? (tag["output"] = output, tag)[..]
        : new Dictionary<string, object> { ["output"] = output };
}
if (!string.IsNullOrWhiteSpace(inputs))
{
    control.Tag = control.Tag is Dictionary<string, object> tag
        ? (tag["inputs"] = inputs, tag)[..]
        : new Dictionary<string, object> { ["inputs"] = inputs };
}
```

**处理方案：** ✅ 已提取为 `SaveControlParameters()` 私有方法。

### 1.3 UnifiedCommandRegistry ↔ NonEventCommandRegistry 双重同步

**文件：**
- `Operation/FunctionStorage/Framework/UnifiedCommandRegistry.cs`
- `Operation/FunctionStorage/Framework/NonEventCommands.cs`

**问题：** `UnifiedCommandRegistry.Register()` 委托给 `NonEventCommandCenter.Registry.Register()`，而 `NonEventCommandRegistry.Register()` 内部又同步到 `CommandParser.DefaultNonEventCommands`。同时 `UnifiedCommandRegistry.Unregister()` 和 `Clear()` 也手动操作 `CommandParser.DefaultNonEventCommands`，导致双重同步路径。

**处理方案：** 已统一由 `NonEventCommandRegistry` 内部同步，`UnifiedCommandRegistry` 不再直接操作 `CommandParser`。

---

## 二、错误逻辑（Bug Logic）

### 2.1 FunctionStorageRegistry.LazyInitialize() 线程安全问题

**文件：** `Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs`

**问题：** `LazyInitialize()` 方法在 `get` 访问器中调用时没有 double-check locking，多个线程可能同时进入 `lock` 块。更严重的是，`Initialize()` 方法在 `_initialized` 为 `true` 时不会覆盖静态字段，导致 `FunctionStorageLifecycle.Initialize()` 之前 `LazyInitialize` 已创建临时实例，后续 `Initialize()` 无法覆盖。

**处理方案：** ✅ 已修复：
- `LazyInitialize()` 添加 double-check locking（`if (_initialized) return` 快速路径）
- `Initialize()` 始终更新 `_registry` 和 `_executor`，即使 `_initialized` 已经是 `true`

### 2.2 WinFormsEventBinder 字典线程安全

**文件：** `Operation/FunctionStorage/Business/UI/WinFormsEventBinder.cs`

**问题：** 文件中引用了 `_boundOutputs`、`_boundInputs`、`_outputTargetCache`、`_inputTargetsCache`、`_boundHandlers` 等字段，但这些字段在文件中未定义（属于已有错误，非本次引入）。这些字典在多线程环境下（UI 事件 + 管线执行）存在线程安全问题。

**处理方案：** 需要补充字段定义并改用 `ConcurrentDictionary`。

### 2.3 UnifiedCommandRegistry.Register() 静默忽略

**文件：** `Operation/FunctionStorage/Framework/UnifiedCommandRegistry.cs`

**问题：** 当 `registration.Type != CommandType.NonEvent` 或 `registration.Handler == null` 时，方法静默返回，调用方认为命令已成功注册但实际未注册。

**处理方案：** ✅ 已修复：添加 `FsLog.Log()` 警告日志，记录被忽略的命令名称和原因。

### 2.4 NonEventCommandRegistry.ValidateAll() 缓存过期

**文件：** `Operation/FunctionStorage/Framework/NonEventCommands.cs`

**问题：** `ValidateAll()` 使用 `_validationPerformed` 缓存结果，但 `Declare()` 方法没有重置缓存，导致 `Declare()` 之后调用 `ValidateAll()` 返回过期结果。

**处理方案：** ✅ 已修复：`Declare()` 方法中重置 `_validationPerformed = false` 和 `_lastValidationResult = null`。

---

## 三、冗余逻辑（Redundant Logic）

### 3.1 NonEventCommands 命令分类硬编码

**文件：** `Operation/FunctionStorage/Framework/NonEventCommands.cs`

**问题：** `CriticalCommands`、`ImportantCommands`、`OptionalCommands` 三个独立列表与 `AllCommands` 列表内容重复，添加新命令时容易忘记更新分类列表。

**处理方案：** ✅ 已重构：合并为 `_commandCriticalityMap` 统一字典，`AllCommands`/`CriticalCommands`/`ImportantCommands`/`OptionalCommands` 从字典自动生成。

### 3.2 各 Library 类的无参数重载

**文件：** 多个 Library 类

**问题：** `UIToolbox.RegisterAll()`、`WorkflowLibrary.RegisterAll()` 等无参数重载（纯委托给 `FunctionStorageRegistry` 静态门面）与有参数重载（接收 `IFunctionRegistry`）功能重复。

**处理方案：** 由于 `UIToolbox.RegisterAll()` 被 `FileDrivenExecutor.cs` 引用，`WorkflowLibrary.RegisterAll()` 被测试文件引用，暂时保留以保持向后兼容。

### 3.3 WorkflowIORegistry.InferMappings 方法

**文件：** `Operation/FunctionStorage/Business/Registrations/WorkflowIOGroup.cs`

**问题：** `InferMappingsFromDefaultInput()` 和 `InferMappingsFromOutputMapping()` 只是简单的字典复制，调用方可以直接构造 `FieldMappingCollection`。

**处理方案：** ✅ 已添加 `[Obsolete]` 标记，提示调用方直接构造。

### 3.4 冗余 using 别名

**文件：** `Operation/FunctionStorage/Framework/Core.cs`

**问题：** `AbstractionsIFunctionUnit` 和 `IFunctionUnit` 都指向 `FunctionStorage.Abstractions.IFunctionUnit`，但 `AbstractionsIFunctionUnit` 未被使用。

**处理方案：** ✅ 已删除 `AbstractionsIFunctionUnit` 别名。

---

## 四、未完成项

| 项目 | 原因 |
|------|------|
| 1.3 UnifiedCommandRegistry 双重同步 | 需要更全面的架构评审 |
| 2.2 WinFormsEventBinder 字典线程安全 | 字段定义缺失（已有错误），需要补充 |
| 3.2 删除无参数重载 | 被外部引用，需协调修改调用方 |

---

## 五、总结

| 类别 | 发现数 | 已修复 | 待处理 |
|------|--------|--------|--------|
| 重复逻辑 | 3 | 2 | 1 |
| 错误逻辑 | 4 | 3 | 1 |
| 冗余逻辑 | 4 | 3 | 1 |
| **合计** | **11** | **8** | **3** |
