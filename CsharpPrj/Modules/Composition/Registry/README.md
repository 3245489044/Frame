# Share.Registry — 通用注册表框架

## 概述

统一项目中散落的注册表实现，提供 **6 种注册表模式** + **事件通知** + **4 种通用注册表实现**：

| 模式 | 接口 | 基类 | 说明 |
|------|------|------|------|
| **集合** | `ISetRegistry<TKey>` | `SetRegistryBase<TKey>` | ★ 只有键没有值，适用于白名单/命令名集合 |
| **键值** | `IMapRegistry<TKey, TValue>` | `MapRegistryBase<TKey, TValue>` | 键值映射，支持批量操作和查询过滤 |
| **工厂** | `IFactoryRegistry<TKey, TValue>` | `FactoryRegistryBase<TKey, TValue>` | ★ 通过工厂委托延迟创建实例，支持 DI |
| **元数据** | `IMetaMapRegistry<TKey, TValue, TMeta>` | `MetaMapRegistryBase<TKey, TValue, TMeta>` | ★ 键值映射 + 附加元数据 |
| **阶段** | `IStageRegistry<TStage, TValue>` | `StageRegistryBase<TStage, TValue>` | 按阶段管理有序值列表 |
| **图** | `IGraphRegistry<TNodeId, TNode, TEdge>` | `GraphRegistryBase<TNodeId, TNode, TEdge>` | 管理节点和边的有向图 |
| **事件通知** | `INotifyRegistry<TKey, TValue>` | `ObservableMapRegistry<TKey, TValue>` | ★ 注册表变更时触发事件通知 |
| **函数描述符** | `IFunctionDescriptor` / `IFunctionRegistry` | `FunctionRegistry` | ★ 函数元数据描述与注册（从 CommandFramework 迁移） |
| **规范化** | `NormalizationRegistry` | — | ★ 简写→完整格式的规范化映射 |
| **扩展方法** | `SetRegistryExtensions` | — | ★ 集合注册表的批量操作扩展 |

## 项目结构

```
Share/Registry/
├── Registry.csproj
├── README.md
├── 01_Interfaces.cs              — 所有注册表接口定义（IRegistry / ISetRegistry / IMapRegistry / IFactoryRegistry / IMetaMapRegistry / IStageRegistry / IGraphRegistry / INotifyRegistry）
├── 02_RegistryBase.cs            — 注册表抽象基类（RegistryBase<TKey>，提取公共逻辑）
├── 03_MapBasedRegistries.cs      — 基于 ConcurrentDictionary 的注册表基类（SetRegistryBase / MapRegistryBase / FactoryRegistryBase / MetaMapRegistryBase）
├── 04_StageGraphRegistries.cs    — 阶段注册表 + 图注册表基类（StageRegistryBase / GraphRegistryBase）
├── 05_FunctionDescriptors.cs     — 函数描述符接口与实现（IFunctionDescriptor / FunctionDescriptor / IFunctionRegistry / IFunctionExecutor）
├── 06_ConcreteRegistries.cs      — 具体注册表实现（FunctionRegistry / NormalizationRegistry）
├── 07_Extensions.cs              — 扩展方法与可观察注册表（SetRegistryExtensions / ObservableMapRegistry）
└── _deprecated/                  — 旧文件备份（合并前的原始文件，不参与编译）
```

## 接口继承体系

```
IRegistry (Clear)
  │
  ├── ISetRegistry<TKey>                            — 纯集合
  ├── IMapRegistry<TKey, TValue>                    — 键值映射
  ├── IFactoryRegistry<TKey, TValue>                — 工厂模式
  ├── IMetaMapRegistry<TKey, TValue, TMeta>         — 带元数据的键值映射
  ├── IStageRegistry<TStage, TValue>                — 阶段管理
  └── IGraphRegistry<TNodeId, TNode, TEdge>         — 图结构

INotifyRegistry<TKey, TValue> : IRegistry           — 事件通知（可选组合）

IFunctionRegistry                                    — 函数注册表（独立接口）
IFunctionDescriptor / IParameterDescriptor           — 函数描述符（独立接口）
IFunctionExecutionResult / IFunctionExecutor         — 函数执行（独立接口）
```

## 基类内置能力

| 基类 | 内置能力（无需额外接口） |
|------|------------------------|
| `MapRegistryBase<TKey, TValue>` | `RegisterRange` / `UnregisterRange` / `Count` / `GetValues` / `Filter` / `FilterEntries` |
| `SetRegistryBase<TKey>` | `Count` / `GetKeys` |
| `FactoryRegistryBase<TKey, TValue>` | `Count` / `GetKeys` / `TryResolve` |
| `MetaMapRegistryBase<TKey, TValue, TMeta>` | `Count` / `GetKeys` |
| `ObservableMapRegistry<TKey, TValue>` | 继承 MapRegistryBase 所有能力 + 事件通知 |
| `FunctionRegistry` | 继承 MetaMapRegistryBase 所有能力 + `Register` / `TryGetDescriptor` |
| `NormalizationRegistry` | 继承 MapRegistryBase 所有能力 |

