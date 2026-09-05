# ExecutionCenter ECS 运算矩阵重构方案

> **编写日期**：2026-07-28  
> **状态**：方案设计  
> **关联文档**：`docs/Refactoring/FileDriven_CLI_GUI_统一执行端口重构方案.md`（应用层重构）  
> **引用框架**：`CsharpPrj/BasicMethod/ECS/ECSFrame.cs`（ECS.Core）

---

## 一、现状与问题

### 1.1 当前 ExecutionCenter 位置

```
CsharpPrj/Modules/ExecutionFramework/ExecutionCenter/
├── ExecutionCenterBootstrapper.cs          ← 引导程序
├── Abstractions/
│   ├── ITaskExecutorPool.cs                ← 接口
│   ├── ITaskMessage.cs                     ← 消息接口
│   └── ITaskResponse.cs                    ← 响应接口
├── Core/
│   ├── TaskExecutorPool.cs                 ← 执行池实现
│   └── TaskQueue.cs                        ← 优先级队列
└── Messages/
    ├── TaskMessageBase.cs                  ← 基类
    ├── TaskResponse.cs                     ← 响应实现
    ├── FileDrivenPipelineMessage.cs        ← 唯一在使用
    ├── ExecuteCommandMessage.cs            ← 从未被消费
    └── LoadConfigMessage.cs                ← 从未被消费
```

### 1.2 问题清单

| 问题 | 说明 |
|------|------|
| **无独立 csproj** | 寄生在 `ExecutionFramework` 项目内，通过 FileDriven 的 global using 扩散引用 |
| **单消费者** | 只注册了一个 `"FileDrivenPipeline"` Handler，消息队列的多消费者能力浪费 |
| **闲置消息** | `ExecuteCommandMessage` 和 `LoadConfigMessage` 定义了类但从未注册 handler |
| **类型匹配脆弱** | `ITaskResponse.Result` 是 `object?`，下游强制类型转换导致 B2 |
| **线性队列** | 只有 FIFO + 优先级，不支持任务依赖/路由/重试策略 |
| **与业务耦合** | handler 注册在 `FrameworkInitializer.RegisterExecutionCenterHandler()` 中 |

### 1.3 现有 ECS 框架基础

```
CsharpPrj/BasicMethod/ECS/
├── ECSFrame.cs                  ← EntityManager + EcsWorld + SystemBase + SystemManager
├── Archetype/
│   ├── ArchetypeManager.cs      ← Archetype 存储引擎核心
│   ├── Archetype.cs             ← Archetype 定义
│   ├── ArchetypeSignature.cs    ← 签名（组件类型组合）
│   └── Chunk.cs                 ← 列式存储块
├── Query/
│   └── EntityQuery.cs           ← EntityQueryBuilder + ForEach 扩展
├── Batch/
│   └── ChunkJob.cs              ← 批量 Chunk 操作调度
└── Components/                  ← 领域组件定义
```

---

## 二、运算矩阵设计

### 2.1 核心思想

将消息队列中的每个**任务**映射为 ECS 中的一个 **Entity**，任务的属性表示为 **Component**，处理逻辑表示为 **System**。

```
传统消息队列：  Enqueue → Queue → Dequeue → Handler → Complete
                    线性流水线

ECS 运算矩阵：  CreateEntity(添加MessageComponent) 
                       ↓
                  SchedulerSystem (按优先级排序 + 依赖解析)
                       ↓
                  RouterSystem (按 RoutingComponent 分发)
                       ↓
                  ExecutorSystem (执行实际 handler)
                       ↓
                  RetrySystem (失败重试/死信)
                       ↓
                  MonitorSystem (指标采集)
                矩阵化 System 网络
```

### 2.2 运算矩阵的"矩阵"含义

```
                        System_A  System_B  System_C  System_D    ← 列=运算节点
                        (调度)    (路由)    (执行)    (监控)
                       
Entity_1(FileDriven):     ✓        ✓         ✓        ✓
Entity_2(Command):        ✓        ✓         ✓        ✓
Entity_3(LoadConfig):     ✓        ✓         ✓        ✓
Entity_4(...):            ✓        ✓         ✓        ✓

    行=任务Entity
    单元格 = 该 Entity 的 Component 被对应 System 读写
    矩阵运算 = 每个 System 更新其关注的组件列
```

