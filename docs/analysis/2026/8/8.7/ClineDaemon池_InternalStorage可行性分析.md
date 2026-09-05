# Cline 传输层 daemon 池重构 — 基于 InternalStorage 的可行性分析与设计

> 日期：2026-08-07 ｜ 模块：`Agent.ClineUsage` / `ClineApi` / `Modules.InternalStorage` ｜ 状态：设计草案（待评审）
> 关联：《Cline传输层挂起根因分析与重构方案.md》（同目录）

## 1. 背景与目标

### 1.1 问题
现有 Cline 传输层**每次调用独立 `node` 进程 + 独立 `ClineCore` 实例**，全部争抢共享 `~/.cline/data`：
- GUI 里 init（`exec Cline_History`）与 click（`plan`）等多路径并发/残留 → `ClineCore.create({backendMode:"local"})` 锁竞争卡死（实测命令行单点正常、GUI 挂 ~100s）。
- 被杀进程不 `dispose()` → SQLite/锁残留，污染后续调用。

### 1.2 目标（用户确认：方案 B）
- **传输层 daemon 池化**：N 个常驻 `node` daemon，每个持有独立 `ClineCore` 单例，请求分发并发执行。
- **根治**：无频繁 create/dispose、无残留锁、单 daemon 超时重启隔离。
- **数据隔离**：多 daemon 共享 `~/.cline/data` 的并发可行性需实测（VSCode 多实例并发已证明引擎可行；`local` 模式 + 共享 SQLite 是否可行待验）。

### 1.3 本文档目的
评估**能否基于现有 `Modules.InternalStorage` 框架构建**，给出分层设计与映射表，明确 InternalStorage 的职责边界。

## 2. InternalStorage 框架能力盘点

| 组件 | 职责 | 关键能力 |
|------|------|----------|
| `StorageEngine` | 存储引擎 | 多会话（Create/Get/DisposeSession）、分层存储（Config/Session/Transient）、快照（Create/RestoreSnapshot）、状态机（Created→Running→Disposed + `StateChanged` 事件） |
| `StorageCell` | 单会话存储单元 | `Set/Get/Remove/GetKeys/SetAdd/SetCount` 等键值 + 集合操作，按 `StorageLayer` 分层 |
| `StorageRegistry` | 存储注册 | 会话元数据注册/查询 |
| `InitializationOrchestrator` | 初始化编排 | `Register(name, fn, dependsOn)` + **依赖拓扑排序** + 串行执行 + `failFast` + `ContextData` 注入 |
| `IInitializer`/`IInitializationContext` | 初始化契约 | 依赖声明、上下文（Storage/Registry/CancellationToken） |
| `EcsSessionManager` | 会话管理 | 会话注册/注销、组件数据同步、快照/恢复、活跃会话枚举 |
| `ECSStorage` | ECS 存储 | archetype/entity/chunk/列存 |

**定位**：`InternalStorage` 是**进程内的内存状态/元数据/生命周期编排基础设施**，不涉及外部进程、I/O 通道或超时。

## 3. 能否基于 InternalStorage 构建？— 结论：**部分可用，需分层**

### 3.1 可映射（InternalStorage 能承担）
| daemon 池需求 | InternalStorage 映射 | 匹配度 |
|---------------|----------------------|--------|
| daemon 生命周期状态机（Starting/Running/Busy/Hung/Stopped） | `StorageEngine.State` 模式 + `StateChanged` 事件 | ★★★ |
| daemon 元数据（pid、启动时间、busy/free、超时、错误） | `StorageEngine.CreateSession(daemonId)` + `StorageCell` 分层存储 | ★★★ |
| 池初始化/启动流程（起 N 个 daemon、依赖） | `InitializationOrchestrator.Register(...dependsOn)` 拓扑编排 | ★★★ |
| 请求/会话状态追踪 | `EcsSessionManager` + 快照 | ★★★ |
| 池状态持久化/恢复 | 快照（Create/RestoreSnapshot） | ★★（进程内，非跨进程持久化） |

