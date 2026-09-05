# FunctionStorage 通用框架完善方案

## 一、现状分析

### 1.1 当前定位

`FunctionStorage` 位于 `Operation/FunctionStorage`，是系统中**唯一的函数注册中心**。它持有 `Share.Registry.FunctionRegistry` 实例，通过 `FunctionStorageRegistry` 静态类提供统一的注册/执行入口。

### 1.2 当前职责

| 组件 | 职责 | 状态 |
|------|------|------|
| `FunctionStorageRegistry` | 统一注册/执行入口，持有 FunctionRegistry 实例 | ✅ 基础完善 |
| `MathLibrary` | 注册数学函数（Add/Subtract/Multiply/Divide） | ✅ 基础完善 |
| `UIToolbox` | 注册 UI 操作函数 + 非事件命令标记函数 | ⚠️ 含临时方案 |
| `WorkflowLibrary` | 从 JSON 文件注册工作流函数 | ✅ 基础完善 |
| `ModelLibrary` | 工作流模型查询 | ✅ 基础完善 |
| `CodePathLibrary` | 已废弃 | ❌ 需清理 |
| `WinFormsEventBinder` | WinForms 事件绑定器 | ✅ 基础完善 |
| `UIConnector` | UI 层桥接 | ✅ 基础完善 |
| `WorkflowExecutionLayer` | 工作流执行层 | ✅ 基础完善 |
| `WorkflowCommandCenter` | 工作流命令中心 | ✅ 基础完善 |

### 1.3 存在的问题

1. **非事件命令注册分散** — `NonEventCommands`、`NonEventCommandRegistry`、`NonEventCommandHandler` 在 `FileDriven.Execution` 中，而 `UIToolbox` 在 `FunctionStorage` 中注册了"标记函数"
2. **缺少统一的命令注册接口** — 没有定义 `INonEventCommandHandler` 接口，导致 `FileDriven.Execution` 中的处理逻辑无法被 `FunctionStorage` 直接引用
3. **缺少统一的上下文抽象** — `NonEventCommandHandler` 依赖 `FileDrivenPipelineContext`，无法在 `FunctionStorage` 中直接复用
4. **CodePathLibrary 已废弃** — 需要清理或替换
5. **缺少完善的错误处理框架** — 函数执行错误处理分散在各处

---

## 二、完善目标

### 2.1 总体目标

将 `FunctionStorage` 打造为**通用的函数注册与执行框架**，使其成为系统中所有函数注册、查询、执行的单一真相来源，不依赖任何上层项目（如 `FileDriven.Execution`）。

### 2.2 具体目标

| 目标 | 说明 | 优先级 |
|------|------|--------|
| **统一注册接口** | 定义清晰的注册接口，支持 Delegate、IFunctionUnit、工作流等多种注册方式 | P0 |
| **统一执行接口** | 定义清晰的执行接口，支持参数自动匹配、类型转换、异步执行 | P0 |
| **非事件命令框架** | 将非事件命令的注册/处理逻辑纳入 FunctionStorage | P0 |
| **上下文抽象** | 定义 `IExecutionContext` 接口，消除对 `FileDrivenPipelineContext` 的依赖 | P1 |
| **错误处理框架** | 统一的错误码、异常处理、重试机制 | P1 |
| **插件化扩展** | 支持外部 Library 动态注册 | P2 |
| **性能优化** | 函数查找缓存、懒加载注册 | P2 |

---

## 三、架构设计

### 3.1 分层架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FunctionStorage                              │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                    接口定义层 (Abstractions/)                  │   │
│  │  IFunctionRegistry  IFunctionExecutor  IExecutionContext       │   │
│  │  INonEventCommandRegistry  INonEventCommandHandler             │   │
│  │  IFunctionUnit  IFunctionExecutionResult                       │   │
│  └─────────────────────────┬───────────────────────────────────┘   │
│                            │ 实现                                  │
│  ┌─────────────────────────▼───────────────────────────────────┐   │
│  │                    核心实现层 (Core/)                         │   │
│  │  FunctionStorageRegistry  FunctionExecutionContext           │   │
│  │  NonEventCommandRegistry  NonEventCommandHandler             │   │
│  │  ArgumentMatcherPipeline  TypeConverter                      │   │
│  └─────────────────────────┬───────────────────────────────────┘   │
│                            │ 注册                                  │
│  ┌─────────────────────────▼───────────────────────────────────┐   │
│  │                    函数库层 (Libraries/)                      │   │
│  │  MathLibrary  UIToolbox  WorkflowLibrary  ModelLibrary        │   │
│  │  NonEventCommands  BuiltInUnitsLibrary                       │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 核心接口定义

