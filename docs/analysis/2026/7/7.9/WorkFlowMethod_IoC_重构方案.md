# WorkFlowMethod IoC 控制反转重构方案

> **文档状态**：草案  
> **创建日期**：2026-07-08  
> **目标**：解决函数注册后运行时查找不到的问题，通过 IoC 控制反转将 Node 内部的函数注册由外部配置

---

## 一、问题分析

### 1.1 运行日志

```
[DEBUG] [WorkFlowMethod] [workflowmethod:WorkflowMethodExecution_BasicRegistry_diag] {
  "hash": "00D45D21",
  "type": "Modules.Registry.FunctionRegistry",
  "count": 0,
  "isInitialized": true,
  "message": "BasicRegistry getter 访问 RegistryCoordinator.Registry"
}
```

关键信息：
- `hash: 00D45D21` — 与 `FrameworkInitializer` 中创建的共享注册表实例一致
- `count: 0` — 注册表为空，`Add` 等函数未找到
- 但 `MathLibrary.RegisterAll(_registry)` 已在 `FileDrivenLifecycle.Initialize()` 中执行

### 1.2 执行路径

```
事件触发（用户点击按钮）
  → EventBinderTransformer.CreateEventHandler()
    → _executionServiceResolver() 解析出 WorkflowMethodExecutionService
      → WorkflowMethodExecutionService.ExecuteFunctionAsync("Add", parameters)
        → WorkFlowMethodUsage.BasicRegistry  // 静态属性，访问 RegistryCoordinator.Registry
          → RegistryCoordinator.Registry  // 返回 ITrConfigRegistry
            → TryGetConfig("Add", out config)  // 从 _configs 字典查找
              → 未找到（count: 0）
                → 回退到 WorkflowFallbackDelegate
                  → 未找到工作流定义: Add
                    → 返回失败
```

### 1.3 根本原因

**两条执行路径使用不同的注册表查找机制，且 `NodeExecutor` 内部硬编码了 `FunctionExecutor` 的创建逻辑。**

| 缺陷 | 描述 | 影响 |
|------|------|------|
| **硬编码依赖** | `NodeExecutor` 直接 `new FunctionExecutor()` | 无法替换执行器实现 |
| **注册表传递链过长** | registry 从 FrameworkInitializer → FileDrivenLifecycle → MathLibrary → FunctionRegistry，再通过 RegistryCoordinator → WorkflowMethodExecutionService → NodeExecutor | 任一环节断裂都会导致注册表不一致 |
| **查找路径重复** | WorkflowMethodExecutionService 先查一次，NodeExecutor 内部再查一次 | 两次查找必须一致，增加了出错面 |
| **静态门面耦合** | `WorkFlowMethodUsage.BasicRegistry`、`RegistryCoordinator.Registry` 等多处静态访问 | 测试困难，状态难以隔离 |

---

## 二、重构目标

1. **消除硬编码依赖**：`NodeExecutor` 不再自己创建 `FunctionExecutor`，由外部注入
2. **统一注册表访问**：所有组件通过 DI 容器获取共享注册表实例
3. **配置驱动注册**：函数注册信息外部化到 JSON 配置文件
4. **消除冗余查找**：`WorkflowMethodExecutionService` 查找到 config 后直接执行，不再二次查找

---

## 三、接口设计

### 3.1 现有接口

```csharp
// ExecutionFramework.Abstractions — 已有
public interface IFunctionExecutor
{
    Task<ExecutionResult> ExecuteAsync(string functionName, object?[]? parameters, EXContext? context = null);
    Task<ExecutionResult> ExecuteAsync(string functionName, Dictionary<string, object> namedParameters, EXContext? context = null);
    bool HasFunction(string functionName);
    IEnumerable<string> GetFunctionNames();
}

// TConfig — 已有
public interface ITrConfigRegistry
{
    void Register(string name, ITrConfig config);
    bool TryGetConfig(string name, out ITrConfig config);
    IEnumerable<string> GetRegisteredNames();
    void Clear();
}
```

### 3.2 新增/修改接口

```csharp
// ─── NodeExecutor 构造函数改为接收 IFunctionExecutor ───
public class NodeExecutor : INodeExecutor
{
    private readonly IFunctionExecutor _executor;  // 改为接口类型
    private readonly IWorkflowLogger? _logger;
    private readonly ConcurrentDictionary<string, ResiliencePipeline<BasicExecutionResult>> _pipelineCache = new();

    public NodeExecutor(
        IFunctionExecutor executor,  // ★ 由外部注入，不再自己创建
        IWorkflowLogger? logger = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger;
    }
}
```

---

