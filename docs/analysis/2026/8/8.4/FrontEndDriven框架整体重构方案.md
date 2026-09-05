# FrontEndDriven 框架整体重构方案

> **状态**：提案（待评审）
> **范围**：`CsharpPrj/Operation/FrontEndDriven` + `CsharpPrj/Operation/BackEndDriven.Gate`（渲染管线 / 事件绑定 / 函数调用三条链路）
> **前置**：2026-08-03 已完成的日志精简重构（合并 start/complete、置 dep）为本方案的第 0 阶段成果，本文档在其基础上向框架层面推进
> **对齐规范**：分层决策遵循 `.clinerules/axioms.md`（L0-L3）；渲染管线属引擎运算层级轴 L1（控制流/迭代循环）；输出提交遵循 SyncProtocolTriple 三阶段语义

---

## 一、背景与目标

### 1.1 现状

FrontEndDriven 渲染链路（MainForm → EcsGate → RenderPipelineComposition → RenderStageRunner → 三聚拢点 Ports）存在**职责重叠、重复骨架、双路径实现**等问题。经阅读以下文件确认：

| 文件 | 角色 |
|------|------|
| `Gate/EcsGate.cs` | 操作门控 + 拦截器（审计）+ 作用域销毁双路径 |
| `Initial/RenderPipeline/PipelineCompositionFactory.cs` | 手工装配全部依赖 |
| `Initial/RenderPipeline/RenderPipelineComposition.cs` | 管线门面（Execute/ExecuteRenderStage/RecalculateLayout/RequestRerender 四入口） |
| `Initial/RenderPipeline/RenderPipelineStages.cs` | 五 Stage（Load/Creation/Collect/Apply/Bind）|
| `Initial/RenderPipeline/RenderStageRunner.cs` | 管线调度器 + 共享上下文 |
| `Ports.cs` | CsgEntryPoint / WinFormsOutputPort / EcsInputPort 三聚拢点 |
| `WinForms/Applier/WinFormApplier.cs` | 控件快照应用器 |
| `ControlInteraction/EventBinding/EventBindingSystem.cs` | 事件绑定（收集→声明→编排→验证→状态） |
| `BackEndDriven/Gate/FunctionCallFacade.cs` | 函数调用门面（入口/超时/审计） |

### 1.2 目标

1. **消除职责重叠**：装配/门面/调度三个概念各归其位；
2. **消除重复骨架**：收集器、销毁、尺寸读取等共享逻辑收敛为单一实现；
3. **修正错误逻辑**：空快照清缓存、Splitter suppression 期间写 ECS、统计口径混用、拦截器异常链断裂；
4. **观测层收敛**：日志与审计合并为统一门面，消除 AuditLogInterceptor 无界写文件问题。

---

## 二、现状问题清单（框架视角分类）

### 2.1 重复逻辑（DRY）

| # | 位置 | 描述 | 影响 |
|---|------|------|------|
| R1 | `Ports.cs` CsgEntryPoint | `Collect()` 与 `CollectSnapshot()` 遍历骨架逐行重复（过滤 SplitterPanelComponent + CollectComponentData + 各自映射），仅节点转换不同 | 加第三输出形态需再复制一遍 |
| R2 | `EcsGate.cs` | `DestroyScope`（同步）与 `DestroyScopeCoreAsync`（异步）两套作用域销毁，发布 Destroying/Destroyed 事件逻辑重复 | 双实现漂移风险（一处改事件序另一处忘改） |
| R3 | `Ports.cs` | `OnSplitterMoved` 与 `SyncActualContainerSizes` 各自独立调适配器 GetWidth/GetHeight | 维度读取无共享辅助 |
| R4 | `FunctionCallFacade.cs` | background/非 background 两分支重复构造 executor 委托，差异仅 Task.Run | 委托构造重复 |

### 2.2 冗余逻辑