#### 3.2.1 IFunctionRegistry — 函数注册表接口

```csharp
/// <summary>
/// 函数注册表接口
/// </summary>
public interface IFunctionRegistry
{
    /// <summary>注册函数（委托方式）</summary>
    void Register(string name, Delegate function, string? description = null);

    /// <summary>注册函数（IFunctionUnit 方式）</summary>
    void Register(IFunctionUnit unit);

    /// <summary>批量注册函数</summary>
    void RegisterRange(params (string Name, Delegate Function, string? Description)[] functions);

    /// <summary>注销函数</summary>
    void Unregister(string name);

    /// <summary>尝试获取函数描述符</summary>
    bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor);

    /// <summary>是否包含指定函数</summary>
    bool Contains(string name);

    /// <summary>获取所有已注册的函数名称</summary>
    IEnumerable<string> GetRegisteredNames();

    /// <summary>清空所有注册</summary>
    void Clear();
}
```

#### 3.2.2 IFunctionExecutor — 函数执行器接口

```csharp
/// <summary>
/// 函数执行器接口
/// </summary>
public interface IFunctionExecutor
{
    /// <summary>按名称执行函数</summary>
    Task<IFunctionExecutionResult> ExecuteAsync(string functionName, object? parameters = null);

    /// <summary>执行委托</summary>
    Task<IFunctionExecutionResult> ExecuteAsync(Delegate function, object? parameters = null);

    /// <summary>执行 IFunctionUnit</summary>
    Task<IFunctionExecutionResult> ExecuteAsync(IFunctionUnit unit, object? parameters = null);

    /// <summary>批量执行函数</summary>
    Task<IFunctionExecutionResult[]> ExecuteBatchAsync(params string[] functionNames);
}
```

#### 3.2.3 IExecutionContext — 执行上下文接口

```csharp
/// <summary>
/// 执行上下文接口
/// <para>抽象命令执行所需的上下文信息，消除对 FileDrivenPipelineContext 的依赖</para>
/// </summary>
public interface IExecutionContext
{
    /// <summary>当前命令字符串</summary>
    string? CurrentCommand { get; }

    /// <summary>解析后的命令信息</summary>
    IParsedCommand? ParsedCommand { get; }

    /// <summary>绑定的函数名</summary>
    string? BoundFunctionName { get; }

    /// <summary>提取的输入值</summary>
    IReadOnlyDictionary<string, object?>? ExtractedValues { get; }

    /// <summary>函数执行结果值</summary>
    object? FunctionResultValue { get; set; }

    /// <summary>函数执行是否成功</summary>
    bool FunctionExecuteSuccess { get; set; }

    /// <summary>错误信息</summary>
    string? FunctionErrorMessage { get; set; }

    /// <summary>共享数据字典</summary>
    IDictionary<string, object?> SharedData { get; }
}
```

#### 3.2.4 INonEventCommandRegistry — 非事件命令注册表接口

```csharp
/// <summary>
/// 非事件命令注册表接口
/// </summary>
public interface INonEventCommandRegistry
{
    /// <summary>注册非事件命令</summary>
    void Register(string commandName, Action<IExecutionContext> handler,
        CommandCriticality criticality = CommandCriticality.Important,
        string? description = null);

    /// <summary>批量注册非事件命令</summary>
    void RegisterRange(params (string Name, Action<IExecutionContext> Handler,
        CommandCriticality Criticality, string? Description)[] commands);

    /// <summary>尝试获取处理函数</summary>
    bool TryGetHandler(string commandName, out Action<IExecutionContext>? handler);

    /// <summary>判断命令是否已注册</summary>
    bool IsRegistered(string commandName);

    /// <summary>校验所有命令的注册完整性</summary>
    NonEventCommandValidationResult ValidateAll();

    /// <summary>获取缺少处理函数的命令列表</summary>
    string[] GetUnhandledCommands();
}
```

#### 3.2.5 INonEventCommandHandler — 非事件命令处理接口