## 四、依赖注入注册

### 4.1 DI 容器配置

在 `FrameworkInitializer.InitializeAppServiceProvider()` 中统一注册所有依赖：

```csharp
private static void InitializeAppServiceProvider()
{
    var services = new ServiceCollection();
    services.AddLogging();

    // 1. 注册共享注册表（单例）
    var registry = Share.Registry.RegistryCoordinator.Registry;
    services.AddSingleton<ITrConfigRegistry>(registry);
    if (registry is IFunctionRegistry funcRegistry)
        services.AddSingleton<IFunctionRegistry>(funcRegistry);

    // 2. 注册 FunctionExecutor（单例，依赖注册表）
    services.AddSingleton<IFunctionExecutor>(sp =>
    {
        var reg = sp.GetRequiredService<ITrConfigRegistry>();
        return new FunctionExecutor(
            reg,
            new NullParameterProcessor(),
            new TypeConverterAdapter());
    });

    // 3. 注册 NodeExecutor（瞬态，依赖 FunctionExecutor）
    services.AddTransient<INodeExecutor>(sp =>
    {
        var executor = sp.GetRequiredService<IFunctionExecutor>();
        return new NodeExecutor(executor);
    });

    // 4. 注册 WorkflowMethodExecutionService（单例，依赖注册表 + NodeExecutor）
    services.AddSingleton<IEventExecutionService, WorkflowMethodExecutionService>();

    var serviceProvider = services.BuildServiceProvider();
    AppServiceProvider.Initialize(serviceProvider);
}
```

### 4.2 依赖关系图

```
AppServiceProvider (DI 容器)
  ├── ITrConfigRegistry (单例) ← 共享 FunctionRegistry 实例
  ├── IFunctionRegistry (单例) ← 同一实例的 IFunctionRegistry 接口
  ├── IFunctionExecutor (单例) ← FunctionExecutor(registry, ...)
  ├── INodeExecutor (瞬态) ← NodeExecutor(executor)
  └── IEventExecutionService (单例) ← WorkflowMethodExecutionService(registry, nodeExecutor)
```

---

## 五、WorkflowMethodExecutionService 改造

### 5.1 构造函数注入

```csharp
public class WorkflowMethodExecutionService : IEventExecutionService
{
    private readonly IFunctionRegistry _registry;
    private readonly INodeExecutor _nodeExecutor;

    public WorkflowMethodExecutionService(
        IFunctionRegistry registry,
        INodeExecutor nodeExecutor)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _nodeExecutor = nodeExecutor ?? throw new ArgumentNullException(nameof(nodeExecutor));
    }

    public async Task<ExecutionResult> ExecuteFunctionAsync(
        string functionName,
        Dictionary<string, object> namedParameters)
    {
        WorkFlowMethodLog.Log("WorkflowMethodExecution_Execute_start",
            new { Function = functionName, ParamCount = namedParameters?.Count ?? 0 });

        try
        {
            // 1. 从注册表查找函数配置（一次查找）
            if (!_registry.TryGetConfig(functionName, out var config))
            {
                // 回退到工作流
                return await FallbackToWorkflow(functionName, namedParameters);
            }

            // 2. 参数转换
            var paramConfigs = config.Parameters ?? Array.Empty<IParamConfig>();
            var builder = new ParameterBuilder();
            var paramDescriptors = paramConfigs.Select(p =>
                new ParamConfigAdapter(p.Name, p.Type, p.IsOptional, p.DefaultValue)).ToArray();
            var buildResult = builder.Build(paramDescriptors, namedParameters);
            if (!buildResult.IsSuccess)
                return ExecutionResult.Failure($"Parameter build failed: {buildResult.ErrorMessage}");
            var positionalArgs = buildResult.Args;

            // 3. 创建 NodeDefinition
            var node = new NodeDefinition
            {
                Id = $"node-{functionName}",
                Name = functionName,
                NodeType = "Function",
                FunctionName = functionName,
                Parameters = positionalArgs,
                IsEnabled = true,
                TimeoutMs = 10000,
                RetryCount = 0,
            };

            // 4. 创建执行上下文
            var wfContext = new PipelineContext($"wf-{functionName}-{Guid.NewGuid():N}");
            wfContext.CurrentNodeKey = functionName;
            foreach (var kvp in namedParameters)
                wfContext.SetData($"param.{kvp.Key}", kvp.Value);

            // 5. 通过注入的 NodeExecutor 执行
            var result = await _nodeExecutor.ExecuteNodeAsync(node, wfContext);

            if (result.IsSuccess)
            {
                TryWriteWorkflowResult(functionName, result.Value);
                return ExecutionResult.Success(result.Value ?? new object());
            }
            return ExecutionResult.Failure(result.ErrorMessage ?? "Unknown error");
        }
        catch (Exception ex)
        {
            WorkFlowMethodLog.Log("WorkflowMethodExecution_Execute_error",
                new { Function = functionName, Error = ex.Message });
            return ExecutionResult.Failure(ex.Message);
        }
    }
}
```

