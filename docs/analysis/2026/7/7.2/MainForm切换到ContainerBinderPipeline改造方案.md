# MainForm 切换到 ContainerBinderPipeline 改造方案

> 日期：2026-07-01
> 目标：消除 MainForm 中手写管线与 Share 层 ContainerBinderPipeline 的重复代码

---

## 一、现状分析

### 当前手写管线（MainForm.ExecuteContainerBinderPipelineAsync）

```
MainForm.LoadSideContainerAsync
  └─ ExecuteContainerBinderPipelineAsync(config, configPath)  ← 手写
       ├─ RegisterControlsFromConfig()          ← 注册控件
       ├─ BindEventsViaRenderPipelineAsync()    ← 渲染管线事件绑定
       └─ InitCommands 循环                      ← 执行初始化命令
```

### 通用管线（ContainerBinderPipeline.ExecuteAsync）

```
ContainerBinderPipeline.ExecuteAsync(context)
  ├─ 1a. InvokeConfigLoaderAsync()              ← 配置加载
  ├─ 1b. RegisterControlsAsync()                ← 控件注册（可选）
  ├─ 2a. BindEventsAsync()                      ← 事件绑定
  ├─ 2b. InvokeRenderPipelineAsync()            ← 渲染管线（可选）
  ├─ 2c. ExecuteInitCommandsAsync()             ← InitCommands（可选）
  └─ 3.  ExecuteCommandsAsync()                 ← 命令执行
```

### 核心差异

| 维度 | 手写管线 | ContainerBinderPipeline |
|------|---------|------------------------|
| 构造函数依赖 | 无（直接使用 MainForm 字段） | 需要 5 个依赖（ILogger, IEventBindingService, IConfigLoader, ICommandExecutor, IComponentRegistry） |
| 命令执行器 | `Func<string, Task<OperationResult<object>>>` | `ICommandExecutor`（`Task ExecuteAsync(string)`） |
| 控件查找 | `_sideContainer.FindControl<Control>(name)` | `context.ControlFinder` 委托 |
| 渲染管线 | 直接调用 `WdUIUsage.RenderPipeline` | 通过 `context.RenderPipeline`（`IRenderPipeline` 接口） |

---

## 二、改造方案

### 总体策略

1. **ContainerBinderPipeline**：新增无参构造函数，所有依赖改为可选（通过 context 传入或跳过）
2. **MainForm**：使用无参构造的 ContainerBinderPipeline，通过 context 传入所需依赖
3. **删除手写管线中的重复方法**

---

## 三、具体步骤

