# Share.Registry 更新与应用指南

## 概述

本文档详细说明如何基于通用理念完善 `Share/Registry`，使其覆盖项目中所有注册逻辑，并统一应用。

---

## 一、现状分析：系统中注册入口的分布

当前项目中存在 **至少 7 个独立的注册体系**，导致函数名冲突（如 "Plus" 在多个注册表中重复注册）、查询路径混乱、诊断困难。

| # | 注册体系 | 位置 | 注册内容 | 注册方式 |
|---|---------|------|---------|---------|
| 1 | **FunctionStorageRegistry** | `Operation/FunctionStorage/Framework/` | 数学函数、UI工具函数、工作流函数 | 静态类 + `ConcurrentDictionary` |
| 2 | **NonEventCommandRegistry** | `Operation/FunctionStorage/Framework/` | 非事件命令（echo/sleep/wait） | 独立接口实现 |
| 3 | **UnifiedCommandRegistry** | `Operation/FunctionStorage/Framework/` | 统一命令注册（桥接） | 静态类 |
| 4 | **CommandFramework 注册表** | `Share/CommandFramework/` | `CommandRegistry` / `EngineCommandRegistry` / `HandlerRegistry` | 继承 `FactoryRegistryBase` / `MapRegistryBase` |
| 5 | **WorkflowIORegistry** | `Operation/FunctionStorage/Business/` | 工作流 IO 映射 | 静态 `ConcurrentDictionary` |
| 6 | **Share.Registry** | `Share/Registry/` | 通用注册表框架（6种模式 + 函数注册表） | 接口 + 基类体系 |
| 7 | **AutoRegisterService** | `Share/Registry/AutoRegister/` | 自动从程序集/源代码提取并注册 | 独立服务 |

---

## 二、目标架构：三层统一注册体系

```
┌─────────────────────────────────────────────────────────────┐
│                    第三层：统一注册中心                        │
│  UnifiedRegistryCenter — 全局单例，聚合所有注册表实例          │
│  - 实现 IRegistryManager（注册表管理）                        │
│  - 实现 IRegistryQuery（统一查询）                            │
│  - 实现 IRegistryDiagnostics（诊断）                         │
│  - 实现 IRegistryLifecycle（生命周期）                       │
├─────────────────────────────────────────────────────────────┤
│                    第二层：领域注册表                          │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐    │
│  │ FunctionStorage│ CommandFramework│ WorkflowRegistry │   │
│  │ Registry       │ Registry       │ (待创建)          │   │
│  │ IRegistryLifecycle│ IRegistryLifecycle│              │   │
│  └──────┬───────┘ └──────┬───────┘ └──────────────────┘    │
│         │                │                                  │
├─────────┼────────────────┼──────────────────────────────────┤
│                    第一层：通用注册表基类                      │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  Share.Registry 基类体系                              │   │
│  │  IRegistry (Clear)                                   │   │
│  │    ├── ISetRegistry<TKey>          — 纯集合           │   │
│  │    ├── IMapRegistry<TKey,TValue>   — 键值映射         │   │
│  │    ├── IFactoryRegistry<TKey,TVal> — 工厂模式         │   │
│  │    ├── IMetaMapRegistry<TK,TV,TM>  — 带元数据键值映射  │   │
│  │    ├── IStageRegistry<TStage,TV>   — 阶段管理         │   │
│  │    ├── IGraphRegistry<TNid,TN,TE>  — 图结构           │   │
│  │    ├── INotifyRegistry<TK,TV>      — 事件通知         │   │
│  │    ├── IFunctionRegistry           — 函数注册表       │   │
│  │    ├── IRegistryManager            — 注册表管理       │   │
│  │    ├── IRegistryQuery              — 统一查询         │   │
│  │    ├── IRegistryDiagnostics        — 诊断             │   │
│  │    └── IRegistryLifecycle          — 生命周期管理     │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## 三、Phase 1：补充 Share/Registry 缺失接口 ✅ 已完成

> **状态：已完成** — 所有新接口已合并到 `08_RegistryInterfaces.cs` 文件中。

### 3.1 新增 `08_RegistryInterfaces.cs` — 所有新接口集中在一个文件

包含以下接口和类型：
- `IRegistryManager` — 注册表管理接口
- `IRegistryQuery` — 统一查询接口
- `IRegistryDiagnostics` — 诊断接口
- `DuplicateRegistration` / `RegistrationSource` / `RegistryDiagnosticInfo` / `RegistryStats` — 诊断数据模型
- `IRegistryLifecycle` — 生命周期接口
- `RegistryLifecycleExtensions` — 生命周期扩展方法
- `ConflictStrategy` 枚举 / `IConflictResolver` — 冲突解决策略

---

## 四、Phase 2：统一 FunctionStorageRegistry

### 4.1 改造 FunctionStorageRegistry

**改造前**（静态类，内部包装 FunctionRegistry）：
```csharp
public static class FunctionStorageRegistry
{
    private static readonly FunctionRegistry Registry = new();
    