**列式存储天然适配矩阵运算**：
- Chunk 的 `_componentColumns` (`Dictionary<int, Array>`) 就是矩阵的列
- `ArchetypeQuery` 筛选行（Entity）
- `ChunkJob.ForEach` 遍历行并执行运算
- 不同 System 操作不同的 Component 列，天然无锁

### 2.3 组件定义

```csharp
// 任务载荷 — 原始消息数据
public struct TaskPayloadComponent : IComponent
{
    public string TaskType;           // "FileDrivenPipeline" / "ExecuteCommand" / "LoadConfig"
    public string PayloadJson;        // 序列化的消息数据
    public DateTime CreatedAt;
}

// 任务状态
public struct TaskStatusComponent : IComponent
{
    public enum State { Pending, Running, Succeeded, Failed, Retrying, Dead }
    public State CurrentState;
    public int RetryCount;
    public int MaxRetries;            // 默认 3
    public string? LastError;
}

// 任务优先级
public struct TaskPriorityComponent : IComponent
{
    public int Priority;              // 0=Critical, 1=High, 2=Normal, 3=Low
}

// 任务路由
public struct TaskRoutingComponent : IComponent
{
    public string TargetEnvironment;   // "CLI" / "GUI" / "Sandbox"
    public string HandlerName;         // handler 标识
}

// 任务依赖
public struct TaskDependencyComponent : IComponent
{
    public Entity[] Dependencies;      // 前置任务 Entity
    public bool AllDependenciesMet;    // 缓存结果
}

// 任务结果
public struct TaskResultComponent : IComponent
{
    public bool Success;
    public string? ErrorMessage;
    public object? ResultValue;        // 强类型在 System 内部转换
    public DateTime CompletedAt;
}

// 任务监控
public struct TaskMetricComponent : IComponent
{
    public TimeSpan QueuedDuration;
    public TimeSpan ExecutionDuration;
    public int CpuCycles;              // 近似值
}
```

### 2.4 System 定义