| # | 位置 | 描述 | 影响 |
|---|------|------|------|
| RD1 | `RenderPipelineComposition.cs` | `ExecuteRenderStage()` / `RequestRerender()` / `RecalculateLayout()` 三入口最终全走同一 `_renderStageRunner.ExecuteRenderStage()`；`RequestRerender` 纯转发无增量 | 入口膨胀 |
| RD2 | `EcsGate.cs` | `LoadConfigAndRenderAsync` = Dispatch(LoadConfig) + 独立再调 `ExecuteRenderStage`，操作式门控与命令式调用混用 | 编排链路双轨 |
| RD3 | `WinFormApplier.cs` | `GetControl == null` 时打 `NotInPool` 日志但**无 continue**，后续 if/else 天然分流；日志位于分流之外无信息量 | 误导读者以为"未找到即跳过" |
| RD4 | `RenderContext` | `ReportCollectedCount`/`ReportExecuted` 两个独立回调，跨阶段状态回传无统一出口 | 回传语义碎片化 |
| RD5 | `EventPlatform.InvokeFunctionAsync` | `Task.Run(() => FunctionCallFacade.ExecuteSyncRequest(...))`，而 ExecuteSyncRequest 内部已 `AsyncHelper.RunSync` 阻塞同步等待 | 双层线程包装（Task.Run 包 RunSync） |

### 2.3 错误逻辑

| # | 位置 | 描述 | 影响 |
|---|------|------|------|
| E1 | `WinFormApplier.Apply` | `ClearEntityIdCache()` 在空快照检查**之前**执行，空快照也清缓存，「跳过」语义不闭合 | 语义不清 |
| E2 | `Ports.cs OnSplitterMoved` | `ApplySnapshot` 设 `_suppressProjectionEvents=true` 期间仍写 `SplitterRatioComponent`（仅跳过回调） | 渲染期间用户拉 Splitter → ECS 组件被改但无事件/重渲染 → ECS 与显示不一致无补偿 |
| E3 | `EventBindingSystem.LogResult` | `verifiedSkippedCount = declarations.Count - verifiedBoundCount`，但 `verifiedBoundCount` 来自 orchestrator 原始值；`output.VerifiedBoundCount` 是**验证后**值——两口径不同源 | 验证失败时 skipped 统计与 zero_bound_diagnostic 可能矛盾 |
| E4 | `EcsGate.ExecuteAsync` | `OnBefore` 拦截循环在 try **之外**，interceptor 抛异常时 OnError 不触发 | 拦截器错误链断裂（异常路径不完整） |
| E5 | `AuditLogInterceptor` | `File.AppendAllText` 无缓冲逐次打开 + `catch {}` 静默吞异常 | 审计失败不可见、与现有日志系统双写 |
| E6 | `WinFormApplier.ApplyRelations` | `TryParseEntityId` 失败直接 continue 无日志 | 静默失败（同文件其他跳过点均有日志） |

---

## 三、目标架构（分层职责）

```
┌─────────────────────────────────────────────────────────┐
│ L0 装配层  PipelineCompositionFactory                     │
│   单一职责：构建依赖图（DI Composition Root）              │
└──────────────┬──────────────────────────────────────────┘
               ▼
┌─────────────────────────────────────────────────────────┐
│ L1 门面层  RenderPipelineComposition（唯一入口）           │
│   单一职责：呈现「渲染管线」一个可操作对象                 │
│   Execute(load+creation) / ExecuteRenderStage(render)   │
│   （删除 RecalculateLayout/RequestRerender 独立入口）      │
└──────────────┬──────────────────────────────────────────┘
               ▼
┌─────────────────────────────────────────────────────────┐
│ L1 调度层  RenderStageRunner（五 Stage 管线）              │
│   单一职责：Stage 编排 + 上下文传递                        │
│   （RenderContext 状态回传收敛为单一 ResultSnapshot）      │
└──────────────┬──────────────────────────────────────────┘
               ▼
┌─────────────────────────────────────────────────────────┐
│ L2 三聚拢点  EcsInputPort / CsgEntryPoint /               │
│             WinFormsOutputPort                           │
│   单一职责各站点：输入/收集/输出                           │
│   （CsgEntryPoint 收集骨架通用化）                         │
│   （WinFormApplier 空快照/错误语义修正）                   │
└─────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────┐
│ L2 支持  EventBindingSystem / FunctionCallFacade          │
│   （统计口径统一 / 线程包装收敛）                          │
└─────────────────────────────────────────────────────────┘
```

**关键原则**：操作式门控（EcsGate 的 `EcsOperation + Interceptor`）与命令式渲染（`RenderPipelineComposition.Execute*`）**择一为主**。本方案主张以「命令式管线门面」为主，EcsGate 收敛为「薄调度适配器」（仅保留操作映射 + 作用域生命周期钩子）。

---

## 四、重构分区方案

### 4.1 区 A：装配与门面（L0-L1）

