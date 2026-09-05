# WorkFlowMethod 上下文体系整理方案

> **版本**：v1.0  
> **日期**：2026-06-22  
> **范围**：`Operation/WorkFlowMethod` 目录  
> **目标**：消除上下文混乱，统一上下文模型，明确职责边界

---

## 目录

1. [现状分析](#1-现状分析)
2. [核心问题](#2-核心问题)
3. [目标架构](#3-目标架构)
4. [整理方案](#4-整理方案)
5. [迁移计划](#5-迁移计划)
6. [附录：完整上下文清单](#6-附录完整上下文清单)

---

## 1. 现状分析

### 1.1 当前上下文体系全景

```
┌─────────────────────────────────────────────────────────────────────┐
│                     WorkFlowMethod 上下文体系（现状）                    │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Context/  ─── 旧 Orchestration 上下文体系（遗留）                      │
│  ├── Abstractions/   接口定义层                                       │
│  │   ├── IWorkflowContext     ← 工作流上下文接口                       │
│  │   ├── IExecutionContext    ← 执行上下文接口                         │
│  │   ├── IRuntimeContext      ← 运行时上下文接口                       │
│  │   └── IWorkflowRuntime     ← 运行时接口                            │
│  ├── Execution/      执行上下文                                        │
│  │   ├── WorkflowExecutionContext  ← 执行上下文（POCO）               │
│  │   ├── ExecutionStateManager     ← 执行状态管理器                    │
│  │   ├── ExecutionStateRecord      ← 执行状态记录                     │
│  │   ├── WorkflowExecutionHelper   ← 执行辅助                         │
│  │   ├── WorkflowExecutionResult   ← [Obsolete] 执行结果              │
│  │   ├── WorkflowStatus            ← 状态枚举                         │
│  │   ├── HistoryEntry              ← 历史记录                         │
│  │   ├── INodeExecutor             ← 节点执行器接口                    │
│  │   └── INodeExecutorFactory      ← 执行器工厂                       │
│  ├── Models/          数据模型                                         │
│  │   └── CallFrameRecord           ← 调用帧记录                       │
│  └── Runtime/         运行时上下文                                      │
│      ├── WorkflowRuntimeContext         ← 核心运行时上下文              │
│      ├── WorkflowRuntimeContextManager  ← 运行时上下文管理器            │
│      ├── WorkflowRuntimeContextAccessor ← 运行时上下文访问器            │
│      ├── WorkflowConfiguration          ← 运行时配置                   │
│      └── WorkflowContext                ← AsyncLocal 异步上下文        │
│                                                                     │
│  CentralPipeline/  ─── 中央管道上下文（新体系）                          │
│  ├── WorkflowContext           ← 管道上下文（核心统一容器）              │
│  ├── WorkflowCentralPipeline   ← 中央管道                             │
│  ├── PipelineStages            ← 管道阶段定义                         │
│  ├── SkeletonRegistry          ← Skeleton 注册表                     │
│  └── SkeletonFactory           ← Skeleton 工厂                       │
│                                                                     │
│  Execution/  ─── 执行层上下文（新体系）                                  │
│  ├── Session/                                                       │
│  │   ├── ExecutionSession      ← 执行会话（状态容器）                  │
│  │   ├── DataContext           ← 数据上下文                           │
│  │   ├── NodeExecutionState    ← 节点执行状态                         │
│  │   └── DebugLog              ← 调试日志                             │
│  ├── Engine/                                                         │
│  │   ├── WorkflowExecutionEngine  ← 执行引擎                          │
│  │   ├── ExecutionManager         ← 会话管理器                        │
│  │   ├── ExecutionOrchestrator    ← 执行编排器                        │
│  │   └── ...                                                         │
│  └── Adapters/                                                       │
│      └── ShareContextAdapter     ← Share 层上下文适配器                │
│                                                                     │
│  LoopFramework/  ─── 循环框架上下文                                     │
│  ├── Core/                                                           │
│  │   ├── LoopContext            ← 循环上下文                          │
│  │   ├── LoopEngine             ← 循环引擎                           │
│  │   └── ...                                                         │
│  └── Abstractions/                                                   │
│      └── IContextManager<T>     ← 上下文管理器接口                     │
│                                                                     │
│  CallFrame/  ─── 调用帧上下文                                          │
│  ├── Core/                                                           │
│  │   ├── CallFrame              ← 调用帧（继承 TraceTree）             │
│  │   └── CallFrameService       ← 调用帧服务                          │
│  └── Abstractions/                                                   │
│      ├── ICallFrame             ← 调用帧接口                          │
│      └── ICallFrameService      ← 调用帧服务接口                      │
│                                                                     │
│  Infrastructure/  ─── 基础设施上下文                                    │
│  ├── ExecutionContext.cs        ← TypedExecutionContext（旧）         │
│  ├── PipelineExecutor.cs        ← 泛型管线执行器                       │
│  └── MigrationBridge.cs         ← 迁移桥接                            │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.2 上下文数据流（现状）

```
                    ┌──────────────────┐
                    │  WorkflowCentral │
                    │    Pipeline      │
                    └────────┬─────────┘
                             │ 持有
                    ┌────────▼─────────┐
                    │   WorkflowContext │ ←── 管道上下文（核心容器）
                    │  (CentralPipeline)│
                    └──┬────┬────┬─────┘
                       │    │    │
          ┌────────────┘    │    └──────────────┐
          ▼                 ▼                    ▼
   ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐
   │ GraphSnapshot│ │ExecutionSess.│ │  SharedData      │
   │ (执行凭证)    │ │ (状态容器)    │ │ (ConcurrentDict) │
   └──────────────┘ └──────┬───────┘ └──────────────────┘
                           │
                    ┌──────┴───────┐
                    │  DataContext  │ ←── 数据上下文
                    │  (键值存储)   │
                    └──────────────┘

   ┌─────────────────────────────────────────────┐
   │  Context/Runtime/  ←── 旧运行时上下文（并行存在）│
   │  WorkflowRuntimeContext                      │
   │  WorkflowRuntimeContextManager               │
   │  WorkflowContext (AsyncLocal)                │
   └─────────────────────────────────────────────┘
```

---

## 2. 核心问题

### 问题 1：命名冲突 — 三个 `WorkflowContext`

| 完整类型 | 所在文件 | 职责 |
|---------|---------|------|
| `WorkFlowMethod.CentralPipeline.WorkflowContext` | `CentralPipeline/WorkflowContext.cs` | **管道上下文** — 统一状态容器 |
| `WorkFlowMethod.Orchestration.Context.Runtime.WorkflowContext` | `Context/Runtime/WorkflowContext.cs` | **异步上下文** — AsyncLocal 传递 |
| `WorkFlowMethod.Orchestration.Context.Abstractions.IWorkflowContext` | `Context/Abstractions/IWorkflowContext.cs` | **接口** — 工作流上下文接口 |

**影响**：代码阅读和维护时容易混淆，using 别名泛滥。

### 问题 2：职责重叠 — 多处重复的"状态容器"

| 容器 | 存储方式 | 用途 |
|------|---------|------|
| `WorkflowContext.SharedData` (CentralPipeline) | `ConcurrentDictionary` | 管道级共享数据 |
| `DataContext` (Execution/Session) | `ConcurrentDictionary` | 会话级数据存储 |
| `WorkflowRuntimeContext._nodeCache` | `ConcurrentDictionary` | 运行时节点缓存 |
| `WorkflowRuntimeContext._executions` | `ConcurrentDictionary` | 运行时执行状态 |
| `ExecutionStateManager._states` | `ConcurrentDictionary` | 执行状态管理 |
| `TypedExecutionContext._data` (Infrastructure) | `MemoryCache` | 旧执行上下文 |

**影响**：数据分散在多个容器中，难以追踪数据流向，容易产生不一致。

### 问题 3：传递方式不统一

| 方式 | 使用场景 | 问题 |
|------|---------|------|
| `AsyncLocal` | `WorkflowContext.Current` | 隐式传递，难以追踪 |
| 静态 API | `WorkflowRuntimeContextManager.StaticInstance` | 全局状态，线程不安全 |
| 依赖注入 | `IWorkflowRuntimeContextAccessor` | 好，但未全面推广 |
| 参数传递 | Skeleton 链中的 `WorkflowContext` | 好，但旧代码未使用 |

### 问题 4：状态枚举不统一

| 枚举 | 定义位置 | 值 |
|------|---------|-----|
| `WorkflowStatus` | `Context/Execution/WorkflowStatus.cs` | Idle, Running, Paused, Completed, Failed, Canceled |
| `ExecutionStatus` | `Context/Execution/ExecutionStateRecord.cs` | NotStarted, Running, Paused, Completed, Terminated, Faulted |
| `PipelineStatus` | `CentralPipeline/WorkflowCentralPipeline.cs` | Created, Ready, Running, Paused, Stopped, Faulted |
| `NodeStatus` | `Execution/Session/NodeExecutionState.cs` | (待确认) |

**影响**：状态转换逻辑分散，难以统一管理。

### 问题 5：旧代码兼容性包袱

- `WorkflowExecutionResult`（`Context/Execution/`）标记为 `[Obsolete]`
- `WorkflowRuntimeContextManager` 保留 `AsyncLocal` 静态 API
- `MigrationBridge` 专门处理旧 API 桥接
- `TypedExecutionContext` 和 `ContextFactory`（`Infrastructure/`）与 `DataContext` 功能重复

---

## 3. 目标架构

### 3.1 上下文分层模型（目标）

```
┌──────────────────────────────────────────────────────────────┐
│                    上下文分层模型（目标）                         │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  第 1 层：运行时上下文（Runtime Context）               │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │ WorkflowRuntimeContext                          │  │   │
│  │  │  ├── 节点注册表（NodeRegistry）                   │  │   │
│  │  │  ├── 链注册表（ChainRegistry）                    │  │   │
│  │  │  ├── 执行状态管理（ExecutionStateManager）         │  │   │
│  │  │  └── 历史记录（History）                          │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  │  生命周期：应用级单例，长期存活                          │   │
│  └──────────────────────────────────────────────────────┘   │
│                           │                                   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  第 2 层：管道上下文（Pipeline Context）               │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │ PipelineContext（重命名自 WorkflowContext）       │  │   │
│  │  │  ├── GraphSnapshot（执行凭证，不可变）             │  │   │
│  │  │  ├── ExecutionSession（状态容器）                 │  │   │
│  │  │  ├── CurrentNodeKey / CurrentInput / Output     │  │   │
│  │  │  └── CancellationToken                          │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  │  生命周期：单次执行，执行结束即销毁                       │   │
│  └──────────────────────────────────────────────────────┘   │
│                           │                                   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  第 3 层：会话上下文（Session Context）                │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │ ExecutionSession                               │  │   │
│  │  │  ├── DataContext（数据存储）                      │  │   │
│  │  │  ├── NodeExecutionStates（节点状态）              │  │   │
│  │  │  ├── DebugLog（日志）                            │  │   │
│  │  │  └── CallFrame（调用链追踪）                      │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  │  生命周期：单次执行，与会话绑定                          │   │
│  └──────────────────────────────────────────────────────┘   │
│                           │                                   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  第 4 层：循环上下文（Loop Context）                   │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │ LoopContext                                     │  │   │
│  │  │  ├── IterationCount（迭代计数）                   │  │   │
│  │  │  ├── LoopStatus（循环状态）                       │  │   │
│  │  │  └── CustomData（自定义数据）                     │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  │  生命周期：循环执行期间存活                              │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### 3.2 核心原则

1. **单一职责**：每个上下文类型只有一个明确的职责
2. **分层依赖**：上层可依赖下层，下层不依赖上层
3. **显式传递**：所有上下文通过构造函数或方法参数显式传递，淘汰 `AsyncLocal` 和静态 API
4. **统一接口**：所有上下文实现统一的 `IContext` 标记接口
5. **生命周期明确**：每个上下文有明确的创建和销毁时机

---

## 4. 整理方案

### 4.1 第一阶段：命名规范化（低风险，可立即执行）

#### 4.1.1 重命名 CentralPipeline.WorkflowContext

```
WorkflowContext → PipelineContext
```

**变更文件**：
- `CentralPipeline/WorkflowContext.cs` → 类名改为 `PipelineContext`
- `CentralPipeline/WorkflowContextSnapshot` → 类名改为 `PipelineContextSnapshot`
- 所有引用 `WorkflowContext` 的 Skeleton 文件

**理由**：这是最核心的上下文容器，需要一个无歧义的名称。

#### 4.1.2 重命名 Context/Runtime/WorkflowContext

```
WorkflowContext → AsyncWorkflowContext
```

**变更文件**：
- `Context/Runtime/WorkflowContext.cs` → 类名改为 `AsyncWorkflowContext`

**理由**：明确标识其使用 `AsyncLocal` 的特性，为后续淘汰做准备。

#### 4.1.3 重命名 Context/Execution/WorkflowExecutionContext

```
WorkflowExecutionContext → ExecutionContext
```

**变更文件**：
- `Context/Execution/WorkflowExecutionContext.cs`

**理由**：简化名称，与接口 `IExecutionContext` 对应。

### 4.2 第二阶段：职责合并（中等风险，需谨慎）

#### 4.2.1 合并 SharedData 和 DataContext

**现状**：
- `PipelineContext.SharedData`（`ConcurrentDictionary<string, object>`）
- `ExecutionSession.Data`（`DataContext` 实例）

**方案**：
```
PipelineContext 不再持有 SharedData
→ 所有数据访问统一通过 ExecutionSession.Data（DataContext）
→ PipelineContext 提供快捷方法委托给 Session.Data
```

**变更**：
```csharp
// PipelineContext 中
public T GetData<T>(string key) => Session?.Data?.Get<T>(key) ?? default;
public void SetData<T>(string key, T value) => Session?.Data?.Set(key, value);
```

#### 4.2.2 合并 TypedExecutionContext 和 DataContext

**现状**：
- `Infrastructure/TypedExecutionContext`（旧执行上下文）
- `Execution/Session/DataContext`（新数据上下文）

**方案**：
```
标记 TypedExecutionContext 为 [Obsolete]
→ 所有使用 TypedExecutionContext 的地方迁移到 DataContext
→ ContextFactory 迁移到 ExecutionSession 管理
```

#### 4.2.3 合并执行状态管理

**现状**：
- `WorkflowRuntimeContext._executions`（运行时执行状态）
- `ExecutionStateManager._states`（执行状态管理器）
- `ExecutionSession._nodeStates`（节点执行状态）

**方案**：
```
执行状态统一由 ExecutionSession 管理
→ WorkflowRuntimeContext 只保留节点/链注册功能
→ ExecutionStateManager 合并到 ExecutionSession
```

### 4.3 第三阶段：传递方式统一（高风险，需全面测试）

#### 4.3.1 淘汰 AsyncLocal

**现状**：
- `Context/Runtime/WorkflowContext.cs` 使用 `AsyncLocal<WorkflowContext>`
- `WorkflowRuntimeContextManager` 使用 `AsyncLocal` 静态 API

**方案**：
```
全面采用依赖注入 + 参数传递
→ 移除 AsyncWorkflowContext（原 WorkflowContext）
→ WorkflowRuntimeContextManager 改为纯实例类
→ 通过 DI 容器注入 IWorkflowRuntimeContextAccessor
```

#### 4.3.2 淘汰静态 API

**现状**：
- `WorkflowRuntimeContextManager.StaticInstance`
- `WorkflowRuntimeContextManager.RuntimeCurrent`

**方案**：
```
标记为 [Obsolete]
→ 所有调用方改为通过 DI 获取 IWorkflowRuntimeContextAccessor
→ 最终移除静态 API
```

### 4.4 第四阶段：状态枚举统一（低风险）

#### 4.4.1 统一状态枚举

**方案**：
```csharp
// 统一的工作流状态枚举
public enum WorkflowExecutionStatus
{
    Pending,      // 待执行
    Running,      // 运行中
    Paused,       // 已暂停
    Completed,    // 已完成
    Failed,       // 已失败
    Cancelled,    // 已取消
    Faulted       // 异常终止
}
```

**变更**：
- `WorkflowStatus` → 标记为 `[Obsolete]`，映射到新枚举
- `ExecutionStatus` → 标记为 `[Obsolete]`，映射到新枚举
- `PipelineStatus` → 保留，但内部使用新枚举

### 4.5 第五阶段：清理废弃代码（低风险）

#### 4.5.1 移除清单

| 类型 | 文件 | 替代 |
|------|------|------|
| `WorkflowExecutionResult`（Context/Execution） | `Context/Execution/WorkflowExecutionResult.cs` | `Execution/Engine/WorkflowExecutionResult` |
| `MigrationBridge` | `Infrastructure/MigrationBridge.cs` | 直接使用新 API |
| `TypedExecutionContext` | `Infrastructure/ExecutionContext.cs` | `DataContext` |
| `ContextFactory` | `Infrastructure/ExecutionContext.cs` | `ExecutionSession` |
| `NodeProcessCallFrame` | `NodeProcess/NodeProcessSkeleton.cs` | `CallFrameRecord` |

---

## 5. 迁移计划

### 5.1 阶段时间线

```
阶段 1：命名规范化  ─── 1-2 天
    ↓
阶段 2：职责合并    ─── 3-5 天
    ↓
阶段 3：传递方式统一 ─── 5-7 天
    ↓
阶段 4：状态枚举统一 ─── 1-2 天
    ↓
阶段 5：清理废弃代码 ─── 1-2 天
```

### 5.2 详细步骤

#### 步骤 1：命名规范化

```
1.1 重命名 CentralPipeline.WorkflowContext → PipelineContext
    - 修改类定义
    - 更新所有 Skeleton 中的类型引用
    - 更新 PipelineBuilder 中的引用
    - 更新 WorkflowCentralPipeline 中的引用

1.2 重命名 Context/Runtime/WorkflowContext → AsyncWorkflowContext
    - 修改类定义
    - 添加 [Obsolete] 标记

1.3 重命名 Context/Execution/WorkflowExecutionContext → ExecutionContext
    - 修改类定义
    - 更新 WorkflowExecutionHelper 中的引用
    - 更新 INodeExecutor 中的引用
```

#### 步骤 2：职责合并

```
2.1 合并 SharedData → DataContext
    - PipelineContext 移除 _sharedData 字段
    - 所有 GetData/SetData 委托给 Session.Data
    - 更新所有通过 SharedData 访问数据的代码

2.2 合并 TypedExecutionContext → DataContext
    - 标记 TypedExecutionContext 为 [Obsolete]
    - 查找所有使用 TypedExecutionContext 的地方
    - 逐一迁移到 DataContext

2.3 合并执行状态管理
    - ExecutionSession 增加状态管理方法
    - ExecutionStateManager 标记为 [Obsolete]
    - 迁移 ExecutionStateManager 的调用方
```

#### 步骤 3：传递方式统一

```
3.1 淘汰 AsyncLocal
    - 标记 AsyncWorkflowContext 为 [Obsolete]
    - 查找所有使用 WorkflowContext.Current 的地方
    - 改为通过方法参数传递 PipelineContext

3.2 淘汰静态 API
    - 标记 WorkflowRuntimeContextManager 静态 API 为 [Obsolete]
    - 所有调用方改为 DI 注入
    - 最终移除静态 API
```

#### 步骤 4：状态枚举统一

```
4.1 创建统一状态枚举 WorkflowExecutionStatus
4.2 标记旧枚举为 [Obsolete]
4.3 逐一迁移状态判断逻辑
```

#### 步骤 5：清理废弃代码

```
5.1 移除 [Obsolete] 类型
5.2 移除 MigrationBridge
5.3 移除重复的 CallFrameRecord 别名
```

### 5.3 风险控制

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| 命名重命名导致编译错误 | 低 | 一次性全局重命名，IDE 重构工具 |
| 职责合并导致数据丢失 | 中 | 先添加兼容层，逐步迁移，保留旧代码直到验证通过 |
| 静态 API 淘汰影响旧代码 | 中 | 先标记 `[Obsolete]`，给出迁移指南，一个版本后移除 |
| 状态枚举统一影响状态判断 | 低 | 提供隐式转换操作符，确保旧代码编译通过 |

---

## 6. 附录：完整上下文清单

### 6.1 保留的核心上下文

| 上下文类型 | 文件 | 职责 | 生命周期 |
|-----------|------|------|---------|
| `PipelineContext`（原 `WorkflowContext`） | `CentralPipeline/` | 管道级状态容器 | 单次执行 |
| `ExecutionSession` | `Execution/Session/` | 会话状态容器 | 单次执行 |
| `DataContext` | `Execution/Session/` | 数据存储 | 与会话绑定 |
| `LoopContext` | `LoopFramework/Core/` | 循环运行时信息 | 循环期间 |
| `CallFrame` | `CallFrame/Core/` | 调用链追踪 | 与会话绑定 |
| `WorkflowRuntimeContext` | `Context/Runtime/` | 运行时节点/链注册 | 应用级 |

### 6.2 待合并/淘汰的上下文

| 上下文类型 | 文件 | 处理方式 |
|-----------|------|---------|
| `WorkflowContext.SharedData` | `CentralPipeline/WorkflowContext.cs` | 合并到 `DataContext` |
| `TypedExecutionContext` | `Infrastructure/ExecutionContext.cs` | 淘汰，使用 `DataContext` |
| `ContextFactory` | `Infrastructure/ExecutionContext.cs` | 淘汰，使用 `ExecutionSession` |
| `AsyncWorkflowContext`（原 `WorkflowContext`） | `Context/Runtime/WorkflowContext.cs` | 淘汰 `AsyncLocal` |
| `WorkflowRuntimeContextManager` 静态 API | `Context/Runtime/` | 淘汰，使用 DI |
| `ExecutionStateManager` | `Context/Execution/` | 合并到 `ExecutionSession` |
| `WorkflowExecutionResult`（旧） | `Context/Execution/` | 淘汰，使用引擎版本 |
| `MigrationBridge` | `Infrastructure/MigrationBridge.cs` | 淘汰 |
| `NodeProcessCallFrame` | `NodeProcess/NodeProcessSkeleton.cs` | 淘汰，使用 `CallFrameRecord` |

### 6.3 保留的接口

| 接口 | 文件 | 说明 |
|------|------|------|
| `IWorkflowContext` | `Context/Abstractions/` | 保留，作为统一接口 |
| `IExecutionContext` | `Context/Abstractions/` | 保留 |
| `IRuntimeContext` | `Context/Abstractions/` | 保留 |
| `IWorkflowRuntime` | `Context/Abstractions/` | 保留 |
| `IWorkflowRuntimeContextAccessor` | `Infrastructure/Abstractions/` | 保留，推广使用 |
| `ICallFrame` | `CallFrame/Abstractions/` | 保留 |
| `ICallFrameService` | `CallFrame/Abstractions/` | 保留 |
| `IContextManager<T>` | `LoopFramework/Abstractions/` | 保留 |
| `IDataContext` | `Share/WorkflowExecution/` | 保留，`DataContext` 已实现 |

---

## 修订历史

| 版本 | 日期 | 修改内容 | 作者 |
|------|------|---------|------|
| v1.0 | 2026-06-22 | 初始版本 | CLine |