```csharp
// === System 1: 调度器 ===
// 职责：从 Pending 状态的任务中选择下一个执行的
// 查询条件：WithAll<TaskStatusComponent, TaskPriorityComponent, TaskRoutingComponent>
//           WithNone<TaskResultComponent>
// 排序：按 PriorityComponent.Priority 升序，按 CreatedAt 降序
// 输出：标记 Running
class SchedulerSystem : SystemBase
{
    public override void Update(float deltaTime)
    {
        // 查询所有 Pending 任务
        EntityManager.CreateQuery()
            .WithAll<TaskStatusComponent, TaskPriorityComponent>()
            .ForEach((ChunkAccessor<TaskStatusComponent, TaskPriorityComponent> accessor) =>
            {
                for (int i = 0; i < accessor.Count; i++)
                {
                    ref var status = ref accessor.GetComponent1(i);
                    if (status.CurrentState == TaskStatusComponent.State.Pending)
                    {
                        // 检查依赖
                        if (CheckDependencies(entity))  // 实现在另一个 Component
                        {
                            status.CurrentState = TaskStatusComponent.State.Running;
                        }
                    }
                }
            });
    }
}

// === System 2: 路由器 ===
// 职责：根据 RoutingComponent 将任务分发到目标执行器
// 查询条件：WithAll<TaskRoutingComponent, TaskStatusComponent>
//           状态 = Running
// 分发：按 HandlerName 查找注册的执行器
class RouterSystem : SystemBase
{
    private readonly Dictionary<string, Func<Entity, ChunkAccessor, Task>> _handlers;
    
    public override void Update(float deltaTime)
    {
        EntityManager.CreateQuery()
            .WithAll<TaskRoutingComponent, TaskStatusComponent>()
            .ForEach<TaskRoutingComponent, TaskStatusComponent>(accessor =>
            {
                for (int i = 0; i < accessor.Count; i++)
                {
                    ref var status = ref accessor.GetComponent2(i);
                    if (status.CurrentState != TaskStatusComponent.State.Running) continue;
                    
                    var routing = accessor.GetComponent1(i);
                    if (_handlers.TryGetValue(routing.HandlerName, out var handler))
                    {
                        var entity = accessor.GetEntity(i);
                        // 异步执行 handler
                        Task.Run(() => handler(entity, accessor));
                    }
                }
            });
    }
}

// === System 3: 执行器 ===
// 职责：调用实际 handler 执行业务逻辑
// 查询条件：由 RouterSystem 触发，直接操作 Entity 的 TaskPayloadComponent
abstract class ExecutorSystemBase : SystemBase
{
    protected abstract string HandlerName { get; }
    protected abstract Task ExecuteAsync(Entity entity, TaskPayloadComponent payload);
    
    public override void Update(float deltaTime) { }
    // RouterSystem 通过反射或委托调用 ExecuteAsync
}

class FileDrivenPipelineExecutor : ExecutorSystemBase
{
    protected override string HandlerName => "FileDrivenPipeline";
    protected override async Task ExecuteAsync(Entity entity, TaskPayloadComponent payload)
    {
        // 反序列化 payload.PayloadJson → FileDrivenPipelineMessage
        // 调用 ScriptEngine.Execute()
        // 写入 TaskResultComponent
    }
}

// === System 4: 重试/死信 ===
// 职责：检查 Failed 状态的任务，决定重试或进入死信
// 查询条件：WithAll<TaskStatusComponent>
//           状态 = Failed
class RetrySystem : SystemBase
{
    public override void Update(float deltaTime)
    {
        EntityManager.CreateQuery()
            .WithAll<TaskStatusComponent>()
            .ForEach<TaskStatusComponent>(accessor =>
            {
                for (int i = 0; i < accessor.Count; i++)
                {
                    ref var status = ref accessor.GetComponent1(i);
                    if (status.CurrentState != TaskStatusComponent.State.Failed) continue;
                    
                    if (status.RetryCount < status.MaxRetries)
                    {
                        status.CurrentState = TaskStatusComponent.State.Pending;
                        status.RetryCount++;
                    }
                    else
                    {
                        status.CurrentState = TaskStatusComponent.State.Dead;
                        // 触发死信通知
                    }
                }
            });
    }
}

// === System 5: 监控 ===
// 职责：采集任务延迟、成功/失败率等指标
class MonitorSystem : SystemBase
{
    public override void Update(float deltaTime)
    {
        // 定期遍历所有 Completed 任务，输出指标到执行日志
    }
}
```

### 2.5 运算矩阵的 Archetype 布局

```
Archetype_待调度:  TaskPayloadComponent + TaskStatusComponent + TaskPriorityComponent
Archetype_可路由:  上述 + TaskRoutingComponent + TaskDependencyComponent
Archetype_执行中:  上述 + TaskResultComponent
Archetype_完成:    上述 + TaskMetricComponent
Archetype_重试:    TaskPayloadComponent + TaskStatusComponent(retry>0)
Archetype_死信:    TaskStatusComponent(Dead) + TaskPayloadComponent + TaskResultComponent
```

Entity 随组件变化自动迁移 Archetype（ECS 原生行为）。

---

## 三、目录结构

