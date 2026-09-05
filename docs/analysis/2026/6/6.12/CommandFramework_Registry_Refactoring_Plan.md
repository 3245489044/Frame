# CommandFramework 注册方案重构 — 基于 Share.Registry

> 文档版本：v1.0  
> 更新日期：2026-06-12  
> 目标：使用 Share.Registry 统一替换 CommandFramework 中 5 个独立的注册子系统

---

## 目录

1. [现状分析](#1-现状分析)
2. [目标架构](#2-目标架构)
3. [迁移映射表](#3-迁移映射表)
4. [详细迁移方案](#4-详细迁移方案)
5. [接口兼容性分析](#5-接口兼容性分析)
6. [实施步骤](#6-实施步骤)
7. [风险与注意事项](#7-风险与注意事项)

---

## 1. 现状分析

### 1.1 CommandFramework 中的 5 个注册子系统

| # | 注册子系统 | 类 | 当前存储 | 注册内容 | 线程安全 |
|---|-----------|-----|---------|---------|---------|
| 1 | 命令工厂注册表 | `CommandRegistry` | `ConcurrentDictionary<string, Func<IServiceProvider?, ICommand>>` | 命令工厂委托 | ✅ |
| 2 | 命令引擎注册 | `CommandEngine` | `Dictionary<string, CommandRegistration>` | 执行/撤销委托 | ❌ |
| 3 | 函数注册表 | `FunctionRegistry` | `ConcurrentDictionary<string, RegisteredFunction>` | Delegate + FunctionDescriptor | ✅ |
| 4 | 非事件命令处理器 | `NonEventCommandHandler` | `Dictionary<string, Func<ParsedCommand, Task<...>>>` | 命令处理函数 | ❌ |
| 5a | 解析器-非事件命令集合 | `CommandParser` | `HashSet<string>` | 命令名集合 | ❌ |
| 5b | 解析器-规范化映射 | `CommandParser` | `Dictionary<string, string>` | 简写→完整格式映射 | ❌ |

**问题：**
- 3 个使用非线程安全集合（`Dictionary`/`HashSet`）
- 5 个独立的注册实现，代码重复
- 缺乏统一的查询/过滤/批量操作能力
- 缺乏事件通知机制

### 1.2 Share.Registry 现有能力

Share.Registry 已提供 **6 种注册表模式**：

| 模式 | 接口 | 基类 | 线程安全 | 额外能力 |
|------|------|------|---------|---------|
| **集合** | `ISetRegistry<TKey>` | `SetRegistryBase<TKey>` | ✅ ConcurrentDictionary | Count / GetKeys |
| **键值** | `IMapRegistry<TKey, TValue>` | `MapRegistryBase<TKey, TValue>` | ✅ ConcurrentDictionary | RegisterRange / UnregisterRange / Count / GetValues / Filter / FilterEntries |
| **工厂** | `IFactoryRegistry<TKey, TValue>` | `FactoryRegistryBase<TKey, TValue>` | ✅ ConcurrentDictionary | Resolve / TryResolve / Count / GetKeys |
| **元数据** | `IMetaMapRegistry<TKey, TValue, TMeta>` | `MetaMapRegistryBase<TKey, TValue, TMeta>` | ✅ ConcurrentDictionary | TryGet / TryGetMetadata / Count / GetKeys |
| **阶段** | `IStageRegistry<TStage, TValue>` | `StageRegistryBase<TStage, TValue>` | ✅ ReaderWriterLockSlim | InsertAt / Replace / ReplaceAll / GetValues / GetStages |
| **图** | `IGraphRegistry<TNodeId, TNode, TEdge>` | `GraphRegistryBase<TNodeId, TNode, TEdge>` | ✅ ConcurrentDictionary | TopologicalSort / GetNode / GetAllNodes / GetAllEdges |
| **事件通知** | `INotifyRegistry<TKey, TValue>` | `ObservableMapRegistry<TKey, TValue>` | ✅ | 继承 MapRegistryBase 所有能力 + 事件通知 |

---

## 2. 目标架构

### 2.1 迁移后架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                    CommandFramework                              │
│                                                                  │
│  ┌──────────────────────┐   ┌────────────────────────────────┐  │
│  │   CommandRegistry     │   │   CommandEngine                │  │
│  │   (FactoryRegistry)   │   │   (MapRegistry + 历史栈)       │  │
│  └──────────┬───────────┘   └───────────────┬────────────────┘  │
│             │                               │                    │
│  ┌──────────┴───────────┐   ┌───────────────┴────────────────┐  │
│  │   FunctionRegistry    │   │   NonEventCommandHandler       │  │
│  │   (MetaMapRegistry)   │   │   (MapRegistry)                │  │
│  └──────────┬───────────┘   └───────────────┬────────────────┘  │
│             │                               │                    │
│  ┌──────────┴───────────┐   ┌───────────────┴────────────────┐  │
│  │   CommandParser       │   │   CommandFrameworkHost          │  │
│  │   (SetRegistry +      │   │   (统一入口，组合所有 Registry) │  │
│  │    MapRegistry)       │   └────────────────────────────────┘  │
│  └──────────────────────┘                                       │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │               Share.Registry (统一基类)                   │   │
│  │  ISetRegistry  │  IMapRegistry  │  IFactoryRegistry      │   │
│  │  IMetaMapRegistry │ INotifyRegistry                      │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 依赖关系

```
CommandFramework.csproj
  ├── Share/Pipeline/Pipeline.csproj          (已有)
  └── Share/Registry/Registry.csproj          (★ 新增依赖)
```

---

## 3. 迁移映射表

### 3.1 完整映射

| 现有类 | 当前存储 | 目标基类 | 变更类型 | 影响范围 |
|--------|---------|---------|---------|---------|
| `CommandRegistry` | `ConcurrentDictionary<string, Func<...>>` | `FactoryRegistryBase<string, ICommand>` | **继承替换** | 内部实现 |
| `CommandEngine._commands` | `Dictionary<string, CommandRegistration>` | `MapRegistryBase<string, CommandRegistration>` | **字段替换** | 内部实现 |
| `FunctionRegistry._functions` | `ConcurrentDictionary<string, RegisteredFunction>` | `MetaMapRegistryBase<string, Delegate, FunctionDescriptor>` | **继承替换** | 内部实现 |
| `NonEventCommandHandler._handlers` | `Dictionary<string, Func<...>>` | `MapRegistryBase<string, Func<...>>` | **字段替换** | 内部实现 |
| `CommandParser._nonEventCommands` | `HashSet<string>` | `SetRegistryBase<string>` | **字段替换** | 内部实现 |
| `CommandParser._normalizationMap` | `Dictionary<string, string>` | `MapRegistryBase<string, string>` | **字段替换** | 内部实现 |

### 3.2 方法映射

#### CommandRegistry → FactoryRegistryBase

| 当前方法 | 目标方法 | 备注 |
|---------|---------|------|
| `Register<T>()` | 继承后添加 | 便捷方法，通过类型名自动注册 |
| `Register<T>(string name)` | 继承后添加 | 便捷方法，指定名称注册类型 |
| `Register(string name, Func<...> factory)` | `Register(name, factory)` | 直接继承 |
| `Register(ICommand command)` | 继承后添加 | 便捷方法，注册实例 |
| `Resolve(string name, IServiceProvider?)` | `Resolve(name, sp?)` | 直接继承 |
| `IsRegistered(string name)` | `Contains(name)` | 直接继承 |
| `GetRegisteredCommands()` | `GetKeys()` | 直接继承 |
| `Count` | `Count` | 直接继承 |
| `Clear()` | `Clear()` | 直接继承 |
| `RegisterRange(...)` | `RegisterRange(...)` | 直接继承（MapRegistryBase 提供） |

#### CommandEngine._commands → MapRegistryBase

| 当前方法 | 目标方法 | 备注 |
|---------|---------|------|
| `RegisterCommand(name, execute, undo, desc)` | `Register(name, registration)` | 内部适配 |
| `RegisterCommand(ICommand command)` | `Register(name, registration)` | 内部适配 |
| `IsCommandRegistered(name)` | `Contains(name)` | 直接继承 |
| `GetRegisteredCommands()` | `GetKeys()` | 直接继承 |

#### FunctionRegistry → MetaMapRegistryBase

| 当前方法 | 目标方法 | 备注 |
|---------|---------|------|
| `Register(name, function, description?)` | `Register(name, function, descriptor)` | 继承后包装 |
| `Unregister(name)` | `Unregister(name)` | 直接继承 |
| `TryGetDescriptor(name, out descriptor)` | `TryGetMetadata(name, out descriptor)` | 直接继承 |
| `GetRegisteredNames()` | `GetKeys()` | 直接继承 |
| `Contains(name)` | `Contains(name)` | 直接继承 |
| `Clear()` | `Clear()` | 直接继承 |

#### NonEventCommandHandler._handlers → MapRegistryBase

| 当前方法 | 目标方法 | 备注 |
|---------|---------|------|
| `RegisterHandler(actionName, handler)` | `Register(name, handler)` | 直接继承 |
| `CanHandle(actionName)` | `Contains(name)` | 直接继承 |

#### CommandParser._nonEventCommands → SetRegistryBase

| 当前方法 | 目标方法 | 备注 |
|---------|---------|------|
| `RegisterNonEventCommand(name)` | `Register(name)` | 直接继承 |
| `IsNonEventCommand(verb, action)` | `Contains(name)` | 直接继承 |

#### CommandParser._normalizationMap → MapRegistryBase

| 当前方法 | 目标方法 | 备注 |
|---------|---------|------|
| `RegisterNormalization(shorthand, full)` | `Register(key, value)` | 直接继承 |
| `Normalize(command)` | `TryGet(key, out value)` | 直接继承 |

---

## 4. 详细迁移方案

### 4.1 Phase 1：CommandParser 迁移（低风险）

**文件：** `Share/CommandFramework/CommandParser.cs`

```csharp
// ★ 改造前
public class CommandParser : ICommandParser
{
    private readonly HashSet<string> _nonEventCommands;
    private readonly Dictionary<string, string> _normalizationMap;

    public CommandParser(
        HashSet<string>? nonEventCommands = null,
        Dictionary<string, string>? normalizationMap = null)
    {
        _nonEventCommands = nonEventCommands ?? new HashSet<string>(
            DefaultNonEventCommands, StringComparer.OrdinalIgnoreCase);
        _normalizationMap = normalizationMap ?? new Dictionary<string, string>(
            DefaultNormalizationMap, StringComparer.OrdinalIgnoreCase);
    }
}

// ★ 改造后
using Share.Registry;

public class CommandParser : ICommandParser
{
    private readonly ISetRegistry<string> _nonEventCommands;
    private readonly IMapRegistry<string, string> _normalizationMap;

    public CommandParser(
        ISetRegistry<string>? nonEventCommands = null,
        IMapRegistry<string, string>? normalizationMap = null)
    {
        _nonEventCommands = nonEventCommands ?? new SetRegistryBase<string>(
            StringComparer.OrdinalIgnoreCase);
        _normalizationMap = normalizationMap ?? new MapRegistryBase<string, string>(
            StringComparer.OrdinalIgnoreCase);
    }

    // 注册方法委托
    public void RegisterNonEventCommand(string commandName)
        => _nonEventCommands.Register(commandName);

    public void RegisterNormalization(string shorthand, string fullCommand)
        => _normalizationMap.Register(shorthand, fullCommand);

    // 查询方法委托
    public bool IsNonEventCommand(string verb, string? action)
    {
        if (string.IsNullOrEmpty(verb)) return false;
        if (string.Equals(verb, "do", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrEmpty(action) && _nonEventCommands.Contains(action);
        return _nonEventCommands.Contains(verb);
    }
}
```

**变更要点：**
- 构造函数参数类型从 `HashSet<string>` → `ISetRegistry<string>`，`Dictionary<string, string>` → `IMapRegistry<string, string>`
- 默认值从 `new HashSet<string>(...)` → `new SetRegistryBase<string>(...)`
- 方法实现委托给 Registry 接口
- 获得线程安全（`ConcurrentDictionary` 替代 `HashSet`/`Dictionary`）

---

### 4.2 Phase 2：NonEventCommandHandler 迁移（低风险）

**文件：** `Share/CommandFramework/Execution/CommandRouter.cs`

```csharp
// ★ 改造前
public class NonEventCommandHandler : INonEventCommandHandler
{
    private readonly Dictionary<string, Func<ParsedCommand, Task<CommandExecutionResult<object?>>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public void RegisterHandler(string actionName,
        Func<ParsedCommand, Task<CommandExecutionResult<object?>>> handler)
    {
        _handlers[actionName] = handler;
    }

    public bool CanHandle(string actionName)
        => !string.IsNullOrEmpty(actionName) && _handlers.ContainsKey(actionName);
}

// ★ 改造后
using Share.Registry;

public class NonEventCommandHandler : INonEventCommandHandler
{
    private readonly IMapRegistry<string, Func<ParsedCommand, Task<CommandExecutionResult<object?>>>> _handlers;

    public NonEventCommandHandler(
        IMapRegistry<string, Func<ParsedCommand, Task<CommandExecutionResult<object?>>>>? handlers = null)
    {
        _handlers = handlers ?? new MapRegistryBase<string,
            Func<ParsedCommand, Task<CommandExecutionResult<object?>>>>(
            StringComparer.OrdinalIgnoreCase);
    }

    public void RegisterHandler(string actionName,
        Func<ParsedCommand, Task<CommandExecutionResult<object?>>> handler)
        => _handlers.Register(actionName, handler);

    public bool CanHandle(string actionName)
        => !string.IsNullOrEmpty(actionName) && _handlers.Contains(actionName);
}
```

**变更要点：**
- 字段类型从 `Dictionary<...>` → `IMapRegistry<...>`
- 构造函数支持依赖注入 Registry 实例
- 获得线程安全 + 批量操作 + 查询过滤能力

---

### 4.3 Phase 3：CommandRegistry 迁移（中风险）

**文件：** `Share/CommandFramework/CommandRegistry.cs`

```csharp
// ★ 改造前
namespace CommandFramework.Registration;

public class CommandRegistry
{
    private readonly ConcurrentDictionary<string, Func<IServiceProvider?, ICommand>> _commandFactories
        = new(StringComparer.OrdinalIgnoreCase);

    public void Register<T>() where T : ICommand, new()
    {
        var name = typeof(T).FullName ?? typeof(T).Name;
        _commandFactories[name] = _ => new T();
    }

    public void Register<T>(string name) where T : ICommand, new()
        => _commandFactories[name] = _ => new T();

    public void Register(string name, Func<IServiceProvider?, ICommand> factory)
        => _commandFactories[name] = factory;

    public void Register(ICommand command)
        => _commandFactories[command.Name] = _ => command;

    public ICommand? Resolve(string name, IServiceProvider? serviceProvider = null)
    {
        if (_commandFactories.TryGetValue(name, out var factory))
            return factory(serviceProvider);
        return null;
    }

    public bool IsRegistered(string name) => _commandFactories.ContainsKey(name);
    public string[] GetRegisteredCommands() => _commandFactories.Keys.ToArray();
    public int Count => _commandFactories.Count;
    public void Clear() => _commandFactories.Clear();
}

// ★ 改造后
using Share.Registry;

namespace CommandFramework.Registration;

public class CommandRegistry : FactoryRegistryBase<string, ICommand>
{
    public CommandRegistry()
        : base(StringComparer.OrdinalIgnoreCase) { }

    // ★ 便捷方法 1：通过类型名自动注册
    public void Register<T>() where T : ICommand, new()
    {
        var name = typeof(T).FullName ?? typeof(T).Name;
        Register(name, _ => new T());
    }

    // ★ 便捷方法 2：指定名称注册类型
    public void Register<T>(string name) where T : ICommand, new()
        => Register(name, _ => new T());

    // ★ 便捷方法 3：注册实例（包装为工厂）
    public void Register(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("命令名称不能为空");
        Register(command.Name, _ => command);
    }

    // ★ 保持现有 API 签名不变
    public new ICommand? Resolve(string name, IServiceProvider? serviceProvider = null)
        => base.Resolve(name, serviceProvider);

    public bool IsRegistered(string name) => Contains(name);
    public string[] GetRegisteredCommands() => GetKeys().ToArray();
}
```

**变更要点：**
- 继承 `FactoryRegistryBase<string, ICommand>` 替代手动管理 `ConcurrentDictionary`
- 3 个便捷方法（`Register<T>()` / `Register<T>(name)` / `Register(ICommand)`）保留
- `IsRegistered` → `Contains`（保留别名保持 API 兼容）
- `GetRegisteredCommands()` → `GetKeys().ToArray()`（保留别名）
- 获得 `TryResolve` 安全解析方法

---

### 4.4 Phase 4：FunctionRegistry 迁移（中风险）

**文件：** `Share/CommandFramework/Execution/FunctionExecution.cs`

```csharp
// ★ 改造前
public class FunctionRegistry : IFunctionRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredFunction> _functions =
        new(StringComparer.OrdinalIgnoreCase);

    internal class RegisteredFunction
    {
        public Delegate Delegate { get; set; } = null!;
        public FunctionDescriptor Descriptor { get; set; } = null!;
    }

    public void Register(string name, Delegate function, string? description = null)
    {
        _functions[name] = new RegisteredFunction
        {
            Delegate = function,
            Descriptor = CreateDescriptor(name, function, description)
        };
    }

    public bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor)
    {
        if (_functions.TryGetValue(name, out var func))
        {
            descriptor = func.Descriptor;
            return true;
        }
        descriptor = null!;
        return false;
    }

    internal RegisteredFunction? GetFunction(string name)
        => _functions.TryGetValue(name, out var func) ? func : null;
}

// ★ 改造后
using Share.Registry;

public class FunctionRegistry : MetaMapRegistryBase<string, Delegate, FunctionDescriptor>,
    IFunctionRegistry
{
    public FunctionRegistry()
        : base(StringComparer.OrdinalIgnoreCase) { }

    // ★ 便捷注册方法（自动创建 FunctionDescriptor）
    public void Register(string name, Delegate function, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Function name cannot be empty", nameof(name));

        var descriptor = CreateDescriptor(name, function, description);
        Register(name, function, descriptor);
    }

    // ★ IFunctionRegistry 接口实现
    public new bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor)
    {
        if (TryGetMetadata(name, out FunctionDescriptor? meta))
        {
            descriptor = meta;
            return true;
        }
        descriptor = null!;
        return false;
    }

    public IEnumerable<string> GetRegisteredNames() => GetKeys();

    // ★ 内部使用：获取注册的函数（供 FunctionExecutor 使用）
    internal (Delegate? Delegate, FunctionDescriptor? Descriptor) GetFunction(string name)
    {
        if (TryGet(name, out var del, out var desc))
            return (del, desc);
        return (null, null);
    }
}
```

**变更要点：**
- 继承 `MetaMapRegistryBase<string, Delegate, FunctionDescriptor>` 替代手动管理 `ConcurrentDictionary`
- 内部类 `RegisteredFunction` 不再需要（由两个独立 Dictionary 替代）
- `Register` 方法包装为自动创建 `FunctionDescriptor`
- `TryGetDescriptor` 委托给 `TryGetMetadata`
- `GetFunction` 返回元组替代内部类

---

### 4.5 Phase 5：CommandEngine._commands 迁移（低风险）

**文件：** `Share/CommandFramework/Execution/CommandEngine.cs`

```csharp
// ★ 改造前
public class CommandEngine : ICommandEngine
{
    private readonly Dictionary<string, CommandRegistration> _commands =
        new(StringComparer.OrdinalIgnoreCase);

    private class CommandRegistration
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Func<CommandExecutionContext, CancellationToken, Task> Execute { get; set; } = null!;
        public Func<CommandExecutionContext, CancellationToken, Task>? Undo { get; set; }
    }
}

// ★ 改造后
using Share.Registry;

public class CommandEngine : ICommandEngine
{
    private readonly IMapRegistry<string, CommandRegistration> _commands;

    public CommandEngine(
        IEnvironmentAdapter? environmentAdapter = null,
        IControlValueAccessor? controlValueAccessor = null,
        IStateStore? stateStore = null,
        CommandRegistry? commandRegistry = null,
        IMapRegistry<string, CommandRegistration>? commandRegistry_ = null)  // ★ 新增参数
    {
        _commands = commandRegistry_ ?? new MapRegistryBase<string, CommandRegistration>(
            StringComparer.OrdinalIgnoreCase);
        // ... 其他初始化
    }

    public void RegisterCommand(string name,
        Func<CommandExecutionContext, CancellationToken, Task> execute,
        Func<CommandExecutionContext, CancellationToken, Task>? undo = null,
        string? description = null)
    {
        _commands.Register(name, new CommandRegistration
        {
            Name = name,
            Description = description,
            Execute = execute,
            Undo = undo
        });
    }

    public bool IsCommandRegistered(string name)
        => !string.IsNullOrWhiteSpace(name) && _commands.Contains(name);

    public string[] GetRegisteredCommands()
        => _commands.GetKeys().ToArray();
}
```

**变更要点：**
- 字段类型从 `Dictionary<...>` → `IMapRegistry<...>`
- 构造函数支持依赖注入 Registry 实例
- 获得线程安全 + 批量操作 + 查询过滤能力
- 内部类 `CommandRegistration` 保持不变

---

## 5. 接口兼容性分析

### 5.1 公共 API 兼容性

| 类 | 公共方法 | 变更 | 兼容性 |
|---|---------|------|-------|
| `CommandRegistry` | `Register<T>()` / `Register<T>(name)` / `Register(name, factory)` / `Register(ICommand)` / `Resolve` / `IsRegistered` / `GetRegisteredCommands` / `Count` / `Clear` / `RegisterRange` | 继承 `FactoryRegistryBase`，保留所有公共方法签名 | ✅ 完全向后兼容 |
| `CommandEngine` | `RegisterCommand(name, execute, undo, desc)` / `RegisterCommand(ICommand)` / `IsCommandRegistered` / `GetRegisteredCommands` | 内部存储替换，公共方法签名不变 | ✅ 完全向后兼容 |
| `FunctionRegistry` | `Register(name, function, desc)` / `Unregister` / `TryGetDescriptor` / `GetRegisteredNames` / `Contains` / `Clear` | 继承 `MetaMapRegistryBase`，保留所有公共方法签名 | ✅ 完全向后兼容 |
| `NonEventCommandHandler` | `RegisterHandler` / `CanHandle` / `HandleAsync` | 内部存储替换，公共方法签名不变 | ✅ 完全向后兼容 |
| `CommandParser` | `RegisterNonEventCommand` / `RegisterNormalization` / `IsNonEventCommand` / `Normalize` / `Parse` | 内部存储替换，公共方法签名不变 | ✅ 完全向后兼容 |

### 5.2 构造函数兼容性

| 类 | 当前构造函数 | 改造后构造函数 | 兼容性 |
|---|-------------|---------------|-------|
| `CommandParser` | `(HashSet?, Dictionary?)` | `(ISetRegistry?, IMapRegistry?)` | ⚠️ 参数类型变更，需更新调用方 |
| `NonEventCommandHandler` | `()` | `(IMapRegistry?)` | ✅ 新增可选参数，默认行为不变 |
| `CommandEngine` | `(env?, accessor?, store?, registry?)` | `(env?, accessor?, store?, registry?, mapReg?)` | ✅ 新增可选参数，默认行为不变 |
| `CommandRegistry` | `()` | `()` | ✅ 不变 |
| `FunctionRegistry` | `()` | `()` | ✅ 不变 |

**⚠️ CommandParser 构造函数变更说明：**
- 当前：`CommandParser(HashSet<string>? nonEventCommands, Dictionary<string, string>? normalizationMap)`
- 改造后：`CommandParser(ISetRegistry<string>? nonEventCommands, IMapRegistry<string, string>? normalizationMap)`
- 影响：所有直接调用 `new CommandParser(hashSet, dict)` 的地方需要改为 `new CommandParser(setReg, mapReg)`
- 缓解：提供静态工厂方法或重载，接受旧类型参数并自动转换

---

## 6. 实施步骤

### 6.1 前置条件

- [ ] 确认 `Share/Registry/Registry.csproj` 已编译通过
- [ ] 在 `CommandFramework.csproj` 中添加对 `Share/Registry` 的项目引用

### 6.2 实施路线图

```
Phase 1: 添加依赖（5 分钟）
  └─ CommandFramework.csproj 添加 <ProjectReference Include="..\Registry\Registry.csproj" />

Phase 2: CommandParser 迁移（30 分钟）— 低风险
  ├─ 替换 _nonEventCommands: HashSet → ISetRegistry<string>
  ├─ 替换 _normalizationMap: Dictionary → IMapRegistry<string, string>
  ├─ 更新构造函数参数类型
  └─ 更新 DefaultNonEventCommands / DefaultNormalizationMap 初始化方式

Phase 3: NonEventCommandHandler 迁移（20 分钟）— 低风险
  ├─ 替换 _handlers: Dictionary → IMapRegistry<...>
  └─ 更新构造函数支持依赖注入

Phase 4: CommandRegistry 迁移（30 分钟）— 中风险
  ├─ 改为继承 FactoryRegistryBase<string, ICommand>
  ├─ 保留 3 个便捷注册方法
  └─ 保留 IsRegistered / GetRegisteredCommands 别名

Phase 5: FunctionRegistry 迁移（30 分钟）— 中风险
  ├─ 改为继承 MetaMapRegistryBase<string, Delegate, FunctionDescriptor>
  ├─ 移除内部类 RegisteredFunction
  ├─ 更新 Register 方法包装
  └─ 更新 FunctionExecutor.GetFunction 调用

Phase 6: CommandEngine 迁移（20 分钟）— 低风险
  ├─ 替换 _commands: Dictionary → IMapRegistry<...>
  └─ 更新构造函数支持依赖注入

Phase 7: 编译验证与测试（30 分钟）
  ├─ 编译整个解决方案
  ├─ 运行现有单元测试
  └─ 修复编译错误和测试失败
```

### 6.3 预计总工时

| 阶段 | 工时 | 风险等级 |
|------|------|---------|
| Phase 1: 添加依赖 | 5 分钟 | 无风险 |
| Phase 2: CommandParser | 30 分钟 | 低 |
| Phase 3: NonEventCommandHandler | 20 分钟 | 低 |
| Phase 4: CommandRegistry | 30 分钟 | 中 |
| Phase 5: FunctionRegistry | 30 分钟 | 中 |
| Phase 6: CommandEngine | 20 分钟 | 低 |
| Phase 7: 编译验证 | 30 分钟 | — |
| **总计** | **约 2.5 小时** | |

---

## 7. 风险与注意事项

### 7.1 风险矩阵

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| `CommandParser` 构造函数参数类型变更导致编译错误 | **高** | 搜索所有 `new CommandParser(` 调用点，逐一更新 |
| `FunctionRegistry.GetFunction()` 返回类型变更 | **中** | 内部方法，影响 `FunctionExecutor`，同步更新即可 |
| `CommandRegistry` 继承后 `Resolve` 行为差异 | **低** | `FactoryRegistryBase.Resolve` 异常时返回 `default`，与当前行为一致 |
| 线程安全升级引入的竞态条件 | **低** | `ConcurrentDictionary` 保证原子操作，比 `Dictionary` 更安全 |
| 项目引用导致循环依赖 | **低** | `Registry.csproj` 无外部依赖，不会产生循环引用 |

### 7.2 回滚方案

如果迁移后出现问题，可以逐个文件回滚：

1. **单个文件回滚**：使用 git checkout 恢复单个文件
2. **整体回滚**：`git revert <commit-hash>`
3. **渐进式迁移**：每个 Phase 独立提交，便于定位问题

### 7.3 迁移后验证清单

- [ ] 所有 5 个注册子系统编译通过
- [ ] `CommandParser` 的 `Parse` / `Normalize` / `IsNonEventCommand` 行为不变
- [ ] `NonEventCommandHandler` 的 `HandleAsync` / `RegisterHandler` / `CanHandle` 行为不变
- [ ] `CommandRegistry` 的 `Resolve` / `Register<T>` / `Register(ICommand)` 行为不变
- [ ] `FunctionRegistry` 的 `Register` / `TryGetDescriptor` / `GetRegisteredNames` 行为不变
- [ ] `CommandEngine` 的 `RegisterCommand` / `ExecuteAsync` / `UndoAsync` / `RedoAsync` 行为不变
- [ ] `CommandFrameworkHost` 和 `CommandFrameworkUsage` 的公共 API 不变
- [ ] 所有现有单元测试通过

---

## 附录：文件变更清单

### 修改文件（6 个）

| 文件 | 变更内容 |
|------|---------|
| `Share/CommandFramework/CommandFramework.csproj` | 添加 `Share/Registry` 项目引用 |
| `Share/CommandFramework/CommandParser.cs` | `HashSet` → `ISetRegistry`，`Dictionary` → `IMapRegistry` |
| `Share/CommandFramework/Execution/CommandRouter.cs` | `Dictionary` → `IMapRegistry` |
| `Share/CommandFramework/CommandRegistry.cs` | 继承 `FactoryRegistryBase` |
| `Share/CommandFramework/Execution/FunctionExecution.cs` | 继承 `MetaMapRegistryBase` |
| `Share/CommandFramework/Execution/CommandEngine.cs` | `Dictionary` → `IMapRegistry` |

### 新增依赖

| 项目 | 引用方式 |
|------|---------|
| `Share/Registry/Registry.csproj` | `<ProjectReference Include="..\Registry\Registry.csproj" />` |

### 无需修改的文件

| 文件 | 原因 |
|------|------|
| `Share/CommandFramework/CommandFrameworkHost.cs` | 通过公共 API 调用，内部实现变更不影响 |
| `Share/CommandFramework/CommandFrameworkUsage.cs` | 通过 Host 公共 API 调用，不受影响 |
| `Share/CommandFramework/CommandFrameworkOptions.cs` | 配置选项不变 |
| `Share/CommandFramework/ICommand.cs` | 接口定义不变 |
| `Share/CommandFramework/Execution/CommandResults.cs` | 结果类型不变 |
| `Share/CommandFramework/Execution/InMemoryStateStore.cs` | 状态存储不变 |
| `Share/CommandFramework/Pipeline/PipelineEntry.cs` | 管线入口不变 |

---

> **文档结束**
>
> 本文档为使用 Share.Registry 重构 CommandFramework 注册方案的完整计划，
> 涵盖现状分析、目标架构、迁移映射、详细方案、接口兼容性、实施步骤和风险评估。
