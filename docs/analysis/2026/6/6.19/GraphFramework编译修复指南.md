# GraphFramework 编译错误修复指南

> **目标**：根据底层库（ECS.Core、GraphFoundation、SkeletonFlow）的实际 API，重构 GraphFramework 中的代码以修复所有 40 个编译错误。
> **原则**：不添加适配层，直接根据新 API 重构。

---

## 一、底层 API 现状速查

### 1.1 ECS.Core (BasicMethod/ECS/ECSFrame.cs)

| API | 实际签名 | 说明 |
|-----|---------|------|
| `SystemBase` | `EntityManager` 属性 + `Initialize(EntityManager em)` | **没有** `World` 属性 |
| `EntityManager.AddComponent` | `AddComponent<T>(Entity, T)` where T : struct, IComponent | 只有泛型版本 |
| `EntityManager.RemoveComponent` | `RemoveComponent<T>(Entity)` where T : struct, IComponent | 只有泛型版本 |
| `EntityManager.SetComponent` | `SetComponent<T>(Entity, T)` where T : struct, IComponent | 只有泛型版本 |
| `EntityManager.TryGetComponent` | `TryGetComponent<T>(Entity, out T)` where T : struct, IComponent | 只有泛型版本 |
| `EntityManager.HasComponent` | `HasComponent<T>(Entity)` + `HasComponent(Entity, Type)` | 有泛型和非泛型两个重载 |
| `EntityManager.GetComponent` | `GetComponent<T>(Entity)` where T : struct, IComponent | 只有泛型版本 |
| `EntityManager.GetAllEntities` | `GetAllEntities()` | 返回 `IEnumerable<Entity>` |
| `EntityManager.GetComponentTypes` | **不存在** | 无此方法 |
| `EcsWorld` | `EntityManager` 属性 + `SystemManager` 属性 | 无其他特殊方法 |

### 1.2 GraphFoundation (BasicMethod/GraphFoundation/)

| API | 实际类型 | 说明 |
|-----|---------|------|
| `GraphFoundation.Core.Graph<,,>` | **不存在**，实际是 `CoreGraph<,,>` | 类名已变更 |
| `GraphStatistics.Compute()` | 返回 `Stats` 内部类 | 统计属性在 `Stats` 类中 |
| `IGraphStorage.TryUpdateNode` | `TryUpdateNode(TKey, TNodeData, string?, out TNodeData?, out string?)` | 需要 5 个参数 |

### 1.3 SkeletonFlow (BasicMethod/FrameMethod/SkeletonFlow.cs)

| API | 实际方法 | 说明 |
|-----|---------|------|
| `SkeletonContext` | 有 `SetItem(key, value)` / `RemoveItem(key)` / `Items` 属性 | **没有** `GetPlaceholder`/`SetPlaceholder` |

---

## 二、错误分组与修复步骤

### 步骤 1：GraphKeyScenarios.cs — Graph → CoreGraph（4 个错误）

**错误**：`CS0234` — 命名空间 `GraphFoundation.Core` 中不存在 `Graph<,,>`

**根因**：类名已从 `Graph` 变更为 `CoreGraph`

**修改**：将 4 处 `new GraphFoundation.Core.Graph<...>()` 改为 `new GraphFoundation.Core.CoreGraph<...>()`

```csharp
// 修改前
var graph = new GraphFoundation.Core.Graph<string, object, object>();

// 修改后
var graph = new GraphFoundation.Core.CoreGraph<string, object, object>();
```

涉及行：22, 36, 50, 67

---

### 步骤 2：EcsRelationManager.cs — 变量名冲突（1 个错误）

**错误**：`CS0136` — 无法在此范围中声明名为 `toSet` 的局部变量

**根因**：第 165 行 `foreach` 内部声明的 `toSet` 与外层第 113 行的 `toSet` 冲突

**修改**：将第 165 行的 `toSet` 重命名为 `toSetForTarget`

```csharp
// 修改前（第 165 行）
if (_toIndex.TryGetValue(entry.Target, out var toSet))

// 修改后
if (_toIndex.TryGetValue(entry.Target, out var toSetForTarget))
```

---