### 5.2 关键变化

| 项目 | 当前 | 重构后 |
|------|------|--------|
| 注册表获取 | `WorkFlowMethodUsage.BasicRegistry`（静态） | `_registry`（构造函数注入） |
| NodeExecutor 创建 | `new NodeExecutor(registry)`（硬编码） | `_nodeExecutor`（构造函数注入） |
| 查找次数 | 2 次（Service + Executor 内部） | 1 次（Service 查找，Executor 直接执行） |
| 静态依赖 | `WorkFlowMethodUsage`、`RegistryCoordinator` | 无静态依赖 |

---

## 六、配置驱动的函数注册

### 6.1 JSON 配置文件格式

```json
// FileDriven/DrivenConfig/FunctionRegistrations/MathFunctions.registration.json
{
  "registrations": [
    {
      "name": "Add",
      "type": "delegate",
      "source": "FileDriven.Business.Registrations.MathLibrary, FileDriven.Business",
      "method": "Add",
      "parameters": [
        { "name": "a", "type": "System.Double" },
        { "name": "b", "type": "System.Double" }
      ],
      "description": "加法运算: a + b"
    },
    {
      "name": "Subtract",
      "type": "delegate",
      "source": "FileDriven.Business.Registrations.MathLibrary, FileDriven.Business",
      "method": "Subtract",
      "parameters": [
        { "name": "a", "type": "System.Double" },
        { "name": "b", "type": "System.Double" }
      ],
      "description": "减法运算: a - b"
    },
    {
      "name": "SetValue",
      "type": "workflow",
      "workflowRef": "Workflows/SetValue.workflow.json",
      "description": "设置值"
    }
  ]
}
```

### 6.2 注册加载器

```csharp
/// <summary>
/// 函数注册加载器 — 从 JSON 配置加载函数注册
/// <para>支持 delegate 和 workflow 两种注册类型</para>
/// </summary>
public class FunctionRegistrationLoader
{
    private readonly IFunctionRegistry _registry;
    private readonly ILogger? _logger;

    public FunctionRegistrationLoader(IFunctionRegistry registry, ILogger? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;
    }

    /// <summary>
    /// 从配置文件加载注册
    /// </summary>
    /// <param name="configPath">JSON 配置文件路径</param>
    public void LoadFromConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            _logger?.LogWarning($"Registration config not found: {configPath}");
            return;
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<RegistrationConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config?.Registrations == null || config.Registrations.Count == 0)
        {
            _logger?.LogWarning($"No registrations found in: {configPath}");
            return;
        }

        foreach (var reg in config.Registrations)
        {
            try
            {
                switch (reg.Type?.ToLowerInvariant())
                {
                    case "delegate":
                        RegisterDelegate(reg);
                        break;
                    case "workflow":
                        RegisterWorkflow(reg);
                        break;
                    default:
                        _logger?.LogWarning($"Unknown registration type '{reg.Type}' for '{reg.Name}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to register '{reg.Name}': {ex.Message}");
            }
        }
    }

    private void RegisterDelegate(RegistrationEntry reg)
    {
        // 通过反射加载类型和方法
        var type = Type.GetType(reg.Source);
        if (type == null)
            throw new InvalidOperationException($"Type not found: {reg.Source}");

        var method = type.GetMethod(reg.Method,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException($"Method not found: {type.FullName}.{reg.Method}");

        // 创建委托
        var delegateType = GetDelegateType(method);
        var dlg = method.CreateDelegate(delegateType);

        // 构建参数配置
        var paramConfigs = reg.Parameters?.Select(p => new ParamConfig
        {
            Name = p.Name,
            Type = p.Type,
            IsOptional = p.IsOptional,
            DefaultValue = p.DefaultValue
        }).ToArray() ?? Array.Empty<IParamConfig>();

        // 注册到注册表
        _registry.Register(reg.Name, dlg, paramConfigs, reg.Description);
        _logger?.LogInformation($"Registered delegate function: {reg.Name}");
    }

    private void RegisterWorkflow(RegistrationEntry reg)
    {
        // 注册工作流引用
        var trConfig = TrConfig.FromWorkflowRef(reg.WorkflowRef);
        trConfig.Name = reg.Name;
        trConfig.Description = reg.Description;
        ((ITrConfigRegistry)_registry).Register(reg.Name, trConfig);
        _logger?.LogInformation($"Registered workflow function: {reg.Name}");
    }

    private static Type GetDelegateType(MethodInfo method)
    {
        var paramTypes = method.GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();

        if (method.ReturnType == typeof(void))
            return Expression.GetActionType(paramTypes);
        else
            return Expression.GetFuncType(paramTypes.Concat(new[] { method.ReturnType }).ToArray());
    }
}

// ─── 配置模型 ───

public class RegistrationConfig
{
    public List<RegistrationEntry> Registrations { get; set; } = new();
}

public class RegistrationEntry
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "delegate";  // "delegate" | "workflow"
    public string Source { get; set; } = "";         // 程序集限定类型名
    public string Method { get; set; } = "";         // 方法名
    public string WorkflowRef { get; set; } = "";    // 工作流文件引用
    public List<ParameterConfig> Parameters { get; set; } = new();
    public string Description { get; set; } = "";
}

public class ParameterConfig
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "System.String";
    public bool IsOptional { get; set; }
    public string DefaultValue { get; set; } = "";
}
```