    public static void Register(string name, Delegate function, string? description = null)
        => Registry.Register(name, function, description);
    
    public static bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor)
        => Registry.TryGetDescriptor(name, out descriptor);
}
```

**改造后**（实例类，实现 IRegistryLifecycle）：
```csharp
public class FunctionStorageRegistry : IRegistryLifecycle
{
    private readonly FunctionRegistry _registry;
    private bool _initialized;
    
    public FunctionStorageRegistry(FunctionRegistry registry)
    {
        _registry = registry;
    }
    
    public bool IsInitialized => _initialized;
    
    public void Initialize()
    {
        if (_initialized) return;
        MathLibrary.RegisterAll(_registry);           // 传入注册表实例
        BuiltInUnitsLibrary.RegisterAll(_registry);
        UIToolbox.RegisterAll(_registry);
        MathBrowserFunctions.RegisterAll(_registry);
        WorkflowLibrary.RegisterAll(_registry);
        _initialized = true;
    }
    
    public void Shutdown()
    {
        _registry.Clear();
        _initialized = false;
    }
    
    // 委托方法
    public void Register(string name, Delegate function, string? description = null)
        => _registry.Register(name, function, description);
    
    public bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor)
        => _registry.TryGetDescriptor(name, out descriptor);
    
    public bool TryGetDelegate(string name, out Delegate? function)
        => _registry.TryGetDelegate(name, out function);
    
    public IEnumerable<string> GetRegisteredNames() => _registry.GetRegisteredNames();
}
```

### 4.2 改造所有 RegisterAll() 方法

所有 `RegisterAll()` 方法改为**接收 `IFunctionRegistry` 参数**，而非直接调用 `FunctionStorageRegistry.Register()`：

```csharp
// 改造前
public static class MathLibrary
{
    public static void RegisterAll()
    {
        FunctionStorageRegistry.Register("Add", (Func<double, double, double>)Add, "Addition: a + b");
        FunctionStorageRegistry.Register("Plus", (Func<double, double, double>)Add, "Addition alias: a + b");
        // ...
    }
}

// 改造后
public static class MathLibrary
{
    public static void RegisterAll(IFunctionRegistry registry)
    {
        registry.Register("Add", (Func<double, double, double>)Add, "Addition: a + b");
        registry.Register("Plus", (Func<double, double, double>)Add, "Addition alias: a + b");
        // ...
    }
}
```

需要改造的文件：
- `Operation/FunctionStorage/Business/Registrations/MathLibrary.cs`
- `Operation/FunctionStorage/Business/Registrations/BuiltInUnitsLibrary.cs`
- `Operation/FunctionStorage/Business/Registrations/UIToolbox.cs`
- `Operation/FunctionStorage/Business/Registrations/MathBrowserFunctions.cs`
- `Operation/FunctionStorage/Business/Registrations/WorkflowLibrary.cs`

---

## 五、Phase 3：统一 CommandFramework 注册表

### 5.1 创建 CommandFrameworkRegistry

```csharp
// Share/CommandFramework/CommandFrameworkRegistry.cs
public class CommandFrameworkRegistry : IRegistryLifecycle
{
    public CommandRegistry Commands { get; }
    public EngineCommandRegistry EngineCommands { get; }
    public HandlerRegistry Handlers { get; }
    public NonEventCommandRegistry NonEventCommands { get; }
    
