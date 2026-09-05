# WdUI 事件绑定逻辑迁移方案

> **目标**：将 WdUI 中散落的事件绑定逻辑收拢到 EventTriggerFramework，实现职责分离
> **状态**：方案设计阶段
> **日期**：2026-07-02

---

## 1. 现状分析

### 1.1 当前架构

```
┌─────────────────────────────────────────────────────────────┐
│  WdUI (WinFormsRenderPipeline)                              │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  ExecuteBindEventsStage()                            │   │
│  │  ├─ 持有 _eventBinder: IEventBindingService?         │   │
│  │  ├─ 类型判断: _eventBinder is DefaultEventBindingService │   │
│  │  ├─ searchRoot 参数传递                              │   │
│  │  └─ FindForm() 查找父窗体                            │   │
│  │                                                      │   │
│  │  DisposeSession()                                    │   │
│  │  └─ _eventBinder.UnbindEventsAsync()                 │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  职责：UI 渲染 + 事件绑定编排（耦合）                        │
└─────────────────────────────────────────────────────────────┘
         │ 接口调用
         ▼
┌─────────────────────────────────────────────────────────────┐
│  EventTriggerFramework (DefaultEventBindingService)         │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  BindEvents() / BindEventsAsync()                    │   │
│  │  ├─ EventBindingPipelineRunner                       │   │
│  │  ├─ EventBindingTransformers                         │   │
│  │  └─ WinFormsEventBinder                              │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  职责：事件绑定 + 事件触发                                   │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 问题清单

| # | 问题 | 位置 | 影响 |
|---|------|------|------|
| 1 | **类型判断耦合**：`ExecuteBindEventsStage` 判断 `_eventBinder is DefaultEventBindingService` 走不同路径 | `WdUI.RenderPipeline.cs:268` | WdUI 依赖 EventTriggerFramework 的具体实现类型 |
| 2 | **searchRoot 参数穿透**：WdUI 的 sideload 场景需要传递 `searchRoot`，但 `IEventBindingService` 接口没有此参数 | `WdUI.RenderPipeline.cs:270` | 接口不够通用，WdUI 需要知道具体实现 |
| 3 | **FindForm 逻辑在 WdUI**：事件绑定前需要查找父 Form，这个逻辑在 WdUI 中 | `WdUI.RenderPipeline.cs:263` | 事件绑定的前置条件不应由渲染层处理 |
| 4 | **DisposeSession 中解绑事件**：WdUI 的会话清理逻辑中调用了 `_eventBinder.UnbindEventsAsync()` | `WdUI.RenderPipeline.cs:545-553` | 解绑逻辑应封装在 EventTriggerFramework 内部 |
| 5 | **幂等性检查在 WdUI**：`_boundTargets` 字典和 `ReferenceEquals` 检查在 WdUI 中 | `WdUI.RenderPipeline.cs:472-480` | 绑定状态管理应属于事件绑定框架的职责 |

### 1.3 涉及的文件

| 文件 | 行数 | 涉及内容 |
|------|------|----------|
| `CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs` | 614 | `_eventBinder` 字段、`ExecuteBindEventsStage`、`DisposeSession`、`BindEventsAsync`、`_boundTargets` |
| `CsharpPrj/Operation/EventTriggerFramework/DefaultEventBindingService.cs` | 402 | `BindEvents` 方法（含 `searchRoot` 参数）、`WinFormsEventBinder` 属性 |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingPipeline.cs` | 233 | 管线执行器 |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | 564 | 各 Transformer 实现 |
| `CsharpPrj/Share/EventBindingOrchestrator/Services/LazyEventExecutionService.cs` | — | 延迟执行服务 |

---

## 2. 迁移目标

### 2.1 目标架构

```
┌─────────────────────────────────────────────┐
│  WdUI (WinFormsRenderPipeline)              │
│                                             │
│  ExecuteBindEventsStage()                   │
│  └─ _eventBinder.BindEventsAsync(target)    │
│     （纯接口调用，无类型判断）               │
│                                             │
│  职责：仅 UI 渲染                           │
└─────────────────────────────────────────────┘
         │ 统一接口调用
         ▼
┌─────────────────────────────────────────────┐
│  EventTriggerFramework                      │
│                                             │
│  DefaultEventBindingService                 │
│  ├─ BindEventsAsync(target, config)         │
│  │   ├─ 内部处理 FindForm                   │
│  │   ├─ 内部处理 searchRoot                 │
│  │   └─ 内部处理幂等性检查                  │
│  ├─ UnbindEventsAsync(target)               │
│  └─ WinFormsEventBinder (内部)              │
│                                             │
│  职责：事件绑定 + 触发 + 状态管理           │
└─────────────────────────────────────────────┘
```

