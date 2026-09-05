# FunctionRegistry 架构简化方案

## 一、问题诊断

### 1.1 核心问题

从日志可以看出：

```
[lifecycle.after_mathlibrary] RegisteredCount: 4    ← MathLibrary.RegisterAll 注册了 4 个函数
[WorkflowMethodExecution_Registry_notfound_diag] totalRegistered: 0  ← 同一个注册表实例查询返回 0
```

**同一个 `FunctionRegistry` 实例，注册时有 4 个函数，查询时返回 0 个。**

### 1.2 根因分析

`FunctionRegistry` 继承自 `MetaMapRegistryBase<string, Delegate, FunctionDescriptor>`，内部维护了**三个字典**：

```
MetaMapRegistryBase (基类)
  ├── _values:     ConcurrentDictionary<string, Delegate>       ← 基类字典1
  └── _metadatas:  ConcurrentDictionary<string, FunctionDescriptor>  ← 基类字典2

FunctionRegistry (子类)
  ├── _units:      Dictionary<string, IFunctionUnit>            ← 自有字典3
  └── _configs:    ConcurrentDictionary<string, ITrConfig>      ← 自有字典4
```

**查询路径混乱：**

| 方法 | 查询的字典 | 说明 |
|------|-----------|------|
| `GetRegisteredNames()` | `_values.Keys`（基类 `GetKeys()`） | 基类字典1 |
| `Contains()` | `_values.ContainsKey`（基类 `StoreContainsKey`） | 基类字典1 |
| `TryGetDelegate()` | `_values.TryGetValue`（基类 `TryGet`） | 基类字典1 |
| `TryGetConfig()` | 优先 `_configs`，回退 `_metadatas` | 字典4 → 字典2 |
| `TryGetDescriptor()` | `_metadatas.TryGetValue`（基类 `TryGetMetadata`） | 基类字典2 |

**`Register(string, Delegate, string?)` 的写入路径：**

```csharp
public void Register(string name, Delegate function, string? description = null)
{
    var descriptor = FunctionDescriptor.FromDelegate(function, description);
    descriptor.Name = name;
    descriptor.Delegate = function;
    Register(name, function, descriptor);  // → 写入 _values + _metadatas（基类）
    _configs[name] = descriptor;           // → 写入 _configs（子类）
}
```

**问题：** 如果 `_values` 字典被清空或不同步，`GetRegisteredNames()` 返回 0，但 `_configs` 中仍有数据。

### 1.3 冗余架构层级

```
FunctionRegistry
  ├── 继承 MetaMapRegistryBase<string, Delegate, FunctionDescriptor>
  │     └── 继承 RegistryBase<string>
  │           └── 实现 IRegistry
  │
  ├── 实现 IFunctionRegistry
  │     └── 继承 ITrConfigRegistry
  │           └── 包含 Register/Unregister/TryGetConfig/Contains/GetRegisteredNames/Clear
  │
  ├── 显式实现 ITrConfigRegistry.Register(ITrConfig)
  ├── 显式实现 ITrConfigRegistry.TryGetConfig()
  └── 公开 TryGetConfig() 委托给显式实现
```

**6 层继承/接口，4 个字典，3 条不同的查询路径。**

---

## 二、简化方案

### 2.1 核心思路

**`FunctionRegistry` 不再继承 `MetaMapRegistryBase`，改为直接实现 `IFunctionRegistry`，只维护一个 `_configs` 字典。**

### 2.2 具体改动

#### 2.2.1 `FunctionRegistry` 重写

