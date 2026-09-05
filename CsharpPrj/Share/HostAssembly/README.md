# Share.HostAssembly — 通用宿主装配框架

> 从 Agent.Launcher 的组合根装配 / 可观测降级 / 线程安全基建 / 统一错误边界 / 表驱动分发等模式中**提炼的通用框架**。
> 零第三方依赖、不绑定任何具体业务/领域类型，可被任意宿主复用。

## 模块一览

| 命名空间 | 类型 | 职责 |
|----------|------|------|
| `Abstractions` | `IHostAssembly` / `BootstrapResult` | 宿主装配门面契约 + 装配摘要（可用性 + 降级清单 + 失败归因） |
| `Stages` | `IAssemblyStage<TContext>` / `AssemblyStagePipeline<TContext>` | 门控阶段装配管线（CanExecute 跳过、顺序执行） |
| `Resilience` | `FailSilentRegistry` / `ResilientRegistration` | 可开关可回退注册器（失败不阻断宿主、聚合降级清单、诊断回调） |
| `Concurrency` | `ThreadSafeSlot<T>` / `SyncAsyncBridge` | 线程安全槽位容器 / 同步-异步桥（防同步上下文死锁） |
| `Disposal` | `AsyncDisposeAdapter` | IAsyncDisposable → IDisposable 同步释放链适配 |
| `Patterns` | `SingletonFactoryRegistry<TKey,TValue>` | 按 key 单实例工厂注册表（消除同资源二次开碰撞） |
| `Errors` | `ErrorCategory` / `IErrorClassifier` / `DefaultErrorClassifier` / `ActionErrorBoundary` / `BoundaryResult<T>` | 统一错误分类与错误边界（消除散落 try/catch） |
| `Commands` | `CommandDispatcher<TRequest>` | 表驱动 action→handler 分发 + 允许动作白名单 |
| `Storage` | `InMemoryKeyedStore<TValue>` | 线程安全内存键值快照存储 |

## 设计原则

- **位置即语义**：阶段注册序 / 分发动作即装配与分发语义（对应 clinerules LO.Resolve 策略=table）。
- **可观测降级**：可选模块失败不阻断宿主，但聚合降级清单让宿主可感知（非静默吞错）。
- **取消透传**：`OperationCanceledException` 在错误边界中显式透传，不降级包装。
- **单一实现**：每个通用骨架只有一份权威实现，消费方引用同一实现（clinerules TH-12）。