### 2.2 原则

1. **WdUI 只通过接口调用**，不引用 EventTriggerFramework 的具体类型
2. **EventTriggerFramework 封装所有绑定细节**，包括 FindForm、searchRoot、幂等性
3. **接口定义在 Share 层**，WdUI 和 EventTriggerFramework 都引用接口
4. **保持向后兼容**，迁移过程中不破坏现有调用方

---

## 3. 迁移步骤

### Step 1: 增强 IEventBindingService 接口

**目标**：在 Share 层的接口中添加 `searchRoot` 参数支持，消除 WdUI 的类型判断

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventBindingService.cs`

```csharp
// 当前接口
public interface IEventBindingService
{
    Task BindEventsAsync(object target, UIConfiguration config);
    Task UnbindEventsAsync(object target);
}

// 增强后接口
public interface IEventBindingService
{
    /// <param name="searchRoot">可选：控件搜索根容器（sideload 场景使用）</param>
    Task BindEventsAsync(object target, UIConfiguration config, Control? searchRoot = null);
    Task UnbindEventsAsync(object target);
}
```

**影响**：
- `DefaultEventBindingService` 实现新增 `searchRoot` 参数
- `WinFormsRenderPipeline` 可以直接传 `searchRoot`，无需类型判断

---

### Step 2: 将 FindForm 逻辑迁移到 DefaultEventBindingService

**目标**：WdUI 不再负责查找父 Form，由 EventTriggerFramework 内部处理

**文件**：`CsharpPrj/Operation/EventTriggerFramework/DefaultEventBindingService.cs`

```csharp
// 迁移前（在 WdUI 中）
var form = FindForm(target);
if (form != null)
{
    if (_eventBinder is DefaultEventBindingService defaultBinder)
        defaultBinder.BindEvents(form, config, searchRoot: target);
}

// 迁移后（在 DefaultEventBindingService 中）
public async Task BindEventsAsync(object target, UIConfiguration config, Control? searchRoot = null)
{
    var form = FindForm(target as Control);
    if (form == null) return;
    // ... 绑定逻辑
}
```

**注意**：`FindForm` 方法在 WdUI 中委托到 `Framework.Shared.ControlFinder.FindForm`，迁移时需要确保 EventTriggerFramework 能访问到 `ControlFinder`。如果存在依赖问题，可以在 EventTriggerFramework 中实现一个简化的 `FindForm` 版本。

---

### Step 3: 将幂等性检查迁移到 DefaultEventBindingService

**目标**：WdUI 不再维护 `_boundTargets` 字典

**文件**：`CsharpPrj/Operation/EventTriggerFramework/DefaultEventBindingService.cs`

```csharp
// 在 DefaultEventBindingService 中新增
private readonly ConcurrentDictionary<object, WeakReference<UIConfiguration>> _boundTargets = new();

public async Task BindEventsAsync(object target, UIConfiguration config, Control? searchRoot = null)
{
    // 幂等性检查
    if (_boundTargets.TryGetValue(target, out var weakRef) &&
        weakRef.TryGetTarget(out var boundConfig) &&
        ReferenceEquals(boundConfig, config))
    {
        return; // 已绑定且 config 未变，跳过
    }
    
    // ... 绑定逻辑 ...
    
    // 记录已绑定状态
    _boundTargets[target] = new WeakReference<UIConfiguration>(config);
}
```

**WdUI 侧删除**：
- 删除 `_boundTargets` 字段（`WdUI.RenderPipeline.cs:125`）
- 删除 `BindEventsAsync` 中的幂等性检查代码（`WdUI.RenderPipeline.cs:472-480`）
- 删除 `DisposeSession` 中的 `_boundTargets.TryRemove`（`WdUI.RenderPipeline.cs:540`）

---

### Step 4: 简化 WdUI 的 ExecuteBindEventsStage

**目标**：WdUI 的事件绑定阶段只做接口调用

**文件**：`CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs`

```csharp
// 迁移后
protected override async Task ExecuteBindEventsStage(Control target, IRenderSession session)
{
    if (_eventBinder == null)
        throw new InvalidOperationException(
            $"[WinFormsRenderPipeline] EventBinder 未设置 (target: '{target.Name}')");

    if (session.Config is not UIConfiguration config)
        throw new InvalidOperationException(
            $"[WinFormsRenderPipeline] 会话配置不是 UIConfiguration 类型");

    // ★ 统一接口调用，无类型判断
    await _eventBinder.BindEventsAsync(target, config, searchRoot: target).ConfigureAwait(false);

    WdUILog.Log("wdui:RenderPipeline_ExecuteBindEventsStage_done",
        new { targetName = target.Name, viewCount = config.Views?.Count ?? 0 });
}
```

---

### Step 5: 简化 DisposeSession 中的解绑逻辑

**目标**：WdUI 的会话清理只通知解绑，不关心具体实现

**文件**：`CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs`

```csharp
// 迁移后
public void DisposeSession(Control target)
{
    // 通知解绑（EventTriggerFramework 内部处理 FindForm）
    if (_eventBinder != null)
    {
        try
        {
            await _eventBinder.UnbindEventsAsync(target).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WdUILog.Log("wdui:RenderPipeline_DisposeSession_UnbindError",
                new { targetName = target.Name, error = ex.Message });
        }
    }

    // 清理 LayoutPanel 和会话...
}
```

---

## 4. 迁移顺序

```
Step 1: 增强 IEventBindingService 接口
    ↓
