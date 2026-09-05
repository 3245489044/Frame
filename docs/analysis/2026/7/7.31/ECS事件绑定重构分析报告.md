# ECS 事件绑定重构分析报告

> **日期**：2026-07-29
> **范围**：MainLayout.json 函数执行无效问题的调用链分析 + ECS 管线事件绑定重构

---

## 一、问题描述

`FileDriven/DrivenConfig/JsonConfig/MainLayout.json` 中定义的 5 个工具栏按钮（Load Config、Load Workflow、Clear、Help、Load Side）及其绑定的函数（`functionName` + `events.click`）在 GUI 模式下点击无效。

## 二、调用链追踪

### 2.1 GUI 启动路径

```
EnableMent\交互启动.bat
  → FileDriven.Launcher.exe (--show-gui)
    → FileDrivenExecutor.Run(args, ExecutorMode.Default)
      → FileDrivenExecutor.StartGuiMode()
        → StartGuiModePipeline.ExecuteAsync()
          → FileDrivenGuiInitializerOrchestrator.CreateAndConfigure()
            → CreateContextAsync()
              → RenderPipelineComposition.Create()        ← 创建 ECS 管线
              → new FileDrivenApplicationService(composition, dispatcher)
              → new MainForm(appService)
                → OnLoadAsync()
                  → LoadAndRenderAsync(UIScope.MainLayout, MainLayoutPath, _rootView)
```

### 2.2 UI 配置加载路径

```
MainForm.OnLoadAsync()
  → foreach phase in [MainLayout, ConfigSelector]
    → _app.LoadAndRenderAsync(scope, configPath, hostControl)
      → LoadUIConfigAsync(scope, configPath, hostControl)
        ① _composition.DestroyScopeEntities("MainLayout")   清除旧 Entity
        ② JsonConfigService.LoadConfigAsync(configPath)
           → PreprocessConfig → JsonTemplateProcessor.Process  模板展开
           → JsonFileService.DeserializeConfig             反序列化
        ③ _composition.LoadConfiguration(config, "MainLayout")
           → EcsInputPort.LoadConfig()
             → ConfigDrivenControlFactory.Convert()
               → CreateRootEntity()     创建根容器 Entity
               → ConvertChildren()      递归创建控件 Entity
                 → ControlFactory.CreateFromConfig()
                   → AddEventComponents()
                     ✓ 遍历 events.EventsDictionary
                     ✓ 创建 EventBindingsComponent → 存入 ECS World
        ④ _composition.RegisterRootContainer(hostControl)
        ⑤ _composition.ExecuteCreationStage()
           → EcsCapabilityApplier.Update()     创建 WinForms 控件
           → LayoutSystem.Update()             计算布局
           → SplitContainerSystem.Update()     处理分割容器
           → StyleSystem.Update()              应用样式
           → EventBindingSystem.Update()       事件绑定
      → _composition.ExecuteRenderStage()
        → CsgEntryPoint.CollectSnapshot()      收集控件快照
        → WinFormsOutputPort.ApplySnapshot()   渲染到 WinForms
```

### 2.3 函数注册路径

```
FrameworkInitializer.Initialize()
  → "Registry" 步骤:
    UnifiedRegistryCenter 初始化
    → RegisterRegistry("FunctionLibrary", FunctionLibraryLifecycle)
      → 扫描 function-libraries.json
        → "UIToolbox": "FunctionStorage.UIToolbox, FileDriven.Business"
          → UIToolbox.RegisterAll(ITrConfigRegistry)
            → Register("LoadConfig", Func<string>)   ← LoadConfig 函数
            → Register("LoadWorkflow", Func<string>) ← LoadWorkflow 函数
            → Register("ClearOutput", Func<string>)  ← ClearOutput 函数
            → Register("ShowHelp", Func<string>)     ← ShowHelp 函数
            → Register("LoadSidePanel", Func<string>)← LoadSidePanel 函数
            → Register("Execute", Func<string>)      ← Execute 函数
  
  → "AppService" 步骤:
    InitializeAppServiceProvider()
    → DI 容器构建 + BuildServiceProvider()
    → IFunctionExecutor 注入到 PipelineEntryPool
```

### 2.4 FrameworkContext + EventBus 路径