## 使用示例

### 模式选择决策树

```
需要注册什么？
  │
  ├─ 只有键，没有值 → ISetRegistry<TKey>
  │    例：命令名白名单、权限列表
  │
  ├─ 键 → 值（一对一映射）
  │   ├─ 值需要延迟创建（工厂模式）→ IFactoryRegistry<TKey, TValue>
  │   │    例：命令工厂、插件工厂
  │   ├─ 值需要附加元数据 → IMetaMapRegistry<TKey, TValue, TMeta>
  │   │    例：函数注册表（值=Delegate，元数据=FunctionDescriptor）
  │   └─ 普通键值映射 → IMapRegistry<TKey, TValue>
  │        例：配置映射、处理器注册表
  │
  ├─ 阶段 → 有序值列表 → IStageRegistry<TStage, TValue>
  │    例：管线阶段、中间件链
  │
  └─ 节点 + 边（图结构）→ IGraphRegistry<TNodeId, TNode, TEdge>
       例：工作流、依赖图
```

### 1. 集合注册表（Set）

```csharp
// 直接使用
var set = new SetRegistryBase<string>(StringComparer.OrdinalIgnoreCase);
set.Register("command1");
set.Register("command2");
if (set.Contains("command1")) { ... }
int count = set.Count;

// 继承扩展
public class NonEventCommandSet : SetRegistryBase<string>
{
    public NonEventCommandSet()
        : base(StringComparer.OrdinalIgnoreCase) { }
}
```

### 2. 键值注册表（Map）

```csharp
// 直接使用
var registry = new MapRegistryBase<string, IMyInterface>();
registry.Register("key1", new MyImpl());
if (registry.TryGet("key1", out var value)) { ... }

// 批量操作
registry.RegisterRange(new[] {
    new KeyValuePair<string, IMyInterface>("k1", v1),
    new KeyValuePair<string, IMyInterface>("k2", v2),
});

// 查询过滤
var allValues = registry.GetValues();
var filtered = registry.Filter(v => v.IsActive);
var entries = registry.FilterEntries((k, v) => k.StartsWith("test"));

// 继承扩展
public class MyRegistry : MapRegistryBase<string, IMyInterface>
{
    protected override void OnBeforeRegister(string key, IMyInterface value)
    {
        // 注册前逻辑（如事件订阅）
    }
}
```

### 3. 工厂注册表（Factory）

```csharp
// 直接使用
var factory = new FactoryRegistryBase<string, ICommand>();
factory.Register("cmd1", sp => new MyCommand(sp?.GetService<IDependency>()));

// 解析实例（延迟创建）
var cmd = factory.Resolve("cmd1", serviceProvider);

// 安全解析（异常时返回 false）
if (factory.TryResolve("cmd1", out var cmd2, serviceProvider)) { ... }

// 继承扩展
public class CommandRegistry : FactoryRegistryBase<string, ICommand>
{
    public CommandRegistry()
        : base(StringComparer.OrdinalIgnoreCase) { }
}
```

### 4. 带元数据的键值映射（MetaMap）

```csharp
// 直接使用
var meta = new MetaMapRegistryBase<string, Delegate, FunctionDescriptor>();
meta.Register("func1", someDelegate, new FunctionDescriptor { ... });

// 获取值和元数据
if (meta.TryGet("func1", out var del, out var descriptor)) { ... }
if (meta.TryGetMetadata("func1", out var metaOnly)) { ... }

// 继承扩展
public class FunctionRegistry
    : MetaMapRegistryBase<string, Delegate, FunctionDescriptor>
{
    public FunctionRegistry()
        : base(StringComparer.OrdinalIgnoreCase) { }

    public void Register(string name, Delegate function, string? description = null)
    {
        var descriptor = FunctionDescriptor.FromDelegate(function, description);
        Register(name, function, descriptor);
    }

    public bool TryGetDescriptor(string name, out FunctionDescriptor? descriptor)
        => TryGetMetadata(name, out descriptor);
}
```

### 5. 阶段注册表（Stage）

```csharp
var registry = new StageRegistryBase<MyStage, IMyPipeline>();
registry.Register(MyStage.Initialize, pipeline1);
registry.Register(MyStage.Process, pipeline2);
registry.InsertAt(MyStage.Process, 0, pipeline3); // 插入到首位

var pipelines = registry.GetValues(MyStage.Process); // 原子快照
```

### 6. 图注册表（Graph）

```csharp
var registry = new GraphRegistryBase<string, MyNode, MyEdge>();
registry.RegisterNode("A", nodeA);
registry.RegisterNode("B", nodeB);
registry.RegisterEdge("A", "B", edgeAB);

var sorted = registry.TopologicalSort("A"); // DFS 拓扑排序（含循环检测）
```

### 7. 可观察的键值映射（Observable）

