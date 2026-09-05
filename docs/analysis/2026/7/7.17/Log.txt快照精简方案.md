# Log.txt 快照精简方案

> **目标**：精简 Log.txt 中诊断快照的冗长输出，正常场景下不记录快照，异常场景下完整保留诊断信息。
>
> **原则**：修改调用方而非日志框架，能整合的都应整合。

---

## 一、现状分析

### 1.1 Log.txt 结构统计

Log.txt 共 **921 行**，其中：

| 类别 | 行数 | 占比 |
|------|------|------|
| 诊断快照 JSON（2个） | ~850 行 | **92%** |
| 普通日志 | ~71 行 | 8% |

### 1.2 冗长根因

#### 根因 1：诊断快照无条件输出

`RenderPipelineOrchestrator.cs` 中有 **2 处**无条件调用 `OutputDiagnosticSnapshot()`：

- `RenderFullAsync()` 第117-121行
- `BindEventsAsync()` 第149-152行

无论事件绑定成功还是失败，每次渲染都会输出完整的诊断快照 JSON。

#### 根因 2：每个控件产生 3 条冗余事件

在 `GenericEventBindingPipeline.WalkAndBind()` 和 `EventBindingPipelineFactory.CreateGenericPipeline()` 中，**每个控件**都会记录 3 个事件：

1. **`pipeline.walk_and_bind.visiting`** — WalkAndBind 中记录
2. **`transformer.PipelineChain`** — WithLifecycle.onBefore 中记录
3. **`PipelineChain.exit`** — WithLifecycle.onAfter 中记录

Log.txt 中所有控件的 `success=false` 且 `skip=false`，说明 PipelineChain 没有实际绑定任何东西，但每个控件仍然产生了 3 条日志。

- 第一个快照：9 个控件 × 3 = 27 个事件
- 第二个快照：16 个控件 × 3 = 48 个事件

#### 根因 3：重复的 DEBUG 日志

Log.txt 第1-2行 `"控件引用获取完成"` 重复了两次。

#### 根因 4：DIAG 控制台输出与快照信息重复

Log.txt 第902-916行的 `[DIAG] Control: 'xxx' Type='yyy' Template='(none)'` 信息已经包含在诊断快照的 `walk_and_bind.visiting` 事件中。

---

## 二、调用链分析

### 2.1 调用关系

```
MainForm / 其他调用方
  └─ ConfigContainer.RenderConfigAsync()          [WdUI.Container.cs]
       └─ _orchestrator.RenderFullAsync()          [RenderPipelineOrchestrator.cs]
            ├─ WinFormsRenderPipeline.Render()     [WdUI.RenderPipeline.cs]
            ├─ WinFormsRenderPipeline.ApplyLayout()
            └─ WinFormsRenderPipeline.BindEventsAsync()
                 └─ ExecuteBindEventsStage()
                      └─ _orchestrator!.ExecuteWithWalkAsync()
                           └─ GenericEventBindingPipeline.ExecuteWithWalk()
                                └─ WalkAndBind()
                                     └─ 每个控件记录 visiting 事件
                           └─ WithLifecycle (EventBindingPipelineFactory.cs)
                                └─ onBefore: 记录 transformer.PipelineChain
                                └─ onAfter: 记录 PipelineChain.exit
                      └─ 无条件输出诊断快照 ← 问题
            └─ OutputDiagnosticSnapshot() ← 问题（无条件）
```

### 2.2 关键代码位置

| 文件 | 行号 | 问题 |
|------|------|------|
| `RenderPipelineOrchestrator.cs` | ~117-121 | RenderFullAsync 中无条件调用 OutputDiagnosticSnapshot |
| `RenderPipelineOrchestrator.cs` | ~149-152 | BindEventsAsync 中无条件调用 OutputDiagnosticSnapshot |
| `RenderPipelineOrchestrator.cs` | ~227 | OutputDiagnosticSnapshot 为 protected |
| `WdUI.RenderPipeline.cs` | ~351-365 | ExecuteBindEventsStage 中无条件输出快照 |
| `GenericEventBindingPipeline.cs` | ~137-143 | WalkAndBind 中记录 visiting 事件 |
| `EventBindingPipelineFactory.cs` | ~130-165 | WithLifecycle 中 onBefore/onAfter 各记录一条事件 |
| `MainForm.cs` | 未知 | 重复的"控件引用获取完成"日志 |

---

## 三、精简方案

### 修改 1：RenderPipelineOrchestrator.cs — 移除内部快照调用，改为 public

**改动 a** — `RenderFullAsync()` 中移除快照输出：

```csharp
// 当前（第117-121行）：
// 4. 输出诊断快照
if (_eventOrchestrator != null)
{
    OutputDiagnosticSnapshot();
}

// 改为：移除快照输出，让调用方决定
```

**改动 b** — `BindEventsAsync()` 中移除快照输出：

```csharp
// 当前（第149-152行）：
if (_eventOrchestrator != null)
{
    OutputDiagnosticSnapshot();
}

// 改为：移除快照输出，让调用方决定
```

