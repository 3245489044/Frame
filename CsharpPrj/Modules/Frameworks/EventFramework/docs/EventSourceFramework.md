# EventSource Framework — 事件源框架设计文档

> **版本：** v1.0
> **维护者：** WorkFlowPrj Team
> **更新日期：** 2026-07-13
> **定位：** Modules.EventFramework 的核心抽象层

---

## 目录

- [EventSource Framework — 事件源框架设计文档](#eventsource-framework--事件源框架设计文档)
  - [目录](#目录)
  - [1. 设计哲学](#1-设计哲学)
    - [1.1 核心思想](#11-核心思想)
    - [1.2 事件源的本质](#12-事件源的本质)
    - [1.3 设计原则](#13-设计原则)
  - [2. 事件源定义](#2-事件源定义)
    - [2.1 IEventSourceNode — 事件源节点接口](#21-ieventsourcenode--事件源节点接口)
    - [2.2 接口拆分说明](#22-接口拆分说明)
    - [2.3 事件源的生命周期](#23-事件源的生命周期)
  - [3. 事件源分类](#3-事件源分类)
    - [3.1 按来源分类](#31-按来源分类)
    - [3.2 按生命周期分类](#32-按生命周期分类)
    - [3.3 按通信模式分类](#33-按通信模式分类)
  - [4. 框架架构](#4-框架架构)
    - [4.1 分层架构](#41-分层架构)
    - [4.2 模块职责](#42-模块职责)
    - [4.3 依赖关系](#43-依赖关系)
  - [5. 核心管线](#5-核心管线)
    - [5.1 事件绑定管线](#51-事件绑定管线)
    - [5.2 事件触发管线](#52-事件触发管线)
    - [5.3 管线与事件源的关系](#53-管线与事件源的关系)
  - [6. 适配器模式](#6-适配器模式)
    - [6.1 WinForms 适配器](#61-winforms-适配器)
    - [6.2 WPF 适配器（未来）](#62-wpf-适配器未来)
    - [6.3 定时器适配器](#63-定时器适配器)
  - [7. 构建方案](#7-构建方案)
    - [7.1 阶段一：接口定义（Modules.EventFramework.Abstractions）](#71-阶段一接口定义moduleseventframeworkabstractions)
    - [7.2 阶段二：核心实现（Modules.EventFramework.Core）](#72-阶段二核心实现moduleseventframeworkcore)
    - [7.3 阶段三：管线实现（Modules.EventFramework.Pipeline）](#73-阶段三管线实现moduleseventframeworkpipeline)
    - [7.4 阶段四：WinForms 适配器（Share.EventBindingOrchestrator.Adapters）](#74-阶段四winforms-适配器shareeventbindingorchestratoradapters)
    - [7.5 阶段五：Share 层委托包装（向后兼容）](#75-阶段五share-层委托包装向后兼容)
    - [7.6 阶段六：迁移与测试](#76-阶段六迁移与测试)
  - [8. 与现有代码的关系](#8-与现有代码的关系)
    - [8.1 现有代码映射](#81-现有代码映射)

---

## 1. 设计哲学

### 1.1 核心思想

**EventFramework 构建的是"事件源框架"（EventSource Framework），而不是"控件框架"（Control Framework）。**

控件只是事件源的一种具体形态。事件源可以是一切能发出通知的对象：

```
事件源（IEventSourceNode）
├── UI 控件（WinForms Control / WPF Control / HTML Element）
├── 定时器（Timer）
├── 文件系统监视器（FileSystemWatcher）
├── 消息队列消费者（MQ Consumer）
├── 数据库变更监听（CDC）
├── 网络 Socket
├── 硬件信号（GPIO 中断）
└── 任何实现了 IEventSourceNode 接口的对象
```

### 1.2 事件源的本质

剥离所有 UI 框架特性后，事件源的本质是三个正交能力的组合：

| # | 本质 | 接口 | 说明 |
|---|------|------|------|
| 1 | **可订阅的通知源** | `IEventSource` | 对象在特定时刻发出通知，外部可以订阅 |
| 2 | **可读写的状态容器** | `IValueContainer` | 对象持有可读写的状态值 |
| 3 | **可遍历的树节点** | `ITreeNode` | 对象可以包含子对象，形成树形结构 |

这三个能力是正交的——一个事件源可以只具备其中一部分能力：

- **定时器**：只有通知能力（1），没有状态（2），没有子节点（3）
- **TextBox**：有通知能力（1），有状态（2），没有子节点（3）
- **Form/Panel**：有通知能力（1），可能有状态（2），有子节点（3）
- **文件监视器**：只有通知能力（1），没有状态（2），没有子节点（3）

### 1.3 设计原则

```
1. 面向接口编程：EventFramework 只依赖接口，不依赖具体实现
2. 单一职责：每个接口只描述一个正交能力
3. 适配器模式：具体 UI 框架通过适配器接入
4. 管线化：事件绑定和触发通过 StagePipeline 编排
5. 零 UI 依赖：核心框架不引用任何 UI 程序集
```

---

## 2. 事件源定义

### 2.1 IEventSourceNode — 事件源节点接口

```csharp
// ============================================================
// IEventSourceNode.cs — 事件源节点接口
//
// 事件源是一切能发出通知的对象的抽象。
// 控件只是事件源的一种具体形态。
//
// 设计要点：
//   1. 三个正交能力：通知源 + 状态容器 + 树节点
//   2. 每个能力可选实现（通过 CanXxx 属性查询）
//   3. 线程安全
//   4. 零 UI 框架依赖
// ============================================================

namespace Modules.EventFramework.Abstractions;

/// <summary>
/// 事件源节点 — 一切能发出通知的对象的抽象
/// <para>控件只是事件源的一种具体形态。</para>
/// <para>三个正交能力：通知源 + 状态容器 + 树节点，每个能力可选实现。</para>
/// </summary>
public interface IEventSourceNode : IDisposable
{
    /// <summary>事件源唯一标识</summary>
    string Id { get; }

    /// <summary>事件源名称（如控件名、定时器名）</summary>
    string Name { get; }

    /// <summary>事件源类型名称（如 "Button"、"Timer"、"FileWatcher"）</summary>
    string SourceType { get; }

    // ============================================================
    // 能力一：可订阅的通知源（IEventSource）
    // ============================================================

    /// <summary>是否支持事件订阅</summary>
    bool CanSubscribe { get; }

    /// <summary>
    /// 订阅事件
    /// </summary>
    /// <param name="eventName">事件名称（如 "Click"、"Elapsed"、"Changed"）</param>
    /// <param name="handler">事件处理器</param>
    /// <returns>是否订阅成功</returns>
    bool Subscribe(string eventName, Delegate handler);

    /// <summary>
    /// 取消订阅事件
    /// </summary>
    /// <param name="eventName">事件名称</param>
    /// <param name="handler">事件处理器（null 时取消该事件所有订阅）</param>
    /// <returns>是否取消成功</returns>
    bool Unsubscribe(string eventName, Delegate? handler = null);

    /// <summary>
    /// 获取支持的事件名称列表
    /// </summary>
    IEnumerable<string> GetSupportedEvents();

    // ============================================================
    // 能力二：可读写的状态容器（IValueContainer）
    // ============================================================

    /// <summary>是否支持读取状态</summary>
    bool CanRead { get; }

    /// <summary>是否支持写入状态</summary>
    bool CanWrite { get; }

    /// <summary>
    /// 读取当前状态值
    /// </summary>
    object? GetValue();

    /// <summary>
    /// 写入状态值
    /// </summary>
    void SetValue(object? value);

    // ============================================================
    // 能力三：可遍历的树节点（ITreeNode）
    // ============================================================

    /// <summary>是否有子节点</summary>
    bool HasChildren { get; }

    /// <summary>
    /// 获取直接子节点
    /// </summary>
    IEnumerable<IEventSourceNode> GetChildren();

    /// <summary>
    /// 按名称递归查找子节点
    /// </summary>
    IEventSourceNode? FindChild(string name);
}
```

### 2.2 接口拆分说明

`IEventSourceNode` 是三个正交接口的组合。如果某些场景只需要部分能力，可以直接使用拆分后的接口：

```csharp
// 只关心通知能力
public interface IEventSource : IDisposable
{
    string Id { get; }
    string Name { get; }
    string SourceType { get; }
    bool CanSubscribe { get; }
    bool Subscribe(string eventName, Delegate handler);
    bool Unsubscribe(string eventName, Delegate? handler = null);
    IEnumerable<string> GetSupportedEvents();
}

// 只关心状态读写
public interface IValueContainer
{
    bool CanRead { get; }
    bool CanWrite { get; }
    object? GetValue();
    void SetValue(object? value);
}

// 只关心树形遍历
public interface ITreeNode
{
    string Name { get; }
    bool HasChildren { get; }
    IEnumerable<ITreeNode> GetChildren();
    ITreeNode? FindChild(string name);
}
```

### 2.3 事件源的生命周期

```
创建 → 初始化 → 订阅事件 → [事件触发 → 执行函数] × N → 取消订阅 → 销毁
```

```csharp
public abstract class EventSourceNodeBase : IEventSourceNode
{
    public string Id { get; }
    public abstract string Name { get; }
    public abstract string SourceType { get; }

    // 生命周期状态
    public bool IsInitialized { get; private set; }
    public bool IsDisposed { get; private set; }

    // 订阅管理
    private readonly ConcurrentDictionary<string, List<Delegate>> _subscriptions = new();

    public virtual bool CanSubscribe => true;
    public virtual bool CanRead => false;
    public virtual bool CanWrite => false;
    public virtual bool HasChildren => false;

    public virtual bool Subscribe(string eventName, Delegate handler)
    {
        if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentNullException(nameof(eventName));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        var list = _subscriptions.GetOrAdd(eventName, _ => new List<Delegate>());
        lock (list)
        {
            list.Add(handler);
        }
        return OnSubscribe(eventName, handler);
    }

    public virtual bool Unsubscribe(string eventName, Delegate? handler = null)
    {
        if (handler == null)
        {
            _subscriptions.TryRemove(eventName, out _);
            return OnUnsubscribeAll(eventName);
        }

        if (_subscriptions.TryGetValue(eventName, out var list))
        {
            lock (list)
            {
                list.Remove(handler);
            }
        }
        return OnUnsubscribe(eventName, handler);
    }

    // 子类实现具体的事件绑定逻辑
    protected abstract bool OnSubscribe(string eventName, Delegate handler);
    protected abstract bool OnUnsubscribe(string eventName, Delegate handler);
    protected virtual bool OnUnsubscribeAll(string eventName) => true;

    public virtual object? GetValue() => throw new NotSupportedException();
    public virtual void SetValue(object? value) => throw new NotSupportedException();
    public virtual IEnumerable<IEventSourceNode> GetChildren() => Enumerable.Empty<IEventSourceNode>();
    public virtual IEventSourceNode? FindChild(string name) => null;

    public virtual IEnumerable<string> GetSupportedEvents() => _subscriptions.Keys;

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        OnDispose();
        _subscriptions.Clear();
    }

    protected virtual void OnDispose() { }
}
```

---

## 3. 事件源分类

### 3.1 按来源分类

| 类别 | 示例 | 通知能力 | 状态能力 | 树能力 | 适配器 |
|------|------|---------|---------|-------|--------|
| **UI 控件** | Button, TextBox, ListView | ✅ | ✅ | ✅ | WinForms/WPF 适配器 |
| **定时器** | System.Timers.Timer | ✅ | ❌ | ❌ | TimerEventSourceNode |
| **文件系统** | FileSystemWatcher | ✅ | ❌ | ❌ | FileWatcherEventSourceNode |
| **消息队列** | MQ Consumer | ✅ | ❌ | ❌ | MqEventSourceNode |
| **数据库** | SqlDependency, CDC | ✅ | ❌ | ❌ | DbEventSourceNode |
| **网络** | Socket, HttpListener | ✅ | ❌ | ❌ | NetworkEventSourceNode |
| **硬件** | GPIO, SerialPort | ✅ | ❌ | ❌ | HardwareEventSourceNode |

### 3.2 按生命周期分类

| 类型 | 说明 | 示例 |
|------|------|------|
| **持久型** | 长期存在，多次触发 | UI 控件、定时器 |
| **一次性** | 触发一次后自动销毁 | 按钮点击（单次）、一次性定时器 |
| **条件型** | 满足条件时触发 | 文件变更、数据库变更 |

### 3.3 按通信模式分类

| 模式 | 说明 | 示例 |
|------|------|------|
| **推送（Push）** | 事件源主动通知 | Button.Click、Timer.Elapsed |
| **拉取（Polling）** | 外部定期检查状态 | 文件大小变化、数据库状态 |
| **混合** | 推送 + 拉取回退 | 带心跳检测的网络连接 |

---

## 4. 框架架构

### 4.1 分层架构

```
┌─────────────────────────────────────────────────────────────────┐
│                     Modules.EventFramework                       │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Abstractions/          ← 接口定义层（零依赖）            │   │
│  │    IEventSourceNode                                      │   │
│  │    IEventBus                                             │   │
│  │    IEventBindingRegistry                                 │   │
│  │    IEventNameNormalizer                                  │   │
│  │    IValueAccessor                                        │   │
│  │    IEventSubscription                                    │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Core/                   ← 核心实现层                    │   │
│  │    EventSourceNodeBase                                   │   │
│  │    EventBus                                              │   │
│  │    EventBindingRegistry                                  │   │
│  │    EventNameNormalizer                                   │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Pipeline/               ← 管线编排层                    │   │
│  │    EventBindingStageBase                                 │   │
│  │    ConfigBindingStage                                    │   │
│  │    DeclarationBindingStage                               │   │
│  │    EventTriggerServiceBase                               │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Models/                 ← 数据模型层                    │   │
│  │    EventBindingDeclaration                               │   │
│  │    EventBindingContext                                   │   │
│  │    EventBindingInfo                                      │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Adapters/               ← 内置适配器                    │   │
│  │    TimerEventSourceNode                                  │   │
│  │    （其他通用适配器）                                      │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ 适配
┌─────────────────────────────────────────────────────────────────┐
│                     UI 框架适配层                                 │
│                                                                  │
│  Share.EventBindingOrchestrator.Adapters                         │
│  ├── WinFormsEventSourceNode   ← WinForms Control → IEventSourceNode│
│  ├── WinFormsEventBus          ← WinForms 事件总线               │
│  └── WinFormsValueAccessor     ← WinForms 值读写器               │
│                                                                  │
│  WdUI.WinForms.Adapters（未来）                                   │
│  └── WinFormsControlFinderStrategy                               │
│                                                                  │
│  WdUI.WPF.Adapters（未来）                                        │
│  ├── WpfEventSourceNode       ← WPF Control → IEventSourceNode  │
│  ├── WpfEventBus              ← WPF 事件总线                     │
│  └── WpfValueAccessor         ← WPF 值读写器                     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 4.2 模块职责

| 层 | 命名空间 | 职责 | 依赖 |
|----|---------|------|------|
| **接口定义** | `Modules.EventFramework.Abstractions` | 定义所有接口和抽象类 | 无 |
| **核心实现** | `Modules.EventFramework.Core` | 接口的默认实现 | Abstractions |
| **管线编排** | `Modules.EventFramework.Pipeline` | 事件绑定和触发的管线编排 | Core, Abstractions |
| **数据模型** | `Modules.EventFramework.Models` | 声明式绑定模型、上下文 | Abstractions |
| **内置适配器** | `Modules.EventFramework.Adapters` | 通用事件源适配器（定时器等） | Core |
| **UI 适配层** | 各 UI 框架自己的适配器命名空间 | 具体 UI 框架的事件源适配 | Abstractions |

### 4.3 依赖关系

```
Modules.EventFramework.Abstractions    ← 零依赖
        ↑
Modules.EventFramework.Core           ← 依赖 Abstractions
        ↑
Modules.EventFramework.Pipeline       ← 依赖 Core, Abstractions
Modules.EventFramework.Models         ← 依赖 Abstractions
Modules.EventFramework.Adapters       ← 依赖 Core
        ↑
Share.EventBindingOrchestrator        ← 依赖 EventFramework + WinForms
WdUI.WinForms                         ← 依赖 EventFramework.Abstractions
WdUI.WPF                              ← 依赖 EventFramework.Abstractions
```

---

## 5. 核心管线

### 5.1 事件绑定管线

将事件源与函数绑定，当事件触发时执行指定函数：

```
输入：IEventSourceNode + 事件绑定声明
管线：
  [验证声明] → [规范化事件名] → [创建事件处理器] → [订阅事件源] → [记录绑定结果]
输出：绑定结果（成功/失败）
```

```csharp
// 管线阶段定义
public abstract class EventBindingStageBase : IPipelineStage
{
    // 1. 验证事件绑定声明
    // 2. 规范化事件名（ToPascalCase）
    // 3. 创建事件处理器（参数构建 + 函数执行 + 输出写回）
    // 4. 通过 IEventSourceNode.Subscribe() 订阅事件
    // 5. 记录绑定结果
}

// Config 路径：从配置模型读取事件绑定信息
public class ConfigBindingStage : EventBindingStageBase
{
    // 从 IEventSourceNode 的配置元数据中读取事件绑定信息
    // 适用于 UI 控件场景（配置驱动）
}

// Declaration 路径：从 EventBindingDeclaration 读取事件绑定信息
public class DeclarationBindingStage : EventBindingStageBase
{
    // 从 EventBindingDeclaration 对象中读取事件绑定信息
    // 适用于声明式编程场景（代码驱动）
}
```

### 5.2 事件触发管线

当事件源触发事件时，执行绑定的函数并写回输出：

```
输入：事件源 + 事件名 + 事件参数
管线：
  [防重入检查] → [构建参数] → [执行函数/工作流] → [处理结果] → [写回输出]
输出：函数执行结果
```

```csharp
// 事件触发服务基类（模板方法模式）
public abstract class EventTriggerServiceBase
{
    public async Task<IEventTriggerResult> TriggerEventAsync(
        IEventSourceNode source, string eventName, object? eventArgs)
    {
        // 1. 查找事件绑定
        var binding = FindEventBinding(source, eventName);

        // 2. 提取输入值
        var inputValues = await ExtractInputValuesAsync(binding.Inputs);

        // 3. 执行函数
        var result = await ExecuteFunctionAsync(binding.FunctionName, inputValues);

        // 4. 写回输出
        if (!string.IsNullOrEmpty(binding.Output))
            await WriteOutputAsync(binding.Output, result.Value);

        return result;
    }
}
```

### 5.3 管线与事件源的关系

```
事件绑定阶段：
  EventFramework.Pipeline
       │
       │ 调用 IEventSourceNode.Subscribe(eventName, handler)
       ▼
  IEventSourceNode（接口）
       │
       ├── WinFormsEventSourceNode → Control.Click += handler
       ├── WpfEventSourceNode      → Button.AddHandler(ClickEvent, handler)
       ├── TimerEventSourceNode    → timer.Elapsed += handler
       └── ...

事件触发阶段：
  事件源触发事件
       │
       ▼
  handler(sender, args)
       │
       ▼
  EventFramework.Pipeline.EventTriggerServiceBase
       │
       │ 调用 IEventSourceNode.GetValue() 读取输入
       │ 调用 IEventSourceNode.SetValue() 写回输出
       ▼
  IEventSourceNode（接口）
```

---

## 6. 适配器模式

### 6.1 WinForms 适配器

```csharp
// ============================================================
// WinFormsEventSourceNode.cs — WinForms 事件源适配器
//
// 将 System.Windows.Forms.Control 包装为 IEventSourceNode。
// 通过反射操作，零编译时 WinForms 依赖。
// ============================================================

namespace Share.EventBindingOrchestrator.Adapters;

public class WinFormsEventSourceNode : EventSourceNodeBase
{
    private readonly object _control;
    private readonly Lazy<Dictionary<string, EventInfo>> _eventCache;

    public override string Name => GetControlName();
    public override string SourceType => _control.GetType().Name;
    public override bool CanRead => _control.GetType().GetProperty("Text") != null;
    public override bool CanWrite => _control.GetType().GetProperty("Text")?.CanWrite == true;
    public override bool HasChildren => GetChildControls().Any();

    public WinFormsEventSourceNode(object control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _eventCache = new Lazy<Dictionary<string, EventInfo>>(() =>
        {
            return _control.GetType()
                .GetEvents(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        });
    }

    protected override bool OnSubscribe(string eventName, Delegate handler)
    {
        if (_eventCache.Value.TryGetValue(eventName, out var eventInfo))
        {
            eventInfo.AddEventHandler(_control, handler);
            return true;
        }
        return false;
    }

    protected override bool OnUnsubscribe(string eventName, Delegate handler)
    {
        if (_eventCache.Value.TryGetValue(eventName, out var eventInfo))
        {
            eventInfo.RemoveEventHandler(_control, handler);
            return true;
        }
        return false;
    }

    public override object? GetValue()
    {
        var textProp = _control.GetType().GetProperty("Text");
        return textProp?.GetValue(_control);
    }

    public override void SetValue(object? value)
    {
        var textProp = _control.GetType().GetProperty("Text");
        if (textProp?.CanWrite == true)
            textProp.SetValue(_control, value?.ToString() ?? "");
    }

    public override IEnumerable<IEventSourceNode> GetChildren()
    {
        foreach (var child in GetChildControls())
            yield return new WinFormsEventSourceNode(child);
    }

    public override IEventSourceNode? FindChild(string name)
    {
        return FindChildRecursive(this, name);
    }

    private string GetControlName()
    {
        var nameProp = _control.GetType().GetProperty("Name");
        return nameProp?.GetValue(_control)?.ToString() ?? _control.GetType().Name;
    }

    private IEnumerable<object> GetChildControls()
    {
        // 通过反射获取 Controls 属性
        var controlsProp = _control.GetType().GetProperty("Controls");
        if (controlsProp?.GetValue(_control) is IEnumerable controls)
        {
            foreach (var child in controls)
                yield return child;
        }
    }

    private static IEventSourceNode? FindChildRecursive(WinFormsEventSourceNode parent, string name)
    {
        foreach (var child in parent.GetChildren())
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                return child;
            if (child is WinFormsEventSourceNode winChild && winChild.HasChildren)
            {
                var found = FindChildRecursive(winChild, name);
                if (found != null) return found;
            }
        }
        return null;
    }

    protected override void OnDispose()
    {
        // 清理所有事件订阅
        foreach (var kvp in _eventCache.Value)
        {
            // 实际清理逻辑
        }
    }
}
```

### 6.2 WPF 适配器（未来）

```csharp
namespace WdUI.WPF.Adapters;

public class WpfEventSourceNode : EventSourceNodeBase
{
    private readonly FrameworkElement _element;

    public override string Name => _element.Name;
    public override string SourceType => _element.GetType().Name;
    public override bool CanRead => _element is TextBox || _element is ComboBox;
    public override bool CanWrite => _element is TextBox;

    protected override bool OnSubscribe(string eventName, Delegate handler)
    {
        // WPF 路由事件绑定
        var routedEvent = FindRoutedEvent(eventName);
        if (routedEvent != null)
        {
            _element.AddHandler(routedEvent, handler);
            return true;
        }
        // 回退到反射绑定 CLR 事件
        return base.OnSubscribe(eventName, handler);
    }

    public override object? GetValue()
    {
        return _element switch
        {
            TextBox tb => tb.Text,
            ComboBox cb => cb.SelectedItem,
            _ => null
        };
    }

    public override void SetValue(object? value)
    {
        if (_element is TextBox tb)
            tb.Text = value?.ToString() ?? "";
    }

    public override IEnumerable<IEventSourceNode> GetChildren()
    {
        return LogicalTreeHelper.GetChildren(_element)
            .OfType<FrameworkElement>()
            .Select(e => new WpfEventSourceNode(e));
    }
}
```

### 6.3 定时器适配器

```csharp
namespace Modules.EventFramework.Adapters;

public class TimerEventSourceNode : EventSourceNodeBase
{
    private readonly System.Timers.Timer _timer;

    public override string Name { get; }
    public override string SourceType => "Timer";
    public override bool CanSubscribe => true;
    public override bool CanRead => false;
    public override bool CanWrite => false;

    public TimerEventSourceNode(string name, double intervalMs)
    {
        Name = name;
        _timer = new System.Timers.Timer(intervalMs);
    }

    protected override bool OnSubscribe(string eventName, Delegate handler)
    {
        if (string.Equals(eventName, "Elapsed", StringComparison.OrdinalIgnoreCase))
        {
            _timer.Elapsed += (ElapsedEventHandler)handler;
            _timer.Start();
            return true;
        }
        return false;
    }

    protected override bool OnUnsubscribe(string eventName, Delegate handler)
    {
        if (string.Equals(eventName, "Elapsed", StringComparison.OrdinalIgnoreCase))
        {
            _timer.Elapsed -= (ElapsedEventHandler)handler;
            if (_timer.Elapsed == null || _timer.Elapsed.GetInvocationList().Length == 0)
                _timer.Stop();
            return true;
        }
        return false;
    }

    protected override void OnDispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
```

---

## 7. 构建方案

### 7.1 阶段一：接口定义（Modules.EventFramework.Abstractions）

**目标**：定义 `IEventSourceNode` 及其相关接口

**新增文件**：

| 文件 | 内容 |
|------|------|
| `Abstractions/IEventSourceNode.cs` | 事件源节点主接口 |
| `Abstractions/IEventSource.cs` | 通知源接口（拆分） |
| `Abstractions/IValueContainer.cs` | 状态容器接口（拆分） |
| `Abstractions/ITreeNode.cs` | 树节点接口（拆分） |
| `Abstractions/IEventBus.cs` | 事件总线接口 |
| `Abstractions/IEventBindingRegistry.cs` | 事件绑定注册中心接口 |
| `Abstractions/IEventNameNormalizer.cs` | 事件名规范化器接口 |
| `Abstractions/IValueAccessor.cs` | 值读写器接口 |
| `Abstractions/IEventSubscription.cs` | 事件订阅接口 |
| `Abstractions/IEventTriggerService.cs` | 事件触发服务接口 |
| `Abstractions/IEventBindingResolver.cs` | 事件绑定解析器接口 |

**迁移来源**：从 `Share.EventBindingOrchestrator.Abstractions` 复制接口定义，调整命名空间。

### 7.2 阶段二：核心实现（Modules.EventFramework.Core）

**目标**：提供接口的默认实现

**新增文件**：

| 文件 | 内容 |
|------|------|
| `Core/EventSourceNodeBase.cs` | 事件源节点基类 |
| `Core/EventBus.cs` | 通用事件总线 |
| `Core/EventBindingRegistry.cs` | 事件绑定注册中心 |
| `Core/EventNameNormalizer.cs` | 事件名规范化器 |
| `Core/ReflectionControlFinderStrategy.cs` | 反射控件查找策略 |
| `Core/ReflectionEventBinderStrategy.cs` | 反射事件绑定策略 |

**迁移来源**：
- `EventSourceNodeBase`：新建
- `EventBus`：从 `Modules.EventFramework.Bus.DefaultEventBus` 调整
- `EventBindingRegistry`：从 `DefaultEventMappingRegistry` 迁移
- `EventNameNormalizer`：从 `DefaultEventNameNormalizer` 迁移
- `ReflectionControlFinderStrategy`：从 Share 层迁移
- `ReflectionEventBinderStrategy`：从 Share 层迁移

### 7.3 阶段三：管线实现（Modules.EventFramework.Pipeline）

**目标**：提供事件绑定和触发的管线编排

**新增文件**：

| 文件 | 内容 |
|------|------|
| `Pipeline/EventBindingStageBase.cs` | 事件绑定阶段基类 |
| `Pipeline/ConfigBindingStage.cs` | 配置驱动绑定阶段 |
| `Pipeline/DeclarationBindingStage.cs` | 声明驱动绑定阶段 |
| `Pipeline/EventTriggerServiceBase.cs` | 事件触发服务基类 |

**迁移来源**：从 `Share.EventBindingOrchestrator.Stages` 和 `Share.EventBindingOrchestrator.Abstractions` 迁移，调整泛型参数以支持 `IEventSourceNode`。

### 7.4 阶段四：WinForms 适配器（Share.EventBindingOrchestrator.Adapters）

**目标**：提供 WinForms 事件源适配器

**新增/修改文件**：

| 文件 | 内容 |
|------|------|
| `Adapters/WinFormsEventSourceNode.cs` | WinForms 事件源适配器（新建） |
| `Adapters/WinFormsEventBus.cs` | WinForms 事件总线（保留） |
| `Adapters/WinFormsValueAccessor.cs` | WinForms 值读写器（保留） |

### 7.5 阶段五：Share 层委托包装（向后兼容）

**目标**：保持现有 API 不变，内部委托给 EventFramework

**修改文件**：

| 文件 | 修改方式 |
|------|---------|
| `Abstractions/GlobalUsings.cs` | 添加 global using 转发到 EventFramework |
| `ControlTreeWalker.cs` | 已有委托包装，无需修改 |
| `EventBindingHelper.cs` | 保留 WinForms 特有方法 |
| `Stages/ConfigBindingStage.cs` | 改为委托包装 |
| `Stages/DeclarationBindingStage.cs` | 改为委托包装 |

### 7.6 阶段六：迁移与测试

**目标**：确保所有现有功能正常

1. 编译验证：确保解决方案编译通过
2. 单元测试：为 EventFramework 新增接口和实现编写单元测试
3. 集成测试：验证 WinForms 事件绑定功能正常
4. 回归测试：确保 Share 层委托包装行为一致

---

## 8. 与现有代码的关系

### 8.1 现有代码映射

| 现有代码（Share 层） | 目标位置（EventFramework） | 迁移方式 |
|---------------------|--------------------------|---------|
| `IControlFinderStrategy` | `Abstractions/IEventSourceNode.cs`（合并） | 复制 + 调整 |
| `IEventBinderStrategy` | `Abstractions/IEventSource.cs`（合并） | 复制 + 调整 |
| `IEventMappingRegistry` | `Abstractions/IEventBindingRegistry.cs` | 复制 + 重命名 |
| `IEventNameNormalizer` | `Abstractions/IEventNameNormalizer.cs` | 复制 |
| `IValueAccessor` | `Abstractions/IValueContainer.cs`（合并） | 复制 + 调整 |
| `IEventTriggerService` | `Abstractions/IEventTriggerService.cs` | 复制 |
| `IEventBindingResolver` | `Abstractions/IEventBindingResolver.cs` | 复制 |
| `IUISubsystemInitializer` | `Abstractions/IUISubsystemInitializer.cs` | 复制 |
| `ReflectionControlFinderStrategy` | `Core/ReflectionControlFinderStrategy.cs` | 迁移 |
| `ReflectionEventBinderStrategy` | `Core/ReflectionEventBinderStrategy.cs` | 迁移 |
| `DefaultEventMappingRegistry` | `Core/EventBindingRegistry.cs` | 迁移 + 重命名 |
| `DefaultEventNameNormalizer` | `Core/EventNameNormalizer.cs` | 迁移 |
| `EventBindingDeclaration` | `Models/EventBindingDeclaration.cs` | 迁移 |
| `EventBindingContext` | `Models/EventBindingContext.cs` | 迁移（泛型化） |
| `EventBindingStageBase` | `Pipeline/EventBindingStageBase.cs` | 迁移 |
| `ConfigBindingStage` | `Pipeline/ConfigBindingStage.cs` | 迁移 |
| `DeclarationBindingStage` | `Pipeline/DeclarationBindingStage.cs` | 迁移 |
| `EventTriggerServiceBase` | `Pipeline/EventTriggerServiceBase.cs` | 迁移 |
| `WinFormsEventBus` | 保留在 Share 层 | 不变 |
| `WinFormsValueAccessor` | 保留在 Share 层 | 不变 |
| `EventBindingHelper` | 保留在 Share 层 | 不变 |
| `ControlTreeWalker`（Share 层） | 保留委托包装 | 已有 |
| `EventBindingValidator`（Share 层） | 保留委托包装 | 已有 |