    private bool _initialized;
    
    public CommandFrameworkRegistry(
        CommandRegistry? commands = null,
        EngineCommandRegistry? engineCommands = null,
        HandlerRegistry? handlers = null,
        NonEventCommandRegistry? nonEventCommands = null)
    {
        Commands = commands ?? new CommandRegistry();
        EngineCommands = engineCommands ?? new EngineCommandRegistry();
        Handlers = handlers ?? new HandlerRegistry();
        NonEventCommands = nonEventCommands ?? new NonEventCommandRegistry();
    }
    
    public bool IsInitialized => _initialized;
    
    public void Initialize()
    {
        if (_initialized) return;
        Handlers.RegisterBuiltIn();
        NonEventCommands.RegisterBuiltIn();
        _initialized = true;
    }
    
    public void Shutdown()
    {
        Commands.Clear();
        EngineCommands.Clear();
        Handlers.Clear();
        NonEventCommands.Clear();
        _initialized = false;
    }
}
```

---

## 六、Phase 4：创建 UnifiedRegistryCenter

### 6.1 核心实现

```csharp
// Share/Registry/UnifiedRegistryCenter.cs
public class UnifiedRegistryCenter : IRegistryManager, IRegistryQuery, IRegistryDiagnostics, IRegistryLifecycle
{
    private readonly ConcurrentDictionary<string, IRegistry> _registries = new();
    private readonly List<IRegistryLifecycle> _lifecycles = new();
    private readonly IConflictResolver _conflictResolver;
    private bool _initialized;
    
    public UnifiedRegistryCenter(IConflictResolver? conflictResolver = null)
    {
        _conflictResolver = conflictResolver ?? new DefaultConflictResolver();
    }
    
    // ════════════════════════════════════════════════════════════
    // IRegistryManager 实现
    // ════════════════════════════════════════════════════════════
    
    public void RegisterRegistry<T>(string name, T registry) where T : IRegistry
    {
        ArgumentNullException.ThrowIfNull(registry);
        
        if (_registries.TryGetValue(name, out var existing))
            throw new InvalidOperationException(
                $"Registry '{name}' is already registered as {existing.GetType().Name}");
        
        _registries[name] = registry;
        if (registry is IRegistryLifecycle lifecycle)
            _lifecycles.Add(lifecycle);
    }
    
    public T GetRegistry<T>(string name) where T : IRegistry
    {
        if (_registries.TryGetValue(name, out var registry) && registry is T typed)
            return typed;
        throw new KeyNotFoundException($"Registry '{name}' of type {typeof(T).Name} not found");
    }
    
    public bool TryGetRegistry<T>(string name, [MaybeNullWhen(false)] out T registry) where T : IRegistry
    {
        if (_registries.TryGetValue(name, out var r) && r is T typed)
        {
            registry = typed;
            return true;
        }
        registry = default;
        return false;
    }
    
    // ════════════════════════════════════════════════════════════
    // IRegistryLifecycle 实现
    // ════════════════════════════════════════════════════════════
    
    public bool IsInitialized => _initialized;
    
    public void Initialize()
    {
        if (_initialized) return;
        foreach (var lifecycle in _lifecycles)
        {
            if (!lifecycle.IsInitialized)
                lifecycle.Initialize();
        }
        _initialized = true;
    }
    
    public void Shutdown()
    {
        if (!_initialized) return;
        foreach (var lifecycle in _lifecycles.Reverse<IRegistryLifecycle>())
        {
            if (lifecycle.IsInitialized)
                lifecycle.Shutdown();
        }
        _initialized = false;
    }
    
    // ════════════════════════════════════════════════════════════
    // IRegistryQuery 实现
    // ════════════════════════════════════════════════════════════
    