```csharp
/// <summary>
/// 非事件命令处理接口
/// <para>定义所有非事件命令的标准处理签名</para>
/// </summary>
public interface INonEventCommandHandler
{
    /// <summary>处理 load 命令</summary>
    void HandleLoad(IExecutionContext context);

    /// <summary>处理 close 命令</summary>
    void HandleClose(IExecutionContext context);

    /// <summary>处理 echo 命令</summary>
    void HandleEcho(IExecutionContext context);

    /// <summary>处理 sleep 命令</summary>
    void HandleSleep(IExecutionContext context);

    /// <summary>处理 show 命令</summary>
    void HandleShow(IExecutionContext context);

    /// <summary>处理 look 命令</summary>
    void HandleLook(IExecutionContext context);

    /// <summary>处理 wait 命令</summary>
    void HandleWait(IExecutionContext context);

    /// <summary>处理 assert 命令</summary>
    void HandleAssert(IExecutionContext context);

    /// <summary>处理 discover 命令</summary>
    void HandleDiscover(IExecutionContext context);

    /// <summary>处理 list 命令</summary>
    void HandleList(IExecutionContext context);

    /// <summary>处理 validate 命令</summary>
    void HandleValidate(IExecutionContext context);

    /// <summary>处理 exit 命令</summary>
    void HandleExit(IExecutionContext context);

    /// <summary>处理 sideload 命令</summary>
    void HandleSideload(IExecutionContext context);
}
```

### 3.3 目录结构规划

```
Operation/FunctionStorage/
├── FunctionStorage.csproj
├── FunctionStorageRegistry.cs        # 统一注册入口（增强）
├── FsLog.cs                          # 日志
├── UIConnector.cs                    # UI 桥接
├── UIConnectorEventBinder.cs         # UI 事件绑定
├── WinFormsEventBinder.cs            # WinForms 事件绑定器
├── WorkflowCommandCenter.cs          # 工作流命令中心
├── WorkflowExecutionLayer.cs         # 工作流执行层
├── WorkflowHelpers.cs                # 工作流辅助
├── WorkflowIOGroup.cs                # 工作流 IO 组
├── WorkflowSkeletons.cs              # 工作流骨架
│
├── Abstractions/                     # ★ 新增：接口定义层
│   ├── IFunctionRegistry.cs          #   函数注册表接口
│   ├── IFunctionExecutor.cs          #   函数执行器接口
│   ├── IExecutionContext.cs          #   执行上下文接口
│   ├── INonEventCommandRegistry.cs   #   非事件命令注册表接口
│   ├── INonEventCommandHandler.cs    #   非事件命令处理接口
│   ├── IFunctionUnit.cs              #   函数单元接口
│   └── IFunctionExecutionResult.cs   #   执行结果接口
│
├── Core/                             # ★ 新增：核心实现层
│   ├── FunctionExecutionContext.cs   #   执行上下文实现
│   ├── NonEventCommandRegistry.cs    #   非事件命令注册表实现
│   ├── NonEventCommandHandler.cs     #   非事件命令处理实现（纯逻辑部分）
│   ├── ArgumentMatcherPipeline.cs    #   参数匹配策略链
│   └── TypeConverter.cs             #   类型转换器
│
├── Libraries/                        # ★ 新增：函数库层
│   ├── MathLibrary.cs                #   数学函数库
│   ├── UIToolbox.cs                  #   UI 操作函数库
│   ├── WorkflowLibrary.cs            #   工作流函数库
│   ├── ModelLibrary.cs               #   模型查询库
│   ├── NonEventCommands.cs           #   非事件命令名称定义
│   ├── BuiltInUnitsLibrary.cs        #   内置类型转换单元库
│   └── CodePathLibrary.cs            #   （待清理或替换）
│
├── Models/                           # 数据模型
│   └── WorkflowDefinition.cs
│
└── obj/
```

---

## 四、实施步骤

### 阶段一：接口定义（P0）

**目标：** 定义清晰的接口层，作为 FunctionStorage 的契约基础。

| 步骤 | 内容 | 产出 |
|------|------|------|
| 1.1 | 创建 `Abstractions/` 目录 | 目录结构 |
| 1.2 | 定义 `IFunctionRegistry` 接口 | `IFunctionRegistry.cs` |
| 1.3 | 定义 `IFunctionExecutor` 接口 | `IFunctionExecutor.cs` |
| 1.4 | 定义 `IExecutionContext` 接口 | `IExecutionContext.cs` |
| 1.5 | 定义 `INonEventCommandRegistry` 接口 | `INonEventCommandRegistry.cs` |
| 1.6 | 定义 `INonEventCommandHandler` 接口 | `INonEventCommandHandler.cs` |
| 1.7 | 定义 `IFunctionUnit` 接口 | `IFunctionUnit.cs` |
| 1.8 | 定义 `IFunctionExecutionResult` 接口 | `IFunctionExecutionResult.cs` |