**现状问题**：
- `PipelineCompositionFactory.Create` 手工拼接 13 个依赖对象，依赖图隐式；
- `RenderPipelineComposition` 同时是门面、装配结果、状态持有者（HasExecuted/LastCollectedNodeCount/MatrixEngine），职责过载；
- `EcsGate` 双重职责（操作门控 + 作用域销毁 + 拦截器）。

**外部调用方清单（P2/P3 门面收敛的前置改动点，已全量确认）**：

| 当前 API | 调用方 | 收敛后 |
|----------|--------|--------|
| `RecalculateLayout()` | `ContainerControl.cs`（`_core?.RenderBridge?.RecalculateLayout()`）、`WinFormsRenderBridge.cs`、`PipelineCompositionFactory.cs`（`SetSplitterChangedCallback`） | 统一走 `ExecuteRenderStage()`；`SetSplitterChangedCallback` 内部回调改为 `composition.ExecuteRenderStage()` |
| `RequestRerender()` | `FunctionExecutedRenderBridge.cs` | 统一走 `ExecuteRenderStage()` |
| `LoadConfigAndRenderAsync(config, scope, host)` | `ContainerProjector.cs` | 保持签名（对外 API 不变），内部改为单一原子操作 |
| `RecalculateLayout()` 内部 | `RenderStageRunner.RecalculateLayout()`（= layout 重算 + ExecuteRenderStage） | `RenderStageRunner` 内部方法名改为 `RecalculateAndRender` 明确语义 |

**目标设计**：

1. **装配收敛**：`PipelineCompositionFactory` 保留为唯一组装点，但内部按「World 区（EcsWorld/EntityManager/ControlPool）→ Ports 区（三聚拢点）→ 系统区（Layout/Style/Splitter/Capability/Binding）→ 门面」四段注释分区，消除隐式依赖（`CsgFacade`/`ControlRegistryAwareFinder` 的 ServiceProvider 即时解析统一提到装配顶部）。
2. **门面瘦身**：`RenderPipelineComposition` 公开 API 收敛为：
   - `Execute(config, rootContainer, scope)`（Load+Creation）
   - `ExecuteRenderStage()`（Render 区间）
   - `LoadConfiguration` / `RegisterRootContainer` / `DestroyScopeEntities` / `Clear` / `Dispose`
   - **删除** `RecalculateLayout()` 与 `RequestRerender()` 独立公开方法 → 内部统一走 `_renderStageRunner.ExecuteRenderStage()`（R2/RD1 相关调用方改为显式调用渲染入口）。
3. **EcsGate 收敛**：保留 `ExecuteAsync<TResult>` 操作门控（供外部业务调用的统一入口），但：
   - `LoadConfigAndRenderAsync` 内部不再组合 `Dispatch(LoadConfig)` + 独立 `ExecuteRenderStage` 双轨，改为一个 `LoadAndRenderOperation` 原子操作（RD2）；
   - 作用域销毁统一收敛到**单一 `IScopeDestroyer`** 实现（同步/异步二选一，建议统一异步），删除 `DestroyScope` 同步双轨（R2）。

**涉及文件**：`PipelineCompositionFactory.cs`、`RenderPipelineComposition.cs`、`EcsGate.cs`

### 4.2 区 B：调度与上下文（L1）

**现状问题**：`RenderContext` 双回调回传状态（RD4）；Stage 间靠 `ctx.Snapshot/CollectedCount/AppliedCount` 三个独立字段传递。

**目标设计**：
- 引入 `RenderRunResult`（`scope / rootEntityId / collectedCount / appliedCount / elapsedMs / renderExecuted`）作为**管线单次执行唯一回传对象**；
- `RenderContext` 只承载执行中输入（config/rootContainer/scope/Part*），不再持有输出字段；
- `RenderStageRunner` 在 `ExecuteCore/ExecuteRenderStage` 完成后组装 `RenderRunResult` 返回；
- `RenderPipelineComposition` 的 `ExecuteSummary` 日志直接消费 `RenderRunResult`（与第 0 阶段日志合并无缝衔接）。

**涉及文件**：`RenderPipelineStages.cs`、`RenderStageRunner.cs`、`RenderPipelineComposition.cs`

### 4.3 区 C：三聚拢点（L2）

**现状问题**：
- `CsgEntryPoint.Collect()` / `CollectSnapshot()` 骨架重复（R1）；
- `WinFormApplier.Apply` 空快照清缓存（E1）、`NotInPool` 日志在分流外（RD3）、`ApplyRelations` 解析失败静默（E6）；
- `OnSplitterMoved` suppression 期间写 ECS（E2）；
- 尺寸读取重复（R3）。