### 步骤 1：修改 ContainerBinderPipeline — 新增无参构造函数 + Func<> 支持

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Pipeline/ContainerBinderPipeline.cs`

#### 1.1 新增无参构造函数

```csharp
/// <summary>
/// 无参构造函数 — 所有依赖通过 IContainerBinderContext 传入或跳过
/// <para>适用于 MainForm 等调用方，无需准备完整的 DI 容器。</para>
/// </summary>
public ContainerBinderPipeline()
{
    _logger = null!;
    _eventBindingService = null!;
    _configLoader = null!;
    _commandExecutor = null!;
    _componentRegistry = null!;
}
```

#### 1.2 修改 ExecuteAsync，支持无参构造模式

```csharp
public async Task ExecuteAsync(IContainerBinderContext context)
{
    if (context == null)
        throw new ArgumentNullException(nameof(context));

    // 无参构造模式下，从 context 获取依赖
    var logger = _logger ?? context.Services?.GetService(typeof(ILogger<ContainerBinderPipeline>)) as ILogger<ContainerBinderPipeline>
                 ?? NullLogger<ContainerBinderPipeline>.Instance;

    logger.LogInformation("容器绑定管线开始执行");

    try
    {
        // 阶段 1a: 配置加载（需要 _configLoader 或 context 中有 ConfigLoader）
        if (_configLoader != null || context.Services?.GetService(typeof(IConfigLoader)) != null)
            await InvokeConfigLoaderAsync(context);
        else
            logger.LogDebug("ConfigLoader 未设置，跳过配置加载阶段");

        // 阶段 1b: 控件注册（可选 — 需要 context.ControlRegistry）
        await RegisterControlsAsync(context);

        // 阶段 2a: 事件绑定（需要 _eventBindingService）
        if (_eventBindingService != null || context.Services?.GetService(typeof(IEventBindingService)) != null)
            await BindEventsAsync(context);
        else
            logger.LogDebug("EventBindingService 未设置，跳过事件绑定阶段");

        // 阶段 2b: 渲染管线（可选 — 需要 context.RenderPipeline）
        await InvokeRenderPipelineAsync(context);

        // 阶段 2c: InitCommands 执行（可选 — 需要 _commandExecutor 或 context 中有 CommandExecutor）
        await ExecuteInitCommandsAsync(context);

        // 阶段 3: 命令执行（需要 _commandExecutor）
        if (_commandExecutor != null)
            await ExecuteCommandsAsync(context);
        else
            logger.LogDebug("CommandExecutor 未设置，跳过命令执行阶段");

        logger.LogInformation("容器绑定管线执行完成");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "容器绑定管线执行失败: {Message}", ex.Message);
        throw;
    }
}
```

#### 1.3 修改 ExecuteInitCommandsAsync，支持从 context 获取命令执行器

```csharp
private async Task ExecuteInitCommandsAsync(IContainerBinderContext context)
{
    var config = context.Config;
    if (config?.InitCommands == null || config.InitCommands.Count == 0)
    {
        // _logger 可能为 null，使用 context 中的日志
        return;
    }

    // 获取命令执行器：优先使用构造函数注入的，回退到 context 中的 Func<>
    var executor = _commandExecutor;
    var executorFunc = context.Items.TryGetValue("CommandExecutor", out var funcObj) 
                      ? funcObj as Func<string, Task<OperationResult<object>>> 
                      : null;

    if (executor == null && executorFunc == null)
    {
        // 无命令执行器，跳过
        return;
    }

    foreach (var cmd in config.InitCommands)
    {
        if (string.IsNullOrWhiteSpace(cmd)) continue;

        try
        {
            if (executor != null)
                await executor.ExecuteAsync(cmd);
            else if (executorFunc != null)
                await executorFunc(cmd);
        }
        catch (Exception ex)
        {
            // 单条命令失败不影响后续命令
        }
    }
}
```

#### 1.4 修改 ExecuteCommandsAsync，支持从 context 获取命令执行器

```csharp
private async Task ExecuteCommandsAsync(IContainerBinderContext context)
{
    var config = context.Config;
    if (config == null) return;

    // 获取命令执行器
    var executor = _commandExecutor;
    var executorFunc = context.Items.TryGetValue("CommandExecutor", out var funcObj) 
                      ? funcObj as Func<string, Task<OperationResult<object>>> 
                      : null;

    if (executor == null && executorFunc == null) return;

    await ControlTreeWalker.WalkAllViewsAsync(config, async ctrl =>
    {
        if (ctrl.Events != null)
        {
            var commandName = TryGetParameterFromItems(ctrl, "command");
            if (!string.IsNullOrEmpty(commandName))
            {
                if (executor != null)
                    await executor.ExecuteAsync(commandName);
                else if (executorFunc != null)
                    await executorFunc(commandName);
            }
        }
    });
}
```

#### 1.5 修改 ResolveComponentRegistry，支持无参构造模式

```csharp
private IComponentRegistry? ResolveComponentRegistry(IContainerBinderContext context)
{
    // 优先从上下文的 Services 中获取
    if (context.Services != null)
    {
        var registry = context.Services.GetService(typeof(IComponentRegistry)) as IComponentRegistry;
        if (registry != null)
            return registry;
    }

    // 回退到构造函数注入的 ComponentRegistry
    if (_componentRegistry != null)
        return _componentRegistry;

    // 无参构造模式下，从全局 ComponentRegistry.Instance 获取
    return ComponentRegistry.Instance;
}
```

---

### 步骤 2：修改 MainForm.ExecuteContainerBinderPipelineAsync

**文件**：`FileDriven/FileDriven.UI/Core/MainForm.cs`

#### 2.1 替换手写管线

```csharp
/// <summary>
/// 通过 ContainerBinderPipeline 执行控件注册/InitCommands/事件绑定
/// <para>★ 2026-07-01 重构：使用 Share 层的通用管线替代手写版本。</para>
/// </summary>
private async Task ExecuteContainerBinderPipelineAsync(UIConfiguration config, string configPath)
{
    // 创建上下文
    var context = new ContainerBinderContext(_sideContainer, configPath)
    {
        Config = config,
        ControlRegistry = ControlRegistry,
        RenderPipeline = new WinFormsRenderPipelineAdapter(WdUIUsage.RenderPipeline),
        ControlFinder = name => _sideContainer.FindControl<Control>(name)
    };

    // 将 CommandExecutor 存入 Items 字典，供管线中的 InitCommands/Commands 阶段使用
    if (CommandExecutor != null)
    {
        context.Items["CommandExecutor"] = CommandExecutor;
    }

    // 使用无参构造的管线
    var pipeline = new ContainerBinderPipeline();
    await pipeline.ExecuteAsync(context);
}
```

#### 2.2 删除不再需要的方法

删除以下私有方法：
- `RegisterControlsFromConfig` — 由 `ContainerBinderPipeline.RegisterControlsAsync` 接管
- `BindEventsViaRenderPipelineAsync` — 由 `ContainerBinderPipeline.InvokeRenderPipelineAsync` 接管

---

### 步骤 3：确认项目引用

**文件**：`FileDriven/FileDriven.UI/FileDriven.UI.csproj`

确认已包含以下项目引用：
```xml
<ProjectReference Include="..\..\CsharpPrj\Share\EventBindingOrchestrator\EventBindingOrchestrator.csproj" />
```

---

## 四、影响范围

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| `CsharpPrj/Share/EventBindingOrchestrator/Pipeline/ContainerBinderPipeline.cs` | 修改 | 新增无参构造函数 + 各阶段方法支持从 context 获取依赖 |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | 修改 | `ExecuteContainerBinderPipelineAsync` 替换为管线调用 + 删除 2 个私有方法 |
| `FileDriven/FileDriven.UI/FileDriven.UI.csproj` | 确认 | 确保引用了 EventBindingOrchestrator |

---

## 五、风险与注意事项

1. **日志降级**：无参构造模式下使用 `NullLogger`，管线日志不会输出到 MainForm 的 `_outputTextBox`。如需日志，可通过 `context.Services` 传入日志服务。

2. **阶段跳过**：无参构造模式下，阶段 1a（配置加载）和阶段 2a（事件绑定）会被跳过，因为 MainForm 不需要这两个阶段（配置已在 ConfigLoader 中加载，事件绑定由渲染管线处理）。

3. **CommandExecutor 传递**：通过 `context.Items["CommandExecutor"]` 传递 `Func<>` 委托，管线内部通过 `TryGetValue` 获取。这是弱类型方式，但保持了 `IContainerBinderContext.Items` 的设计意图（步骤间共享数据）。

4. **ComponentRegistry 回退**：无参构造模式下，`ResolveComponentRegistry` 回退到 `ComponentRegistry.Instance`，与 MainForm 当前行为一致。
