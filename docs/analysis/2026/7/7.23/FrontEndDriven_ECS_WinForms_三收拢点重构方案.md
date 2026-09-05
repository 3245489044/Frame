# FrontEndDriven — ECS 管道与 WinForms 渲染适配：三收拢点重构方案

> **基于**：`FrontEndDriven_ECS_WinForms_适配分析.md` v2.0  
> **分析日期**：2026-07-23  
> **目标**：将 CSG 协议层集中到三个收拢点，消除分散调用  
> **状态**：📋 方案设计完成，待实施

---

## 目录

- [一、问题分析](#一问题分析)
- [二、架构设计：三收拢点](#二架构设计三收拢点)
- [三、详细设计](#三详细设计)
  - [① EcsInputPort — ECS 输入端点](#-ecsinputport--ecs-输入端点)
  - [② CsgEntryPoint — CSG 入口](#-csgentrypoint--csg-入口)
  - [③ WinFormsOutputPort — WinForms 输出端点](#-winformsoutputport--winforms-输出端点)
- [四、管线执行流程](#四管线执行流程)
- [五、文件变更清单](#五文件变更清单)
- [六、实施步骤](#六实施步骤)
- [七、验收标准](#七验收标准)

---

## 一、问题分析

### 1.1 当前架构的问题

当前代码中，CSG 协议层的调用分散在多个位置：

```
LayoutSystem          → _csg.Execute()  ← 分散
StyleSystem           → _csg.Execute()  ← 分散
EventBindingSystem    → _bindingDelegate ← 绕过 CSG
SplitContainerSystem  → _adapter.*()    ← 绕过 CSG
ComponentDataBridge   → _adapter.*()    ← 绕过 CSG（反射）
EcsRenderPipeline     → 未调用 CSG     ← 未接入
```

**核心问题**：
1. **CSG 调用点分散** — 4 个 System 各自构建 CanonicalForm、各自调用 `_csg.Execute()`
2. **部分逻辑完全绕过 CSG** — SplitContainerSystem、ComponentDataBridge 直接操作控件
3. **System 复杂度增加** — 每个 System 需要处理"有 CSG / 无 CSG"两种模式
4. **缺乏事务性** — 多次 Execute 可能部分成功，状态不一致

### 1.2 根因

根本原因在于：**ECS System 既负责计算（写 Component），又负责输出（操作控件）**。职责未分离导致 CSG 无法集中。

---

## 二、架构设计：三收拢点

### 2.1 设计原则

1. **职责分离** — ECS System 只写 Component，不感知输出方式
2. **单一入口/出口** — CSG 只有一处入口，WinForms 只有一处出口
3. **管线末尾统一提交** — 所有 System 执行完毕后，一次性收集、转换、输出

### 2.2 三收拢点架构

```
┌──────────────────────────────────────────────────────────────────┐
│                      EcsRenderPipeline                            │
│                                                                   │
│  ┌─────────────────┐    ┌─────────────────┐    ┌──────────────┐  │
│  │  ① EcsInputPort  │    │  ② CsgEntryPoint │    │ WinForms     │  │
│  │  (ECS 输入端点)   │───→│  (CSG 入口)      │───→│ OutputPort   │  │
│  │                  │    │                  │    │ (WinForms    │  │
│  │  输入：           │    │  读取 Component   │    │  输出端点)   │  │
│  │  - 配置加载       │    │  构建 Canonical  │    │             │  │
│  │  - 事件回传       │    │  Form            │    │  输出：      │  │
│  │  - 数据变更       │    │                  │    │  - 差异更新  │  │
│  └────────┬────────┘    └────────┬────────┘    │  - 控件操作   │  │
│           │                      │             └──────────────┘  │
│           ▼                      │                               │
│  ┌─────────────────┐             │                               │
│  │    ECS World     │             │                               │
│  │  (Entity +       │◄────────────┘                               │
│  │   Component)     │    CsgEntryPoint 只读 Component             │
│  └─────────────────┘                                             │
└──────────────────────────────────────────────────────────────────┘
```

### 2.3 职责边界

| 收拢点 | 职责 | 不负责 |
|--------|------|--------|
| **① EcsInputPort** | 所有进入 ECS World 的数据入口 | 不操作控件，不构建 CanonicalForm |
| **② CsgEntryPoint** | ECS Component → CanonicalForm 的唯一转换 | 不操作控件，不修改 Component |
| **③ WinFormsOutputPort** | CanonicalForm → WinForms 控件的唯一输出 | 不读取 Component，不构建 CanonicalForm |

---

## 三、详细设计

### ① EcsInputPort — ECS 输入端点

**文件**：`CsharpPrj/Operation/FrontEndDriven/ECS/Ports/EcsInputPort.cs`  
**依赖**：`EntityManager`, `ControlPool`, `ControlFactory`  
**风险**：低

#### 接口定义

```csharp
public sealed class EcsInputPort
{
    /// <summary>加载 UI 配置，创建 Entity 树</summary>
    public Entity LoadConfig(UIConfiguration config);

    /// <summary>处理 UI 事件回传，更新对应 Component</summary>
    public void HandleEvent(Entity entity, string eventName, object? args);

    /// <summary>应用后端数据变更，更新 Component</summary>
    public void ApplyDataChange(Entity entity, Type componentType, object? newValue);

    /// <summary>管线执行完成回调（触发事件回传处理）</summary>
    public event Action<CanonicalForm>? PipelineCompleted;
}
```

#### 内部实现

```csharp
public Entity LoadConfig(UIConfiguration config)
{
    // 委托给 ConfigDrivenControlFactory
    var rootEntity = _configDrivenFactory.CreateRootEntity(config);
    _configDrivenFactory.ConvertChildren(config, rootEntity);
    return rootEntity;
}

public void HandleEvent(Entity entity, string eventName, object? args)
{
    // 查找 EventBindingsComponent，执行对应 functionName 的处理
    // 更新 Component 状态
    // 触发管线重执行
}

public void ApplyDataChange(Entity entity, Type componentType, object? newValue)
{
    // 通过反射或已知类型映射更新 Component
    // 触发管线重执行
}
```

#### 设计要点

- `ConfigDrivenControlFactory` 作为 `EcsInputPort` 的内部实现，不对外暴露
- 事件回传和数据变更统一通过 Component 更新来驱动，不直接操作控件
- `PipelineCompleted` 事件供外部监听，触发下一轮管线执行

---

### ② CsgEntryPoint — CSG 入口

**文件**：`CsharpPrj/Operation/FrontEndDriven/RenderPipeline/CsgEntryPoint.cs`  
**依赖**：`EntityManager`, `ControlPool`, `UIProtocol`, `CanonicalFormBuilder`  
**风险**：中

#### 接口定义

```csharp
public sealed class CsgEntryPoint
{
    /// <summary>收集 ECS World 状态，构建完整 CanonicalForm</summary>
    public CanonicalForm Collect();
}
```

#### 内部实现

```csharp
public CanonicalForm Collect()
{
    var builder = new CanonicalFormBuilder()
        .WithMetadata("pipelineStage", "render")
        .WithMetadata("timestamp", DateTime.UtcNow.ToString("O"));

    // 1. 收集布局信息（ComputedLayoutComponent）
    foreach (var entity in _entityManager.GetEntities<ComputedLayoutComponent>())
    {
        var layout = _entityManager.GetComponent<ComputedLayoutComponent>(entity);
        var control = _controlPool.GetControl(entity);
        var controlName = control != null ? GetControlName(control) : entity.Id.ToString();

        builder.AddNode(
            id: entity.Id.ToString(),
            label: controlName,
            type: new CanonicalNodeType(UIProtocol.NodeType_Control),
            properties: new Dictionary<string, object?>
            {
                [UIProtocol.Prop_Left] = layout.Left,
                [UIProtocol.Prop_Top] = layout.Top,
                [UIProtocol.Prop_Width] = layout.Width,
                [UIProtocol.Prop_Height] = layout.Height,
                [UIProtocol.Meta_EntityId] = entity.Id.ToString()
            });
    }

    // 2. 收集样式信息（MergedStyleComponent）
    foreach (var entity in _entityManager.GetEntities<MergedStyleComponent>())
    {
        var style = _entityManager.GetComponent<MergedStyleComponent>(entity);
        // ... 构建样式属性
    }

    // 3. 收集事件绑定信息（EventBindingsComponent + EventsBoundComponent）
    foreach (var entity in _entityManager.GetEntities<EventBindingsComponent, EventsBoundComponent>())
    {
        var bindings = _entityManager.GetComponent<EventBindingsComponent>(entity);
        // ... 构建事件边
    }

    // 4. 收集值同步信息（ControlValueComponent）
    // ...

    return builder.Build();
}
```

#### 设计要点

- **只读操作** — 只读取 Component，不修改任何 ECS 状态
- **完整快照** — 每次 Collect 都构建完整的控件树 CanonicalForm，而非增量
- **差异比较由下游负责** — CsgEntryPoint 只负责"当前状态是什么"，不关心"变了什么"
- **性能优化** — 可通过 Archetype 查询批量获取 Entity，减少遍历次数

---

### ③ WinFormsOutputPort — WinForms 输出端点

**文件**：`CsharpPrj/Operation/FrontEndDriven/WinForms/Ports/WinFormsOutputPort.cs`  
**依赖**：`WinFormsUIAdapter`, `UIReconciler`, `CsgFacade`  
**风险**：中

#### 接口定义

```csharp
public sealed class WinFormsOutputPort
{
    /// <summary>应用 CanonicalForm 到 WinForms 控件树</summary>
    public void Apply(CanonicalForm targetForm);
}
```

#### 内部实现

```csharp
public void Apply(CanonicalForm targetForm)
{
    // 1. 通过 CsgFacade 验证一致性
    var validationResult = _csg.ValidateConsistency(targetForm);
    if (!validationResult.IsValid)
    {
        // 记录验证失败信息，但不阻塞输出
        LogWarning("CanonicalForm 验证失败", validationResult.Errors);
    }

    // 2. 通过 UIReconciler 执行差异更新
    _reconciler.Apply(targetForm);
}
```

#### UIReconciler 的内部逻辑

```csharp
public sealed class UIReconciler
{
    private CanonicalForm? _lastAppliedState;
    private readonly WinFormsUIAdapter _adapter;
    private readonly CsgFacade _csg;

    public void Apply(CanonicalForm targetState)
    {
        if (_lastAppliedState == null)
        {
            // 首次：全量应用
            _adapter.ApplyCanonical(targetState);
        }
        else
        {
            // 后续：只执行差异部分
            var operations = _csg.Diff(_lastAppliedState, targetState);
            _csg.Execute(batch =>
            {
                foreach (var op in operations)
                    batch.AddStep(op.Id, op.Type, op.Data);
            });
        }

        _lastAppliedState = targetState;
    }
}
```

#### 设计要点

- **唯一输出出口** — 所有 WinForms 控件操作都经过这里
- **差异更新** — 内部维护上一次状态，只执行差异部分
- **CsgFacade 封装** — 验证、差异比较、执行都通过 CsgFacade

---

## 四、管线执行流程

### 4.1 完整管线流程

```
EcsRenderPipeline.ExecutePipeline():

  // ── ECS System 执行（只写 Component，不操作控件）──
  1. _capabilityApplier.Update()         // 移除 PendingCapabilityComponent
  2. _layoutSystem.Update()              // 计算布局 → 写入 ComputedLayoutComponent
  3. _splitContainerSystem.Update()      // 订阅 SplitterMoved 事件
  4. _styleSystem.Update()               // 合并样式 → 写入 MergedStyleComponent
  5. _eventBindingSystem.Update()        // 绑定事件 → 写入 EventsBoundComponent

  // ── 三收拢点串联 ──
  6. var form = _csgEntry.Collect()      // ② CSG 入口：读取 Component → CanonicalForm
  7. _winFormsOutput.Apply(form)         // ③ WinForms 输出：CanonicalForm → 控件差异更新
```

### 4.2 事件回传流程

```
用户操作控件
  → WinForms 事件触发
  → WinFormsEventAdapter 捕获事件
  → EcsInputPort.HandleEvent(entityId, eventName, args)
    → 更新对应 Entity 的 Component
    → 触发管线重执行
      → CsgEntryPoint.Collect()
      → WinFormsOutputPort.Apply()
```

### 4.3 数据变更流程

```
后端数据变更
  → ComponentDataBridge 接收 ComponentUpdated 事件
  → EcsInputPort.ApplyDataChange(entity, componentType, newValue)
    → 更新 Component
    → 触发管线重执行
      → CsgEntryPoint.Collect()
      → WinFormsOutputPort.Apply()
```

---

## 五、文件变更清单

### 5.1 回退文件（移除 CSG 分散调用）

| # | 文件路径 | 操作 | 说明 |
|---|---------|------|------|
| 1 | `ControlRender/Layout/Systems/LayoutSystem.cs` | 回退 | 移除 `_csg` 字段、CsgFacade 构造函数、CanonicalForm 构建逻辑 |
| 2 | `ControlRender/Style/Systems/StyleSystem.cs` | 回退 | 移除 `_csg` 字段、CsgFacade 构造函数、CanonicalForm 构建逻辑 |
| 3 | `RenderPipeline/EcsRenderPipeline.cs` | 修改 | 移除 System 构造函数中的 CsgFacade 注入，改为注入三个收拢点 |

### 5.2 新增文件

| # | 文件路径 | 说明 |
|---|---------|------|
| 1 | `ECS/Ports/EcsInputPort.cs` | ① ECS 输入端点 |
| 2 | `RenderPipeline/CsgEntryPoint.cs` | ② CSG 入口 |
| 3 | `WinForms/Ports/WinFormsOutputPort.cs` | ③ WinForms 输出端点 |

### 5.3 修改文件

| # | 文件路径 | 说明 |
|---|---------|------|
| 1 | `RenderPipeline/EcsRenderPipeline.cs` | `ExecutePipeline()` 末尾串联三个收拢点 |
| 2 | `ComponentDataIntegration/ComponentDataIntegration.cs` | 移除反射，通过 EcsInputPort 触发管线 |
| 3 | `FrontEndDriven.csproj` | 添加 CSG.Unified 项目引用 |

### 5.4 废弃文件

| # | 文件路径 | 说明 |
|---|---------|------|
| 1 | `Shared/Abstractions/IWinFormsControlAdapter.cs` | 标记 `[Obsolete]` |
| 2 | `WinForms/Adapter/WinFormsControlAdapter.cs` | 标记 `[Obsolete]` |

### 5.5 保持不变的文件

| # | 文件路径 | 说明 |
|---|---------|------|
| 1 | `ControlRender/Layout/Systems/SplitContainerSystem.cs` | 保持现状，不引入 CSG |
| 2 | `ControlInteraction/EventBinding/Systems/EventBindingSystem.cs` | 保持现状，通过委托绑定 |
| 3 | `Protocol/UIProtocol.cs` | 已存在，保持不变 |
| 4 | `WinForms/Adapter/WinFormsUIAdapter.cs` | 已存在，保持不变 |
| 5 | `WinForms/Adapter/WinFormsEventAdapter.cs` | 已存在，保持不变 |
| 6 | `WinForms/Adapter/ControlMapAdapter.cs` | 已存在，保持不变 |
| 7 | `RenderPipeline/UIReconciler.cs` | 已存在，保持不变 |

---

## 六、实施步骤

### 阶段 1：回退 System 中的 CSG 代码

#### 步骤 1.1：回退 LayoutSystem.cs

**修改**：
- 移除 `_csg` 字段
- 移除 `LayoutSystem(EntityManager, ControlPool, IWinFormsControlAdapter, CsgFacade)` 构造函数
- 移除 `Update()` 中的 `if (_csg != null) { ... } else { ... }` 分支
- 恢复为统一的 `Adapter.SetBounds()` 调用
- 保留 `[Obsolete]` 旧构造函数标记

**验收**：
- [ ] 编译通过
- [ ] LayoutSystem 不再引用 CSG.Unified 和 UIProtocol

#### 步骤 1.2：回退 StyleSystem.cs

**修改**：
- 移除 `_csg` 字段
- 移除 `StyleSystem(EntityManager, ControlPool, IWinFormsControlAdapter, CsgFacade)` 构造函数
- 移除 `Update()` 中的 `if (_csg != null) { ... } else { ... }` 分支
- 恢复为统一的 `ApplyStyle()` 调用

**验收**：
- [ ] 编译通过
- [ ] StyleSystem 不再引用 CSG.Unified 和 UIProtocol

### 阶段 2：新增三个收拢点

#### 步骤 2.1：新增 CsgEntryPoint.cs

**文件**：`CsharpPrj/Operation/FrontEndDriven/RenderPipeline/CsgEntryPoint.cs`

**内容**：
- `Collect()` 方法遍历所有 Entity，读取 Component 状态
- 收集布局（ComputedLayoutComponent）
- 收集样式（MergedStyleComponent）
- 收集事件绑定（EventBindingsComponent + EventsBoundComponent）
- 构建完整 CanonicalForm 返回

**验收**：
- [ ] 编译通过
- [ ] 能正确收集所有已处理 Entity 的状态
- [ ] 输出的 CanonicalForm 包含布局、样式、事件信息

#### 步骤 2.2：新增 WinFormsOutputPort.cs

**文件**：`CsharpPrj/Operation/FrontEndDriven/WinForms/Ports/WinFormsOutputPort.cs`

**内容**：
- 封装 `WinFormsUIAdapter` + `UIReconciler` + `CsgFacade`
- `Apply(CanonicalForm)` 方法执行差异更新

**验收**：
- [ ] 编译通过
- [ ] 首次 Apply 全量应用
- [ ] 后续 Apply 只执行差异部分

#### 步骤 2.3：新增 EcsInputPort.cs

**文件**：`CsharpPrj/Operation/FrontEndDriven/ECS/Ports/EcsInputPort.cs`

**内容**：
- 封装 `ConfigDrivenControlFactory`
- `LoadConfig()` 加载配置
- `HandleEvent()` 处理事件回传
- `ApplyDataChange()` 处理数据变更

**验收**：
- [ ] 编译通过
- [ ] 配置加载功能正常

### 阶段 3：管线集成

#### 步骤 3.1：重构 EcsRenderPipeline.cs

**修改**：
- 构造函数注入三个收拢点（或内部创建）
- `Initialize()` 中创建三个收拢点实例
- `ExecutePipeline()` 末尾串联：System 执行 → CsgEntryPoint.Collect() → WinFormsOutputPort.Apply()

**验收**：
- [ ] 编译通过
- [ ] 管线执行后控件正确渲染
- [ ] 差异更新正常工作

#### 步骤 3.2：改造 ComponentDataBridge

**修改**：
- 移除 `_syncHandlers` 字典
- 移除所有 `Sync*Value` 方法
- 移除 `RegisterSyncHandler()` 和 `TryLazyRegister()`
- `OnComponentUpdated()` 改为通过 `EcsInputPort.ApplyDataChange()` 触发管线

**验收**：
- [ ] 编译通过
- [ ] 组件值变更能正确同步到控件
- [ ] 不再使用反射读取属性

#### 步骤 3.3：更新项目引用

**修改**：`FrontEndDriven.csproj` 添加 CSG.Unified 引用

### 阶段 4：清理

#### 步骤 4.1：标记旧接口为废弃

- `IWinFormsControlAdapter.cs` — 添加 `[Obsolete]`
- `WinFormsControlAdapter.cs` — 添加 `[Obsolete]`

#### 步骤 4.2：更新实施计划文档

- 更新 `FrontEndDriven_ECS_WinForms_实施计划.md` 状态为已完成

#### 步骤 4.3：记录知识图谱

- 使用 `create_entities` 和 `create_relations` 记录新架构知识

---

## 七、验收标准

### 7.1 架构验收

- [ ] CSG 只有一处入口（CsgEntryPoint.Collect）
- [ ] WinForms 只有一处出口（WinFormsOutputPort.Apply）
- [ ] ECS System 不感知 CSG，只写 Component
- [ ] 三个收拢点职责清晰，无职责重叠

### 7.2 功能验收

- [ ] 现有 UI 渲染功能不变（回归测试通过）
- [ ] 布局计算正确输出到控件
- [ ] 样式合并正确输出到控件
- [ ] 事件绑定正常工作
- [ ] SplitContainer 分割管理正常
- [ ] 组件值变更能正确同步到控件

### 7.3 代码质量验收

- [ ] 无重复的 CanonicalForm 构建逻辑
- [ ] 无分散的 `_csg.Execute()` 调用
- [ ] 无反射读取控件属性
- [ ] 旧接口标记为 `[Obsolete]` 但保持可用

---

## 附录：架构对比

| 方面 | 旧方案（分散注入） | 新方案（三收拢点） |
|------|------------------|------------------|
| CSG 调用点 | 4 个 System 各自调用 | 管线末尾 1 处 |
| System 复杂度 | 每个 System 有 CSG 分支逻辑 | System 只写 Component，零 CSG 感知 |
| 向后兼容 | 每个 System 需要两套构造函数 | System 完全不变 |
| 事务性 | 多次 Execute，可能部分成功 | 一次 Collect + 一次 Apply，全有或全无 |
| 可调试性 | 分散在各处，难以追踪 | 集中一处，断点/日志方便 |
| 职责分离 | System 既计算又输出 | System 只计算，收拢点负责输出 |