### 步骤 3：GraphSystemRunner.cs — system.World → system.Initialize（2 个错误）

**错误**：`CS1061` — `SystemBase` 未包含 `World` 的定义

**根因**：`ECS.Core.SystemBase` 没有 `World` 属性，只有 `EntityManager` 属性和 `Initialize(EntityManager)` 方法

**修改**：将 `system.World = World` 改为 `system.Initialize(World.EntityManager)`

```csharp
// 修改前（第 51 行和第 70 行）
system.World = World;

// 修改后
system.Initialize(World.EntityManager);
```

---

### 步骤 4：NormalizationSystem.cs — World → EntityManager（2 个错误）

**错误**：`CS0103` — 当前上下文中不存在名称 `World`

**根因**：`SystemBase` 没有 `World` 属性，应直接使用 `EntityManager`

**修改**：将 `World.EntityManager` 改为 `EntityManager`

```csharp
// 修改前（第 91-92 行）
var entity = World.EntityManager.CreateEntity();
World.EntityManager.AddComponent(entity, resultComponent);

// 修改后
var entity = EntityManager.CreateEntity();
EntityManager.AddComponent(entity, resultComponent);
```

---

### 步骤 5：GraphComponentStore.cs — 补全 TryUpdateNode 参数（1 个错误）

**错误**：`CS7036` — 未提供与所需参数 `oldData` 对应的参数

**根因**：`IGraphStorage.TryUpdateNode` 需要 5 个参数，但调用只传了 3 个

**修改**：补全 `out` 参数

```csharp
// 修改前（第 71 行）
_storage.TryUpdateNode(key, nodeComp.Data, null);

// 修改后
_storage.TryUpdateNode(key, nodeComp.Data, null, out var oldData, out var oldSymbol);
```

---

### 步骤 6：StatisticsSystem.cs — 修正 GraphStatistics 调用（8 个错误）

**错误**：`CS1061` — `GraphStatistics` 未包含 `NodeCount`/`EdgeCount`/`AvgDegree` 等属性

**根因**：`GraphStatistics.Compute()` 返回 `Stats` 内部类，统计属性在 `Stats` 类中，不在 `GraphStatistics` 上

**修改**：先调用 `Compute()` 获取 `Stats` 对象，再访问其属性

```csharp
// 修改前（第 55-69 行）
var stats = new GraphStatistics<TKey, TNodeData, TEdgeData>(_graph);
stats.Compute();

new GraphStatsComponent
{
    NodeCount = stats.NodeCount,
    EdgeCount = stats.EdgeCount,
    AvgOutDegree = stats.AvgDegree,
    AvgInDegree = stats.AvgDegree,
    AvgWeight = stats.AvgWeight,
    IsolatedNodeCount = stats.IsolatedNodeCount,
    ConnectedComponentCount = stats.ConnectedComponents,
    HasCycle = stats.HasCycle
};

// 修改后
var stats = new GraphStatistics<TKey, TNodeData, TEdgeData>(_graph);
var result = stats.Compute();

new GraphStatsComponent
{
    NodeCount = result.NodeCount,
    EdgeCount = result.EdgeCount,
    AvgOutDegree = result.AvgOutDegree,
    AvgInDegree = result.AvgInDegree,
    AvgWeight = result.AvgWeight,
    IsolatedNodeCount = result.IsolatedNodeCount,
    ConnectedComponentCount = result.ConnectedComponentCount,
    HasCycle = result.HasCycle
};
```

---

### 步骤 7：GraphKey.cs — 在 GraphBuildContext 中实现 GetPlaceholder/SetPlaceholder（4 个错误）

**错误**：`CS1061` — `SkeletonContext` 未包含 `GetPlaceholder`/`SetPlaceholder` 的定义

**根因**：`SkeletonFlow.SkeletonContext` 没有 `GetPlaceholder`/`SetPlaceholder` 方法

**修改方案**：`GraphBuildContext` 已经包装了 `SkeletonContext`，可以直接在 `GraphBuildContext` 中实现这些方法，基于 `_skeletonContext.Items` 字典操作。