    public IFunctionDescriptor? ResolveFunction(string name)
    {
        // 1. 查 FunctionRegistry（通过 FunctionStorageRegistry 包装）
        if (TryGetRegistry<FunctionStorageRegistry>("functions", out var funcStorage)
            && funcStorage.TryGetDescriptor(name, out var desc))
            return desc;
        
        // 2. 查 CommandFramework 的 HandlerRegistry
        if (TryGetRegistry<CommandFrameworkRegistry>("commands", out var cmdFramework))
        {
            // HandlerRegistry 是 IMapRegistry<string, Func<ParsedCommand, Task<CommandExecutionResult<object?>>>>
            // 可在此扩展查询逻辑
        }
        
        return null;
    }
    
    public bool TryExecuteFunction(string name, object?[]? args, out object? result)
    {
        result = null;
        
        // 1. 查 FunctionRegistry
        if (TryGetRegistry<FunctionStorageRegistry>("functions", out var funcStorage)
            && funcStorage.TryGetDelegate(name, out var del))
        {
            try
            {
                result = del.DynamicInvoke(args);
                return true;
            }
            catch { return false; }
        }
        
        // 2. 查 CommandFramework
        // ...
        
        return false;
    }
    
    // ════════════════════════════════════════════════════════════
    // IRegistryDiagnostics 实现
    // ════════════════════════════════════════════════════════════
    
    public IReadOnlyList<DuplicateRegistration> FindDuplicates()
    {
        var nameMap = new Dictionary<string, List<RegistrationSource>>(
            StringComparer.OrdinalIgnoreCase);
        
        foreach (var (regName, registry) in _registries)
        {
            switch (registry)
            {
                case IFunctionRegistry fr:
                    foreach (var funcName in fr.GetRegisteredNames())
                    {
                        AddToMap(nameMap, funcName, regName, registry.GetType().Name, "Delegate");
                    }
                    break;
                    
                case IMapRegistry<string, Delegate> mr:
                    foreach (var key in mr.GetKeys())
                    {
                        AddToMap(nameMap, key, regName, registry.GetType().Name, "Delegate");
                    }
                    break;
                    
                case ISetRegistry<string> sr:
                    foreach (var key in sr.GetKeys())
                    {
                        AddToMap(nameMap, key, regName, registry.GetType().Name, null);
                    }
                    break;
                    
                case IFactoryRegistry<string, ICommand> cr:
                    foreach (var key in cr.GetKeys())
                    {
                        AddToMap(nameMap, key, regName, registry.GetType().Name, "ICommand");
                    }
                    break;
            }
        }
        
        return nameMap
            .Where(kvp => kvp.Value.Count > 1)
            .Select(kvp => new DuplicateRegistration
            {
                Name = kvp.Key,
                Registrations = kvp.Value
            })
            .ToList();
    }
    
    private static void AddToMap(
        Dictionary<string, List<RegistrationSource>> map,
        string name,
        string registryName,
        string registryTypeName,
        string? valueTypeName)
    {
        if (!map.ContainsKey(name))
            map[name] = new List<RegistrationSource>();
        map[name].Add(new RegistrationSource
        {
            RegistryName = registryName,
            RegistryTypeName = registryTypeName,
            ValueTypeName = valueTypeName
        });
    }
    
    public RegistryDiagnosticInfo Diagnose()
    {
        var info = new RegistryDiagnosticInfo();
        foreach (var (name, registry) in _registries)
        {
            var count = registry switch
            {
                IFunctionRegistry fr => fr.GetRegisteredNames().Count(),
                IMapRegistry<string, Delegate> mr => mr.Count,
                ISetRegistry<string> sr => sr.Count,
                IFactoryRegistry<string, ICommand> cr => cr.Count,
                IMapRegistry<string, string> nr => nr.Count,
                _ => 0
            };
            info.Add(name, new RegistryStats(count, registry.GetType().Name));
        }
        info.Duplicates.AddRange(FindDuplicates());
        return info;
    }
    
