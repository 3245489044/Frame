# 数据迁移参考：旧 SchedulerState 快照 → ur_tasks PayloadJson（P8）

> 日期：2026-08-13
> 状态：**暂不执行**（当前无生产数据）。本文件为后续有数据时的一次性迁移参考，非本阶段执行项。

## 背景

重构后 `AgentTaskItem`（实现 `ILoopState`）成为唯一权威实体，单条 `ur_tasks.PayloadJson` 为唯一权威状态基座。
旧的 `SchedulerState` 快照（经 `IPersistenceStore.Snapshots` 以 `hash(userInput)` 派生键保存）需并入 `ur_tasks.PayloadJson`，
消除「`ur_tasks` 存任务 + 快照存储存运行态」的双份数据对齐。

## 迁移目标

| 旧（重构前） | 新（重构后） |
|---|---|
| `ur_tasks` 表：TaskKey/Priority/MaxRetries/Status/PayloadJson（仅输入契约） | `ur_tasks` 表：PayloadJson = 完整 `AgentTaskItem`（输入契约 + 运行时 + 生命周期 + Data） |
| `SchedulerState` 快照（Snapshots 存储，键 = thread-{hash}.scheduler） | 快照字段合并进 `ur_tasks.PayloadJson`；运行态 = 任务的一部分 |

## 迁移方式（应用级，非纯 SQL）

旧快照 JSON 的 schema（`SchedulerStateSnapshot`：Round/BudgetLevel/TokenValue/TokenMax/Halt/ContinuousFailures/
CurrentStrategyId/AvailableStrategyIds/AgentStatus/RecentTransitions/Data）与新的 `AgentTaskItem` PayloadJson 字段同名同构，
故可**先反序列化为 `AgentTaskItem`，再经 `AgentTaskItemJsonConverter` 重新序列化**写回 `ur_tasks.PayloadJson`。
（新 PayloadJson 经 `[JsonConverter]` 单一状态基座生成，字段一一对应，无需逐字段 SQL 拼接。）

```csharp
// 一次性迁移工具伪代码（应用层，读取旧快照 → 合成 AgentTaskItem → 写回 ur_tasks）
foreach (var (taskKey, oldJson) in LoadLegacySchedulerSnapshots())   // SELECT * FROM SchedulerState
{
    var old = LegacyDeserialize<SchedulerStateSnapshot>(oldJson);      // 旧 DTO（保留读取路径）
    var item = new AgentTaskItem
    {
        TaskKey = taskKey,
        Round = old.Round,
        Status = TaskExecutionStatus.NotStarted,                       // 重启后由任务池回填
        ContinuousFailures = old.ContinuousFailures,
        CurrentStrategyId = old.CurrentStrategyId ?? "",
    };
    item.AvailableStrategyIds.AddRange(old.AvailableStrategyIds ?? Empty);
    foreach (var kv in old.AgentStatus ?? Empty) item.AgentStatus[kv.Key] = kv.Value;
    item.RecentTransitions.AddRange(old.RecentTransitions ?? Empty);
    DeterministicCoreCopier.CopyFromValues(item, old.BudgetLevel, old.TokenValue, old.TokenMax, old.Halt);
    foreach (var kv in old.Data ?? Empty) item.Data.Set(kv.Key, kv.Value);

    // UPDATE ur_tasks SET PayloadJson=@json, Status=@status, UpdatedAt=@now WHERE TaskKey=@key
    UpdatePayload(item.TaskKey, item);
}
```

## 说明

- **无生产数据 → 无需执行**。表 `SchedulerState` 若不存在则本步无操作。
- `AgentTaskItemJsonConverter` 的 `Read` 保留了对旧字段名的兼容读取（Round/BudgetLevel/TokenValue/TokenMax/Halt/...），
  因此即便旧 PayloadJson 直接落库，新读取路径也能还原核心字段（缺省回退默认值）。
- 若未来存在旧 `SchedulerState` 快照，按上述工具合并后即可删除旧快照存储，完成单根收敛。