`GraphBuildContext` 中已有（第 1736-1744 行）：
```csharp
public Placeholder GetPlaceholder(string key) => _skeletonContext.GetPlaceholder(key);
public void SetPlaceholder(string key, Placeholder placeholder) => _skeletonContext.SetPlaceholder(key, placeholder);
```

**修改为**：
```csharp
public Placeholder GetPlaceholder(string key)
{
    if (_skeletonContext.Items.TryGetValue(key, out var value) && value is Placeholder placeholder)
        return placeholder;
    throw new KeyNotFoundException($"Placeholder '{key}' not found in context.");
}

public void SetPlaceholder(string key, Placeholder placeholder)
{
    _skeletonContext.SetItem(key, placeholder);
}
```

同时，`GraphComponentSkeleton` 中（第 2113, 2121 行）也直接调用了 `ctx.GetPlaceholder` 和 `ctx.SetPlaceholder`（`ctx` 是 `SkeletonContext` 类型），需要改为通过 `GraphBuildContext` 调用：

```csharp
// 修改前（第 2108-2121 行）
var buildContext = new GraphBuildContext(ctx);

foreach (var key in _component.InputKeys)
{
    _ = ctx.GetPlaceholder(key);  // 直接调用 SkeletonContext
}

var outputs = _component.Build(buildContext);

foreach (var kv in outputs)
    ctx.SetPlaceholder(kv.Key, kv.Value);  // 直接调用 SkeletonContext

// 修改后
var buildContext = new GraphBuildContext(ctx);

foreach (var key in _component.InputKeys)
{
    _ = buildContext.GetPlaceholder(key);  // 通过 GraphBuildContext
}

var outputs = _component.Build(buildContext);

foreach (var kv in outputs)
    buildContext.SetPlaceholder(kv.Key, kv.Value);  // 通过 GraphBuildContext
```

---

### 步骤 8：EcsIndexManager.cs — 修正 TryGetComponent 调用（1 个错误）

**错误**：`CS1501` — `TryGetComponent` 方法没有采用 3 个参数的重载

**根因**：`EntityManager.TryGetComponent` 只有泛型版本 `TryGetComponent<T>(Entity, out T)`，没有 `(Entity, Type, out object)` 重载

**修改**：使用 `HasComponent(Entity, Type)` 检查后，通过反射调用泛型 `TryGetComponent<T>`

```csharp
// 修改前（第 280 行）
if (!_entityManager.TryGetComponent(entity, definition.ComponentType, out var component))
    return;

// 修改后
if (!_entityManager.HasComponent(entity, definition.ComponentType))
    return;

// 通过反射调用泛型 TryGetComponent<T>
var tryGetMethod = typeof(EntityManager).GetMethod("TryGetComponent")?
    .MakeGenericMethod(definition.ComponentType);
var parameters = new object[] { entity, null! };
if (!(bool)tryGetMethod!.Invoke(_entityManager, parameters)!)
    return;
var component = parameters[1];
```

---

### 步骤 9：EcsTransaction.cs — 反射调用泛型 ECS 方法（11 个错误）

**错误**：多个 `CS1061`/`CS1501`/`CS0453`/`CS1503` — ECS API 不匹配

**根因**：`EntityManager` 的所有组件操作方法都是泛型的（`AddComponent<T>`、`RemoveComponent<T>`、`TryGetComponent<T>`、`SetComponent<T>`），且要求 `T : struct, IComponent`。但 `EcsTransaction` 中的命令类在运行时才能确定组件类型，无法在编译时使用泛型。

**修改方案**：在 `EcsTransaction.cs` 中添加一个反射辅助类，通过反射调用泛型方法。

#### 9.1 添加反射辅助类

在 `EcsTransaction.cs` 文件内（或在 `Ecs/` 目录下新建文件）添加：