    public void DumpAllRegistrations(TextWriter writer)
    {
        var diag = Diagnose();  // 一次调用获取所有信息（内部已调用 FindDuplicates）
        writer.WriteLine($"=== Registry Diagnostic ({diag.GeneratedAt:yyyy-MM-dd HH:mm:ss}) ===");
        writer.WriteLine();
        
        foreach (var (name, stats) in diag.Stats)
        {
            writer.WriteLine($"  [{name}] ({stats.TypeName}) — {stats.Count} items");
        }
        
        if (diag.Duplicates.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("=== Duplicate Registrations ===");
            foreach (var dup in diag.Duplicates)
            {
                writer.WriteLine($"  '{dup.Name}':");
                foreach (var src in dup.Registrations)
                    writer.WriteLine($"    - {src.RegistryName} ({src.RegistryTypeName})");
            }
        }
    }
}
```

### 6.2 依赖注入注册扩展

```csharp
// Share/Registry/ServiceCollectionExtensions.cs
public static class RegistryServiceCollectionExtensions
{
    public static IServiceCollection AddUnifiedRegistry(this IServiceCollection services)
    {
        // 注册基础注册表（不对外暴露 FunctionRegistry，防止被绕过）
        services.AddSingleton<FunctionRegistry>(sp => new FunctionRegistry());
        
        // 注册领域注册表（实现 IRegistryLifecycle）
        services.AddSingleton<FunctionStorageRegistry>();
        services.AddSingleton<CommandFrameworkRegistry>();
        
        // 注册统一注册中心（同时实现 IRegistryManager / IRegistryQuery / IRegistryDiagnostics / IRegistryLifecycle）
        services.AddSingleton<UnifiedRegistryCenter>(sp =>
        {
            var hub = new UnifiedRegistryCenter(new ConfigurableConflictResolver(
                defaultStrategy: ConflictStrategy.Overwrite,
                overrides: new Dictionary<string, ConflictStrategy>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Plus"] = ConflictStrategy.Skip  // Plus 以首次注册为准
                }));
            
            // 注册所有注册表到中心
            hub.RegisterRegistry("functions", sp.GetRequiredService<FunctionStorageRegistry>());
            hub.RegisterRegistry("commands", sp.GetRequiredService<CommandFrameworkRegistry>());
            
            return hub;
        });
        
        // 暴露接口
        services.AddSingleton<IRegistryManager>(sp => sp.GetRequiredService<UnifiedRegistryCenter>());
        services.AddSingleton<IRegistryQuery>(sp => sp.GetRequiredService<UnifiedRegistryCenter>());
        services.AddSingleton<IRegistryDiagnostics>(sp => sp.GetRequiredService<UnifiedRegistryCenter>());
        services.AddSingleton<IRegistryLifecycle>(sp => sp.GetRequiredService<UnifiedRegistryCenter>());
        