```csharp
public class MyHandlerRegistry
    : ObservableMapRegistry<string, IHandler>
{
    // 直接继承，批量操作和查询过滤方法开箱即用
}

// 使用
var registry = new MyHandlerRegistry();
registry.ItemRegistered += (sender, args) =>
    Console.WriteLine($"Registered: {args.Key}");
registry.ItemUpdated += (sender, args) =>
    Console.WriteLine($"Updated: {args.Key}");
registry.ItemUnregistered += (sender, args) =>
    Console.WriteLine($"Unregistered: {args.Key}");
```

### 8. 函数注册表（FunctionRegistry）

```csharp
// 直接使用
var registry = new FunctionRegistry();
registry.Register("add", (int a, int b) => a + b, "Add two numbers");
registry.Register("greet", (string name) => $"Hello, {name}!");

// 获取描述符
if (registry.TryGetDescriptor("add", out var descriptor))
{
    Console.WriteLine($"Function: {descriptor.Name}");
    Console.WriteLine($"Parameters: {string.Join(", ", descriptor.Parameters.Select(p => p.Name))}");
    Console.WriteLine($"Return: {descriptor.ReturnType.Name}");
}

// 获取委托
if (registry.TryGet("add", out var del, out var meta))
{
    var result = del.DynamicInvoke(3, 4);
    Console.WriteLine($"3 + 4 = {result}");
}
```

### 9. 规范化注册表（NormalizationRegistry）

```csharp
// 简写→完整格式映射
var norm = new NormalizationRegistry();
norm.Register("cmd", "command");
norm.Register("cfg", "configure");
norm.Register("dbg", "debug");

// 查找完整格式（使用基类 TryGet）
if (norm.TryGet("cmd", out var full))
    Console.WriteLine(full); // "command"

// 批量注册
norm.RegisterRange(new[]
{
    new KeyValuePair<string, string>("init", "initialize"),
    new KeyValuePair<string, string>("del", "delete"),
});
```

### 10. 集合注册表扩展方法

```csharp
var set = new SetRegistryBase<string>();
set.RegisterRange(new[] { "cmd1", "cmd2", "cmd3" });
// 等价于：
// set.Register("cmd1"); set.Register("cmd2"); set.Register("cmd3");

var keys = set.GetKeysArray(); // ["cmd1", "cmd2", "cmd3"]

set.UnregisterRange(new[] { "cmd2" });
// 等价于：set.Unregister("cmd2");
```

## 迁移指南

### 从 CommandFramework.Registration 迁移

```csharp
// 改造前 — 使用 CommandFramework 的注册表
using CommandFramework.Registration;

public class MyService
{
    private readonly FunctionRegistry _functions;
    private readonly HandlerRegistry _handlers;
    private readonly NormalizationMapRegistry _normalization;
}

// 改造后 — 使用 Share.Registry 的通用注册表
using Share.Registry;

public class MyService
{
    private readonly FunctionRegistry _functions;      // 同名，命名空间不同
    private readonly IMapRegistry<string, Delegate> _handlers;  // 使用 IMapRegistry 替代已删除的 HandlerRegistry
    private readonly NormalizationRegistry _normalization;        // 同名，命名空间不同
}
```

### 从 Dictionary/HashSet 迁移

```csharp
// 改造前 — 使用 HashSet + Dictionary 手动管理
public class CommandParser
{
    private readonly HashSet<string> _nonEventCommands;
    private readonly Dictionary<string, string> _normalizationMap;
}

// 改造后 — 使用 Registry 基类
public class CommandParser
{
    private readonly ISetRegistry<string> _nonEventCommands;
    private readonly IMapRegistry<string, string> _normalizationMap;

    public CommandParser(
        ISetRegistry<string>? nonEventCommands = null,
        IMapRegistry<string, string>? normalizationMap = null)
    {
        _nonEventCommands = nonEventCommands
            ?? new SetRegistryBase<string>(StringComparer.OrdinalIgnoreCase);
        _normalizationMap = normalizationMap
            ?? new MapRegistryBase<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
```

### 从 ConcurrentDictionary 迁移

```csharp
// 改造前 — 手动管理工厂字典
public class CommandRegistry
{
    private readonly ConcurrentDictionary<string, Func<IServiceProvider?, ICommand>> _factories;
}

// 改造后 — 使用 FactoryRegistryBase
public class CommandRegistry : FactoryRegistryBase<string, ICommand>
{
    public CommandRegistry()
        : base(StringComparer.OrdinalIgnoreCase) { }
    // 继承所有方法：Register / Resolve / Contains / GetKeys / Count
}
```

### 显式接口实现（不破坏现有 API）

```csharp
// 现有类保持原有方法不变，新增接口实现
public class FunctionRegistry : IFunctionRegistry, IMapRegistry<string, Delegate>
{
    void IMapRegistry<string, Delegate>.Register(string key, Delegate value) => Register(key, value);
    bool IMapRegistry<string, Delegate>.TryGet(string key, out Delegate? value) { ... }
    // ...
}
```