```
  → "FrameworkContext" 步骤:
    → new FrameworkContext(EnableEventBus=true, ...)
    → new EventBusFacade(enhanced)                    ← 创建事件总线
  
  → "WinForms" 步骤:
    InitializeWinFormsSubsystem(context)
    → new WinFormsEventBus()                          ← Share 版事件总线
    → ctx.EventBus = winFormsEventBus                 ← 替换 FrameworkContext 的 EventBus
    → Register<IEventBus>(winFormsEventBus)
    → Register<IControlFinder>(WinFormsControlFinderToTConfigAdapter)
    → Register<IControlFinderStrategy>(ReflectionControlFinderStrategy)
    → Resolve<IFunctionResolver>()                    ← ★ 此前返回 null
    → Resolve<IParameterBinder>()                     ← ★ 此前返回 null
    → new EventBindingOrchestrator(resolver, binder, null, options)
    → Register<EventBindingOrchestrator>(orchestrator)
```

## 三、根因分析

### 3.1 根因 P0：IFunctionResolver / IParameterBinder 未注册

**证据**：`BackEndDriven.IoC.ServiceCollectionExtensions.AddEventBindingServices()` 定义了注册：
```csharp
services.TryAddSingleton<IFunctionResolver, FunctionResolver>();
services.TryAddSingleton<IParameterBinder, ParameterBinder>();
```
但在 `FrameworkInitializer.InitializeAppServiceProvider()` 构建 `ServiceCollection` 的流程中**从未被调用**。

结果：
- `AppServiceProvider.Resolve<IFunctionResolver>()` 返回 **null**
- `EventBindingOrchestrator` 构造函数中 `_functionResolver = null`
- 绑定步骤中 `CreateEventHandler()` 的 `functionResolver.ResolveDelegate(functionName)` 将失败

### 3.2 根因 P1：EventBindingSystem 硬编码 Click 绕过完整绑定

**旧架构的 `EventBindingSystem.Update()`**：
```csharp
foreach (var binding in bindings.Bindings)
{
    EventHandler clickHandler = (s, e) =>
    {
        // 直接写 ExecutionRequestComponent，绕过完整绑定流程
        _entityManager.AddComponent(entity,
            new ExecutionRequestComponent
            {
                FunctionName = functionName,
                SourceEntityId = entity.Id,
                Timestamp = DateTime.UtcNow
            });
    };
    control.Click += clickHandler;
}
```

问题：
1. ECS 层直接硬编码 `control.Click += handler`，违反职责分离
2. 不经过 `EventBindingOrchestrator` 的完整绑定流程（参数绑定、工作流注册、诊断收集）
3. 只支持 `click` 事件（`switch` 中 `default` 分支仅记录日志并跳过）
4. 自己管理 handler 缓存和释放（`_boundHandlers` 字典 + `ClearBoundHandlers()`）

### 3.3 双轨事件绑定架构

旧架构存在两条完全独立的事件绑定路径：

| 维度 | ECS EventBindingSystem | Share.EventBindingOrchestrator |
|------|----------------------|-------------------------------|
| 所在项目 | FrontEndDriven（ECS 层） | Share（公用层） |
| 绑定方式 | `control.Click += handler`（硬编码） | EventBus 通用绑定 |
| 参数绑定 | 无 | `IParameterBinder`（完整） |
| 函数解析 | 无 | `IFunctionResolver.ResolveDelegate` |
| 工作流支持 | 无 | 自动包装单节点工作流 |
| 事件类型 | 仅 `click` | 任意事件 |
| 谁调用 | `RenderPipelineComposition.ExecuteCreationStage()` | 创建后注册到 AppServiceProvider，但**ECS 管线从未调用** |

`EventBindingOrchestrator` 虽然被创建并注册，但 ECS 管线完全未使用它。

### 3.4 架构示意图（重构前）

```
ECS 管线 (RenderPipelineComposition)
  └── ExecuteCreationStage()
       ├── EcsCapabilityApplier.Update()    ✓ 创建控件
       ├── LayoutSystem.Update()            ✓ 布局
       ├── SplitContainerSystem.Update()    ✓ 分割器
       ├── StyleSystem.Update()             ✓ 样式
       └── EventBindingSystem.Update()      ✗ 硬编码 click
            └── control.Click += handler
                 └── ExecutionRequestComponent
                      └── FunctionExecutionSystem → Pipeline

EventBindingOrchestrator (Share) — 闲置
  ├── IFunctionResolver    (null ← 未注册)
  ├── IParameterBinder      (null ← 未注册)
  ├── IEventBinder
  ├── EventBus → WinFormsEventBus
  └── 完整绑定管线: ContextInit → ControlTreeWalk
       → ValidateInput → DiagnosticsScope → StatisticsCollect
```

## 四、重构方案

### 4.1 目标架构