### 3.2 不可映射（必须在 InternalStorage 之外实现）
| daemon 池需求 | 归属层 |
|---------------|--------|
| **进程生命周期**（spawn node daemon、kill、重启、进程树清理） | `ClineClient`（Process/IPC） |
| **进程间通信协议**（stdin/stdout JSON-Line 请求响应、id 关联） | `ClineClient`（新 daemon 协议） |
| **请求超时/看门狗/并发闸门** | `ClineClient` + `Modules.Threading`（AEF/AsyncOperationScheduler） |
| **Cline 数据目录隔离**（实测决定） | `ClineClient`（进程环境/CLINE_HOME） |

**结论**：InternalStorage **不能也不应**替代"进程管理 + IPC + 超时"。它的正确定位是——**daemon 池的"状态与编排"旁路基础设施**。用它做状态机、元数据、初始化编排和会话追踪；进程/IPC/超时由 ClineClient 承担。

## 4. 目标架构设计（daemon 池，分层）

```
┌─ ClineLibrary（函数注册层）─────────────────────────────┐
│  Cline_Ask/Plan/Resume/History → ClineClient            │
├─ ClineClient（池管理器 + 传输）──────────────────────────┤
│  ClineDaemonPool                                        │
│   ├─ 进程管理：spawn node daemon / kill / 重启          │
│   ├─ IPC：JSON-Line 请求/响应（id 关联 + TCS）          │
│   ├─ 超时/看门狗：请求超时→重启该 daemon                 │
│   └─ 数据隔离：每 daemon 独立环境（待实测）              │
├─ Modules.InternalStorage（状态/编排层）─────────────────┤
│  ClineDaemonStateStore（新建，包装 StorageEngine）       │
│   ├─ daemon 状态机 + 元数据（每 daemon 一 Session）      │
│   ├─ 池初始化（InitializationOrchestrator 编排启动）     │
│   └─ 请求/会话追踪（EcsSessionManager 模式）             │
└─────────────────────────────────────────────────────────┘
```

## 5. 具体落地映射表

| 新组件 | 基于 InternalStorage | 承载内容 |
|--------|---------------------|----------|
| `ClineDaemonStateStore` | `StorageEngine` + `StorageCell` | 每 daemon 一个 session：状态、pid、busy/free、启动时间、lastError、超时计数 |
| `ClineDaemonState` 枚举 | `StorageEngineState` 模式 | Starting/Running/Busy/Hung/Stopped + `StateChanged` 事件 |
| `ClineDaemonPoolInitializer` | `InitializationOrchestrator` | 池启动：检查 cli.js → 创建 N 个 daemon → 心跳探活 → 失败重试 |
| `ClineRequestTracker` | `EcsSessionManager` 模式 | 请求 id ↔ daemon 关联、会话/快照 |
| 池健康/统计 | `StorageRegistry` | 活跃 daemon、busy 数、hung 数（诊断用） |

## 6. 建议与风险

- **建议**：daemon 池的**进程/IPC/超时**与**状态/编排**分离——前者放 `ClineClient`，后者新建 `ClineDaemonStateStore` 包装 `InternalStorage`。避免把进程管理硬塞进 InternalStorage（违背其定位）。
- **风险 R1（最高）**：`~/.cline/data` 共享并发可行性未实测。若 `local` 模式 + 共享 SQLite 会锁竞争，daemon 池需**数据隔离**（独立环境/CLINE_HOME）或**换 hub/CLI 通道**。→ 需前置实测（同起 2~3 个 daemon `plan --trace`）。
- **风险 R2**：InternalStorage 快照是进程内内存，不解决跨进程持久化；daemon 池重启后的状态恢复需自己持久化到文件。
- **风险 R3**：`InitializationOrchestrator` 是串行初始化，适合"启动编排"，不适合"运行期并发请求分发"（那是池管理器 + AEF 的事）。