        return services;
    }
}
```

---

## 七、Phase 5：迁移调用方 ✅ 已完成

> **状态：已完成** — `FileDrivenExecutor.InitializeFramework()` 已改造为使用 `FunctionStorageLifecycle`。

### 7.1 FileDrivenExecutor 改造

**改造前**（散落的 RegisterAll() 调用）：
```csharp
MathLibrary.RegisterAll();
UIToolbox.RegisterAll();
MathBrowserFunctions.RegisterAll();
BuiltInUnits.RegisterAll(FunctionStorageAdapter.RegistryAdapter);
// + AutoRegister 的 Maths 嵌套类注册
```

**改造后**（通过 FunctionStorageLifecycle 统一管理）：
```csharp
// ★ Phase 5：使用 FunctionStorageLifecycle 统一管理所有 Library 注册
//   替代散落的 MathLibrary.RegisterAll()、UIToolbox.RegisterAll() 等调用。
//   如果 FunctionStorageLifecycle 初始化失败，异常向上传播，框架启动失败（快速失败）。
_functionStorageLifecycle = new FunctionStorageLifecycle();
_functionStorageLifecycle.Initialize();
```

**关闭时**：
```csharp
// ShutdownFramework 中
if (_functionStorageLifecycle != null)
{
    _functionStorageLifecycle.Shutdown();
    _functionStorageLifecycle = null;
}
```

### 7.2 设计要点

1. **无回退路径**：`FunctionStorageLifecycle` 是唯一的注册入口。如果初始化失败，异常向上传播，框架启动失败（快速失败原则），开发者能立即发现问题。
2. **生命周期配对**：`Initialize()` 在 `InitializeFramework` 中调用，`Shutdown()` 在 `ShutdownFramework` 中调用，确保资源正确释放。
3. **移除冗余 using**：移除了不再直接使用的 `FunctionStorage.Registrations`、`MathematicalAlgorithms`、`Share.Registry.AutoRegister` 等命名空间引用。

---

## 八、Phase 6：诊断工具

### 8.1 命令行诊断

```csharp
// 在 CentralConsole 或 CLI 启动入口中添加诊断命令
public static void DumpRegistryDiagnostic(IRegistryDiagnostics diagnostics)
{
    var diag = diagnostics.Diagnose();  // 一次调用获取所有信息
    
    using var writer = new StringWriter();
    writer.WriteLine($"=== Registry Diagnostic ({diag.GeneratedAt:yyyy-MM-dd HH:mm:ss}) ===");
    writer.WriteLine();
    
    foreach (var (name, stats) in diag.Stats)
        writer.WriteLine($"  [{name}] ({stats.TypeName}) — {stats.Count} items");
    
    if (diag.Duplicates.Count > 0)
    {
        writer.WriteLine();
        writer.WriteLine($"⚠ Found {diag.Duplicates.Count} duplicate registrations!");
        foreach (var dup in diag.Duplicates)
        {
            writer.WriteLine($"  '{dup.Name}':");
            foreach (var src in dup.Registrations)
                writer.WriteLine($"    - {src.RegistryName} ({src.RegistryTypeName})");
        }
    }
    
    Console.WriteLine(writer.ToString());
}
```

### 8.2 启动时自动诊断

在应用启动时自动检测重复注册并记录日志：

```csharp
// Program.cs 或 MainForm.OnLoad 中
var diagnostics = serviceProvider.GetRequiredService<IRegistryDiagnostics>();
var diag = diagnostics.Diagnose();
if (diag.Duplicates.Count > 0)
{
    Logger.Warn($"Found {diag.Duplicates.Count} duplicate function registrations:");
    foreach (var dup in diag.Duplicates)
        Logger.Warn($"  '{dup.Name}' → {string.Join(", ", dup.Registrations.Select(r => r.RegistryName))}");
}
```

---

## 九、实施路线图

| 阶段 | 内容 | 文件 | 风险 | 状态 |
|------|------|------|------|------|
| **Phase 1** | 补充 `Share/Registry` 缺失接口（IRegistryManager、IRegistryQuery、IRegistryDiagnostics、IRegistryLifecycle、IConflictResolver） | `08_RegistryInterfaces.cs` | 低 | ✅ 已完成 |
| **Phase 2** | 改造 `FunctionStorageRegistry` 为 `IRegistryLifecycle`，改造所有 `RegisterAll()` | 修改 6 个文件 | 中 | ✅ 已完成 |
| **Phase 3** | 统一 `CommandFramework` 注册表为 `IRegistryLifecycle` | 新增 1 个文件，修改 2 个 | 中 | ✅ 已完成 |
| **Phase 4** | 创建 `UnifiedRegistryCenter` + DI 扩展 | `09_UnifiedRegistryCenter.cs` + `10_ServiceCollectionExtensions.cs` | 低 | ✅ 已完成 |
| **Phase 5** | 迁移调用方（FileDrivenExecutor.InitializeFramework 使用 FunctionStorageLifecycle） | 修改 1 个文件（FileDrivenExecutor.cs） | 高 | ✅ 已完成 |
| **Phase 6** | 添加冲突检测 + 诊断工具 | 修改 2-3 个文件 | 低 | ⏳ 待实施 |

### 迁移策略

1. **向后兼容**：所有新增接口和类与现有代码共存，不破坏现有 API
2. **渐进式迁移**：先添加新接口和实现，再逐步替换调用方
3. **并行运行**：新旧注册体系可同时运行，通过诊断工具验证一致性
4. **最终清理**：所有调用方迁移完成后，移除旧的静态注册入口

---

## 十、关键收益

| 收益 | 说明 |
|------|------|
| **消除多重注册混乱** | 所有注册集中到 `UnifiedRegistryCenter`，冲突可检测、可控制 |
| **统一查询入口** | 不再需要在多个注册表间手动查找，`ResolveFunction()` 跨注册表搜索 |
| **生命周期管理** | `Initialize()` / `Shutdown()` 统一管理，消除散落的 `RegisterAll()` 调用 |
| **可诊断性** | `DumpAllRegistrations()` 一键输出所有注册状态，`FindDuplicates()` 自动检测冲突 |
| **渐进式迁移** | 现有代码可逐步迁移，不破坏现有 API，新旧体系可并行运行 |
| **依赖注入友好** | 所有注册表通过 DI 容器管理，便于单元测试和替换 |

---

## 附录 A：Phase 2~5 代码审查修复记录（2026-06-14）

### 发现的问题与修复

| # | 文件 | 问题 | 修复 |
|---|------|------|------|
| 1 | `09_UnifiedRegistryCenter.cs` | `RegisterRegistry` 未集成 `IConflictResolver`，遇到重复注册直接抛异常，未利用已定义的冲突解决策略 | 在 `RegisterRegistry` 中集成 `IConflictResolver`，支持 `Overwrite`/`Skip`/`Throw` 三种策略；当 `UnifiedRegistryCenter` 已初始化时，新注册的生命周期自动初始化 |
| 2 | `FunctionStorageRegistry.cs` | `FunctionStorageLifecycle.Shutdown()` 未清理 `NonEventCommandRegistry`，与静态 `FunctionStorageRegistry.Clear()` 行为不一致 | 在 `Shutdown()` 中补充 `NonEventCommandRegistry` 清理逻辑 |
| 3 | `FunctionStorageRegistry.cs` | 静态门面 `LazyInitialize()` 可能创建临时实例，随后被外部 `Initialize()` 覆盖，存在短暂的双重实例风险 | 新增 `IsExternallyInitialized` 属性供诊断使用；`LazyInitialize` 的锁机制确保线程安全，外部 `Initialize` 优先 |
| 4 | `MathBrowserFunctions.cs` | 委托内部硬编码 `FunctionStorageRegistry.GetRegisteredNames()` / `FunctionStorageRegistry.TryGetDescriptor()` / `FunctionStorageRegistry.ExecuteAsync()` 等静态门面调用，未使用传入的 `registry` 参数 | 所有委托闭包改为捕获并使用传入的 `registry` 参数；`Math_Execute` 改用 `reg.TryGetDelegate` + `DynamicInvoke` 替代 `FunctionStorageRegistry.ExecuteAsync` |
| 5 | `WorkflowLibrary.cs` | `ValidateStepFunctions` 内部硬编码 `FunctionStorageRegistry.Contains()`，未使用传入的 `registry` 参数 | 方法签名改为 `ValidateStepFunctions(IFunctionRegistry registry, WorkflowDefinition def)`，内部使用 `registry.Contains()` |

### 修复原则

1. **闭包捕获 registry 引用**：所有委托内部应使用传入的 `IFunctionRegistry` 参数，而非硬编码静态门面 `FunctionStorageRegistry`
2. **生命周期一致性**：`Initialize()` 和 `Shutdown()` 应成对清理所有注册资源
3. **冲突策略可配置**：`RegisterRegistry` 应尊重 `IConflictResolver` 的决策，而非硬编码 `Throw`
4. **编译验证**：所有修改后 `Registry.csproj` 和 `FunctionStorage.csproj` 均编译通过（0 错误，0 新增警告）