**改动 c** — `OutputDiagnosticSnapshot()` 改为 public：

```csharp
// 当前：protected void OutputDiagnosticSnapshot()
// 改为：public void OutputDiagnosticSnapshot()
```

### 修改 2：WdUI.Container.cs (ConfigContainer) — 调用方按需记录快照

**位置**：`RenderConfigAsync()` 第286-287行之后

```csharp
(eventsBoundSuccess, eventsBoundError) =
    await _orchestrator.RenderFullAsync(this, config);

// ★ 新增：事件绑定失败时才输出诊断快照
if (!eventsBoundSuccess)
{
    _orchestrator.OutputDiagnosticSnapshot();
}
```

### 修改 3：WdUI.RenderPipeline.cs — 移除 ExecuteBindEventsStage 中的快照输出

**位置**：`ExecuteBindEventsStage()` 第351-365行

**当前**：无条件输出快照（第354-365行）
**改为**：移除整个快照输出块（因为 RenderFullAsync 的调用方已按需处理）

```csharp
// 当前代码（第351-365行）：
// ★ 2026-07-12 新增：输出 DiagnosticContext 快照，确认事件绑定管线执行详情
var snapshot = EventBindingDiagnosticContext.GetSnapshotString();
if (string.IsNullOrWhiteSpace(snapshot) || snapshot == "{}")
{
    WdUILog.Log("wdui:DiagnosticSnapshot_Empty",
        new { warning = "Diagnostic snapshot is empty: ..." });
}
else
{
    WdUILog.Log("wdui:DiagnosticSnapshot", new { snapshot });
}

// 改为：移除整个块
```

### 修改 4：合并 visiting/transformer/exit 为一条事件

**改动 a** — `GenericEventBindingPipeline.cs` `WalkAndBind()` 第137-143行：

**当前**：每个控件记录 `pipeline.walk_and_bind.visiting` 事件
**改为**：移除 visiting 记录（合并到 WithLifecycle 中）

```csharp
// 当前（第137-143行）：
EventBindingDiagnosticContext.Record(EventNames.PipelineWalkAndBindVisiting, new
{
    controlName,
    hasEvents,
    hasBindings,
    hasCustomEvents
});

// 改为：移除这段代码
```

**改动 b** — `EventBindingPipelineFactory.cs` `WithLifecycle` 第130-165行：

**当前**：onBefore 记录 `transformer.PipelineChain`，onAfter 记录 `PipelineChain.exit`
**改为**：合并为一条 `pipeline.walk_and_bind.processed` 事件：

```csharp
// 当前：
.WithLifecycle(
    onBefore: (ctx, _) =>
    {
        EventBindingDiagnosticContext.Record("transformer.PipelineChain", new { ... });
        return ctx;
    },
    onAfter: (ctx, result) =>
    {
        EventBindingDiagnosticContext.Record("PipelineChain.exit", new { ... });
        return ctx;
    },
    onError: (ctx, ex) => { ... }
)

// 改为：
.WithLifecycle(
    onAfter: (ctx, result) =>
    {
        var controlName = result.CurrentControlConfig?.Name
            ?? EventBindingHelper.GetControlName(result.CurrentControl)
            ?? "(unknown)";
        var hasEvents = result.CurrentControlConfig?.Events != null;
        var hasBindings = result.CurrentControlConfig?.Events?.Bindings != null;
        var hasCustomEvents = result.CurrentControlConfig?.Events?.EventsDictionary?.Count > 0;
        EventBindingDiagnosticContext.Record("pipeline.walk_and_bind.processed", new
        {
            controlName,
            hasEvents,
            hasBindings,
            hasCustomEvents,
            success = !result.SkipCurrentControl
        });
    },
    onError: (ctx, ex) => { ... }  // 保留异常记录
)
```

### 修改 5：移除重复的 DEBUG 日志

**位置**：`MainForm.cs` 中重复的 `"控件引用获取完成"` 日志

需要找到 MainForm.cs 中对应的代码，移除重复行。

---

## 四、预期效果

| 场景 | 当前行数 | 精简后行数 | 精简率 |
|------|---------|-----------|--------|
| 事件绑定成功（正常） | ~460 行/次 | ~30 行/次 | **93%** |
| 事件绑定失败（异常） | ~460 行/次 | ~460 行/次 | 0%（完整保留） |

### 具体变化

| 修改项 | 变化 |
|--------|------|
| 修改 1+2+3 | 正常场景不再输出诊断快照 JSON |
| 修改 4 | 每个控件从 3 条事件 → 1 条事件（-66%） |
| 修改 5 | 移除 1 行重复日志 |

---

## 五、实施顺序

1. **修改 1** — RenderPipelineOrchestrator.cs（基础：暴露方法 + 移除内部调用）
2. **修改 2** — WdUI.Container.cs（调用方按需记录）
3. **修改 3** — WdUI.RenderPipeline.cs（移除重复的快照输出）
4. **修改 4** — GenericEventBindingPipeline.cs + EventBindingPipelineFactory.cs（合并事件）
5. **修改 5** — MainForm.cs（移除重复日志）
