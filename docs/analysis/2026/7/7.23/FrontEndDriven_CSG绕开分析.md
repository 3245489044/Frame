# FrontEndDriven CSG 框架绕开分析报告

> **分析日期**：2026-07-23  
> **分析范围**：`CsharpPrj/Operation/FrontEndDriven/` 全目录  
> **分析目标**：识别所有逻辑绕开 CSG 框架（CanonicalForm 规范形式）直接操作 WinForms 控件的代码路径

---

## 1. CSG 框架规范数据流

三收拢点重构（2026-07-23）定义的规范路径：

```
EcsInputPort → ECS World (Systems 只写 Component)
  → CsgEntryPoint.Collect() (读取 Component → CanonicalForm)
    → WinFormsOutputPort.Apply() (CanonicalForm → 控件差异更新)
```

**核心原则**：任何不经过 CanonicalForm 中间表示就直接操作 WinForms 控件的数据流，都属于"绕开 CSG 框架"。

---

## 2. 已修复/已消除的绕开点 ✅

| 位置 | 状态 | 说明 |
|------|------|------|
| `LayoutSystem.Update()` | ✅ 已修复 | 改为只写 `ComputedLayoutComponent`，不再直接操作控件 |
| `StyleSystem.Update()` | ✅ 已修复 | 改为只写 `MergedStyleComponent`，不再直接操作控件 |
| `StyleSystem.ApplyStyle()` | ✅ 死代码 | 方法体存在（第94-111行）但 `Update()` 中已注释调用，未被执行 |
| `ConfigDrivenControlFactory` | ✅ 已修复 | 改为只创建 Entity 和 Component，不再直接创建控件 |
| `WinFormsEventAdapter` | ✅ 已修复 | 改为通过 `EcsInputPort` 回传事件，不直接操作 ECS |
| `ComponentDataBridge.SyncLayoutValue()` | ✅ 已修复 | 改为空操作（只写注释），不再直接 `SetBounds` |

---

## 3. 仍然存在的绕开点 ⚠️

### 3.1 ComponentDataBridge — 严重绕开

**文件**：`ComponentDataIntegration.cs`（第83-440行）

**数据流**：
```
ECS Component 变更
  → EntityManager.ComponentUpdated 事件触发
    → ComponentDataBridge.OnComponentUpdated() (第212行)
      → SyncXxxValue() 方法
        → _valueWriter.SetValue() / _listSelectionAdapter.SelectItem()
          → WinForms 控件属性变更（绕过 CSG）
```

**绕开方法清单**：

| 方法 | 行号 | 操作 | 绕开程度 |
|------|------|------|---------|
| `SyncTextValue` | 263-277 | `_valueWriter.SetValue(control, text)` | ⚠️ 直接操作控件 |
| `SyncSelectionValue` | 282-308 | `_valueWriter.SetValue(control, targetValue)` | ⚠️ 直接操作控件 |
| `SyncCheckStateValue` | 313-327 | `_valueWriter.SetValue(control, targetValue)` | ⚠️ 直接操作控件 |
| `SyncNumericValue` | 332-346 | `_valueWriter.SetValue(control, targetValue)` | ⚠️ 直接操作控件 |
| `SyncDateTimeValue` | 351-370 | `_valueWriter.SetValue(control, targetValue)` | ⚠️ 直接操作控件 |
| `SyncListSelectionValue` | 403-430 | `_listSelectionAdapter.SelectItem(control, idx)` | ⚠️ 直接操作控件 |
| `SyncLayoutValue` | 378-398 | ✅ 已改为空操作 | 已修复 |

**影响分析**：
- `ComponentDataBridge` 在 `EcsRenderPipeline.Initialize()` 中初始化（第232行），订阅 `EntityManager.ComponentUpdated` 事件
- 每次组件变更都会触发同步，是**运行时高频路径**
- 数据完全绕过 `CsgEntryPoint.Collect()` → `WinFormsOutputPort.Apply()` 的 CSG 路径
- 虽然通过 `IControlValueAccessor` 和 `IListSelectionAdapter` 接口间接操作控件（不是直接引用 `System.Windows.Forms`），但**数据流没有经过 CanonicalForm**

**应该改为**：
```
ECS Component 变更
  → 标记 Entity 为 Dirty
    → 触发管线重执行
      → CsgEntryPoint.Collect() 收集变更
        → WinFormsOutputPort.Apply() 统一应用
```

---

### 3.2 EcsCapabilityApplier — 中等绕开

**文件**：`ControlRender/Creation/Systems/ControlCreateSystem.cs`（第35-166行）

**数据流**：
```
EcsCapabilityApplier.Update() (第62行)
  → CreateControlInstance() (第159行) — 创建控件实例
  → _controlPool.Register() (第93行) — 注册到 ControlPool
  → AddToParentControl() (第108行) — 通过 _adapter.AddControl() 添加到父控件
```

**绕开路径**：
- `AddToParentControl()` 中直接调用 `_adapter.AddControl(parentControl, childControl)`（第149行）
- 控件创建和父子关系建立没有经过 CSG 协议层