```csharp
/// <summary>
/// ECS 反射辅助 — 通过反射调用 EntityManager 的泛型方法
/// </summary>
internal static class EcsReflectionHelper
{
    private static readonly MethodInfo _addComponentMethod = typeof(EntityManager)
        .GetMethod("AddComponent", new[] { typeof(Entity), typeof(IComponent).MakeByRefType() })!;
    // 注意：实际需要获取正确的 MethodInfo，因为泛型方法需要 MakeGenericMethod

    private static MethodInfo GetGenericMethod(string name, Type[] parameterTypes)
    {
        return typeof(EntityManager).GetMethods()
            .First(m => m.Name == name && m.IsGenericMethodDefinition
                && m.GetParameters().Length == parameterTypes.Length
                && m.GetParameters().Select((p, i) => p.ParameterType.IsGenericParameter || p.ParameterType == parameterTypes[i]).All(x => x));
    }

    public static void AddComponent(EntityManager em, Entity entity, object component)
    {
        var componentType = component.GetType();
        var method = GetGenericMethod("AddComponent", new[] { typeof(Entity), componentType });
        var generic = method.MakeGenericMethod(componentType);
        generic.Invoke(em, new[] { entity, component });
    }

    public static void RemoveComponent(EntityManager em, Entity entity, Type componentType)
    {
        var method = typeof(EntityManager).GetMethod("RemoveComponent")!
            .MakeGenericMethod(componentType);
        method.Invoke(em, new[] { entity });
    }

    public static bool TryGetComponent(EntityManager em, Entity entity, Type componentType, out object? component)
    {
        var method = typeof(EntityManager).GetMethod("TryGetComponent")!
            .MakeGenericMethod(componentType);
        var parameters = new object[] { entity, null! };
        var result = (bool)method.Invoke(em, parameters)!;
        component = result ? parameters[1] : null;
        return result;
    }

    public static void SetComponent(EntityManager em, Entity entity, object component)
    {
        var componentType = component.GetType();
        var method = typeof(EntityManager).GetMethod("SetComponent")!
            .MakeGenericMethod(componentType);
        method.Invoke(em, new[] { entity, component });
    }

    public static List<(Type type, object component)> GetComponentTypes(EntityManager em, Entity entity)
    {
        // 通过反射获取实体的所有组件
        var result = new List<(Type, object)>();
        // 由于 EntityManager 没有公开 GetComponentTypes，需要遍历所有已注册的组件类型
        var componentArraysField = typeof(EntityManager)
            .GetField("_componentArrays", BindingFlags.NonPublic | BindingFlags.Instance);
        if (componentArraysField?.GetValue(em) is Dictionary<Type, IComponentArray> arrays)
        {
            foreach (var kvp in arrays)
            {
                if (TryGetComponent(em, entity, kvp.Key, out var component) && component != null)
                {
                    result.Add((kvp.Key, component));
                }
            }
        }
        return result;
    }
}
```

#### 9.2 修改 DestroyEntityCommand

```csharp
// 修改前（第 60, 70-76 行）
private readonly List<(Type type, object component)> _savedComponents = new();

public void Execute(EcsWorld world)
{
    foreach (var componentType in world.EntityManager.GetComponentTypes(_entity))
    {
        if (world.EntityManager.TryGetComponent(_entity, componentType, out var component))
        {
            _savedComponents.Add((componentType, component));
        }
    }
    world.EntityManager.DestroyEntity(_entity);
}

// 修改后
private readonly List<(Type type, object component)> _savedComponents = new();

public void Execute(EcsWorld world)
{
    _savedComponents.Clear();
    var saved = EcsReflectionHelper.GetComponentTypes(world.EntityManager, _entity);
    _savedComponents.AddRange(saved);
    world.EntityManager.DestroyEntity(_entity);
}
```

#### 9.3 修改 AddComponentCommand

```csharp
// 修改前（第 111 行）
world.EntityManager.AddComponent(_entity, _component);

// 修改后
EcsReflectionHelper.AddComponent(world.EntityManager, _entity, _component);
```

#### 9.4 修改 RemoveComponentCommand

```csharp
// 修改前（第 139, 142 行）
if (world.EntityManager.TryGetComponent(_entity, _componentType, out var component))
{
    _savedComponent = component;
    world.EntityManager.RemoveComponent(_entity, _componentType);
}

// 修改后
if (EcsReflectionHelper.TryGetComponent(world.EntityManager, _entity, _componentType, out var component))
{
    _savedComponent = component;
    EcsReflectionHelper.RemoveComponent(world.EntityManager, _entity, _componentType);
}
```