### 阶段二：核心实现（P0）

**目标：** 实现核心接口，构建 FunctionStorage 的核心能力。

| 步骤 | 内容 | 产出 |
|------|------|------|
| 2.1 | 创建 `Core/` 目录 | 目录结构 |
| 2.2 | 实现 `FunctionExecutionContext` | 上下文实现 |
| 2.3 | 实现 `NonEventCommandRegistry` | 非事件命令注册表 |
| 2.4 | 实现 `NonEventCommandHandler`（纯逻辑部分） | 非事件命令处理 |
| 2.5 | 增强 `FunctionStorageRegistry` 实现新接口 | 增强的注册入口 |
| 2.6 | 实现参数匹配策略链 | `ArgumentMatcherPipeline.cs` |
| 2.7 | 实现类型转换器 | `TypeConverter.cs` |

### 阶段三：函数库迁移（P0）

**目标：** 将非事件命令相关的注册逻辑从 `FileDriven.Execution` 迁移到 `FunctionStorage`。

| 步骤 | 内容 | 产出 |
|------|------|------|
| 3.1 | 创建 `Libraries/` 目录 | 目录结构 |
| 3.2 | 迁移 `NonEventCommands` 到 `Libraries/` | `NonEventCommands.cs` |
| 3.3 | 迁移 `NonEventCommandHandler` 纯逻辑部分 | 增强的 `NonEventCommandHandler` |
| 3.4 | 创建 `BuiltInUnitsLibrary` | `BuiltInUnitsLibrary.cs` |
| 3.5 | 更新 `UIToolbox` 消除标记函数 | 增强的 `UIToolbox` |
| 3.6 | 清理 `CodePathLibrary` | 删除或替换 |

### 阶段四：FileDriven.Execution 适配（P1）

**目标：** 更新 `FileDriven.Execution` 使用新的 FunctionStorage 接口。

| 步骤 | 内容 | 产出 |
|------|------|------|
| 4.1 | 更新 `NonEventCommandRegistry` 为轻量包装器 | 适配后的注册中心 |
| 4.2 | 更新 `NonEventCommandHandler` 依赖上下文的部分 | 适配后的处理逻辑 |
| 4.3 | 更新 `PipelineFactory.RegisterBuiltInCommands()` | 适配后的注册调用 |
| 4.4 | 更新 `FileDrivenExecutor.InitializeFramework()` | 适配后的初始化流程 |

### 阶段五：错误处理框架（P1）

**目标：** 构建统一的错误处理框架。

| 步骤 | 内容 | 产出 |
|------|------|------|
| 5.1 | 定义错误码枚举 | `FunctionErrorCode.cs` |
| 5.2 | 定义函数执行异常 | `FunctionExecutionException.cs` |
| 5.3 | 实现重试机制 | `RetryPolicy.cs` |
| 5.4 | 集成到执行器 | 增强的 `FunctionExecutor` |

### 阶段六：测试与文档（P2）

**目标：** 确保框架质量，编写使用文档。

| 步骤 | 内容 | 产出 |
|------|------|------|
| 6.1 | 编写单元测试 | 测试用例 |
| 6.2 | 编写集成测试 | 集成测试 |
| 6.3 | 编写使用文档 | `README.md` |
| 6.4 | 编写迁移指南 | `MIGRATION_GUIDE.md` |

---

## 五、关键设计决策

### 5.1 上下文抽象

**决策：** 定义 `IExecutionContext` 接口，替代 `FileDrivenPipelineContext` 的硬依赖。

**原因：** `NonEventCommandHandler` 中的处理逻辑（如 `HandleEcho`、`HandleSleep`）实际上只需要上下文中的少量字段（`ParsedTarget`、`FunctionResultValue`、`FunctionExecuteSuccess` 等），不需要完整的 `FileDrivenPipelineContext`。通过接口抽象，这些逻辑可以安全地迁移到 `FunctionStorage` 中。

**适配方式：** `FileDrivenPipelineContext` 实现 `IExecutionContext` 接口，在 `FileDriven.Execution` 中通过适配器模式将完整上下文传递给 `FunctionStorage` 中的处理逻辑。

### 5.2 标记函数消除

**决策：** 将 `UIToolbox` 中的 12 个非事件命令标记函数替换为真实函数。

**原因：** 标记函数是临时方案，其注释明确说明"实际逻辑在 NonEventCommandHandler"。通过上下文抽象，这些函数可以直接调用 `NonEventCommandHandler` 中的处理逻辑。