```
CsharpPrj/ExecutionCenter/                            ★ 独立项目
├── ExecutionCenter.csproj                             ★ 新建
├── Abstractions/
│   ├── ITaskExecutorPool.cs                           ← 保留（兼容入口）
│   ├── ITaskMessage.cs                                ← 保留（兼容）
│   └── ITaskResponse.cs                               ← 保留（兼容）
├── Matrix/                                            ★ 运算矩阵核心
│   ├── Components/
│   │   ├── TaskPayloadComponent.cs                    ★ 新建
│   │   ├── TaskStatusComponent.cs                     ★ 新建
│   │   ├── TaskPriorityComponent.cs                   ★ 新建
│   │   ├── TaskRoutingComponent.cs                    ★ 新建
│   │   ├── TaskDependencyComponent.cs                 ★ 新建
│   │   ├── TaskResultComponent.cs                     ★ 新建
│   │   └── TaskMetricComponent.cs                     ★ 新建
│   ├── Systems/
│   │   ├── SchedulerSystem.cs                         ★ 新建
│   │   ├── RouterSystem.cs                            ★ 新建
│   │   ├── RetrySystem.cs                             ★ 新建
│   │   └── MonitorSystem.cs                           ★ 新建
│   ├── MatrixEngine.cs                                ★ 新建（托管 World + Systems）
│   └── TaskArchetype.cs                               ★ 新建（便捷创建 Entity）
├── Handlers/                                          ★ 具体业务处理器（作为 System 或回调）
│   ├── IMatrixHandler.cs                              ★ 新建
│   ├── FileDrivenPipelineHandler.cs                   ★ 新建（从 FrameworkInitializer 迁移）
│   ├── ExecuteCommandHandler.cs                       ★ 新建（激活闲置消息）
│   └── LoadConfigHandler.cs                           ★ 新建（激活闲置消息）
├── Legacy/                                            ← 旧代码保留过渡
│   ├── TaskExecutorPool.cs
│   ├── TaskQueue.cs
│   └── Messages/
│       ├── ExecuteCommandMessage.cs
│       ├── LoadConfigMessage.cs
│       ├── FileDrivenPipelineMessage.cs
│       ├── TaskMessageBase.cs
│       └── TaskResponse.cs
└── ExecutionCenterBootstrapper.cs                     ← 重构为 MatrixBootstrapper
```

### executionCenter.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RootNamespace>ExecutionCenter</RootNamespace>
    <AssemblyName>ExecutionCenter</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\BasicMethod\BasicMethod.csproj" />   <!-- ECS.Core -->
    <ProjectReference Include="..\..\Plate\TConfig\TConfig.csproj" />     <!-- IECSStorage -->
  </ItemGroup>
</Project>
```

---

## 四、与现有 ECS 框架的关系

```
ExecutionCenter.Matrix (新)
      │
      ├── 依赖 ECS.Core 的 EntityManager / EcsWorld / SystemBase
      │    └── CsharpPrj/BasicMethod/ECS/ECSFrame.cs
      │
      ├── 依赖 IECSStorage / IStorageRegistry
      │    └── CsharpPrj/Plate/TConfig/ContextFrame/Abstractions/
      │
      └── 复用 EntityQueryBuilder / ChunkJob 做矩阵运算
           └── CsharpPrj/BasicMethod/ECS/Query/EntityQuery.cs
           └── CsharpPrj/BasicMethod/ECS/Batch/ChunkJob.cs
```

不重复造 ECS 轮子，运算矩阵是 ECS 框架的一个**应用层模式**。

---

## 五、兼容层

为确保 FileDriven 等现有消费者在不修改代码的前提下逐步迁移：

### 5.1 ITaskExecutorPool 兼容

```csharp
// 新 MatrixEngine 实现旧的 ITaskExecutorPool 接口
public class MatrixEngine : ITaskExecutorPool
{
    public async Task<ITaskResponse> SubmitAsync(ITaskMessage task, CancellationToken ct)
    {
        // 将 ITaskMessage 转换为 Entity + Components
        var entity = CreateTaskEntity(task);
        
        // 等待任务完成
        var tcs = new TaskCompletionSource<ITaskResponse>();
        _completions[entity] = tcs;
        
        return await tcs.Task;
    }
}
```

### 5.2 迁移路径

```
阶段 1:  ITaskExecutorPool 接口不变，MatrixEngine 实现该接口
         旧 Handler 注册方式不变，MatrixEngine 包装为 System 调用
         → 现有消费者无感迁移

阶段 2:  ExecuteCommandMessage / LoadConfigMessage 的 Handler 注册到矩阵
         → 激活闲置消息类型

阶段 3:  删除 Legacy/ 代码
         消费者改为直接引用 MatrixEngine
