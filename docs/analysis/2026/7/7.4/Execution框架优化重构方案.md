# Execution 框架优化重构方案

> **分析日期**：2026-07-04  
> **分析范围**：`CsharpPrj/Share/CommandFramework/Execution/` 全部源文件（30+ 文件）  
> **核心理念**：通用框架层只提供抽象和机制，不绑定具体业务

---

## 目录

- [Execution 框架优化重构方案](#execution-框架优化重构方案)
  - [目录](#目录)
  - [1. 当前架构全景](#1-当前架构全景)
  - [2. 六大问题与重构方案](#2-六大问题与重构方案)
    - [2.1 两个 Pipeline 体系并存 — 职责重叠](#21-两个-pipeline-体系并存--职责重叠)
      - [现状](#现状)
      - [症状](#症状)
      - [重构方案](#重构方案)
      - [收益](#收益)
    - [2.2 结果类型体系混乱 — 4 种结果类型并存](#22-结果类型体系混乱--4-种结果类型并存)
      - [现状](#现状-1)
      - [症状](#症状-1)
      - [重构方案](#重构方案-1)
      - [收益](#收益-1)
    - [2.3 状态管理职责边界模糊](#23-状态管理职责边界模糊)
      - [现状](#现状-2)
      - [症状](#症状-2)
      - [重构方案](#重构方案-2)
      - [收益](#收益-2)
    - [2.4 适配器模式重复](#24-适配器模式重复)
      - [现状](#现状-3)
      - [症状](#症状-3)
      - [重构方案](#重构方案-3)
      - [收益](#收益-3)
    - [2.5 命名空间不一致](#25-命名空间不一致)
      - [现状](#现状-4)
      - [重构方案](#重构方案-4)
      - [收益](#收益-4)
    - [2.6 废弃代码未清理](#26-废弃代码未清理)
      - [现状](#现状-5)
      - [处理方式](#处理方式)
  - [3. 重构优先级与步骤](#3-重构优先级与步骤)
    - [执行顺序建议](#执行顺序建议)
  - [4. 核心设计理念](#4-核心设计理念)
    - [4.1 Pipeline 通用化](#41-pipeline-通用化)
    - [4.2 Result 统一化](#42-result-统一化)
    - [4.3 状态分层化](#43-状态分层化)
    - [4.4 适配器合并化](#44-适配器合并化)
  - [附录：重构实施记录](#附录重构实施记录)
    - [Phase 1 — 已完成 ✅](#phase-1--已完成-)
    - [Phase 2 — 待实施 ⏳](#phase-2--待实施-)
    - [补充修复记录](#补充修复记录)

---

## 1. 当前架构全景

```
Execution/
├── CommandEngine.cs                          # 命令引擎（注册/执行/撤销/重做）
├── StateStoreExtensions.cs                   # 状态存储扩展 + ObservableStateStore
│
├── Abstractions/                             # 接口定义层
│   ├── CommandPipeline/                      #   8 个接口
│   │   ├── IControlFinder.cs                 #   通用控件查找器
│   │   ├── IControlValueWriter.cs            #   控件值读写器
│   │   ├── IEventBindingResolver.cs          #   事件绑定解析器
│   │   ├── IEventTypeMapper.cs               #   事件类型映射器
│   │   ├── INonEventCommandLookup.cs         #   非事件命令查找（轻量）
│   │   ├── INonEventCommandRegistry.cs       #   非事件命令注册表
│   │   ├── IScriptConverter.cs               #   脚本转换器
│   │   └── IUIController.cs                  #   UI 控制器
│   └── WorkflowEngine/                       #   2 个接口
│       ├── IPipelineStage.cs                 #   （文件缺失）
│       └── INodeRepository.cs                #   （文件缺失）
│
├── CommandPipeline/                          # 命令管线实现
│   ├── ControlValueAccessorBase.cs           #   控件值访问器基类
│   ├── EnvironmentAdapterBase.cs             #   环境适配器基类
│   ├── Libraries/
│   │   └── NonEventCommands.cs               #   非事件命令名称定义
│   ├── Models/
│   │   └── CommandResults.cs                 #   命令执行结果模型
│   ├── Parsing/                              #   （外部目录）
│   └── Validators/
│       ├── CommandContextValidator.cs         #   CommandContext 验证器
│       └── ParsedCommandValidator.cs          #   ParsedCommand 验证器
│
├── Shared/                                   # 共享组件
│   ├── FunctionExecution.cs                  #   函数执行器
│   ├── Abstractions/
│   │   ├── ICommand.cs                       #   命令接口 + 管线接口 + 状态存储接口
│   │   ├── IExecutionContext.cs              #   执行上下文接口
│   │   └── IStateQuery.cs                    #   状态查询器接口
│   ├── Parameters/
│   │   ├── ParameterResolver.cs              #   参数解析器实现
│   │   └── Abstractions/
│   │       └── IParameterResolver.cs         #   参数解析器接口
│   ├── StateManagement/
│   │   └── InMemoryStateStore.cs             #   内存状态存储
│   └── Utilities/                            #   （空目录）
│
└── WorkflowEngine/                           # 工作流引擎
    ├── WorkflowEngine.cs                     #   工作流引擎
    ├── CentralPipeline.cs                    #   中央管线
    ├── PipelineStages.cs                     #   阶段工厂
    ├── FuncCallRun.cs                        #   函数管线执行
    ├── Models/
    │   ├── WorkflowDefinition.cs             #   工作流定义
    │   └── NodeDefinition.cs                 #   节点定义
    ├── PlaceholderResolution/
    │   └── PlaceholderEngine.cs              #   占位符解析引擎 [Obsolete]
    ├── Runtime/
    │   └── RuntimeContextManager.cs           #   运行时上下文管理器
    └── Skeletons/
        ├── NodeChainSkeleton.cs              #   节点链骨架
        └── NodeProcessSkeleton.cs            #   节点处理骨架
```

---

## 2. 六大问题与重构方案

### 2.1 两个 Pipeline 体系并存 — 职责重叠

#### 现状

当前存在两套独立的 Pipeline 体系：

| 体系 | 位置 | 核心类型 |
|------|------|---------|
| **泛型管线** | `Shared/Abstractions/ICommand.cs` | `ICommandPipeline<TContext>`, `IPipelineStage<TContext>` |
| **工作流管线** | `WorkflowEngine/` | `CentralPipeline`, `IPipelineStage`（非泛型） |

#### 症状

- `CentralPipeline` 自己实现了动态阶段注册、脏标记重建、`InsertBefore`/`InsertAfter` 等能力
- `ICommandPipeline<TContext>` 接口也定义了 `Register`/`InsertBefore`/`InsertAfter`/`Remove`
- 两者功能高度重叠但互不关联，各自维护一套阶段管理逻辑
- `IPipelineStage`（非泛型）与 `IPipelineStage<TContext>`（泛型）命名冲突

#### 重构方案

```
将 Pipeline 抽象为通用框架能力：

  新增：
    PipelineBase<TContext>         通用管线基类（含脏标记、动态阶段、排序、InsertBefore/After）
  
  改造：
    ICommandPipeline<TContext>     增强为完整管线接口（保持向后兼容）
    CentralPipeline                继承 PipelineBase<IWorkflowContext>，只保留特化逻辑
    IPipelineStage                 统一为 IPipelineStage<TContext>，消除非泛型版本
    StageRunner                    保留为内部辅助类
```

**PipelineBase 核心设计**：

```csharp
public abstract class PipelineBase<TContext>
{
    // 固定阶段（构造函数中设置）
    private readonly List<IPipelineStage<TContext>> _fixedStages;
    
    // 动态阶段（运行时注册）
    private readonly ConcurrentDictionary<string, IPipelineStage<TContext>> _dynamicStages;
    
    // 脏标记 + 懒加载
    private List<IPipelineStage<TContext>>? _resolvedStages;
    private bool _dirty = true;
    
    // 公共方法
    public void RegisterStage(string name, IPipelineStage<TContext> stage);
    public bool RemoveStage(string name);
    public void ClearDynamicStages();
    public IPipelineStage<TContext>? GetStage(string name);
    public bool InsertBefore(string targetName, IPipelineStage<TContext> newStage);
    public bool InsertAfter(string targetName, IPipelineStage<TContext> newStage);
    public void MarkDirty();
    public void RebuildPipeline();
    
    // 子类重写
    protected abstract Task<ExecutionResult> ExecuteAsync(
        TContext context, CancellationToken cancellationToken);
}
```

**CentralPipeline 改造后**：

```csharp
public class CentralPipeline : PipelineBase<IWorkflowContext>
{
    // 只需关注工作流特有的逻辑
    protected override async Task<ExecutionResult> ExecuteAsync(
        IWorkflowContext context, CancellationToken cancellationToken)
    {
        // 状态机管理 + 遍历阶段执行
        // 不再需要自己实现阶段管理逻辑
    }
}
```

#### 收益

- 消除重复的阶段管理逻辑
- `CentralPipeline` 减少约 40% 代码
- 新的 Pipeline 实现只需继承 `PipelineBase<TContext>`

---

### 2.2 结果类型体系混乱 — 4 种结果类型并存

#### 现状

| 类型 | 来源 | 用途 |
|------|------|------|
| `ICommandExecutionResult` | `CommandResults.cs` | 命令执行结果接口 |
| `CommandExecutionResult<T>` | `CommandResults.cs` | 泛型命令执行结果 |
| `IFunctionExecutionResult` | 外部引用（TConfig） | 函数执行结果接口 |
| `ExecutionResult`（`TConfig.ExecutionResult`） | TConfig 命名空间 | 工作流执行结果 |
| `BasicExecutionResult` | using 别名 | 与 `ExecutionResult` 相同 |

#### 症状

- `CommandExecutionResult<T>` 同时实现 `ICommandExecutionResult` 和 `IFunctionExecutionResult`
- `BasicExecutionResult` 只是 `TConfig.ExecutionResult` 的别名，分散在 5 个文件中
- 类型转换和适配增加了不必要的复杂性
- `ICommandExecutionResult` 的 `Value` 属性是 `string?`，而 `CommandExecutionResult<T>.Value` 是 `T?`，接口与实现语义不一致

#### 重构方案

```
统一为 ExecutionResult<T> 模式：

  保留并增强：
    ExecutionResult<T>             泛型结果（取代 CommandExecutionResult<T>）
  
  新增：
    ExecutionResult                非泛型结果（取代 BasicExecutionResult 别名）
  
  消除：
    IFunctionExecutionResult       接口（已被 ExecutionResult<T> 覆盖）
    BasicExecutionResult           别名（直接使用 ExecutionResult）
    ICommandExecutionResult        接口（已被 ExecutionResult<T> 覆盖）
```

**统一后的结果类型**：

```csharp
public class ExecutionResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
    
    public static ExecutionResult<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static ExecutionResult<T> Failure(string error, string? errorCode = null) 
        => new() { IsSuccess = false, Error = error, ErrorCode = errorCode };
}

public class ExecutionResult : ExecutionResult<object?>
{
    public static new ExecutionResult Success() => new() { IsSuccess = true };
    public static new ExecutionResult Failure(string error, string? errorCode = null) 
        => new() { IsSuccess = false, Error = error, ErrorCode = errorCode };
    public static ExecutionResult Cancelled() => new() { IsSuccess = false, Error = "Cancelled" };
}
```

#### 收益

- 消除 2 个冗余接口和 1 个别名
- 结果处理路径唯一
- 命令、函数、工作流三层共用同一结果类型

---

### 2.3 状态管理职责边界模糊

#### 现状

| 组件 | 职责 | 问题 |
|------|------|------|
| `IStateStore` | 通用键值存储 | 职责清晰 ✅ |
| `IStateQuery` | 查询运行时状态 | 与 `IStateStore` 有重叠 ❌ |
| `ParameterResolver` | 参数合并与节点结果存储 | 混入了状态管理职责 ❌ |
| `ObservableStateStore` | 带事件通知的存储包装 | 职责清晰 ✅ |

#### 症状

- `ParameterResolver` 内部用 `ConcurrentDictionary` 存储执行参数和节点结果，本质是一个带 `executionId` 分区的状态存储
- `IStateQuery` 的 `HasControlValue()` / `GetLoadedConfigNames()` 与 `IStateStore` 的 `Has()` / `GetAllKeys()` 语义重叠
- `ParameterResolver` 同时承担"参数合并"和"结果存储"两个职责

#### 重构方案

```
状态管理三层抽象：

  存储层：
    IStateStore                     键值存储（保持现状）
    InMemoryStateStore              实现（保持现状）
  
  分区层（新增）：
    IPartitionedStateStore          带 executionId 分区的状态存储
    InMemoryPartitionedStateStore   实现（替代 ParameterResolver 的存储职责）
  
  查询层：
    IStateQuery                     跨组件运行时状态查询（保持现状，明确职责边界）

  ParameterResolver 拆分为：
    - 参数合并逻辑保留在 ParameterResolver
    - 存储职责委托给 IPartitionedStateStore
```

**IPartitionedStateStore 设计**：

```csharp
public interface IPartitionedStateStore
{
    void Set(string partitionId, string key, object value);
    object? Get(string partitionId, string key);
    IDictionary<string, object> GetAll(string partitionId);
    void StoreResult(string partitionId, string nodeKey, object result);
    object? GetResult(string partitionId, string nodeKey);
    IDictionary<string, object> GetAllResults(string partitionId);
    void Cleanup(string partitionId);
}
```

**ParameterResolver 改造后**：

```csharp
public class ParameterResolver : IParameterResolver
{
    private readonly IPartitionedStateStore _stateStore;
    
    public ParameterResolver(IPartitionedStateStore stateStore)
    {
        _stateStore = stateStore;
    }
    
    // 只保留参数合并逻辑
    public IDictionary<string, object> MergeParameters(
        string executionId, IDictionary<string, object>? nodeParameters)
    {
        // 合并执行参数 + 节点参数
    }
    
    // 存储操作委托给 _stateStore
    public void StoreNodeResult(string executionId, string nodeKey, NodeExecutionResult result)
        => _stateStore.StoreResult(executionId, nodeKey, result.Result);
}
```

#### 收益

- `ParameterResolver` 减少约 50% 代码
- 职责单一化，每个组件只做一件事
- 分区存储可被其他需要 `executionId` 分区的场景复用

---

### 2.4 适配器模式重复

#### 现状

| 接口 | 方法 | 注册方式 |
|------|------|---------|
| `IControlValueAccessor` | `GetValue` / `SetValue` / `IsSupported` | `RegisterAdapter<TControl>` |
| `IControlValueWriter` | `WriteValue` / `ReadValue` / `SupportsType` | `RegisterAdapter<TControl>` |

#### 症状

- 两个接口功能高度重叠（读/写控件值 + 注册适配器）
- `ControlValueAccessorBase` 有 163 行实现，`IControlValueWriter` 只有接口定义
- 调用方需要同时了解两个接口
- `IControlValueWriter.WriteValue(object, object?)` 与 `IControlValueAccessor.SetValue(object, object?)` 语义完全相同

#### 重构方案

```
合并为 IControlValueAccessor（保留更完整的接口）：

  保留：
    IControlValueAccessor           完整接口（GetValue/SetValue/IsSupported/RegisterAdapter）
    ControlValueAccessorBase        完整实现
  
  废弃/删除：
    IControlValueWriter             标记为 [Obsolete("Use IControlValueAccessor instead")]
    或直接删除（确认无外部引用后）
```

#### 收益

- 消除 1 个冗余接口
- 减少调用方的认知负担
- 适配器注册机制唯一

---

### 2.5 命名空间不一致

#### 现状

| 文件位置 | 当前命名空间 | 应统一为 |
|---------|-------------|---------|
| `WorkflowEngine/WorkflowEngine.cs` | `WorkflowExecution.Engine` | `CommandFramework.Execution.WorkflowEngine` |
| `WorkflowEngine/CentralPipeline.cs` | `WorkflowExecution.Pipeline` | `CommandFramework.Execution.WorkflowEngine` |
| `WorkflowEngine/FuncCallRun.cs` | `WorkflowExecution.Execution` | `CommandFramework.Execution.WorkflowEngine` |
| `WorkflowEngine/Skeletons/` | `WorkflowExecution.Skeletons` | `CommandFramework.Execution.WorkflowEngine.Skeletons` |
| `WorkflowEngine/Runtime/` | `WorkflowExecution.Runtime` | `CommandFramework.Execution.WorkflowEngine.Runtime` |
| `WorkflowEngine/PlaceholderResolution/` | `WorkflowExecution.PlaceholderResolution` | `CommandFramework.Execution.WorkflowEngine.PlaceholderResolution` |
| `CommandPipeline/` | `CommandFramework.Execution` | ✅ 已一致 |
| `Shared/` | `CommandFramework.Execution` | ✅ 已一致 |

#### 重构方案

```
将所有 WorkflowExecution.* 命名空间迁移到 CommandFramework.Execution.WorkflowEngine.*
```

具体映射：

| 原命名空间 | 目标命名空间 |
|-----------|-------------|
| `WorkflowExecution.Engine` | `CommandFramework.Execution.WorkflowEngine` |
| `WorkflowExecution.Pipeline` | `CommandFramework.Execution.WorkflowEngine` |
| `WorkflowExecution.Execution` | `CommandFramework.Execution.WorkflowEngine` |
| `WorkflowExecution.Models` | `CommandFramework.Execution.WorkflowEngine.Models` |
| `WorkflowExecution.Skeletons` | `CommandFramework.Execution.WorkflowEngine.Skeletons` |
| `WorkflowExecution.Runtime` | `CommandFramework.Execution.WorkflowEngine.Runtime` |
| `WorkflowExecution.PlaceholderResolution` | `CommandFramework.Execution.WorkflowEngine.PlaceholderResolution` |
| `WorkflowExecution.Abstractions` | `CommandFramework.Execution.WorkflowEngine.Abstractions` |

#### 收益

- 消除命名空间混乱
- IDE 导航更清晰
- 与项目其他模块命名风格一致

---

### 2.6 废弃代码未清理

#### 现状

| 代码 | 标记 | 状态 |
|------|------|------|
| `PlaceholderEngine` | `[Obsolete("...保留仅为向后兼容")]` | 确认无引用后可删除 |
| `PlaceholderResolutionContext` | 无标记 | 仅被 `PlaceholderEngine` 使用，一并删除 |
| `INodeRepository` | 无标记 | 文件在 list_files 中显示但 read_file 报错，需确认 |
| `IPipelineStage`（WorkflowEngine 版） | 无标记 | 文件在 list_files 中显示但 read_file 报错，需确认 |

#### 处理方式

1. **PlaceholderEngine + PlaceholderResolutionContext**：搜索整个解决方案确认无引用后删除
2. **INodeRepository / IPipelineStage**：确认文件实际状态后清理引用

---

## 3. 重构优先级与步骤

| 优先级 | 重构项 | 涉及文件 | 预估工作量 |
|--------|--------|---------|-----------|
| **P0** | 统一 Pipeline 体系（`PipelineBase<TContext>` + `CentralPipeline` 继承） | `ICommand.cs`, `CentralPipeline.cs`, `PipelineStages.cs`, `IPipelineStage` | 4 文件 |
| **P0** | 统一结果类型体系（消除 `BasicExecutionResult` 别名） | `CommandResults.cs`, `CentralPipeline.cs`, `PipelineStages.cs`, `NodeChainSkeleton.cs`, `NodeProcessSkeleton.cs` | 5 文件 |
| **P1** | 合并 `IControlValueAccessor` / `IControlValueWriter` | `IControlValueWriter.cs`, `ControlValueAccessorBase.cs` | 2 文件 |
| **P1** | `ParameterResolver` 拆分（存储职责委托给 `IPartitionedStateStore`） | `ParameterResolver.cs`, `IParameterResolver.cs` | 2-3 文件 |
| **P2** | 统一命名空间（`WorkflowExecution.*` → `CommandFramework.*`） | 6 文件 | 6 文件 |
| **P2** | 清理废弃代码（`PlaceholderEngine` 等） | 2 文件 | 2 文件 |

### 执行顺序建议

```
Phase 1 (P0) — 核心架构统一
  ├── 统一结果类型体系（消除别名）
  └── 统一 Pipeline 体系（PipelineBase + CentralPipeline 继承）

Phase 2 (P1) — 职责边界清晰化
  ├── 合并 IControlValueAccessor / IControlValueWriter
  └── ParameterResolver 拆分

Phase 3 (P2) — 代码整洁
  ├── 统一命名空间
  └── 清理废弃代码
```

---

## 4. 核心设计理念

整个重构围绕 **"通用框架层只提供抽象和机制，不绑定具体业务"** 的理念：

### 4.1 Pipeline 通用化

```
PipelineBase<TContext> 提供：
  ├── 阶段注册/移除/清空（动态插拔）
  ├── 脏标记 + 懒加载重建
  ├── InsertBefore/InsertAfter（顺序控制）
  └── 排序（按 Order 属性）

CentralPipeline 只需特化：
  ├── 状态机管理（Pending → Running → Completed/Failed）
  └── 工作流特有的初始化/最终化逻辑
```

### 4.2 Result 统一化

```
一个 ExecutionResult<T> 贯穿三层：
  ├── 命令层：CommandEngine.ExecuteAsync 返回 ExecutionResult
  ├── 函数层：FunctionExecutor.ExecuteAsync 返回 ExecutionResult<T>
  └── 工作流层：CentralPipeline.ExecuteAsync 返回 ExecutionResult
```

### 4.3 状态分层化

```
三层各司其职：
  ├── 存储层（IStateStore）：纯粹的键值存储
  ├── 分区层（IPartitionedStateStore）：带 executionId 分区的存储
  └── 查询层（IStateQuery）：跨组件的运行时状态查询
```

### 4.4 适配器合并化

```
一个 IControlValueAccessor 覆盖所有场景：
  ├── 读值：GetValue / GetValue<TResult>
  ├── 写值：SetValue
  ├── 类型检查：IsSupported
  └── 扩展：RegisterAdapter / RegisterAdapterWithResult
```

---

> **文档版本**：v1.1  
> **状态**：部分已实施

---

## 附录：重构实施记录

### Phase 1 — 已完成 ✅

| 重构项 | 状态 | 涉及文件 | 说明 |
|--------|------|---------|------|
| 统一结果类型体系 | ✅ 已完成 | `CommandResults.cs` | 新增 `ExecutionResult` / `ExecutionResult<T>` 轻量包装，标记 `CommandExecutionResult<T>` 为 `[Obsolete]` |
| 统一 Pipeline 体系 | ✅ 已完成 | `PipelineBase.cs`, `CentralPipeline.cs` | 新增 `PipelineBase<TContext>` 通用基类，`CentralPipeline` 继承并消除重复的阶段管理逻辑 |
| 统一编排入口 | ✅ 已完成 | `ExecutionOrchestrator.cs` | 新增 `ExecutionOrchestrator` 实现 `IExecutionOrchestrator`，包装 `CentralPipeline` |
| ParameterResolver 拆分 | ✅ 已完成 | `IPartitionedStateStore.cs`, `PartitionedStateStore.cs`, `ParameterResolver.cs` | 新增 `IPartitionedStateStore` / `PartitionedStateStore`，`ParameterResolver` 注入使用 |

### Phase 2 — 待实施 ⏳

| 重构项 | 状态 | 说明 |
|--------|------|------|
| 合并 `IControlValueAccessor` / `IControlValueWriter` | ⏳ 待实施 | 用户确认保留 `IControlValueWriter` 现状 |
| 统一命名空间 | ⏳ 待实施 | `WorkflowExecution.*` → `CommandFramework.Execution.WorkflowEngine.*` |
| 清理废弃代码 | ⏳ 待实施 | `PlaceholderEngine` 等 |
| 构建验证 | ✅ 已完成 | `dotnet build MainProj.sln` — 0 errors, 6 warnings (仅 XML 注释问题) |
| 单元测试 | ⏳ 待实施 | 验证重构后行为一致 |

### 补充修复记录

| 修复项 | 说明 |
|--------|------|
| `FunctionExecution.cs` 命名冲突 | `ExecutionResult` 在 `CommandFramework.Execution` 命名空间内与 `CommandResults.cs` 冲突，改用 `TConfig.ExecutionResult` 完全限定名 |
| `StageRunner` 访问级别 | 从 `internal` 改为 `public`，使 `PipelineBase`（在 `CommandFramework.Execution` 命名空间）可访问 |
| `PipelineBase.cs` using 补充 | 添加 `using WorkflowExecution.Pipeline;` 引用 `StageRunner` |
| `ExecutionOrchestrator.cs` using 补充 | 添加 `using WorkflowExecution.Abstractions;` 引用 `IWorkflowLogger` |
| `FunctionStorageRegistry.cs` 别名 | 添加 `using ExecutionResult = TConfig.ExecutionResult;` 消除 `CommandFramework.Execution.ExecutionResult` 与 `TConfig.ExecutionResult` 的歧义 |


