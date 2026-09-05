# FrontEndDriven — ECS 管道与 WinForms 渲染适配分析

> **分析日期**：2026-07-23  
> **分析范围**：`CsharpPrj/Operation/FrontEndDriven/` 全部代码  
> **文档版本**：v2.0（模块化整理）

---

## 目录

- [第一部分：现状分析](#第一部分现状分析)
  - [一、当前架构总览](#一当前架构总览)
  - [二、核心问题分析](#二核心问题分析)
- [第二部分：结论](#第二部分结论)
  - [三、不适配结论](#三不适配结论)
- [第三部分：方案](#第三部分方案)
  - [四、重构方案：引入 CSG.Unified 作为协议层](#四重构方案引入-csgunified-作为协议层)
- [第四部分：价值](#第四部分价值)
  - [五、协议层的核心价值](#五协议层的核心价值)
- [第五部分：风险](#第五部分风险)
  - [六、风险与注意事项](#六风险与注意事项)
- [附录](#附录)
  - [附录 A：关键代码示例](#附录-a关键代码示例)
  - [附录 B：术语表](#附录-b术语表)
  - [附录 C：涉及文件清单](#附录-c涉及文件清单)

---

# 第一部分：现状分析

## 一、当前架构总览

### 1.1 管线执行流程

```
ConfigDrivenControlFactory.ConvertChildren（配置→Entity+Control）
  → EcsCapabilityApplier.Update（移除 PendingCapabilityComponent）
  → LayoutSystem.Update（比例→像素，设置 Bounds）
  → SplitContainerSystem.Update（SplitterMoved 事件管理）
  → StyleSystem.Update（合并样式，设置字体/颜色）
  → EventBindingSystem.Update（通过委托绑定事件）
  → ComponentDataBridge（订阅 ComponentUpdated 事件，反射同步值）
```

### 1.2 核心依赖关系

```
EcsRenderPipeline
  ├── ECS 核心：EcsWorld, EntityManager, ControlPool, ControlFactory
  ├── 配置驱动：ConfigDrivenControlFactory
  ├── ECS System：EcsCapabilityApplier, LayoutSystem, SplitContainerSystem,
  │               StyleSystem, EventBindingSystem
  ├── 适配层：IWinFormsControlAdapter → WinFormsControlAdapter
  ├── 值同步：ComponentDataBridge → WinFormsControlValueWriter
  └── 注册中心：EntityRegistry
```

### 1.3 数据流方向

```
配置（UIConfiguration）
  ↓
ConfigDrivenControlFactory
  ├── 创建 Entity（能力卡片）+ 添加 Pending*Component
  └── 创建 WinForms Control + 注册到 ControlPool
  ↓
ECS System 管线（同步执行）
  ├── 读取 Component → 计算 → 通过 IWinFormsControlAdapter 直接操作控件
  └── 移除 Pending*Component → 添加 *AppliedComponent
  ↓
ComponentDataBridge（事件驱动）
  └── 订阅 EntityManager.ComponentUpdated → 反射同步到控件
```

---

## 二、核心问题分析

### 2.1 ECS 和 WinForms 紧耦合 ★★★

**严重程度：高**

虽然已经通过 `IWinFormsControlAdapter` 接口解耦了 System.Windows.Forms 的直接引用，但**管线的执行逻辑仍然假设 ECS 和 WinForms 是同步执行的**：

```csharp
// EcsRenderPipeline.cs 第 294-306 行
public void ExecutePipeline()
{
    _capabilityApplier?.Update();
    _layoutSystem?.Update();      // 内部调用 Adapter.SetBounds()
    _splitContainerSystem?.Update();
    _styleSystem?.Update();       // 内部调用 Adapter.SetFont()
    _eventBindingSystem?.Update();
}
```

每个 System 的 `Update()` 方法内部：
1. 从 ECS 读取 Component
2. 计算（比例→像素、样式合并等）
3. **直接通过 `IWinFormsControlAdapter` 操作 WinForms 控件**

**影响**：
- ECS 和 WinForms 的生命周期是绑定的
- 管线执行一次，ECS 和 WinForms 同时更新
- 无法独立测试 ECS 逻辑（必须依赖 WinForms 环境）

### 2.2 无协议层（中间表示）★★★

**严重程度：高**

当前架构没有中间表示层：

```
ECS Component 变更
  → EntityManager.ComponentUpdated 事件
  → ComponentDataBridge.OnComponentUpdated
  → 反射读取 Component 属性（GetProperty("Text")）
  → IWinFormsControlAdapter 设置控件属性
```

ECS 的 Component 结构直接决定了 UI 的更新方式。如果要支持不同的 UI 框架（WPF、Web），需要为每个框架实现一套完整的 System。

### 2.3 无差异比较机制 ★★★

**严重程度：高**

每次管线执行都是**全量更新**，不是增量更新：

```csharp
// LayoutSystem.cs 第 114-117 行
// 每次 Update() 都设置所有待处理控件的 Bounds
if (control != null && !Adapter.IsDisposed(control))
{
    Adapter.SetBounds(control, computed.Left, computed.Top, computed.Width, computed.Height);
}
```

没有"当前 UI 状态快照"和"目标 UI 状态"的比较机制。即使控件的 Bounds 没有变化，也会重新设置。

### 2.4 ComponentDataBridge 反射脆弱 ★★

**严重程度：中**

```csharp
// ComponentDataBridge.cs 第 160-185 行
private void RegisterSyncHandler(string componentTypeName, ...)
{
    // 在已加载的程序集中查找该类型
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
        var type = asm.GetType(componentTypeName);
        // ...
    }
}
```

**问题**：
- 通过字符串名称查找组件类型，运行时反射
- 如果组件类型改名或移动到不同程序集，静默失败
- 每个同步方法都通过反射读取属性（`GetProperty("Text")`、`GetProperty("Value")` 等）
- 延迟注册机制（`TryLazyRegister`）增加了运行时不确定性

### 2.5 不支持增量更新 ★★

**严重程度：中**

当前设计没有提供"增量更新"的入口：

- `ConfigDrivenControlFactory.ConvertChildren` 为每个 Entity 添加 `Pending*Component`
- 管线执行一次，移除所有 `Pending*Component`，添加 `*AppliedComponent`
- 如果要增量更新（例如某个控件的文本变了），需要：
  1. 重新添加 `PendingValueSyncComponent`
  2. 重新执行管线

但当前设计没有提供"增量更新"的 API。

### 2.6 事件方向单向 ★

**严重程度：低**

当前事件流是单向的：

```
ECS → WinForms（通过 IWinFormsControlAdapter）
WinForms → ECS（通过 EventBindHandler 委托）
```

UI 事件（如 Click）通过委托回传到 ECS，但没有统一的协议格式。每个事件处理器需要自己解析参数。

---

# 第二部分：结论

## 三、不适配结论

### 3.1 对比矩阵

| 维度 | 当前状态 | 理想状态 | 不适配程度 |
|------|---------|---------|-----------|
| **耦合度** | ECS 和 WinForms 通过 IWinFormsControlAdapter 紧耦合 | ECS 输出 CanonicalForm，适配层解释执行 | ★★★ |
| **协议层** | 无（ECS Component 直接驱动 UI） | CanonicalForm 作为中间表示 | ★★★ |
| **更新方式** | 全量同步（管线执行一次全部更新） | 差异比较（只更新变化的部分） | ★★★ |
| **增量更新** | 不支持（需要重新添加 Pending*Component） | 支持（通过差异比较自动发现变化） | ★★ |
| **反射脆弱** | ComponentDataBridge 通过字符串名称和反射操作组件 | 强类型协议，编译时检查 | ★★ |
| **事件方向** | 单向（ECS→UI），UI→ECS 通过委托 | 双向 CanonicalForm | ★ |

### 3.2 总体结论

**当前 FrontEndDriven 的 WinForms 渲染和 ECS 管道确实不适配。**

核心问题是**缺乏协议层（中间表示）**，导致：
1. ECS 和 WinForms 紧耦合 — 无法独立测试 ECS 逻辑
2. 无差异比较 — 每次全量更新，性能浪费
3. 无增量更新 — 修改单个控件属性需要重新执行整个管线
4. 反射脆弱 — ComponentDataBridge 的运行时反射不可靠

### 3.3 根因分析

```
缺乏协议层（CanonicalForm）
  ├── ECS System 直接操作控件 → 紧耦合
  ├── 无中间表示 → 无法做差异比较
  ├── 无差异比较 → 只能全量更新
  ├── 全量更新 → 不支持增量更新
  └── 无统一协议 → ComponentDataBridge 用反射凑合
```

**解决方案**：引入 CSG.Unified 的 CanonicalForm 作为协议层，ECS System 只输出 CanonicalForm（描述 UI"应该是什么样"），适配层解释执行。

---

# 第三部分：方案

## 四、重构方案：引入 CSG.Unified 作为协议层

### 4.1 核心思路

**ECS System 输出 CanonicalForm（描述 UI"应该是什么样"），CSG 变换引擎处理变换，适配层解释执行。**

```
ECS System
  │
  ▼ 输出 CanonicalForm（目标 UI 状态）
  │  Nodes = [控件描述...], Edges = [关系...], Metadata = [属性...]
  │
  ▼ CSG.Unified 协议层
  │  CsgFacade
  │  ├── AdapterRegistry → 查找 WinFormsUIAdapter
  │  ├── TransformEngine → CanonicalForm → UI 操作
  │  ├── PlanOptimizer → 合并/优化 UI 更新
  │  └── ValidationEngine → 验证 UI 状态一致性
  │
  ▼ 适配层
  │  WinFormsUIAdapter : IConfiguration
  │  ├── ToCanonical() — 控件树 → CanonicalForm（当前状态）
  │  ├── ApplyCanonical() — CanonicalForm → 控件操作（差异更新）
  │  └── WireEvents() — 控件事件 → CanonicalForm（事件回传）
  │
  ▼
  WinForms 控件
```

### 4.2 架构分层

| 层 | 职责 | 关键组件 | 依赖方向 |
|----|------|---------|---------|
| **ECS 层** | 计算 UI 状态，输出 CanonicalForm | LayoutSystem, StyleSystem, EventBindingSystem | → 协议层 |
| **协议层** | 中间表示、变换、优化、验证 | CsgFacade, CanonicalForm, UIProtocol | ← ECS → 适配层 |
| **适配层** | 将 CanonicalForm 解释为具体 UI 操作 | WinFormsUIAdapter, WinFormsEventAdapter | → WinForms |
| **UI 层** | 实际渲染 | WinForms 控件 | — |

### 4.3 关键组件设计（概要）

#### 4.3.1 UIProtocol — 协议常量定义

定义节点类型、边类型、属性键等协议常量，作为 ECS 和适配层之间的"契约"。

```csharp
public static class UIProtocol
{
    // 节点类型
    public const string NodeType_Control = "ui.control";
    public const string NodeType_Container = "ui.container";
    public const string NodeType_Window = "ui.window";
    
    // 边类型
    public const string EdgeType_Contains = "ui.contains";
    public const string EdgeType_Event = "ui.event.binding";
    public const string EdgeType_Value = "ui.value.binding";
    
    // 节点属性键
    public const string Prop_ControlType = "controlType";
    public const string Prop_Bounds = "bounds";
    public const string Prop_Text = "text";
    public const string Prop_Enabled = "enabled";
    public const string Prop_Visible = "visible";
    public const string Prop_Font = "font";
    public const string Prop_ForeColor = "foreColor";
    public const string Prop_BackColor = "backColor";
    public const string Prop_Value = "value";
    public const string Prop_EventName = "eventName";
    public const string Prop_FunctionName = "functionName";
}
```

> 完整代码见 [附录 A.1](#a1-uiprotocolcs)

#### 4.3.2 ECS System 重构 — 输出 CanonicalForm

每个 ECS System 的 `Update()` 方法改为：
1. 从 ECS 读取 Component
2. 计算（比例→像素、样式合并等）
3. **输出 CanonicalForm**，不再直接操作控件

```csharp
// LayoutSystem.cs — 重构后只输出 CanonicalForm
public class LayoutSystem : ISystem
{
    private readonly CsgFacade _csg;
    
    public void Update()
    {
        var entities = EntityManager.GetEntities<ProportionalLayoutComponent, PendingLayoutComponent>();
        if (entities.Count == 0) return;
        
        var builder = new CanonicalFormBuilder();
        foreach (var entity in entities)
        {
            // ... 计算布局 ...
            builder.AddNode(/* ... */);
        }
        _csg.Execute(batch => batch.AddStep("layoutUpdate", "ApplyLayout", builder.Build()));
    }
}
```

> 完整代码见 [附录 A.2](#a2-ecs-system-重构示例-layoutsystemcs)

#### 4.3.3 WinFormsUIAdapter — 适配层

实现 `IConfiguration`，提供双向转换能力：
- `ToCanonical()` — 将当前 WinForms 控件树转换为 CanonicalForm（当前状态快照）
- `ApplyCanonical()` — 比较当前状态和目标状态，只执行差异操作

> 完整代码见 [附录 A.3](#a3-winformsuiaptercs)

#### 4.3.4 UIReconciler — 差异比较引擎

维护上一次应用的 CanonicalForm 状态，每次 Apply 时比较差异，只执行变化的部分。

> 完整代码见 [附录 A.4](#a4-uireconcilercs)

### 4.4 集成步骤

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 1 | 定义 UI 协议常量 | 新建 `UIProtocol.cs` |
| 2 | 创建 WinFormsUIAdapter（实现 IConfiguration） | 新建 `WinFormsUIAdapter.cs` |
| 3 | 创建 UIReconciler（差异比较） | 新建 `UIReconciler.cs` |
| 4 | 重构 LayoutSystem → 输出 CanonicalForm | `LayoutSystem.cs` |
| 5 | 重构 StyleSystem → 输出 CanonicalForm | `StyleSystem.cs` |
| 6 | 重构 EventBindingSystem → 输出 CanonicalForm | `EventBindingSystem.cs` |
| 7 | 创建 WinFormsEventAdapter（事件→CanonicalForm） | 新建 `WinFormsEventAdapter.cs` |
| 8 | 重构 EcsEventSystem（订阅 CanonicalForm） | 新建 `EcsEventSystem.cs` |
| 9 | 注册 UI 优化规则 | 新建 `UIOptimizationRules.cs` |
| 10 | 删除旧的适配层代码 | IWinFormsControlAdapter、ComponentDataBridge 反射部分 |
| 11 | 集成测试 | 验证完整流程 |

### 4.5 架构对比

| 维度 | 当前架构 | 集成 CSG.Unified 后 |
|------|---------|-------------------|
| **协议层** | 无（ECS 直接调适配层） | CanonicalForm（统一中间表示） |
| **适配方式** | 接口实现 | 适配器注册 + 能力声明 |
| **变换方向** | 单向（ECS→UI） | 双向（ECS↔UI） |
| **差异更新** | 无（全量更新） | CanonicalForm 差异比较 |
| **优化** | 无 | PlanOptimizer + 规则引擎 |
| **验证** | 无 | ValidationEngine |
| **扩展性** | 新增接口实现 | 新增适配器注册 |
| **前端解耦** | ⚠️ ECS 依赖适配层接口 | ✅ ECS 完全不知道 UI |

---

# 第四部分：价值

## 五、协议层的核心价值

### 5.1 CanonicalForm 作为 ECS 的"输出契约"

引入协议层后，ECS 和 WinForms 完全解耦，ECS 的"输出"变成了可序列化的纯数据：

```
ECS System 管线
  ↓ 输出
CanonicalForm（纯数据，JSON 可序列化）
  ├── 打印到控制台 → 人工验证布局/样式/事件是否正确
  ├── 写入日志文件 → 对比不同版本的输出差异
  ├── 单元测试断言 → 验证特定输入下的 ECS 输出
  └── 传递给 WinFormsUIAdapter → 渲染到 UI
```

### 5.2 调试能力提升

**"切片打印"ECS 输出** — 在管线任意阶段打印 CanonicalForm，就能看到 ECS 的"意图"：

```csharp
// 调试示例：在管线执行后打印 CanonicalForm
pipeline.ExecutePipeline();
var canonicalForm = pipeline.GetOutput();  // ECS 输出的协议数据
Console.WriteLine(JsonSerializer.Serialize(canonicalForm, new JsonSerializerOptions { WriteIndented = true }));
```

输出示例：
```json
{
  "nodes": [
    {
      "id": "entity_1",
      "label": "mainPanel",
      "type": "ui.control",
      "properties": {
        "controlType": "panel",
        "bounds": "0,0,800,600",
        "text": "Main Panel",
        "enabled": "True",
        "visible": "True"
      }
    },
    {
      "id": "entity_2",
      "label": "submitButton",
      "type": "ui.control",
      "properties": {
        "controlType": "button",
        "bounds": "100,200,120,30",
        "text": "Submit",
        "font": "Microsoft Sans Serif,9,Bold"
      }
    }
  ],
  "edges": [
    { "from": "entity_1", "to": "entity_2", "type": "ui.contains" },
    { "from": "entity_2", "to": "handler_click", "type": "ui.event.binding",
      "properties": { "eventName": "Click", "functionName": "OnSubmit" } }
  ]
}
```

### 5.3 独立测试与回归测试

**独立测试** — ECS 管线可以脱离 WinForms 环境独立测试：

```csharp
[Test]
public void LayoutSystem_Should_ComputeCorrectBounds()
{
    // Arrange — 设置 ECS 输入
    var entity = entityManager.CreateEntity();
    entityManager.AddComponent(entity, new ProportionalLayoutComponent { Left = 0.1, Top = 0.1, Width = 0.5, Height = 0.3 });
    entityManager.AddComponent(entity, new PendingLayoutComponent());
    
    // Act — 执行管线（不依赖 WinForms）
    layoutSystem.Update();
    
    // Assert — 验证 CanonicalForm 输出
    var output = csgFacade.GetLastOutput();
    var node = output.Nodes.First(n => n.Id == entity.Id.ToString());
    Assert.That(node.Properties["bounds"], Is.EqualTo("80,60,400,180"));
}
```

**回归测试** — 保存正确的 CanonicalForm 作为基线，修改后对比差异：

```csharp
// 保存基线
File.WriteAllText("baseline.json", JsonSerializer.Serialize(canonicalForm));

// 回归测试
var current = JsonSerializer.Serialize(pipeline.GetOutput());
var baseline = File.ReadAllText("baseline.json");
Assert.That(current, Is.EqualTo(baseline), "ECS 输出与基线不一致，请检查修改是否影响了 UI 渲染");
```

### 5.4 总结

**协议层的核心价值不在于"渲染"，而在于"可验证"。** CanonicalForm 作为 ECS 的输出契约，使得：

1. **ECS 可以独立测试** — 不需要 WinForms 环境
2. **调试时"切片"打印** — 看到 ECS 的"意图"
3. **回归测试** — 保存基线，对比差异
4. **UI 框架无关** — 同样的 CanonicalForm 可以渲染到任何 UI 框架

这正是 **"ECS 只负责'应该是什么样'，不关心'怎么渲染'"** 的设计哲学。

---

# 第五部分：风险

## 六、风险与注意事项

### 6.1 风险

| 风险 | 等级 | 说明 | 缓解措施 |
|------|------|------|---------|
| CSG.Unified 成熟度 | 中 | 需要确认 CSG.Unified 是否已经稳定可用 | 先做 POC 验证核心流程 |
| 性能开销 | 低 | CanonicalForm 的序列化/反序列化可能带来额外开销 | 差异比较减少实际操作次数，整体性能可能提升 |
| 迁移成本 | 中 | 需要重构 5 个 ECS System 和 ComponentDataBridge | 逐步迁移，保持向后兼容 |

### 6.2 注意事项

1. **逐步迁移** — 建议先迁移 LayoutSystem，验证可行后再迁移其他 System
2. **兼容性** — 迁移期间需要保持旧的 IWinFormsControlAdapter 接口可用
3. **测试覆盖** — 每个 System 迁移后都需要验证 WinForms 控件正确渲染

---

# 附录

## 附录 A：关键代码示例

### A.1 UIProtocol.cs

```csharp
/// <summary>
/// UI 协议常量定义
/// <para>定义 ECS 和适配层之间的"契约"：节点类型、边类型、属性键。</para>
/// <para>ECS System 输出 CanonicalForm 时使用此协议，</para>
/// <para>适配层（WinFormsUIAdapter）解释 CanonicalForm 时也使用此协议。</para>
/// </summary>
public static class UIProtocol
{
    // ================================================================
    // 节点类型
    // ================================================================

    /// <summary>普通控件（Button、Label、TextBox 等）</summary>
    public const string NodeType_Control = "ui.control";

    /// <summary>容器控件（Panel、SplitContainer 等）</summary>
    public const string NodeType_Container = "ui.container";

    /// <summary>窗口（Form）</summary>
    public const string NodeType_Window = "ui.window";

    // ================================================================
    // 边类型
    // ================================================================

    /// <summary>父子包含关系（容器→子控件）</summary>
    public const string EdgeType_Contains = "ui.contains";

    /// <summary>事件绑定（控件→事件处理器）</summary>
    public const string EdgeType_Event = "ui.event.binding";

    /// <summary>值绑定（控件→数据源）</summary>
    public const string EdgeType_Value = "ui.value.binding";

    // ================================================================
    // 节点属性键
    // ================================================================

    /// <summary>控件类型（panel、button、textbox 等）</summary>
    public const string Prop_ControlType = "controlType";

    /// <summary>控件位置和大小（"left,top,width,height"）</summary>
    public const string Prop_Bounds = "bounds";

    /// <summary>文本内容</summary>
    public const string Prop_Text = "text";

    /// <summary>启用状态</summary>
    public const string Prop_Enabled = "enabled";

    /// <summary>可见状态</summary>
    public const string Prop_Visible = "visible";

    /// <summary>字体描述（"family,size,style"）</summary>
    public const string Prop_Font = "font";

    /// <summary>前景色（HTML 颜色格式）</summary>
    public const string Prop_ForeColor = "foreColor";

    /// <summary>背景色（HTML 颜色格式）</summary>
    public const string Prop_BackColor = "backColor";

    /// <summary>控件值（因控件类型而异）</summary>
    public const string Prop_Value = "value";

    /// <summary>事件名称（Click、TextChanged 等）</summary>
    public const string Prop_EventName = "eventName";

    /// <summary>函数名称（事件触发时调用的函数）</summary>
    public const string Prop_FunctionName = "functionName";
}
```

### A.2 ECS System 重构示例（LayoutSystem.cs）

```csharp
/// <summary>
/// 布局计算 System — 重构后只输出 CanonicalForm
/// <para>不再直接操作 WinForms 控件，</para>
/// <para>通过 CSG 门面发布 CanonicalForm，由适配层解释执行。</para>
/// </summary>
public class LayoutSystem : ISystem
{
    private readonly EntityManager _entityManager;
    private readonly CsgFacade _csg;

    public LayoutSystem(EntityManager entityManager, CsgFacade csg)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _csg = csg ?? throw new ArgumentNullException(nameof(csg));
    }

    /// <summary>
    /// 执行一次更新：计算所有待处理布局的控件
    /// <para>输出 CanonicalForm，不再直接操作控件。</para>
    /// </summary>
    public void Update()
    {
        var entities = _entityManager.GetEntities<
            ProportionalLayoutComponent,
            PendingLayoutComponent>();

        if (entities.Count == 0) return;

        var builder = new CanonicalFormBuilder();

        foreach (var entity in entities)
        {
            var propLayout = _entityManager.GetComponent<ProportionalLayoutComponent>(entity);

            // 计算像素坐标（逻辑不变）
            int parentWidth = 800;  // 从父控件或 ScreenRoot 获取
            int parentHeight = 600;
            int left = (int)(propLayout.Left * parentWidth);
            int top = (int)(propLayout.Top * parentHeight);
            int width = (int)(propLayout.Width * parentWidth);
            int height = (int)(propLayout.Height * parentHeight);

            // 输出 CanonicalForm 节点
            builder.AddNode(
                id: entity.Id.ToString(),
                label: "layout",
                type: CanonicalNodeType.Default,
                properties: new Dictionary<string, object?>
                {
                    [UIProtocol.Prop_Bounds] = $"{left},{top},{width},{height}"
                });

            // 更新 ECS 状态标记
            _entityManager.RemoveComponent<PendingLayoutComponent>(entity);
            _entityManager.AddComponent(entity, new LayoutAppliedComponent());
        }

        // 通过 CSG 门面发布
        _csg.Execute(batch =>
            batch.AddStep("layoutUpdate", "ApplyLayout", builder.Build()));
    }
}
```

### A.3 WinFormsUIAdapter.cs

```csharp
/// <summary>
/// WinForms UI 适配器 — 实现 IConfiguration
/// <para>提供双向转换能力：</para>
/// <para>- ToCanonical()：控件树 → CanonicalForm（当前状态快照）</para>
/// <para>- ApplyCanonical()：CanonicalForm → 控件操作（差异更新）</para>
/// </summary>
public class WinFormsUIAdapter : IConfiguration
{
    private readonly IControlMap _controlMap;

    public string ConfigType => "WinForms.UI";
    public string SchemaVersion => "1.0";

    public WinFormsUIAdapter(IControlMap controlMap)
    {
        _controlMap = controlMap ?? throw new ArgumentNullException(nameof(controlMap));
    }

    /// <summary>
    /// 将当前 WinForms 控件树转换为 CanonicalForm（当前状态快照）
    /// </summary>
    public CanonicalForm ToCanonical()
    {
        var builder = new CanonicalFormBuilder();
        foreach (var (entity, handle) in _controlMap.GetAll())
        {
            if (handle.IsDisposed) continue;

            builder.AddNode(
                id: entity.Id.ToString(),
                label: handle.Name,
                type: UIProtocol.NodeType_Control,
                properties: new Dictionary<string, object?>
                {
                    [UIProtocol.Prop_Bounds] = $"{handle.Left},{handle.Top},{handle.Width},{handle.Height}",
                    [UIProtocol.Prop_Text] = handle.Text,
                    [UIProtocol.Prop_Enabled] = handle.Enabled,
                    [UIProtocol.Prop_Visible] = handle.Visible
                });
        }
        return builder.Build();
    }

    /// <summary>
    /// 将 CanonicalForm 应用到 WinForms 控件（差异更新）
    /// </summary>
    public void ApplyCanonical(CanonicalForm targetForm)
    {
        var currentForm = ToCanonical();
        var diff = CompareForms(currentForm, targetForm);
        foreach (var change in diff)
            ApplyChange(change);
    }

    private List<UIChange> CompareForms(CanonicalForm current, CanonicalForm target)
    {
        var changes = new List<UIChange>();

        // 比较节点属性差异
        foreach (var targetNode in target.Nodes)
        {
            var currentNode = current.Nodes.FirstOrDefault(n => n.Id == targetNode.Id);
            if (currentNode == null)
            {
                changes.Add(new UIChange(ChangeType.Add, targetNode));
                continue;
            }

            foreach (var kvp in targetNode.Properties)
            {
                if (!currentNode.Properties.TryGetValue(kvp.Key, out var currentVal) ||
                    currentVal?.ToString() != kvp.Value?.ToString())
                {
                    changes.Add(new UIChange(ChangeType.Update, targetNode, kvp.Key, kvp.Value));
                }
            }
        }

        // 检测删除的节点
        foreach (var currentNode in current.Nodes)
        {
            if (!target.Nodes.Any(n => n.Id == currentNode.Id))
                changes.Add(new UIChange(ChangeType.Remove, currentNode));
        }

        return changes;
    }

    private void ApplyChange(UIChange change)
    {
        var control = _controlMap.GetControl(change.NodeId);
        if (control == null) return;

        switch (change.Type)
        {
            case ChangeType.Update when change.PropertyKey == UIProtocol.Prop_Bounds:
                var parts = change.PropertyValue?.ToString()?.Split(',');
                if (parts?.Length == 4)
                    control.SetBounds(int.Parse(parts[0]), int.Parse(parts[1]),
                        int.Parse(parts[2]), int.Parse(parts[3]));
                break;

            case ChangeType.Update when change.PropertyKey == UIProtocol.Prop_Text:
                control.Text = change.PropertyValue?.ToString() ?? string.Empty;
                break;

            // ... 其他属性处理 ...
        }
    }
}
```

### A.4 UIReconciler.cs

```csharp
/// <summary>
/// UI 差异比较引擎
/// <para>维护上一次应用的 CanonicalForm 状态，</para>
/// <para>每次 Apply 时比较差异，只执行变化的部分。</para>
/// </summary>
public class UIReconciler
{
    private readonly CsgFacade _csg;
    private CanonicalForm _lastAppliedState = new();

    public UIReconciler(CsgFacade csg)
    {
        _csg = csg ?? throw new ArgumentNullException(nameof(csg));
    }

    /// <summary>
    /// 应用目标 UI 状态（差异更新）
    /// <para>比较 _lastAppliedState 和 targetState 的差异，</para>
    /// <para>只执行变化的部分。</para>
    /// </summary>
    public void Apply(CanonicalForm targetState)
    {
        var validationResult = _csg.ValidateConsistency(_lastAppliedState, targetState);
        if (!validationResult.IsValid)
        {
            var operations = GenerateOperations(validationResult);
            _csg.Execute(batch =>
            {
                foreach (var op in operations)
                    batch.AddStep(op.Id, op.Name, op.Data);
            });
        }
        _lastAppliedState = targetState;
    }

    /// <summary>
    /// 从验证结果生成操作序列
    /// </summary>
    private List<UIOperation> GenerateOperations(ValidationResult result)
    {
        var operations = new List<UIOperation>();

        foreach (var diff in result.Differences)
        {
            switch (diff.Type)
            {
                case DiffType.NodeAdded:
                    operations.Add(new UIOperation(
                        $"{diff.NodeId}_add", "AddControl", diff.Data));
                    break;

                case DiffType.NodeRemoved:
                    operations.Add(new UIOperation(
                        $"{diff.NodeId}_remove", "RemoveControl", diff.Data));
                    break;

                case DiffType.PropertyChanged:
                    operations.Add(new UIOperation(
                        $"{diff.NodeId}_{diff.PropertyKey}", "UpdateProperty", diff.Data));
                    break;
            }
        }

        return operations;
    }
}
```

---

## 附录 B：术语表

| 术语 | 说明 |
|------|------|
| **CanonicalForm** | CSG.Unified 的规范形式，用于表示 UI 状态的中间表示层。包含 Nodes（节点）、Edges（边）、Metadata（元数据） |
| **CSG** | Canonical State Graph — 规范状态图，CSG.Unified 框架的核心概念 |
| **CsgFacade** | CSG 门面，提供 Execute、ValidateConsistency 等高层 API |
| **IConfiguration** | CSG.Unified 的适配器接口，适配层实现此接口以支持 CanonicalForm 的双向转换 |
| **ECS** | Entity-Component-System — 实体-组件-系统架构模式 |
| **Entity** | ECS 中的实体，作为"能力卡片"存在，不维护树结构 |
| **ControlPool** | Entity↔Control 的扁平映射，提供双向查找 |
| **IWinFormsControlAdapter** | 当前架构中的 WinForms 控件操作适配器接口（将被 CanonicalForm 替代） |
| **Pending*Component** | ECS 中的状态标记 Component，表示 Entity 的某个能力尚未处理 |
| **UIReconciler** | UI 差异比较引擎，维护上一次应用的 CanonicalForm 状态 |

---

## 附录 C：涉及文件清单

### 当前架构文件

| 文件路径 | 说明 |
|---------|------|
| `CsharpPrj/Operation/FrontEndDriven/RenderPipeline/EcsRenderPipeline.cs` | ECS 渲染管线主入口 |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs` | 配置直接驱动控件工厂 |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/Systems/ControlCreateSystem.cs` | ECS 能力应用器（EcsCapabilityApplier） |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/Systems/LayoutSystem.cs` | 布局计算 System |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/Systems/SplitContainerSystem.cs` | SplitContainer 分割管理 System |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Style/Systems/StyleSystem.cs` | 样式合并 System |
| `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/Systems/EventBindingSystem.cs` | 事件绑定 System |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/ControlSystemBase.cs` | ECS System 基类 |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Components/PipelineStateComponents.cs` | 管线状态标记 Component |
| `CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/WinFormsControlAdapter.cs` | WinForms 控件操作适配器实现 |
| `CsharpPrj/Operation/FrontEndDriven/Shared/Abstractions/IWinFormsControlAdapter.cs` | WinForms 控件操作适配器接口