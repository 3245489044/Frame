# 事件绑定框架 IoC 重构方案

> **目标**：将事件绑定框架从"编译时白名单"改造为"运行时注册制"，消除所有硬编码点。
>
> **触发问题**：`workflowTreeView` 的 `afterSelect` 事件点击无响应无日志，根因是 `EventConfigConverter` 的 `default: reader.Skip()` 丢弃了未知事件名。

---

## 目录

1. [现状分析](#1-现状分析)
2. [重构目标](#2-重构目标)
3. [架构总览](#3-架构总览)
4. [数据模型重构：EventConfig](#4-数据模型重构eventconfig)
5. [IoC 接口设计](#5-ioc-接口设计)
6. [设计决策](#6-设计决策)
7. [向后兼容策略](#7-向后兼容策略)
8. [迁移路线图](#8-迁移路线图)
9. [常见问题](#9-常见问题)

---

## 1. 现状分析

### 1.1 硬编码点清单

| # | 文件 | 硬编码内容 | 问题 |
|---|------|-----------|------|
| 1 | `EventConfig.cs` | 6 个属性（Click/DoubleClick/TextChanged/SelectedIndexChanged/CheckedChanged/ValueChanged） | 新增事件名需改类定义 |
| 2 | `EventConfigConverter.cs` | 14 个 case 的 switch 语句，`default: reader.Skip()` | 未知事件名被静默丢弃 |
| 3 | `EventMappingService.cs` | `RegisterDefaultAccessors()` 注册 6 个访问器 + `s_standardEventPriority` 优先级列表 | 新增事件需改源码 |
| 4 | `EventNameNormalizer.cs` | `_knownEvents` 字典 100+ 行硬编码映射 | 映射表不可扩展 |
| 5 | `WinFormsEventBus.cs` | DynamicMethod 反射绑定，异常被空 catch 吞掉 | 绑定策略不可替换，诊断困难 |
| 6 | `ControlFinderTransformer.cs` | `GetChildControlsByReflection` 硬编码 Controls/Panel1Controls/Panel2Controls | 自定义容器无法遍历 |

### 1.2 根因分析

```
JSON 配置中的 "afterselect"
    ↓
EventConfigConverter.Read()
    ↓
switch (propertyName) {
    case "click": ...       // 只识别 6 个已知事件
    case "customevents": ... // 只识别 customEvents 容器
    default: reader.Skip();  // ← afterSelect 在这里被丢弃
}
    ↓
EventConfig 中 EventsDictionary 为空
    ↓
EventBinderTransformer 无事可做
    ↓
点击无响应，无日志
```

---

## 2. 重构目标

| 目标 | 说明 |
|------|------|
| **可扩展** | 新增事件名无需修改框架源码，JSON 配置直接使用 |
| **可替换** | 核心行为通过接口注入，默认实现提供完整功能 |
| **可测试** | 接口可 mock，单元测试不依赖真实 WinForms 控件 |
| **向后兼容** | 旧配置和旧 API 无需修改，逐步迁移 |
| **诊断友好** | 事件绑定失败时有明确日志，不静默吞异常 |

---

## 3. 架构总览

```
┌──────────────────────────────────────────────────────────────────────┐
│                      事件绑定框架（重构后）                             │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌──────────────────────────────────────────────────────┐            │
│  │  IEventMappingRegistry（事件映射注册中心）              │            │
│  │  ├─ 内部依赖 IEventNameNormalizer                     │            │
│  │  ├─ RegisterMapping(eventName, accessor)              │            │
│  │  ├─ GetAccessor(eventName) → EventAccessor?           │            │
│  │  └─ 默认实现：DefaultEventMappingRegistry             │            │
│  └──────────────────────┬───────────────────────────────┘            │
│                          │                                           │
│  ┌──────────────────────▼───────────────────────────────┐            │
│  │  EventBinderTransformer                               │            │
│  │  1. 遍历 EventConfig.EventsDictionary                 │            │
│  │  2. 通过 IEventMappingRegistry 查找访问器              │            │
│  │  3. 通过 IWinFormsEventBinderStrategy 绑定事件         │            │
│  └──────────────────────┬───────────────────────────────┘            │
│                          │                                           │
│  ┌──────────────────────▼───────────────────────────────┐            │
│  │  IWinFormsEventBinderStrategy（事件绑定策略）          │            │
│  │  ├─ ReflectionEventBinderStrategy（默认，反射绑定）    │            │
│  │  ├─ TreeViewEventBinderStrategy（扩展，直接订阅）      │            │
│  │  └─ CompositeEventBinderStrategy（策略链）            │            │
│  └──────────────────────────────────────────────────────┘            │
│                                                                      │
│  ┌──────────────────────────────────────────────────────┐            │
│  │  IControlFinderStrategy（控件查找策略）                │            │
│  │  ├─ ReflectionControlFinderStrategy（默认，反射）     │            │
│  │  └─ CustomControlFinderStrategy（扩展）               │            │
│  └──────────────────────────────────────────────────────┘            │
│                                                                      │
│  ┌──────────────────────────────────────────────────────┐            │
│  │  EventConfig（数据模型）                               │            │
│  │  ├─ EventsDictionary（统一存储，新增）                  │            │
│  │  ├─ 旧属性 → 委托给 EventsDictionary（[Obsolete]）     │            │
│  │  └─ Bindings（保持不变）                              │            │
│  └──────────────────────────────────────────────────────┘            │
└──────────────────────────────────────────────────────────────────────┘
```

### 核心数据流

```
JSON 配置
    ↓
EventConfigConverter
    │  default 分支 → 自动归入 EventsDictionary
    ↓
EventConfig.EventsDictionary
    │  {"afterselect": ["workflowTreeView_afterSelect"], ...}
    ↓
EventBinderTransformer.ForEachBoundEvent
    │  遍历 EventsDictionary 中的所有事件
    ↓
IEventMappingRegistry.GetAccessor(eventName)
    │  内部调用 IEventNameNormalizer.ToPascalCase(eventName)
    │  查找事件名对应的访问器
    ↓
IWinFormsEventBinderStrategy.BindEvent(control, eventName, handler)
    │  绑定到实际 WinForms 事件
    ↓
控件事件触发 → 执行工作流
```

---

## 4. 数据模型重构：EventConfig

### 4.1 重构后的 EventConfig

```csharp
[JsonConverter(typeof(EventConfigConverter))]
public class EventConfig
{
    // ================================================================
    // ★ 统一事件存储（新增）
    // ================================================================

    /// <summary>
    /// 统一事件字典 — 存储所有事件绑定
    /// <para>键为事件名（保留 JSON 中的原始大小写），值为函数名列表。</para>
    /// <para>替代 6 个硬编码属性 + CustomEvents 字典。</para>
    /// </summary>
    public Dictionary<string, List<string>> EventsDictionary { get; set; } = new();

    // ================================================================
    // ★ 向后兼容：旧属性委托给 EventsDictionary
    // ================================================================

    [Obsolete("请使用 EventsDictionary[\"click\"] 替代")]
    public List<string>? Click
    {
        get => EventsDictionary.TryGetValue("click", out var v) ? v : null;
        set => EventsDictionary["click"] = value ?? new();
    }

    // DoubleClick / TextChanged / SelectedIndexChanged / CheckedChanged / ValueChanged 同理

    [Obsolete("请使用 EventsDictionary 替代")]
    public Dictionary<string, List<string>> CustomEvents
    {
        get => EventsDictionary;
        set {
            if (value != null)
                foreach (var kv in value)
                    EventsDictionary[kv.Key] = kv.Value;
        }
    }

    // ================================================================
    // ★ 保持不变
    // ================================================================

    public EventBinding? Bindings { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string HandlerType { get; set; } = string.Empty;
    public Dictionary<string, string>? HandlerParams { get; set; }
}
```

### 4.2 重构后的 EventConfigConverter

```csharp
public class EventConfigConverter : JsonConverter<EventConfig>
{
    // 已知的非事件属性名（不归入 EventsDictionary）
    private static readonly HashSet<string> KnownNonEventProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "eventname", "handlertype", "handlerparams",
        "customevents", "bindings"
    };

    public override EventConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject");

        var config = new EventConfig();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return config;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected PropertyName");

            var propertyName = reader.GetString() ?? "";
            reader.Read();

            var lowerName = propertyName.ToLowerInvariant();

            if (KnownNonEventProperties.Contains(lowerName))
            {
                switch (lowerName)
                {
                    case "eventname":
                        config.EventName = reader.GetString() ?? "";
                        break;
                    case "handlertype":
                        config.HandlerType = reader.GetString() ?? "";
                        break;
                    case "handlerparams":
                        config.HandlerParams = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options);
                        break;
                    case "customevents":
                        var customEvents = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(ref reader, options);
                        if (customEvents != null)
                            foreach (var kv in customEvents)
                                config.EventsDictionary[kv.Key] = kv.Value;
                        break;
                    case "bindings":
                        config.Bindings = JsonSerializer.Deserialize<EventBinding>(ref reader, options);
                        break;
                }
            }
            else
            {
                // ★ 重构：所有未知属性名自动归入 EventsDictionary
                var value = ReadStringOrArray(ref reader);
                if (value != null)
                    config.EventsDictionary[propertyName] = value;
            }
        }

        throw new JsonException("Unexpected end of JSON");
    }

    public override void Write(Utf8JsonWriter writer, EventConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var kv in value.EventsDictionary)
            WriteStringOrArray(writer, kv.Key, kv.Value);

        if (!string.IsNullOrEmpty(value.EventName))
            writer.WriteString("eventName", value.EventName);
        if (!string.IsNullOrEmpty(value.HandlerType))
            writer.WriteString("handlerType", value.HandlerType);
        if (value.HandlerParams?.Count > 0)
        {
            writer.WritePropertyName("handlerParams");
            JsonSerializer.Serialize(writer, value.HandlerParams, options);
        }
        if (value.Bindings != null)
        {
            writer.WritePropertyName("bindings");
            JsonSerializer.Serialize(writer, value.Bindings, options);
        }

        writer.WriteEndObject();
    }
}
```

### 4.3 JSON 配置示例

```json
{
  "type": "treeview",
  "name": "workflowTreeView",
  "afterselect": "workflowTreeView_afterSelect",
  "nodeclick": "workflowTreeNode_Click",
  "customEvents": {
    "afterselect": ["workflowTreeView_afterSelect"]
  },
  "bindings": {
    "workflows": {
      "workflowTreeView_afterSelect": { ... }
    }
  }
}
```

**重构后**：`"afterselect"` 和 `"nodeclick"` 在顶层出现，Converter 的 `default` 分支自动存入 `EventsDictionary`，无需任何硬编码。

---

## 5. IoC 接口设计

### 5.1 IEventMappingRegistry — 事件映射注册中心

**职责**：管理"事件名 → 事件访问器"的映射关系。内部依赖 `IEventNameNormalizer` 完成事件名规范化。

```csharp
/// <summary>
/// 事件映射注册中心
/// <para>管理事件名到 WinForms 事件的映射关系。</para>
/// <para>替代 EventMappingService 的硬编码访问器注册和优先级列表。</para>
/// </summary>
public interface IEventMappingRegistry
{
    /// <summary>注册事件映射</summary>
    void RegisterMapping(string eventName, EventAccessor accessor, int priority = 100);

    /// <summary>批量注册</summary>
    void RegisterMappings(IEnumerable<EventMappingEntry> entries);

    /// <summary>获取事件访问器（内部自动规范化事件名）</summary>
    EventAccessor? GetAccessor(string eventName);

    /// <summary>获取所有已注册的事件名（按优先级排序）</summary>
    IEnumerable<string> GetRegisteredEvents();

    /// <summary>移除映射</summary>
    bool UnregisterMapping(string eventName);

    /// <summary>清空</summary>
    void Clear();
}

/// <summary>事件访问器 — 封装事件绑定的具体行为</summary>
public record EventAccessor(
    /// <summary>事件处理委托实例</summary>
    Delegate Handler,
    /// <summary>事件委托类型（如 typeof(TreeViewEventHandler)），null 时使用反射推断</summary>
    Type? EventDelegateType = null
);

/// <summary>事件映射条目</summary>
public record EventMappingEntry(
    string EventName,
    EventAccessor Accessor,
    int Priority = 100
);
```

**默认实现要点**：
- 构造函数中注册所有已知 WinForms 事件映射（从 `EventMappingService` 迁移）
- 构造函数可选注入 `IEventNameNormalizer`（不注入则使用 `DefaultEventNameNormalizer`）
- `GetAccessor` 内部先调用 `IEventNameNormalizer.ToPascalCase()` 规范化事件名，再查字典

### 5.2 IEventNameNormalizer — 事件名规范化

**职责**：将 JSON 中的事件名转为 `Type.GetEvent()` 所需的 PascalCase 格式。

```csharp
public interface IEventNameNormalizer
{
    /// <summary>注册事件名映射（小写 → PascalCase）</summary>
    void RegisterMapping(string lowerCaseName, string pascalCaseName);

    /// <summary>批量注册</summary>
    void RegisterMappings(IEnumerable<KeyValuePair<string, string>> mappings);

    /// <summary>将事件名转为 PascalCase</summary>
    string ToPascalCase(string eventName);

    /// <summary>获取所有已知映射</summary>
    IReadOnlyDictionary<string, string> GetKnownMappings();

    /// <summary>移除映射</summary>
    bool UnregisterMapping(string lowerCaseName);
}
```

**默认实现要点**：
- 构造函数中注册所有已知 WinForms 事件映射（从 `EventNameNormalizer._knownEvents` 迁移）
- `ToPascalCase` 回退逻辑：先查注册表，找不到则按 camelCase 规则拆分（`afterselect` → `AfterSelect`，`nodeclick` → `NodeClick`），而非简单首字母大写

### 5.3 IWinFormsEventBinderStrategy — 事件绑定策略

**职责**：将事件处理委托绑定到控件。

```csharp
public interface IWinFormsEventBinderStrategy
{
    /// <summary>绑定事件到控件</summary>
    bool BindEvent(object control, string eventName, Delegate handler);

    /// <summary>解绑事件</summary>
    bool UnbindEvent(object control, string? eventName);

    /// <summary>判断此策略是否能处理指定控件类型</summary>
    bool CanHandle(object control);
}
```

**默认实现**：`ReflectionEventBinderStrategy` — 通过 `Type.GetEvent()` + `AddEventHandler` 反射绑定，记录日志而非静默吞异常。

**策略链**：`CompositeEventBinderStrategy` 按注册顺序尝试，专用策略失败后**不回退**到通用策略（避免掩盖专用策略的 bug），而是记录警告。

### 5.4 IControlFinderStrategy — 控件查找策略

**职责**：获取控件的子控件列表。

```csharp
public interface IControlFinderStrategy
{
    /// <summary>获取控件的子控件列表</summary>
    IEnumerable<object> GetChildControls(object control);

    /// <summary>判断此策略是否能处理指定控件类型</summary>
    bool CanHandle(object control);
}
```

**默认实现**：`ReflectionControlFinderStrategy` — 通过反射获取 `Controls`/`Panel1Controls`/`Panel2Controls` 属性。

---

## 6. 设计决策

### 6.1 为什么 IEventMappingRegistry 内部依赖 IEventNameNormalizer？

**避免调用方手动两步操作**。如果让调用方先 `normalizer.ToPascalCase()` 再 `registry.GetAccessor()`，容易遗漏规范化步骤导致查找失败。将规范化封装在 `GetAccessor` 内部，调用方只需传入 JSON 中的原始事件名。

### 6.2 为什么 EventAccessor 直接持有 Delegate 而非工厂函数？

`Func<object, Delegate> CreateHandler` 的语义是"根据控件实例创建委托"，但实际场景中 handler 是 JSON 配置中指定的函数名对应的委托，与控件实例无关。直接持有 `Delegate Handler` 更简单、更符合实际使用场景。

### 6.3 为什么策略链不回退？

如果 `TreeViewEventBinderStrategy` 绑定失败（例如不认识的事件名），回退到 `ReflectionEventBinderStrategy` 可能导致"部分成功"的假象——开发者以为 TreeView 策略生效了，实际走的是反射路径。不回退 + 记录警告，迫使开发者修复专用策略。

### 6.4 为什么 EventsDictionary 保留原始大小写？

JSON 中的事件名可能是 `"afterselect"`、`"AfterSelect"` 或 `"afterSelect"`。Converter 的职责是忠实地反序列化，大小写规范化留给 `IEventNameNormalizer` 处理。这样职责清晰，且不影响 JSON 的灵活性。

---

## 7. 向后兼容策略

### 7.1 兼容矩阵

| 旧功能 | 新功能 | 兼容方式 |
|--------|--------|---------|
| `EventConfig.Click` | `EventConfig.EventsDictionary["click"]` | 旧属性委托给 EventsDictionary |
| `EventConfig.CustomEvents` | `EventConfig.EventsDictionary` | CustomEvents 作为别名 |
| `EventMappingService.RegisterDefaultAccessors()` | `IEventMappingRegistry` 默认注册 | 默认实现包含所有旧映射 |
| `EventNameNormalizer.ToPascalCase()` | `IEventNameNormalizer.ToPascalCase()` | 默认实现行为一致 |
| `WinFormsEventBus.BindControlEvent()` | `IWinFormsEventBinderStrategy.BindEvent()` | 默认策略实现相同逻辑 |
| `ControlFinderTransformer` 反射查找 | `IControlFinderStrategy` | 默认策略实现相同逻辑 |

### 7.2 迁移阶段

| 阶段 | 内容 | 影响 |
|------|------|------|
| **阶段一** | EventConfig 新增 EventsDictionary，Converter 的 default 分支自动归入 | **零影响**，旧 JSON 完全兼容 |
| **阶段二** | 新增 IoC 接口 + 默认实现 | **零影响**，默认实现行为与旧代码一致 |
| **阶段三** | 管线迁移到 IoC 接口 | **零影响**，旧管线路径保留 |
| **阶段四** | 旧属性标记 `[Obsolete]` | 编译警告，不影响运行 |
| **阶段五** | 清理旧代码 | 需确认无外部依赖 |

---

## 8. 执行策略：先重构，再 Debug

### 核心原则

> **不要试图在旧框架上修 Bug。先重构到可扩展的架构，Bug 自然消失。**

当前 `afterSelect` 无响应的根因不是"某个 if 条件写错了"，而是**整个事件绑定框架的设计缺陷**——它用编译时白名单限制了运行时行为。在这种架构上修 Bug 只会越修越多：

- 修了 `afterSelect`，下次 `nodeMouseClick` 又不行
- 修了 TreeView，下次 ListView 又不行
- 修了事件名，下次控件类型又不行

**正确的顺序**：先重构框架消除硬编码 → 再验证 Bug 是否已自动修复 → 最后处理残留问题。

### 执行路线图

```
┌─────────────────────────────────────────────────────────────────┐
│  先重构（消除硬编码，建立可扩展架构）                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  阶段一 ─── 数据模型重构                                          │
│  EventConfig + Converter                                         │
│  → afterSelect 不再被丢弃                                        │
│  → 但绑定可能失败（缺访问器）                                     │
│                                                                  │
│  阶段二 ─── IoC 接口 + 默认实现                                   │
│  IEventMappingRegistry + 默认注册所有已知事件                     │
│  → afterSelect 能查到访问器                                       │
│  → 但绑定可能失败（反射策略兼容性）                                │
│                                                                  │
│  阶段三 ─── 管线迁移                                              │
│  EventBinderTransformer 使用 IoC 接口                             │
│  → afterSelect 能正确绑定并触发                                   │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│  再 Debug（处理残留问题）                                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  阶段四 ─── 标记 Obsolete                                        │
│  旧属性标记 [Obsolete]，给调用方编译警告                          │
│  → 不影响运行，仅提示迁移                                        │
│                                                                  │
│  阶段五 ─── 清理旧代码                                           │
│  移除所有硬编码的旧属性、旧 case、旧注册方法                      │
│  → 框架完全基于 IoC 接口                                         │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 阶段一：数据模型重构（先修数据丢失）

**目标**：让 `afterSelect` 不再被 `reader.Skip()` 丢弃。

**改动文件**：
| 文件 | 改动 |
|------|------|
| `EventConfig.cs` | 新增 `EventsDictionary` 属性 |
| `EventConfigConverter.cs` | `default` 分支改为自动归入 `EventsDictionary` |

**验证方法**：
```csharp
// 单元测试：验证反序列化
var json = @"{ ""afterselect"": ""handlerName"" }";
var config = JsonSerializer.Deserialize<EventConfig>(json);
Assert.True(config.EventsDictionary.ContainsKey("afterselect"));
Assert.Equal("handlerName", config.EventsDictionary["afterselect"][0]);
```

**预期结果**：`afterSelect` 不再被丢弃，存入 `EventsDictionary`。但此时绑定管线还不认识它，所以点击仍然无响应——**这是正常的，因为阶段一只修数据不修绑定**。

### 阶段二：IoC 接口定义 + 默认实现（再修绑定能力）

**目标**：让 `afterSelect` 能被绑定到 TreeView 的 `AfterSelect` 事件。

**改动文件**：
| 文件 | 改动 |
|------|------|
| `IEventMappingRegistry.cs` | 新增接口 |
| `DefaultEventMappingRegistry.cs` | 默认实现，注册所有已知 WinForms 事件 |
| `IEventNameNormalizer.cs` | 新增接口 |
| `DefaultEventNameNormalizer.cs` | 默认实现，从 `EventNameNormalizer._knownEvents` 迁移 |
| `IWinFormsEventBinderStrategy.cs` | 新增接口 |
| `ReflectionEventBinderStrategy.cs` | 默认实现，从 `WinFormsEventBus` 迁移 |
| `IControlFinderStrategy.cs` | 新增接口 |
| `ReflectionControlFinderStrategy.cs` | 默认实现，从 `ControlFinderTransformer` 迁移 |

**验证方法**：
```csharp
// 单元测试：验证访问器查找
var registry = new DefaultEventMappingRegistry();
var accessor = registry.GetAccessor("afterselect");
Assert.NotNull(accessor); // 默认注册了 AfterSelect 的访问器
```

**预期结果**：`IEventMappingRegistry` 能查到 `"afterselect"` 对应的访问器。但此时管线还在用旧逻辑，所以绑定仍然不生效——**阶段三才切换管线**。

### 阶段三：管线迁移（最终让事件触发）

**目标**：让 `EventBinderTransformer` 通过 IoC 接口完成事件绑定。

**改动文件**：
| 文件 | 改动 |
|------|------|
| `EventBinderTransformer.cs` | 注入 `IEventMappingRegistry` 和 `IWinFormsEventBinderStrategy` |
| `ControlFinderTransformer.cs` | 注入 `IControlFinderStrategy` |
| `WinFormsEventBus.cs` | 委托给 `IWinFormsEventBinderStrategy` |

**验证方法**：
```csharp
// 集成测试：验证完整绑定链路
var json = @"{ ""afterselect"": ""workflowTreeView_afterSelect"" }";
var config = JsonSerializer.Deserialize<EventConfig>(json);
var treeView = new TreeView();
var transformer = new EventBinderTransformer(registry, strategy);
transformer.BindEvents(treeView, config);
// 模拟选中节点
treeView.SelectedNode = new TreeNode("test");
// 验证 workflowTreeView_afterSelect 被调用
Assert.True(called);
```

**预期结果**：`workflowTreeView` 的 `afterSelect` 事件能正确绑定并触发。**此时 Bug 已自动修复**。

### 阶段四：标记 Obsolete（通知调用方迁移）

**目标**：给旧 API 使用者编译警告。

**改动文件**：
| 文件 | 改动 |
|------|------|
| `EventConfig.cs` | 6 个旧属性 + `CustomEvents` 标记 `[Obsolete]` |
| `EventMappingService.cs` | `RegisterDefaultAccessors()` 标记 `[Obsolete]` |
| `EventNameNormalizer.cs` | `_knownEvents` 标记 `[Obsolete]` |

**验证**：编译产生警告，运行无影响。

### 阶段五：清理旧代码（最终状态）

**目标**：移除所有硬编码的旧代码。

**改动文件**：
| 文件 | 改动 |
|------|------|
| `EventConfig.cs` | 移除 6 个旧属性 |
| `EventConfigConverter.cs` | 移除 switch 中的旧 case |
| `EventMappingService.cs` | 移除旧注册方法 |
| `EventNameNormalizer.cs` | 移除静态 `_knownEvents` |

**验证**：所有功能正常，无编译警告。


---

## 9. 常见问题

### Q1: 阶段一完成后，afterSelect 就能工作了吗？

**能反序列化，但不一定能触发**。阶段一确保 `"afterselect"` 存入 `EventsDictionary`，但还需要：
1. `EventBinderTransformer` 遍历 `EventsDictionary`（已有逻辑）
2. `IEventMappingRegistry` 能查到 `"afterselect"` 的访问器（阶段二提供默认注册）
3. `IWinFormsEventBinderStrategy` 能绑定 `AfterSelect` 事件（阶段二提供默认策略）

阶段一修复的是"数据丢失"问题，阶段二修复的是"绑定能力"问题。

### Q2: 旧 JSON 配置需要修改吗？

**不需要**。旧配置中的 `"click"`、`"doubleclick"` 等属性仍然被 Converter 的 `KnownNonEventProperties` 排除，走专门的 switch case。`"customEvents"` 也保持兼容。只有**不在已知列表中的属性名**才会自动归入 `EventsDictionary`。

### Q3: 如果 JSON 中同时有 `"afterselect"` 和 `"customEvents"."afterselect"` 怎么办？

Converter 按 JSON 顺序处理。如果 `"afterselect"` 先出现，存入 `EventsDictionary["afterselect"]`；后面 `"customEvents"` 中的 `"afterselect"` 会覆盖前面的值。建议 JSON 中只使用一种方式，不要混用。

### Q4: 自定义事件绑定策略如何注册？

```csharp
// 在 DI 容器中注册
services.AddSingleton<IWinFormsEventBinderStrategy>(sp =>
{
    var strategies = new IWinFormsEventBinderStrategy[]
    {
        new TreeViewEventBinderStrategy(),      // 专用策略
        new ReflectionEventBinderStrategy()      // 通用回退
    };
    return new CompositeEventBinderStrategy(strategies);
});
```

### Q5: 如何为自定义控件添加事件映射？

```csharp
// 在模块初始化时注册
var registry = serviceProvider.GetRequiredService<IEventMappingRegistry>();
registry.RegisterMapping("customclick",
    new EventAccessor(new EventHandler(OnCustomClick)));
```

---

> **文档版本**：v2.1
> **创建日期**：2026-07-12
> **触发问题**：workflowTreeView.afterSelect 点击无响应无日志
> **设计原则**：万物皆函数，一切皆配置

---

## 附录：实际重构记录

### 阶段一：数据模型重构（已完成）

| 文件 | 改动 |
|------|------|
| `EventConfig.cs` | 已存在 `EventsDictionary` 属性，Converter 的 `default` 分支已自动归入 |
| `EventConfigConverter.cs` | 已存在 `KnownNonEventProperties` 排除逻辑，未知事件名自动归入 `EventsDictionary` |

**状态**：✅ 已完成（重构前已具备）

### 阶段二：IoC 接口 + 默认实现（已完成）

| 文件 | 改动 |
|------|------|
| `IEventMappingRegistry.cs` | 新增接口，定义 `RegisterMapping`/`GetAccessor`/`GetRegisteredEvents`/`UnregisterMapping`/`Clear` |
| `DefaultEventMappingRegistry.cs` | 默认实现，内部使用 `ConcurrentDictionary`，支持运行时注册 |
| `IEventNameNormalizer.cs` | 新增接口，定义 `RegisterMapping`/`ToPascalCase`/`GetKnownMappings`/`UnregisterMapping` |
| `DefaultEventNameNormalizer.cs` | 默认实现，从 `EventNameNormalizer._knownEvents` 迁移所有映射，支持运行时注册 |
| `IWinFormsEventBinderStrategy.cs` | 新增接口，定义 `BindEvent`/`UnbindEvent`/`CanHandle` |
| `ReflectionEventBinderStrategy.cs` | 默认实现，从 `WinFormsEventBus.BindControlEvent` 迁移，使用反射绑定 |
| `IControlFinderStrategy.cs` | 新增接口，定义 `GetChildControls`/`CanHandle` |
| `ReflectionControlFinderStrategy.cs` | 默认实现，从 `ControlFinderTransformer.GetChildControlsByReflection` 迁移 |

**状态**：✅ 已完成

### 阶段三：管线迁移（已完成）

| 文件 | 改动 |
|------|------|
| `EventBinderTransformer.cs` | 注入 `IEventMappingRegistry` 和 `IWinFormsEventBinderStrategy`，优先使用 IoC 接口遍历事件 |
| `ControlFinderTransformer.cs` | 注入 `IControlFinderStrategy`，构造函数新增可选参数 |
| `EventBindingPipelineFactory.cs` | 传递 IoC 接口实例到管线各步骤 |

**状态**：✅ 已完成

### 阶段四：标记 Obsolete（已完成）

| 文件 | 改动 |
|------|------|
| `EventNameNormalizer.cs` | 标记 `[Obsolete("请使用 IEventNameNormalizer 接口和 DefaultEventNameNormalizer 实现")]` |
| `EventTypeMapperService.cs` | 标记 `[Obsolete("请直接使用 IEventMappingService 接口")]` |

**状态**：✅ 已完成

### 阶段五：清理旧代码（待执行）

| 文件 | 待改动 |
|------|--------|
| `EventConfig.cs` | 移除 6 个旧属性，委托给 `EventsDictionary` |
| `EventConfigConverter.cs` | 移除 switch 中的旧 case |
| `EventMappingService.cs` | 移除旧注册方法 |
| `EventNameNormalizer.cs` | 移除静态 `_knownEvents` |

**状态**：⏳ 待执行（需确认无外部依赖后清理）