#### 9.5 修改 SetComponentCommand

```csharp
// 修改前（第 175, 179, 186, 190 行）
if (world.EntityManager.TryGetComponent(_entity, componentType, out var oldComponent))
{
    _oldComponent = oldComponent;
}
world.EntityManager.SetComponent(_entity, _newComponent);

// ... Undo 中 ...
world.EntityManager.SetComponent(_entity, _oldComponent);
// ...
world.EntityManager.RemoveComponent(_entity, _newComponent.GetType());

// 修改后
if (EcsReflectionHelper.TryGetComponent(world.EntityManager, _entity, componentType, out var oldComponent))
{
    _oldComponent = oldComponent;
}
EcsReflectionHelper.SetComponent(world.EntityManager, _entity, _newComponent);

// ... Undo 中 ...
EcsReflectionHelper.SetComponent(world.EntityManager, _entity, _oldComponent);
// ...
EcsReflectionHelper.RemoveComponent(world.EntityManager, _entity, _newComponent.GetType());
```

#### 9.6 修改 Undo 中的 AddComponent

```csharp
// 修改前（第 88, 150 行）
world.EntityManager.AddComponent(newEntity, component);

// 修改后
EcsReflectionHelper.AddComponent(world.EntityManager, newEntity, component);
```

---

### 步骤 10：NormalizationService.cs — 修复 PipelineStep 类型（1 个错误）

**错误**：`CS0246` — 未能找到类型或命名空间名 `PipelineStep`

**根因**：`PipelineStep.cs` 中只有 `IPipelineStep` 接口和 `PipelineStepBase` 抽象类，没有名为 `PipelineStep` 的具体类

**修改**：`EcsPipeline.AddStep` 接受 `IPipelineStep` 参数，所以需要创建一个实现 `IPipelineStep` 的匿名类或使用 `PipelineStepBase` 派生类

```csharp
// 修改前（第 82-88 行）
pipeline.AddStep(new PipelineStep(
    "Normalization",
    () =>
    {
        system.Update(0);
        return true;
    }));

// 修改后
pipeline.AddStep(new NormalizationPipelineStep<TKey, TNodeData, TEdgeData>(system));

// 在文件末尾或单独文件中添加
internal class NormalizationPipelineStep<TKey, TNodeData, TEdgeData> : PipelineStepBase
    where TKey : notnull
    where TNodeData : class
{
    private readonly NormalizationSystem<TKey, TNodeData, TEdgeData> _system;

    public NormalizationPipelineStep(NormalizationSystem<TKey, TNodeData, TEdgeData> system)
    {
        _system = system;
    }

    public override string Name => "Normalization";

    public override void Execute(EcsWorld world)
    {
        _system.Update(0);
    }
}
```

---

### 步骤 11：GraphFrameworkModule.cs — 适配 GraphSystemRunner API（4 个错误）

**错误**：
- `CS1729` — `GraphSystemRunner` 不包含采用 3 个参数的构造函数
- `CS1061` — `GraphSystemRunner` 未包含 `Initialize`/`Adapter`/`Graph` 的定义

**根因**：`GraphFrameworkModule` 假设 `GraphSystemRunner` 有旧版 API（3 参数构造函数 + `Initialize()` + `Adapter` + `Graph` 属性），但实际新版 `GraphSystemRunner` 只有 `EcsWorld?` 一个可选参数，且没有这些成员。

**修改**：重构 `GraphFrameworkModule` 以适配新版 API

#### 11.1 修改 CreateRunner 方法

```csharp
// 修改前（第 37-40 行）
public static GraphSystemRunner<TKey, TNodeData, TEdgeData> CreateRunner<TKey, TNodeData, TEdgeData>(
    ICoreGraph<TKey, TNodeData, TEdgeData> graph,
    Func<TKey, string>? keyToString = null,
    Func<string, TKey>? stringToKey = null)
    where TKey : notnull
    where TNodeData : class
{
    var runner = new GraphSystemRunner<TKey, TNodeData, TEdgeData>(graph, keyToString, stringToKey);
    runner.Initialize();
    return runner;
}

// 修改后
public static GraphSystemRunner<TKey, TNodeData, TEdgeData> CreateRunner<TKey, TNodeData, TEdgeData>(
    ICoreGraph<TKey, TNodeData, TEdgeData> graph,
    Func<TKey, string>? keyToString = null,
    Func<string, TKey>? stringToKey = null)
    where TKey : notnull
    where TNodeData : class
{
    var runner = new GraphSystemRunner<TKey, TNodeData, TEdgeData>();
    return runner;
}
```