Step 2: 将 FindForm 逻辑迁移到 DefaultEventBindingService
    ↓
Step 3: 将幂等性检查迁移到 DefaultEventBindingService
    ↓
Step 4: 简化 WdUI 的 ExecuteBindEventsStage
    ↓
Step 5: 简化 DisposeSession 中的解绑逻辑
    ↓
验证：编译通过 + 功能测试
```

### 依赖关系

- Step 1 是 Step 2-5 的前提（接口需要先扩展）
- Step 2-3 可以并行执行（互不依赖）
- Step 4-5 依赖 Step 1-3 完成
- 每个步骤完成后都应编译验证

---

## 5. 风险与注意事项

### 5.1 风险

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| `ControlFinder.FindForm` 在 EventTriggerFramework 中不可用 | 中 | 高 | 在 EventTriggerFramework 中实现简化版 FindForm |
| 现有调用方依赖 WdUI 的 `_eventBinder` 属性 | 中 | 中 | 保留属性但标记为 `[Obsolete]` |
| 幂等性检查迁移后行为不一致 | 低 | 高 | 迁移后全面测试 sideload 场景 |
| 接口变更影响其他调用方 | 低 | 中 | 使用可选参数 `searchRoot = null` 保持向后兼容 |

### 5.2 注意事项

1. **`_eventBinder` 属性保留**：`WinFormsRenderPipeline.EventBinder` 属性被外部调用方（如 `FileDrivenExecutor`）使用，迁移后应保留但标记为 `[Obsolete]`，引导调用方通过构造函数注入

2. **`DefaultEventBindingService.BindEvents` 同步方法**：当前 WdUI 通过 `defaultBinder.BindEvents(form, config, searchRoot: target)` 调用同步版本。迁移后统一走异步版本 `BindEventsAsync`

3. **`_boundTargets` 清理时机**：WdUI 的 `DisposeSession` 中清理 `_boundTargets`，迁移到 EventTriggerFramework 后，需要确保 `UnbindEventsAsync` 也清理对应的幂等性记录

4. **测试覆盖**：重点测试 sideload 场景（`_sideContainer` 中的控件事件绑定）和普通场景

---

## 6. 迁移完成后的文件变更清单

### 删除/修改

| 文件 | 变更 |
|------|------|
| `WdUI.RenderPipeline.cs` | 删除 `_boundTargets` 字段 |
| `WdUI.RenderPipeline.cs` | 简化 `ExecuteBindEventsStage`（删除类型判断） |
| `WdUI.RenderPipeline.cs` | 简化 `DisposeSession`（删除 `_boundTargets.TryRemove`） |
| `WdUI.RenderPipeline.cs` | 简化 `BindEventsAsync`（删除幂等性检查） |
| `WdUI.RenderPipeline.cs` | `_eventBinder` 属性标记 `[Obsolete]` |

### 新增/修改

| 文件 | 变更 |
|------|------|
| `IEventBindingService.cs` | 接口新增 `searchRoot` 可选参数 |
| `DefaultEventBindingService.cs` | 新增 `_boundTargets` 字段和幂等性检查 |
| `DefaultEventBindingService.cs` | 新增内部 `FindForm` 方法 |
| `DefaultEventBindingService.cs` | `BindEventsAsync` 支持 `searchRoot` 参数 |

---

## 7. 验收标准

- [ ] 编译通过，0 错误 0 警告
- [ ] WdUI 中无 `DefaultEventBindingService` 类型引用
- [ ] WdUI 中无 `FindForm` 调用
- [ ] WdUI 中无 `_boundTargets` 相关代码
- [ ] 普通 UI 场景事件绑定正常
- [ ] Sideload 场景事件绑定正常
- [ ] 会话清理时事件解绑正常
- [ ] 幂等性检查正常工作（重复绑定被跳过）