```

---

## 六、与 CLI/GUI 执行路径重构的关系

两个工程的关系是**正交**的：

```
ExecutionCenter 重构（本方案）:
  独立项目 + ECS 运算矩阵
      ↓ (提供)
  消息队列基础设施（任务的提交/路由/调度/重试/监控）
      ↓ (消费)
FileDriven 执行路径重构（另一方案）:
  ScriptEngine 直连 → 不经过消息队列
  GUI 管线扁平化
```

- ExecutionCenter 提供**通用的任务调度能力**
- FileDriven 的**脚本执行走直连**，不经过消息队列
- FileDriven 如果未来需要重试/路由/监控等能力，再接入矩阵

**执行顺序建议**：

1. 先完成 ExecutionCenter 独立项目 + 矩阵骨架（阶段 1）
2. 同时或稍后完成 FileDriven 执行路径直连重构
3. 最后激活 ExecuteCommandMessage / LoadConfigMessage 作为矩阵的"首批居民"

---

## 七、实施步骤

### 阶段 1：独立项目 + 矩阵骨架

| 步骤 | 文件 | 说明 |
|------|------|------|
| 1.1 | `CsharpPrj/ExecutionCenter/ExecutionCenter.csproj` | 创建独立项目 |
| 1.2 | `Matrix/Components/*.cs` | 定义 7 个 Component struct |
| 1.3 | `Matrix/Systems/SchedulerSystem.cs` | 调度 System |
| 1.4 | `Matrix/Systems/RouterSystem.cs` | 路由 System |
| 1.5 | `Matrix/Systems/RetrySystem.cs` | 重试/死信 System |
| 1.6 | `Matrix/Systems/MonitorSystem.cs` | 监控 System |
| 1.7 | `Matrix/MatrixEngine.cs` | 矩阵引擎入口 |
| 1.8 | `Matrix/TaskArchetype.cs` | 便捷创建 Entity |
| 1.9 | 迁移 Abstractions 和旧代码到 Legacy | 保持兼容 |

### 阶段 2：激活闲置消息

| 步骤 | 文件 | 说明 |
|------|------|------|
| 2.1 | `Handlers/ExecuteCommandHandler.cs` | 实现 ExecuteCommand 处理器 |
| 2.2 | `Handlers/LoadConfigHandler.cs` | 实现 LoadConfig 处理器 |
| 2.3 | `Handlers/FileDrivenPipelineHandler.cs` | 从 FrameworkInitializer 迁移 |
| 2.4 | 注册到 RouterSystem | 激活消息消费 |

### 阶段 3：清理旧代码

| 步骤 | 文件 | 说明 |
|------|------|------|
| 3.1 | 删除 `Legacy/TaskExecutorPool.cs` | 旧执行池 |
| 3.2 | 删除 `Legacy/TaskQueue.cs` | 旧队列 |
| 3.3 | 删除 `Legacy/Messages/*.cs` | 旧消息类型（已被 Component 替代） |
| 3.4 | 清理 FileDriven 的 global using | 移除 `ExecutionCenter.*` |

---

## 八、风险与缓解

| 风险 | 影响 | 概率 | 缓解 |
|------|------|------|------|
| ECS 框架的 `IComponent` 是 struct 约束，消息体可能过大 | 栈溢出 | 低 | Payload 用 `string PayloadJson` 存储，不在栈上 |
| System 执行 handler 时阻塞 Update 循环 | 所有 System 暂停 | 中 | ExecutorSystem 用 `Task.Run` 卸载到线程池，System 只做状态转换 |
| 现有 FileDriven 依赖于 `ITaskExecutorPool` 接口 | 编译错误 | 中 | MatrixEngine 实现 `ITaskExecutorPool`，保持二进制兼容 |
| 运算矩阵的 System 调用周期 `Update(float dt)` 不适合事件驱动 | System 轮询空转 | 低 | 引入 `Signal()` 机制，有新任务时唤醒；无任务时 System 不执行 |

---

> **版本**：v1.0  
> **关联文档**：`docs/Refactoring/FileDriven_CLI_GUI_统一执行端口重构方案.md`（更新版）