### 6.3 在 FileDrivenLifecycle 中使用

```csharp
public void Initialize()
{
    if (_initialized) return;

    // ... 现有初始化逻辑 ...

    // ★ 重构：使用配置驱动注册替代硬编码
    var loader = new FunctionRegistrationLoader(_registry);

    // 加载所有注册配置文件
    var configDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "DrivenConfig", "FunctionRegistrations");

    if (Directory.Exists(configDir))
    {
        foreach (var configFile in Directory.GetFiles(configDir, "*.registration.json"))
        {
            loader.LoadFromConfig(configFile);
        }
    }

    // ★ 保留代码注册方式作为补充（可选）
    // MathLibrary.RegisterAll(_registry);
    // Libraries.BuiltInUnitsLibrary.RegisterAll(_registry);
    // ...

    _initialized = true;
}
```

---

## 七、消除静态门面

### 7.1 WorkFlowMethodUsage 改造

```csharp
public class WorkFlowMethodUsage : IMethodUsageProvider
{
    // ★ 保留静态 Instance 作为向后兼容入口
    public static WorkFlowMethodUsage Instance { get; } = new();

    // ★ 实例属性替代静态属性
    private IFunctionRegistry? _registry;
    private IFunctionExecutor? _executor;
    private IWorkflowRegistry? _workflowRegistry;

    /// <summary>
    /// 初始化实例（由 DI 容器或 FrameworkInitializer 调用）
    /// </summary>
    public void Initialize(
        IFunctionRegistry registry,
        IWorkflowRegistry workflowRegistry,
        IFunctionExecutor executor)
    {
        _registry = registry;
        _workflowRegistry = workflowRegistry;
        _executor = executor;
    }

    /// <summary>
    /// 获取注册表（优先使用实例字段，回退到 RegistryCoordinator）
    /// </summary>
    public IFunctionRegistry Registry =>
        _registry ?? (IFunctionRegistry)Share.Registry.RegistryCoordinator.Registry;

    /// <summary>
    /// 获取执行器
    /// </summary>
    public IFunctionExecutor Executor =>
        _executor ?? throw new InvalidOperationException("WorkFlowMethodUsage 未初始化");
}
```

### 7.2 RegistryCoordinator 保留为启动入口

```csharp
public static class RegistryCoordinator
{
    private static readonly SingletonCoordinator<ITrConfigRegistry> _coordinator = new();

    /// <summary>
    /// 初始化注册表（仅在框架启动时调用）
    /// <para>★ 重构后：仅作为启动时的初始化入口，运行时通过 DI 获取</para>
    /// </summary>
    public static void Initialize(ITrConfigRegistry registry)
    {
        _coordinator.Set(registry);
    }

    /// <summary>
    /// 获取注册表实例
    /// <para>★ 重构后：保留为回退机制，优先使用 DI 注入</para>
    /// </summary>
    public static ITrConfigRegistry Registry => _coordinator.Instance;

    public static bool IsInitialized => _coordinator.IsSet;
}
```

---

## 八、重构步骤

### Step 1：修改 NodeExecutor 构造函数

- **文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/Engine/NodeExecutor.cs`
- **变更**：构造函数改为接收 `IFunctionExecutor` 接口
- **影响**：所有 `new NodeExecutor(...)` 的调用点需要修改

### Step 2：改造 WorkflowMethodExecutionService

- **文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs`
- **变更**：构造函数注入 `IFunctionRegistry` + `INodeExecutor`，消除静态依赖
- **影响**：`WorkflowFallbackDelegate` 保留为回退机制

