# Strategy.Observability 内核审查报告

> 审查对象：`Frame\CsharpPrj\Strategy\Observability`（通用语义观测内核 E1–E8）。
> 审查范围：重复逻辑 / 冗余逻辑 / 错误逻辑三类，含与 Agent.Contract 的跨工程重叠。
> 结论基调：纯机制层整体清晰，但存在 2 处会静默出错/误报的真实缺陷、1 处跨工程命名冲突，以及一批可低优先级清理的冗余项。
> **状态列（2026-09-17 补齐；原报告无日期/状态列，导致已修项仍列"高优先"）**：`已修` / `未修` / `迁移中`；
> 与本报告相关的收敛批次见《DataCenter-标准单源化-重复逻辑收敛-设计与实施》§6（P0–P4）。

---

## 目录

- [一、重复逻辑](#一重复逻辑)
- [二、冗余逻辑](#二冗余逻辑)
- [三、错误逻辑与隐患](#三错误逻辑与隐患)
- [四、优先级建议](#四优先级建议)

---

## 一、重复逻辑

### 1.1 双套同名 IObservabilitySink（跨工程命名/职责冲突）
Frame 与 Agent 各自定义了一个**同名不同类型**的 `IObservabilitySink`，契约不兼容：

| 工程 | 位置 | 成员 |
|---|---|---|
| Frame | `Bus/ObservabilitySinks.cs:L12` | `Push(PersistedEvent)` |
| Agent | `Agent.Contract/Observability/IObservabilitySink.cs:L4` | `Emit(RunEvent)` |

两个类型共用类型名却语义无关，是「观测碎片化为多机制」（project_memory）的直接体现，易造成 using 混淆。

### 1.2 Timeline / EntityChain 过滤逻辑重复三份
「按 scope（+entity）过滤 → 按 EventSeq 排序」这段逻辑被重写三处：
- `Persistence/FileAppendEventStore.cs:L48-73`
- `Persistence/InMemoryEventStore.cs:L23-46`
- `Query/ObservabilityQuery.cs:L28-38`（又包一层）

### 1.3 两套单调序号机制重复
- `Entity/EntityIdFactory._seq`：按 `scopeId+entityType` 单调自增。
- `Bus/ObservabilityBus._seq / NextSeq`：按 `scopeId` 单调自增。

均为 ConcurrentDictionary 实现的同构机制，却彼此独立。

### 1.4 契约校验同段 JSON 双解析
`Registry/ObservabilityContractRegistry.cs` 对同一 `evt.PayloadJson` 解析两次：
- `MissingRequiredPayloadFields`（L54）→ JsonDocument.Parse
- `ParsePayload`（L74）→ 又一次 JsonDocument.Parse

### 1.5 决策观测与统一内核并行（跨项目）
Agent 侧 `DecisionRecorder` / `RunEvent` / `EventType` 独立通道，与 Frame 统一信封（ur_events）并行存在；`Activity` 事件与 `DecisionRecord` 的 Proposed/Resolved/Fallback 语义重叠，未归一。

---

## 二、冗余逻辑

| # | 位置 | 冗余内容 |
|---|---|---|
| 2.1 | File/InMemory/ObservabilityQuery 异步迭代器尾部 | **6 处**无意义 `await Task.CompletedTask;` 空 await |
| 2.2 | File/InMemoryEventStore | `_disposed` 死字段：仅赋值从不检查，Dispose 后仍可读写 |
| 2.3 | `Metrics/Histogram` | `ConcurrentDictionary` + `lock(_gate)` 双同步原语并存；`Snapshot` 锁内再用 `Interlocked.Read` |
| 2.4 | `Bus/HotRingSink` | ConcurrentQueue + 独立 Interlocked `_count` 双计数器 |
| 2.5 | `Entity/IObservableEntity`、`Query/MetricAggregate`、`Metrics/Counter/Histogram` | 全工程无消费方的预留 API（E1/E7 未接线） |
| 2.6 | `Registry/ObservabilitySemanticsJson` | `Metrics` 声明 `Dictionary<string,string>` 却只用 `.Keys`（应 `string[]`）；`Version` 解析后丢弃 |
| 2.7 | `HierarchySupport/DataContractRegistry` | `TryGet`/`Get`/`IsRegistered` 三查询口并存；`TryGet(out contract!)` 用 null 容忍符；`Get` 无消费方 |
| 2.8 | `Bus/ObservabilityBus` 构造 | `_asyncSinks` + `_sinks` 双列表冗余（OfType 筛选后仍保留全量） |

---

## 三、错误逻辑与隐患

### 3.1 AggregateAsync 的 fromUtc 参数被完全忽略（明确 bug）
`Query/ObservabilityQuery.cs:L40-71` 签名声明 `DateTime? fromUtc`，函数体从未使用。传入时间过滤**静默失效**，聚合结果恒为全时间范围。

```csharp
// 当前实现完全无视 fromUtc：
await foreach (var p in _store.TimelineAsync(scopeId, null, int.MaxValue, ct))
```

### 3.2 契约字段名 PascalCase vs 载荷 camelCase 不匹配
- `ObservabilityEventBuilder` 用 `PropertyNamingPolicy = CamelCase` 序列化 Payload（`Envelope/ObservabilityEventBuilder.cs:L9/L37`）。
- `ObservabilityContractRegistry.MissingRequiredPayloadFields` 用 `TryGetProperty(f)` **精确名**匹配（`Registry/ObservabilityContractRegistry.cs:L103`）。

契约声明 PascalCase 字段名（如 `Reason`/`RuleId`）时**永远校验失败**；大小写策略未统一 → 校验边界易误报。

### 3.3 PersistentEventSink 失败回滚无上限 / 头阻塞
`Bus/ObservabilitySinks.cs:L65` 失败时 `InsertRange(0, batch)` 把批次放回缓冲首部：
- store 持续故障时缓冲无限膨胀（at-least-once，但需封顶）。
- 永远卡在首个失败批 → head-of-line blocking，需防重入。

### 3.4 Activity.Dispose → Complete() 语义污染
`Bus/Obs.cs:L105` 中 `Dispose() => Complete()`：using 块内若抛异常且未显式 `Fail`，会补发 `stage=completed + succeeded`，**把失败帧记成成功**，污染观测语义。

### 3.5 统一信封缺 RunId / RoundId
统一信封（`Envelope/ObservabilityEvent.cs:L10-30`）只有 `EntityId+EventSeq`；关联键（TraceId/SessionId/ScopeKey）仅落在 `PersistedEvent` 行字段。标准要求的 RunId/RoundId 因果链重建维度在信封层缺失——标准落地的断点。

### 3.6 枚举约束「存在即校验，缺失即放过」
`Registry/ObservabilityContractRegistry.cs:L77-88` 枚举仅在校验到字段且为 string 时检查，字段缺失/非字符串时**静默通过**，与 Required 的强制语义不一致。

### 3.7 EntityId.Derive 拼接无常量约束
`Entity/EntityId.cs:L13` `parentScopeId + suffix` 无分隔符/受控生成器，较 `Key` 用 `\u001f` 分隔更易碰撞/歧义。

---

## 四、优先级建议

| 优先级 | 项 | 状态（2026-09-17） | 理由 |
|---|---|---|---|
| **高** | 3.1 fromUtc 被忽略 | **已修**（`ObservabilityQuery.cs:54` 已用 `fromUtc`） | 静默返回错误结果 |
| **高** | 3.2 字段名大小写不匹配 | **已修**（`ObservabilityContractRegistry.cs:112-130` 已大小写不敏感） | 校验误报导致误用 |
| **中** | 1.1 双套 `IObservabilitySink` | 未修 | 命名冲突、碎片化根 |
| **中** | 3.3 / 3.4 缓冲膨胀、Dispose 语义 | 未修 | 运行期风险 |
| **低** | 1.2–1.4、2.1–2.8、3.5–3.7 | 1.4 半修；其余未修 | 可批量清理 / 后续标准落地时收敛 |

> 台账（本项目内已收敛，非本报告条目）：内核**端口声明面**的重复与漂移（词表多副本 / 文案漏词 / kind 择路判定源分散）
> 已随《标准单源化》P0–P3 收敛：`PortImplVocabulary` 为词表唯一源、文案词表段经 `RenderList` 渲染
> （守卫 `PortVocabularySourceGuardTests` 锁「内核零手抄清单 + 零 kind 字面量」）、产物字段判据核 = `OutFieldContract`。