**说明**：
- 控件创建属于**初始化阶段**，CSG 协议层主要负责**运行时状态同步**
- 控件创建完成后，后续的布局/样式/值同步都通过 CSG 路径
- 可以接受，但理论上控件创建也应该通过 `WinFormsOutputPort`

---

### 3.3 SplitContainerSystem.OnSplitterMoved — 轻微绕开

**文件**：`ControlRender/Layout/Systems/SplitContainerSystem.cs`（第124-162行）

**数据流**：
```
SplitterMoved 事件
  → OnSplitterMoved() (第124行)
    → _adapter.GetName() / GetOrientation() / GetWidth() / GetHeight() / GetSplitterDistance()
    → 写入 SplitterRatioComponent
```

**说明**：
- 这是**读操作**（从控件读取状态写入 Component），不涉及状态同步到控件
- 数据流方向是：控件 → ECS Component（读），不是 ECS Component → 控件（写）
- 影响较小，不属于严格意义上的"绕开 CSG"

---

## 4. 总体评估

| 维度 | 评估 |
|------|------|
| **三收拢点重构效果** | ✅ 良好 — 大部分 ECS Systems 已改为只写 Component |
| **主要遗留问题** | ⚠️ **ComponentDataBridge** — 6 个同步方法在管线之外直接操作控件 |
| **次要遗留问题** | ⚠️ EcsCapabilityApplier 控件创建（初始化阶段） |
| **轻微遗留问题** | ⚠️ SplitContainerSystem 读操作（不影响 CSG 输出） |
| **整体风险** | 🟡 中等 — ComponentDataBridge 是运行时高频路径 |

### 绕开路径示意图

```
规范路径（三收拢点）：
  ECS Component → CsgEntryPoint.Collect() → CanonicalForm → WinFormsOutputPort.Apply() → 控件
                                                                                            ↑
绕开路径 1（ComponentDataBridge）：                                                          │
  ECS Component → ComponentDataBridge.OnComponentUpdated() → SyncXxxValue() → _valueWriter ──┘
                                                                                            ↑
绕开路径 2（EcsCapabilityApplier）：                                                          │
  Entity → EcsCapabilityApplier.Update() → CreateControlInstance() → _adapter.AddControl() ──┘
                                                                                            ↑
绕开路径 3（SplitContainerSystem）：                                                          │
  控件 → OnSplitterMoved() → _adapter.GetXxx() → SplitterRatioComponent（读操作，不涉及写）
```

---

## 5. 建议修复方案

### 5.1 ComponentDataBridge 重构（高优先级）

**方案 A**：移除事件订阅，改为管线驱动
1. 移除 `EntityManager.ComponentUpdated` 事件订阅
2. 在 `EcsRenderPipeline.ExecutePipeline()` 中，`CsgEntryPoint.Collect()` 已收集所有 Component 状态
3. `WinFormsOutputPort.Apply()` 统一将 CanonicalForm 差异应用到控件
4. 删除 `ComponentDataBridge` 类，或降级为纯辅助工具

**方案 B**：保留事件订阅，但改为标记 Dirty
1. 保留 `EntityManager.ComponentUpdated` 事件订阅
2. 事件处理器只标记 Entity 为 Dirty（添加 `PendingSyncComponent`）
3. 由管线末尾的 `WinFormsOutputPort` 统一处理同步
4. 避免在管线之外直接操作控件

### 5.2 EcsCapabilityApplier 重构（中优先级）

1. 将控件创建操作封装为 CanonicalForm 节点
2. 通过 `WinFormsOutputPort` 统一执行控件创建
3. 但需要注意：控件创建时控件还不存在，CanonicalForm 无法描述"创建控件"这一操作

### 5.3 SplitContainerSystem 读操作（低优先级）

1. 当前设计合理：读操作从控件读取状态写入 Component
2. 不需要修复，但可以添加注释说明这是有意设计

---

## 6. 附录：代码引用

| 文件 | 关键行 | 说明 |
|------|--------|------|
| `ComponentDataIntegration.cs` | 第132行 | `_entityManager.ComponentUpdated += OnComponentUpdated` — 事件订阅 |
| `ComponentDataIntegration.cs` | 第212-233行 | `OnComponentUpdated()` — 事件分发 |
| `ComponentDataIntegration.cs` | 第263-430行 | 6 个 SyncXxxValue 方法 — 直接操作控件 |
| `ControlCreateSystem.cs` | 第108-154行 | `AddToParentControl()` — 直接 `_adapter.AddControl()` |
| `SplitContainerSystem.cs` | 第124-162行 | `OnSplitterMoved()` — 读操作 |
| `EcsRenderPipeline.cs` | 第359-381行 | `ExecutePipeline()` — 规范路径 |
| `Ports.cs` | 第94-327行 | `CsgEntryPoint.Collect()` — CSG 入口 |
| `Ports.cs` | 第357-403行 | `WinFormsOutputPort.Apply()` — WinForms 出口 |