```
ECS 管线 (RenderPipelineComposition)
  └── ExecuteCreationStage()
       ├── EcsCapabilityApplier.Update()    → 创建控件
       ├── LayoutSystem.Update()            → 布局
       ├── SplitContainerSystem.Update()    → 分割器
       ├── StyleSystem.Update()             → 样式
       └── EventBindingSystem.Update()      ★ 桥接器：收集数据 + 委托
            │
            ↓ 构建 EventBindingDeclaration[]
            ↓
            EventBindingOrchestrator.LoadAndBindDeclarations()
            ├── IFunctionResolver    (✓ 已注册 → FunctionResolver)
            │     └── ResolveDelegate("LoadConfig")
            │           → ITrConfigRegistry.Find("LoadConfig")
            │           → 返回 UIToolbox.LoadConfig 委托
            ├── IParameterBinder     (✓ 已注册 → ParameterBinder)
            ├── EventBus → WinFormsEventBus
            └── CreateEventHandler → 绑定到控件
```

### 4.2 执行路径分离

| 触发方式 | 路径 | 执行引擎 |
|---------|------|---------|
| **UI 按钮点击** | `EventBindingOrchestrator` → `IFunctionResolver.ResolveDelegate` → 直接执行 | **不回 ECS** |
| **CLI 命令/脚本** | `CommandInputPort` → `ExecutionRequestComponent` → `FunctionExecutionSystem` → `Pipeline` | ECS 路径 |

## 五、变更详情

### 文件 1：`FileDriven/FileDriven.Execution/Initial/FrameworkInitializer.cs`

**新增注册代码**（`InitializeAppServiceProvider()` 末尾，BuildServiceProvider 后）：

```csharp
var funcRegistry = AppServiceProvider.Resolve<ITrConfigRegistry>();
var execService = AppServiceProvider.Resolve<IEventExecutionService>();
var funcResolver = new FunctionResolver(funcRegistry, execService);
AppServiceProvider.Register<IFunctionResolver>(funcResolver);

var parameterBuilder = new ParameterBuilder();
AppServiceProvider.Register<IParameterBinder>(
    new ParameterBinder(parameterBuilder));
```

### 文件 2：`CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/EventBindingSystem.cs`

**完全重写**：从 134 行硬编码绑定 → 150 行薄协调器

**核心逻辑**：
```csharp
public void Update()
{
    if (_orchestrator == null) return;

    // ① 收集所有待绑定的 Entity
    var pendingEntities = _entityManager.GetEntities<EventBindingsComponent>()
        .Where(e => state.State == EventBindingState.Pending);

    // ② 构建 EventBindingDeclaration 列表
    var declarations = BuildDeclarations(pendingEntities);

    // ③ 委托给 EventBindingOrchestrator 执行完整绑定
    _orchestrator.LoadAndBindDeclarations(declarations, targetForm);

    // ④ 标记所有已绑定 Entity 状态为 Bound
    MarkAllBound(pendingEntities);
}
```

### 文件范围

| 文件 | 改动 | 行数 |
|------|------|------|
| `FrameworkInitializer.cs` | 新增 IFunctionResolver/IParameterBinder 注册 | +23 行 |
| `EventBindingSystem.cs` | 完全重写（ECS 数据收集 → 委托 Orchestrator） | -134 +150 行 |
| `RenderPipelineComposition.cs` | 无变化（EventBindingSystem 内部解析 Orchestrator） | 0 行 |

## 六、验证

两个项目编译通过，零错误：
- `FrontEndDriven.csproj` → `FrontEndDriven.dll` ✅
- `FileDriven.Execution.csproj` → `FileDriven.Execution.dll` ✅

## 七、后续建议

### 7.1 移除死代码

`EventBindingSystem` 重写后以下不再需要：
- `ClearBoundHandlers()` 方法（已删除）
- `_boundHandlers` 字典和 handler 缓存逻辑（已删除）

### 7.2 FunctionExecutionSystem 路径

CLI 脚本执行仍通过 `ExecutionRequestComponent` → `FunctionExecutionSystem` → `Pipeline`，但 UI 点击不再经过此路径。后续可考虑：
- 分离 UI 事件执行和 CLI 脚本执行为完全独立路径
- UI 事件执行通过 `IFunctionResolver.ResolveDelegate` 直接执行函数
- CLI 脚本通过 `IPipelineStage` 链执行

### 7.3 Observable 优先级

如果 `EventBindingOrchestrator` 找不到 `IFunctionResolver` 或解析失败，`EventBindingSystem` 会静默跳过：
```csharp
if (_orchestrator == null)
{
    FrontEndDrivenLog.Log("EventBindingSystem_orchestrator_missing", ...);
    return;
}
```
建议在调试/开发模式下触发更明显的错误通知。