```csharp
public class FunctionRegistry : IFunctionRegistry
{
    private readonly ConcurrentDictionary<string, ITrConfig> _configs;
    private readonly ConcurrentDictionary<string, IFunctionUnit> _units;

    public FunctionRegistry(IEqualityComparer<string>? comparer = null)
    {
        var eq = comparer ?? StringComparer.OrdinalIgnoreCase;
        _configs = new ConcurrentDictionary<string, ITrConfig>(eq);
        _units = new ConcurrentDictionary<string, IFunctionUnit>(eq);
    }

    // ─── 注册委托 ───
    public void Register(string name, Delegate function, string? description = null)
    {
        var descriptor = FunctionDescriptor.FromDelegate(function, description);
        descriptor.Name = name;
        descriptor.Delegate = function;
        _configs[name] = descriptor;
    }

    // ─── 注册委托（带参数名） ───
    public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)
    {
        var descriptor = FunctionDescriptor.FromContract(function, parameters, description);
        descriptor.Name = name;
        _configs[name] = descriptor;
    }

    // ─── 注册函数单元 ───
    public void Register(string name, IFunctionUnit unit, string? description = null)
    {
        _units[name] = unit;
        var wrappedDelegate = new Func<object?, Task<ExecutionResult>>(async input =>
            await unit.ExecuteAsync(input));
        var descriptor = new FunctionDescriptor
        {
            Name = name,
            Description = description ?? unit.Description,
            ReturnType = unit.OutputType,
            IsAsync = unit.IsAsync,
            Parameters = Array.Empty<IParameterDescriptor>()
        };
        _configs[name] = descriptor;
    }

    // ─── 注册 ITrConfig ───
    public void Register(string name, ITrConfig config)
    {
        _configs[name] = config;
    }

    // ─── 查询 ───
    public bool TryGetConfig(string name, out ITrConfig config)
        => _configs.TryGetValue(name, out config);

    public bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor)
    {
        if (_configs.TryGetValue(name, out var config) && config is IFunctionDescriptor fd)
        {
            descriptor = fd;
            return true;
        }
        descriptor = null!;
        return false;
    }

    public bool TryGetDelegate(string name, out Delegate? function)
    {
        if (_configs.TryGetValue(name, out var config))
        {
            function = config switch
            {
                FunctionDescriptor fd => fd.Delegate,
                TrConfig tc => tc.Delegate,
                _ => null
            };
            return function != null;
        }
        function = null;
        return false;
    }

    public bool TryGetUnit(string name, out IFunctionUnit? unit)
        => _units.TryGetValue(name, out unit);

    public bool Contains(string name)
        => _configs.ContainsKey(name);

    public IEnumerable<string> GetRegisteredNames()
        => _configs.Keys;

    // ─── 生命周期 ───
    public void Unregister(string name)
    {
        _configs.TryRemove(name, out _);
        _units.Remove(name);
    }

    public void Clear()
    {
        _configs.Clear();
        _units.Clear();
    }
}
```

#### 2.2.2 删除不再需要的文件

| 文件 | 原因 |
|------|------|
| `CsharpPrj/Modules/Registry/02_RegistryBase.cs` | `RegistryBase<TKey>` 不再被使用 |
| `CsharpPrj/Modules/Registry/03_MapBasedRegistries.cs` 中的 `MetaMapRegistryBase` | 不再被使用 |
| `CsharpPrj/Modules/Registry/01_Interfaces.cs` 中的 `IMetaMapRegistry` | 不再被使用 |

**注意：** `SetRegistryBase`、`MapRegistryBase`、`FactoryRegistryBase` 如果其他地方还在使用，则保留。

#### 2.2.3 简化接口层级

`IFunctionRegistry` 不再继承 `ITrConfigRegistry`，改为独立接口：

```csharp
public interface IFunctionRegistry
{
    void Register(string name, Delegate function, string? description = null);
    void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null);
    void Register(string name, IFunctionUnit unit, string? description = null);
    void Register(string name, ITrConfig config);
    void Unregister(string name);
    bool TryGetConfig(string name, out ITrConfig config);
    bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor);
    bool TryGetDelegate(string name, out Delegate? function);
    bool TryGetUnit(string name, out IFunctionUnit? unit);
    bool Contains(string name);
    IEnumerable<string> GetRegisteredNames();
    void Clear();
}
```

### 2.3 影响范围

#### 需要修改的文件