**实现方式：** `UIToolbox.RegisterAll()` 中注册的函数直接委托给 `NonEventCommandHandler` 的对应方法，不再需要 `FileDriven.Execution` 中的 switch 分支。

### 5.3 注册中心统一

**决策：** `FunctionStorageRegistry` 同时管理函数注册和非事件命令注册。

**原因：** 函数注册和非事件命令注册本质上是同一件事——将名称映射到处理逻辑。统一管理可以消除"标记函数"和"真实函数"的割裂。

**实现方式：** `FunctionStorageRegistry` 内部持有 `IFunctionRegistry` 和 `INonEventCommandRegistry` 两个实例，对外提供统一的注册入口。

### 5.4 向后兼容

**决策：** 所有接口变更保持向后兼容，不破坏现有调用方。

**原因：** `FunctionStorage` 被 `FileDriven.Execution`、`WdUI`、`WorkFlowMethod` 等多个项目引用，需要确保迁移过程中不影响现有功能。

**实现方式：** 
- 旧 API 标记为 `[Obsolete]` 并保留一个版本周期
- 新 API 通过扩展方法或适配器提供
- 逐步迁移，每个阶段可独立发布

---

## 六、迁移后的调用链

### 6.1 框架初始化

```
FileDrivenExecutor.InitializeFramework()
    ├── FunctionStorageRegistry.Initialize(registry, executor)
    ├── MathLibrary.RegisterAll()           → FunctionStorageRegistry.Register(...)
    ├── UIToolbox.RegisterAll()             → FunctionStorageRegistry.Register(...)
    │   └── 非事件命令 → NonEventCommandRegistry.Register(...)
    ├── WorkflowLibrary.RegisterAll()       → FunctionStorageRegistry.Register(...)
    ├── BuiltInUnitsLibrary.RegisterAll()   → FunctionStorageRegistry.Register(...)
    └── NonEventCommandRegistry.ValidateAll()
```

### 6.2 命令执行

```
命令字符串 "echo Hello"
    │
    ▼
CommandParser.Parse() → 识别为非事件命令
    │
    ▼
NonEventCommandHandler.Handle(context)
    │
    ├── NonEventCommandRegistry.TryGetHandler("echo", out handler)
    │       └── 注册中心在 FunctionStorage 中
    │
    └── handler.Invoke(context)
            └── NonEventCommandHandler.HandleEcho(context)
                    └── 处理逻辑在 FunctionStorage 中
```

### 6.3 函数执行

```
FunctionStorageRegistry.ExecuteAsync("Add", new { a = 1, b = 2 })
    │
    ├── FunctionRegistry.TryGetDescriptor("Add", out descriptor)
    ├── ArgumentMatcherPipeline.BuildArguments(descriptor, parameters)
    ├── TypeConverter.ConvertArguments(arguments, descriptor)
    └── Delegate.DynamicInvoke(convertedArguments)
```

---

## 七、风险评估

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|----------|
| 接口定义不完善导致后续重构 | 高 | 中 | 充分评审接口设计，参考现有使用场景 |
| 上下文抽象遗漏关键字段 | 中 | 中 | 全面梳理 `FileDrivenPipelineContext` 的使用点 |
| 迁移过程中破坏现有功能 | 高 | 低 | 保持向后兼容，逐步迁移，充分测试 |
| 项目依赖关系变更导致编译错误 | 中 | 低 | 先定义接口再实现，确保依赖方向正确 |
| 性能下降（增加接口调用层） | 低 | 低 | 接口调用开销极小，必要时可使用直接调用 |

---

## 八、总结

本方案将 `FunctionStorage` 从"函数存储框架"升级为"通用函数注册与执行框架"，通过定义清晰的接口层、实现核心逻辑、迁移非事件命令注册，使其成为系统中所有函数注册、查询、执行的单一真相来源。

**关键变化：**
1. 新增 `Abstractions/` 接口定义层（8 个接口）
2. 新增 `Core/` 核心实现层（5 个实现类）
3. 新增 `Libraries/` 函数库层（6 个库）
4. 消除 `UIToolbox` 中的 12 个标记函数
5. 通过 `IExecutionContext` 接口消除对 `FileDrivenPipelineContext` 的依赖
6. `FunctionStorage` 不再依赖任何上层项目

**实施优先级：** 阶段一（接口定义）→ 阶段二（核心实现）→ 阶段三（函数库迁移）→ 阶段四（适配）→ 阶段五（错误处理）→ 阶段六（测试文档）