## 7. 结论

**InternalStorage 适合作为 daemon 池的"状态机 + 元数据 + 初始化编排 + 会话追踪"基础设施，可作为旁路状态层复用；但进程管理、IPC、超时、数据隔离必须在 `ClineClient`（+ AEF）实现。** 因此本次重构**可以基于 InternalStorage 构建"状态层"**，但**不能依赖它承载"传输层"**。建议按 §4 分层实施：先做进程/IPC 骨架（ClineClient），再接入 `ClineDaemonStateStore`（InternalStorage）。

## 8. 后续实施顺序（供评审）

| 步骤 | 动作 | 产出 |
|------|------|------|
| 1 | 前置实测：同起 2~3 个 daemon `plan --trace`，确认共享 `~/.cline/data` 并发是否锁竞争 | 决定 daemon 池是否需要数据隔离 |
| 2 | TS 侧新增 `daemon.ts`：单 ClineCore，stdin JSON-Line 串行处理，父进程退出自动清理 | 传输协议验证 |
| 3 | C# 侧 `ClineClient` 接入 daemon 协议 + 池管理器（进程/IPC/超时重启） | 传输层骨架 |
| 4 | 接入 `ClineDaemonStateStore`（InternalStorage 状态层） | 状态/编排 |
| 5 | 回归 `cline_browser_test.json` | 验证根治挂起 + 并发 |

---

## 9. Threading（AEF）复用评估（补充，2026-08-07）

### 9.1 能力盘点
| Threading 组件 | 能力 | 对 daemon 池的复用 |
|------|------|------|
| `AsyncOperationScheduler` | 并发闸门（maxConcurrency）+ 绝对超时（AbsoluteTimeout）+ 防重入（ReentryKey）+ 隔离（IsolationKey）+ 外部取消 | 每 daemon 请求提交为 AsyncOperation；AbsoluteTimeout 到点 kill daemon；maxConcurrency 即池并发上限 |
| `AsyncOperation<T>` | 生命周期/取消贯穿/心跳/进程终止/状态机（AsyncOpPhase） | daemon 请求的统一执行载体；ReportProgress() 作推进心跳 |
| `WatchdogService` | 空闲看门狗：心跳判 IdleHung → kill + Fail | daemon 心跳（ping 响应）判挂起 → kill |
| `IProcessHandle` / `ChildProcessHandle` | kill 整棵进程树 | 包装 daemon 进程，超时/空闲/取消时终止 |
| `AsyncOperationOptions` | IsolationKey（注释即独立 data-dir）、Process（注释即 node/ClineCore 进程） | 本就按 Cline 传输层设计，语义完全对齐 |
| `OperationExtensions.TryWaitAsync` | 非阻塞等待（替代 Task.Wait()） | 请求等待结果不阻塞 UI |

### 9.2 复用评估结论
Threading（AEF）承载 daemon 池的**执行层**：并发闸门、请求超时、空闲看门狗、进程树终止全部已具备，且 AsyncOperationOptions 注释明确指向 node/ClineCore 与独立 data-dir。**可直接复用，无需改造**。

### 9.3 三层架构分工（更新 §4/§7）
- **ClineClient（编排层）**：spawn node daemon / 重启、JSON-Line IPC（id 关联 + TCS）、池逻辑
- **Threading / AEF（执行层）**：AsyncOperationScheduler 并发/超时、WatchdogService 看门狗、IProcessHandle 进程终止、ReentryKey 防重入
- **InternalStorage（状态/编排层）**：StorageEngine 状态机/元数据、InitializationOrchestrator 池初始化、EcsSessionManager 会话追踪

### 9.4 结论更新
本次重构可**复用两层既有框架**：Threading（执行层）+ InternalStorage（状态层），ClineClient 负责编排（进程/IPC）。三者互补，无重复造轮子；新增面集中在 ClineClient 的 daemon 池编排与 TS 侧 daemon.ts。