### Step 3：统一 DI 注册

- **文件**：`FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs`
- **变更**：在 `InitializeAppServiceProvider()` 中注册所有依赖
- **影响**：`IEventExecutionService` 的解析路径改变

### Step 4：创建配置驱动注册器

- **新增文件**：`FileDriven/FileDriven.Business/Registrations/FunctionRegistrationLoader.cs`
- **新增目录**：`FileDriven/DrivenConfig/FunctionRegistrations/`
- **新增文件**：`FileDriven/DrivenConfig/FunctionRegistrations/MathFunctions.registration.json`

### Step 5：迁移硬编码注册到配置

- **文件**：`FileDriven/FileDriven.Business/FileDrivenLifecycle.cs`
- **变更**：`Initialize()` 中使用 `FunctionRegistrationLoader` 替代 `MathLibrary.RegisterAll` 等硬编码调用
- **保留**：`MathLibrary.cs` 等代码注册类作为后备方案

### Step 6：消除静态门面

- **文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs`
- **变更**：`BasicRegistry` 等静态属性改为实例属性，内部转发到 DI 实例

---

## 九、架构对比

| 维度 | 当前架构 | 重构后架构 |
|------|---------|-----------|
| **依赖方向** | NodeExecutor → FunctionExecutor（硬编码 `new`） | 外部 → DI 容器 → NodeExecutor（注入） |
| **注册方式** | 代码硬编码 `MathLibrary.RegisterAll()` | JSON 配置驱动 + 可插拔注册器 |
| **查找路径** | 两次查找（Service 查 + Executor 查） | 一次查找（Service 查，Executor 直接执行） |
| **测试性** | 难以 Mock 注册表和执行器 | 可通过 DI 注入 Mock 实例 |
| **扩展性** | 新增函数需修改代码 + 重新编译 | 新增函数只需添加配置文件 |
| **注册表一致性** | 依赖全局静态状态 `RegistryCoordinator` | 由 DI 容器保证单例一致性 |
| **配置管理** | 无 | 集中式 JSON 配置，支持热加载 |

---

## 十、风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| 现有代码大量使用 `WorkFlowMethodUsage.BasicRegistry` 静态属性 | 编译错误 | 保留静态属性作为委托，内部转发到 `Instance.Registry` |
| `NodeExecutor` 被多处直接 `new` | 遗漏修改导致运行时错误 | 全局搜索 `new NodeExecutor(`，逐一修改为 DI 解析 |
| 配置驱动注册可能遗漏某些动态注册场景 | 功能缺失 | 保留 `IRegistrationProvider` 接口，支持代码注册和配置注册两种模式 |
| 反射加载委托的性能开销 | 启动变慢 | 配置注册仅在初始化时加载一次，不影响运行时性能 |
| 配置文件路径变更 | 注册失败 | 使用 `FileDriven.Utilities.Path.ProjectPathManager` 解析路径 |

---

## 十一、测试策略

### 11.1 单元测试

```csharp
[Test]
public void NodeExecutor_Should_Use_Injected_Executor()
{
    var mockExecutor = new Mock<IFunctionExecutor>();
    var nodeExecutor = new NodeExecutor(mockExecutor.Object);

    var node = new NodeDefinition { FunctionName = "Add", ... };
    var context = new PipelineContext("test");

    nodeExecutor.ExecuteNodeAsync(node, context);

    mockExecutor.Verify(e => e.ExecuteAsync("Add", It.IsAny<object?[]>(), It.IsAny<EXContext>()));
}
```

### 11.2 集成测试

```csharp
[Test]
public void FunctionRegistrationLoader_Should_Register_From_Config()
{
    var registry = new FunctionRegistry();
    var loader = new FunctionRegistrationLoader(registry);

    loader.LoadFromConfig("TestData/test.registration.json");

    Assert.IsTrue(registry.TryGetConfig("Add", out var config));
    Assert.AreEqual("加法运算", config.Description);
}
```

---

## 十二、时间线

| 阶段 | 内容 | 预估工时 |
|------|------|---------|
| Phase 1 | 修改 NodeExecutor 构造函数 + 更新所有调用点 | 2h |
| Phase 2 | 改造 WorkflowMethodExecutionService + DI 注册 | 2h |
| Phase 3 | 创建 FunctionRegistrationLoader + 配置文件 | 3h |
| Phase 4 | 迁移 MathLibrary 等硬编码注册到配置 | 2h |
| Phase 5 | 消除静态门面 + 测试 | 2h |
| **合计** | | **11h** |