#### 11.2 修改 CreateRunnerWithDefaults 方法

```csharp
// 修改前（第 58-59 行）
runner.Register(new TopologySystem<TKey, TNodeData, TEdgeData>(
    graph, runner.Adapter.EntityMap, keyToString));

// 修改后 — 需要创建 EntityMap 和 GraphEntityAdapter
var entityManager = runner.World.EntityManager;
var entityMap = new EntityMap<TKey>(keyToString);
var adapter = new GraphEntityAdapter<TKey, TNodeData, TEdgeData>(graph, entityManager, keyToString);

runner.Register(new TopologySystem<TKey, TNodeData, TEdgeData>(
    graph, entityMap, keyToString));
```

#### 11.3 修改 RegisterOrchestrationSystem 扩展方法

```csharp
// 修改前（第 214-215 行）
var system = new OrchestrationSystem<TKey, TNodeData, TEdgeData>(
    runner.Graph, template, chainCount, nodesPerChain,
    keyGenerator, keyExtractor, setPosition, cloner);

// 修改后 — 需要将 graph 作为参数传入
// 此方法需要额外参数，修改签名
public static GraphSystemRunner<TKey, TNodeData, TEdgeData> RegisterOrchestrationSystem<TKey, TNodeData, TEdgeData>(
    this GraphSystemRunner<TKey, TNodeData, TEdgeData> runner,
    ICoreGraph<TKey, TNodeData, TEdgeData> graph,  // 新增参数
    TNodeData template,
    int chainCount,
    int nodesPerChain,
    DisposeGraphProcessing.IKeyGenerator<TKey>? keyGenerator = null,
    Func<TNodeData, TKey>? keyExtractor = null,
    Action<TNodeData, int>? setPosition = null,
    Func<TNodeData, TNodeData>? cloner = null)
    where TKey : notnull
    where TNodeData : class
{
    var system = new OrchestrationSystem<TKey, TNodeData, TEdgeData>(
        graph, template, chainCount, nodesPerChain,
        keyGenerator, keyExtractor, setPosition, cloner);
    runner.Register(system);
    return runner;
}
```

---

## 三、修复顺序

建议按以下顺序修复，每一步修复后可以编译验证：

| 顺序 | 步骤 | 文件 | 预计修改行数 | 难度 |
|------|------|------|-------------|------|
| 1 | 步骤 1 | `GraphKeyScenarios.cs` | 4 行 | ★☆☆ |
| 2 | 步骤 2 | `EcsRelationManager.cs` | 1 行 | ★☆☆ |
| 3 | 步骤 3 | `GraphSystemRunner.cs` | 2 行 | ★☆☆ |
| 4 | 步骤 4 | `NormalizationSystem.cs` | 2 行 | ★☆☆ |
| 5 | 步骤 5 | `GraphComponentStore.cs` | 1 行 | ★☆☆ |
| 6 | 步骤 6 | `StatisticsSystem.cs` | 10 行 | ★★☆ |
| 7 | 步骤 7 | `GraphKey.cs` | 15 行 | ★★☆ |
| 8 | 步骤 8 | `EcsIndexManager.cs` | 10 行 | ★★★ |
| 9 | 步骤 9 | `EcsTransaction.cs` | 50 行 | ★★★ |
| 10 | 步骤 10 | `NormalizationService.cs` | 25 行 | ★★☆ |
| 11 | 步骤 11 | `GraphFrameworkModule.cs` | 30 行 | ★★★ |

---

## 四、验证方法

修复完成后，在项目根目录执行：

```bash
dotnet build Share/GraphFramework/GraphFramework.csproj
```

或构建整个解决方案：

```bash
dotnet build WorkFlowPrj.sln
```

预期结果：0 个警告，0 个错误。