**目标设计**：

1. **收集骨架通用化（R1）**：
   ```
   IControlCollector<T>（接口）：以实体枚举+过滤为固定流程
     Collect<T>(IConvertFunc<T> convert)：
       foreach entity in GetEntities<ControlTypeComponent>()
         if HasComponent<SplitterPanelComponent> continue
         yield return convert(CollectComponentData(entity))
   ```
   `Collect()` → `Collect<CanonicalNode>`；`CollectSnapshot()` → `Collect<ControlDescriptor 批量 + 关系>`；`CollectComponentData` 成为唯一共享数据源。
2. **WinFormApplier 语义修正（E1/RD3/E6）**：
   - `ClearEntityIdCache()` 移到空快照检查**之后**（仅实际应用时清缓存）；
   - `NotInPool` 日志删除（if/else 天然分流已表达意图），或降级为仅在`创建失败`路径 Debug；
   - `ApplyRelations` 解析失败补 Debug 日志（对齐 `FindControl_ParseFailed`）。
3. **Splitter suppression 语义（E2）**：`_suppressProjectionEvents` 期间 `OnSplitterMoved` **不写** `SplitterRatioComponent`（与回调一致跳过），或写后并入 `ApplySnapshot` 结束前的统一再渲染队列——二选一，建议前者（保持"渲染=快照一致"单一事务语义，与 SyncProtocolTriple.CommitPhase 对齐）。
4. **尺寸读取辅助（R3）**：在 `WinFormsUIAdapter` 增加 `GetSize(control)` 返回 `(width,height)` 统一入口，`OnSplitterMoved` 与 `SyncActualContainerSizes` 共用。

**涉及文件**：`Ports.cs`、`WinFormApplier.cs`、`WinForms/Adapter/WinFormsUIAdapter.cs`

### 4.4 区 D：事件绑定与函数调用（L2 支持）

**现状问题**：
- `EventBindingSystem.LogResult` 统计口径混用（E3）；
- `FunctionCallFacade` 双层线程包装（RD5）、executor 双分支重复（R4）。

**目标设计**：

1. **统计口径统一（E3）**：`LogResult` 的 skipped 一律以**验证后口径**为准：
   ```
   verifiedSkippedCount = output.Result.SkippedControls.Count
   verifiedBoundCount   = output.VerifiedBoundCount
   verifiedErrorCount   = result.ErrorCount
   ```
   日志文案「验证=X绑/Y跳/Z错」保持，消除 orchestrator 原始值混用。
2. **线程包装收敛（RD5/R4）**：`EventPlatform.InvokeFunctionAsync` 内 `Task.Run(ExecuteSyncRequest)` 改为直接 `FunctionCallFacade.ExecuteAsync`（消除 RunSync 同步等待）；`FunctionCallFacade.ExecuteCore` 的 executor 构造统一为单分支（background 差异由调用方 `Task.Run` 承担，或移除 background 分支仅保留 async/await）。

**涉及文件**：`EventBindingSystem.cs`、`EventPlatform.cs`、`FunctionCallFacade.cs`

### 4.5 区 E：观测层（日志与审计）

**现状问题**：本次已完成的日志合并（第 0 阶段）清理了渲染/绑定/函数调用的重复日志；但 `EcsGate.AuditLogInterceptor` 仍以 `File.AppendAllText` 无界写 `ecs_audit.log`，且 `catch {}` 静默吞异常（E5）——与统一日志体系双轨。

**目标设计**：
- **删除 AuditLogInterceptor 的文件直写**，审计记录收敛到统一日志门面（`FrontEndDrivenLog`/`BackEndLog`），由 LogFilterConfig 控制级别（默认 Debug）；
- 若确需独立审计文件，改为缓冲写入 + 卸载时 Flush，移除 `catch {}` 静默（记录 error 并降级）；
- `EcsGate.ExecuteAsync` 拦截器异常链修正（E4）：`OnBefore` 循环移入 try 内或 catch 中补调 `OnError`，保证拦截器异常时错误路径完整。

**涉及文件**：`Gate/EcsGate.cs`、日志定义 JSON

---

## 五、实施路线（阶段划分）