| 文件 | 改动 |
|------|------|
| `CsharpPrj/Modules/Registry/06_ConcreteRegistries.cs` | 重写 `FunctionRegistry`，删除 `NormalizationRegistry`（如果未使用） |
| `CsharpPrj/Plate/TConfig/TrConfig/Interfaces/IFunctionRegistry.cs` | 简化接口，去掉 `ITrConfigRegistry` 继承 |
| `CsharpPrj/Modules/Registry/01_Interfaces.cs` | 删除 `IMetaMapRegistry` 接口 |
| `CsharpPrj/Modules/Registry/02_RegistryBase.cs` | 删除整个文件（如果无其他使用者） |
| `CsharpPrj/Modules/Registry/03_MapBasedRegistries.cs` | 删除 `MetaMapRegistryBase` 类 |

#### 不受影响的文件

| 文件 | 原因 |
|------|------|
| `CsharpPrj/Modules/Registry/RegistryCoordinator.cs` | 只引用 `ITrConfigRegistry` 接口，接口不变 |
| `CsharpPrj/Operation/StateBlock/FunctionStorage/Framework/FunctionStorageRegistry.cs` | 通过 `IFunctionRegistry` 接口调用，接口不变 |
| `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | 通过 `IFunctionRegistry` 接口调用，接口不变 |
| `CsharpPrj/Modules/Registry/09_UnifiedRegistryCenter.cs` | 通过 `IFunctionRegistry` 接口调用，接口不变 |
| `CsharpPrj/Modules/Registry/OverridableFunctionRegistry.cs` | 包装 `ITrConfigRegistry`，接口不变 |
| `CsharpPrj/Modules/Registry/FunctionRegistryExtensions.cs` | 扩展方法，接口不变 |
| `CsharpPrj/Plate/TConfig/TrConfig/Models/TrConfigRegistry.cs` | 独立的轻量实现，不受影响 |
| `FileDriven/FileDriven.Business/FileDrivenMethodUsage.cs` | 通过 `IFunctionRegistry` 接口持有，不受影响 |

### 2.4 验证方法

1. **编译验证**：`dotnet build CsharpPrj/MainProj.sln` 确认无编译错误
2. **注册验证**：运行后检查日志，确认 `GetRegisteredNames()` 返回的数量与注册数量一致
3. **执行验证**：点击按钮触发函数执行，确认能正确找到并执行函数

---

## 三、依赖关系图

### 3.1 当前架构

```
IMethodUsageProvider (FrameworkAbstractions)
  └── FileDrivenMethodUsage (FileDriven)
        ├── Registry → IFunctionRegistry
        │     └── FunctionRegistry
        │           ├── MetaMapRegistryBase<string, Delegate, FunctionDescriptor>
        │           │     ├── RegistryBase<string>
        │           │     │     └── IRegistry
        │           │     └── IMetaMapRegistry<string, Delegate, FunctionDescriptor>
        │           └── IFunctionRegistry : ITrConfigRegistry
        │
        ├── Executor → FunctionExecutor
        │     └── 依赖 ITrConfigRegistry
        │
        ├── Pipeline → PipelineExecutor
        │     └── 依赖 ITrConfigRegistry
        │
        └── WorkflowRegistry → IWorkflowRegistry
              └── DefaultWorkflowRegistry
                    └── 包装 ITrConfigRegistry
```

### 3.2 简化后架构

```
IMethodUsageProvider (FrameworkAbstractions)
  └── FileDrivenMethodUsage (FileDriven)
        ├── Registry → IFunctionRegistry
        │     └── FunctionRegistry (直接实现，无基类)
        │           └── _configs: ConcurrentDictionary<string, ITrConfig>  ← 唯一字典
        │
        ├── Executor → FunctionExecutor
        ├── Pipeline → PipelineExecutor
        └── WorkflowRegistry → IWorkflowRegistry
```

---

## 四、执行计划

| 步骤 | 内容 | 预计耗时 |
|------|------|---------|
| 1 | 确认 `RegistryBase` 和 `MetaMapRegistryBase` 是否有其他使用者 | 5min |
| 2 | 重写 `FunctionRegistry`（去掉基类继承，只保留 `_configs` 字典） | 15min |
| 3 | 简化 `IFunctionRegistry` 接口（去掉 `ITrConfigRegistry` 继承） | 5min |
| 4 | 删除不再需要的基类文件 | 5min |
| 5 | 编译验证 | 5min |
| 6 | 运行测试验证 | 10min |
| **总计** | | **~45min** |