| 阶段 | 内容 | 涉及文件 | 验证 | 风险 |
|------|------|----------|------|------|
| **P0（已完成）** | 日志精简合并 + dep 迁移 | 2 JSON + 5 cs | 编译 + UI 运行 | 低 |
| **P1** | 低风险语义修正：WinFormApplier 空快照顺序/NotInPool 日志/ApplyRelations 补日志；EventBinding 统计口径统一 | WinFormApplier.cs、EventBindingSystem.cs | 编译 + UI 绑定验证 | 低 |
| **P2** | 门面收敛：删除 RecalculateLayout/RequestRerender 独立入口，统一调度；RenderContext → RenderRunResult 回传重构 | RenderPipelineComposition.cs、RenderStageRunner.cs、RenderPipelineStages.cs | 编译 + UI 渲染验证（含布局触发重渲） | 中 |
| **P3** | EcsGate 收敛：LoadAndRender 原子操作、作用域销毁单轨（异步）、拦截器异常链修正、审计收敛 | EcsGate.cs、日志定义 | 编译 + 操作门控验证 | 中 |
| **P4** | 收集骨架通用化：CsgEntryPoint Collect/CollectSnapshot 抽 IControlCollector | Ports.cs | 编译 + 双收集路径验证（CanonicalForm + WinFormSnapshot） | 中 |
| **P5** | 尺寸读取辅助 + FunctionCallFacade/EventPlatform 线程收敛 | WinFormsUIAdapter.cs、FunctionCallFacade.cs、EventPlatform.cs | 编译 + Resize/Splitter 行为验证 + 并发调用验证 | 中-高 |
| **P6** | 观测层收尾：审计与统一日志合并、EcsGate 拦截器异常链最终态 | EcsGate.cs | 编译 + 异常注入验证 | 低 |

**每阶段完成标准**：
1. `dotnet build CsharpPrj/MainProj.sln` 与 `dotnet build FileDriven/FileDriven.sln` 均通过；
2. `UI启动.bat` 运行验证渲染/绑定链路无回归（复用第 0 阶段的日志验证方法：核对 RenderPipelineComposition ExecuteSummary + EventBinding orchestrator_result）；
3. DSL 校验：若改动涉及 `[CR]` 引用或日志消息 `dep` 迁移，运行 `Check-ClinerulesReferences.ps1 -UpdateIndex` 重编译 references.md。

---

## 六、风险与兼容

| 风险 | 缓解 |
|------|------|
| P2 门面收敛破坏外部调用方（搜索 `RecalculateLayout`/`RequestRerender` 引用） | 保留内部方法为 `internal`，公开 API 删除前用 search_files 全量确认引用方 |
| P3 原子操作改变 LoadConfig+Render 时序 | 保持 `DoLoadConfig` 现有「先销毁→再 Load」语义不变量，仅合并入口 |
| P4 收集骨架泛型化影响性能（yield 迭代） | 收集数据量级为控件级（<100），可忽略；若覆盖大列表改用直接 List 构造 |
| E2 语义修改（suppression 期间不写 ECS）影响 SplitterRatio 更新 | 与 `_onSplitterChanged` 行为对齐（同样被抑制），符合"渲染=快照一致"事务语义；需要 UI 手动 Resize 测试覆盖 |

---

## 七、附录

### 7.1 与既有规范的对应

| 本方案条目 | 对应规范 |
|------------|----------|
| 渲染=快照一致事务（E2） | `SyncProtocolTriple.CommitPhase`（逻辑时钟域原子） |
| 统计口径统一（E3） | `EvaluationOutcomeSet` 真值映射唯一权威 |
| 拦截器异常链（E4） | `TruthValueDomainLogic`（错误→F 判定必须完整传播） |
| 日志 dep 迁移（P0） | `ReferenceGuardedDeprecation` + `ConflictResolutionPolicy` |
| 收集骨架通用化（R1） | `IDI-Interaction`/`CausalCone`（单一数据源，防多线漂移） |

### 7.2 第 0 阶段（已完成）关键结论

- 渲染管线 start/complete 合成为 `RenderPipelineComposition_Execute_summary`（单条 Info）；
- 事件绑定成功链 4→2 条 Info；函数调用 entry/exit → `FunctionCallFacade_summary`；
- `EcsInputPort_LoadConfig_start`/`CsgEntryPoint_CollectSnapshot_start` 无参数 Debug 已删；
- 旧消息按 `dep:"replaced-by(X)"` 标记，验证无旧模板残留输出。

---

*本文档为重构提案，具体落地需按 P1-P6 阶段逐项评审后实施。*