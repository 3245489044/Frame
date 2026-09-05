# FrontEndDriven — ECS 管道与 WinForms 渲染适配：代码实施计划

> **基于**：`FrontEndDriven_ECS_WinForms_适配分析.md` v2.0  
> **分析日期**：2026-07-23  
> **目标**：将适配分析方案转化为可执行的代码实施步骤  
> **状态**：✅ 实施完成（2026-07-23）

---

## 目录

- [一、实施总览](#一实施总览)
- [二、前置条件确认](#二前置条件确认)
- [三、详细实施步骤](#三详细实施步骤)
  - [阶段 1：协议层基础设施](#阶段-1协议层基础设施)
  - [阶段 2：ECS System 重构](#阶段-2ecs-system-重构)
  - [阶段 3：ComponentDataBridge 替换](#阶段-3componentdatabridge-替换)
  - [阶段 4：管线集成与清理](#阶段-4管线集成与清理)
  - [阶段 5：文档与知识记录](#阶段-5文档与知识记录)
- [四、文件清单](#四文件清单)
- [五、风险与缓解措施](#五风险与缓解措施)
- [六、验收标准](#六验收标准)

---

## 一、实施总览

### 1.1 架构变化

```
当前架构：                         目标架构：
ECS System                         ECS System
  │                                   │ 输出 CanonicalForm
  ▼                                   ▼
IWinFormsControlAdapter            CSG.Unified 协议层
  │                                   │ CsgFacade
  ▼                                   ├── TransformEngine
WinForms 控件                        ├── PlanOptimizer
                                     ├── ValidationEngine
                                     │
                                     ▼
                                   WinFormsUIAdapter : IConfiguration
                                     ├── ToCanonical() — 当前状态快照
                                     ├── ApplyCanonical() — 差异更新
                                     └── WireEvents() — 事件回传
                                     │
                                     ▼
                                   WinForms 控件
```

### 1.2 实施阶段

| 阶段 | 内容 | 文件数 | 预估工作量 |
|------|------|--------|-----------|
| 1 | 协议层基础设施（新建） | 4 新建 | ⭐⭐⭐ |
| 2 | ECS System 重构（修改） | 4 修改 | ⭐⭐⭐⭐ |
| 3 | ComponentDataBridge 替换 | 1 新建 + 1 修改 | ⭐⭐⭐ |
| 4 | 管线集成与清理 | 1 修改 + 清理 | ⭐⭐ |
| 5 | 文档与知识记录 | 1 更新 | ⭐ |

---

## 二、前置条件确认

### 2.1 已存在的依赖

| 组件 | 路径 | 状态 |
|------|------|------|
| `CsgFacade` | `CsharpPrj/Protocol/CSG/CSG.Unified/CsgFacade.cs` | ✅ 已存在 |
| `CanonicalForm` | `CsharpPrj/Protocol/CSG/CSG.Unified/CanonicalForm/CanonicalForm.cs` | ✅ 已存在 |
| `CanonicalFormBuilder` | `CsharpPrj/Protocol/CSG/CSG.Unified/CanonicalFormBuilder.cs` | ✅ 已存在 |
| `CanonicalNode` / `CanonicalEdge` | `CsharpPrj/Protocol/CSG/CSG.Unified/CanonicalForm/` | ✅ 已存在 |
| `IConfiguration` | `CsharpPrj/Protocol/CSG/CSG.Unified/Contracts/IConfiguration.cs` | ✅ 已存在 |
| `TransformEngine` | `CsharpPrj/Protocol/CSG/CSG.Unified/TransformEngine.cs` | ✅ 已存在 |
| `AdapterRegistry` | `CsharpPrj/Protocol/CSG/CSG.Unified/AdapterRegistry.cs` | ✅ 已存在 |
| `ValidationEngine` | `CsharpPrj/Protocol/CSG/CSG.Unified/ValidationEngine.cs` | ✅ 已存在 |
| `PlanOptimizer` | `CsharpPrj/Protocol/CSG/CSG.Unified/PlanOptimizer.cs` | ✅ 已存在 |

### 2.2 需要新建的文件

所有新建文件位于 `CsharpPrj/Operation/FrontEndDriven/` 下：

| 文件 | 路径 | 说明 |
|------|------|------|
| `UIProtocol.cs` | `FrontEndDriven/Protocol/UIProtocol.cs` | 协议常量定义 |
| `WinFormsUIAdapter.cs` | `FrontEndDriven/WinForms/Adapter/WinFormsUIAdapter.cs` | WinForms 适配器（实现 IConfiguration） |
| `UIReconciler.cs` | `FrontEndDriven/RenderPipeline/UIReconciler.cs` | 差异比较引擎 |
| `WinFormsEventAdapter.cs` | `FrontEndDriven/WinForms/Adapter/WinFormsEventAdapter.cs` | 事件回传适配器 |
| `EcsEventSystem.cs` | `FrontEndDriven/ControlInteraction/EventBinding/Systems/EcsEventSystem.cs` | 事件回传 System |

### 2.3 需要修改的文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `LayoutSystem.cs` | 重构 | 输出 CanonicalForm 替代直接操作控件 |
| `StyleSystem.cs` | 重构 | 输出 CanonicalForm 替代直接操作控件 |
| `EventBindingSystem.cs` | 重构 | 输出 CanonicalForm 替代直接操作控件 |
| `SplitContainerSystem.cs` | 重构 | 输出 CanonicalForm 替代直接操作控件 |
| `ComponentDataIntegration.cs` | 重构 | 通过 CanonicalForm 差异比较替代反射 |
| `EcsRenderPipeline.cs` | 重构 | 集成 CsgFacade，管线执行输出 CanonicalForm |
| `FrontEndDriven.csproj` | 修改 | 添加 CSG.Unified 项目引用 |

### 2.4 需要删除/废弃的文件

| 文件 | 说明 |
|------|------|
| `IWinFormsControlAdapter.cs` | 标记为 `[Obsolete]`，迁移完成后删除 |
| `WinFormsControlAdapter.cs` | 标记为 `[Obsolete]`，迁移完成后删除 |

---

## 三、详细实施步骤

### 阶段 1：协议层基础设施

#### 步骤 1.1：创建 UIProtocol.cs

**文件**：`CsharpPrj/Operation/FrontEndDriven/Protocol/UIProtocol.cs`  
**依赖**：无（纯常量定义）  
**风险**：低

**内容**：
- 节点类型常量（`NodeType_Control`, `NodeType_Container`, `NodeType_Window`）
- 边类型常量（`EdgeType_Contains`, `EdgeType_Event`, `EdgeType_Value`）
- 节点属性键常量（`Prop_ControlType`, `Prop_Bounds`, `Prop_Text`, `Prop_Enabled`, `Prop_Visible`, `Prop_Font`, `Prop_ForeColor`, `Prop_BackColor`, `Prop_Value`, `Prop_EventName`, `Prop_FunctionName`）

**验收**：
- [ ] 编译通过
- [ ] 所有常量命名与适配分析文档一致

---

#### 步骤 1.2：创建 WinFormsUIAdapter.cs

**文件**：`CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/WinFormsUIAdapter.cs`  
**依赖**：`UIProtocol.cs`, `IControlMap`, `CanonicalForm`, `CanonicalFormBuilder`, `IConfiguration`  
**风险**：中（需要与现有 ControlPool 对接）

**内容**：
- 实现 `IConfiguration` 接口
- `ConfigType` → `"WinForms.UI"`
- `SchemaVersion` → `"1.0"`
- `ToCanonical()` — 遍历 ControlPool 中所有控件，生成当前状态 CanonicalForm
- `ApplyCanonical(CanonicalForm targetForm)` — 比较当前状态和目标状态，差异更新
- `CompareForms(CanonicalForm current, CanonicalForm target)` — 属性级差异比较
- `ApplyChange(UIChange change)` — 应用单个变更到控件

**设计要点**：
- 通过 `IControlMap`（即 ControlPool）获取 Entity↔Control 映射
- `ToCanonical()` 读取控件的 Bounds/Text/Enabled/Visible 等属性
- `ApplyCanonical()` 内部调用 `ToCanonical()` 获取当前状态，然后比较差异
- 差异比较支持：节点新增、节点删除、属性更新

**验收**：
- [ ] 编译通过
- [ ] `ToCanonical()` 能正确生成当前控件树的 CanonicalForm
- [ ] `ApplyCanonical()` 能正确执行差异更新

---

#### 步骤 1.3：创建 UIReconciler.cs

**文件**：`CsharpPrj/Operation/FrontEndDriven/RenderPipeline/UIReconciler.cs`  
**依赖**：`CsgFacade`, `CanonicalForm`  
**风险**：低

**内容**：
- 维护 `_lastAppliedState`（上一次应用的 CanonicalForm）
- `Apply(CanonicalForm targetState)` — 比较 `_lastAppliedState` 和 `targetState` 的差异
- 通过 `CsgFacade.ValidateConsistency()` 验证一致性
- 从验证结果生成操作序列（`GenerateOperations`）
- 通过 `CsgFacade.Execute()` 执行操作

**设计要点**：
- 首次调用时 `_lastAppliedState` 为空，直接应用全部
- 后续调用只执行差异部分
- 支持三种差异类型：`NodeAdded`, `NodeRemoved`, `PropertyChanged`

**验收**：
- [ ] 编译通过
- [ ] 首次 Apply 全量应用
- [ ] 后续 Apply 只执行差异部分

---

#### 步骤 1.4：创建 WinFormsEventAdapter.cs

**文件**：`CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/WinFormsEventAdapter.cs`  
**依赖**：`UIProtocol.cs`, `CanonicalForm`, `CanonicalFormBuilder`  
**风险**：低

**内容**：
- 将 WinForms 控件事件转换为 CanonicalForm 边
- `WireEvents(Control control, CanonicalFormBuilder builder)` — 为控件订阅事件，生成事件边
- 支持的事件类型：Click, TextChanged, SelectedIndexChanged, CheckedChanged 等

**设计要点**：
- 事件回传的 CanonicalForm 通过 `CsgFacade.Execute()` 发布
- 每个事件绑定生成一条 `EdgeType_Event` 类型的边
- 边的 properties 包含 `eventName` 和 `functionName`

**验收**：
- [ ] 编译通过
- [ ] 控件事件能正确转换为 CanonicalForm 边

---

### 阶段 2：ECS System 重构

#### 步骤 2.1：重构 LayoutSystem

**文件**：`CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/Systems/LayoutSystem.cs`  
**依赖**：`CsgFacade`, `CanonicalFormBuilder`, `UIProtocol`  
**风险**：高（核心 System，影响布局）

**变化**：
```
当前：计算 → Adapter.SetBounds(control, ...)
目标：计算 → builder.AddNode(...) → _csg.Execute(batch => batch.AddStep(...))
```

**具体修改**：
1. 构造函数新增 `CsgFacade _csg` 参数
2. `Update()` 方法中：
   - 保留比例→像素计算逻辑
   - 移除 `Adapter.SetBounds()` 调用
   - 改为构建 CanonicalForm 节点，通过 `_csg.Execute()` 发布
3. 保留 `PendingLayoutComponent` → `LayoutAppliedComponent` 的状态标记逻辑

**向后兼容**：
- 新增 `LayoutSystem(EntityManager, ControlPool, IWinFormsControlAdapter, CsgFacade)` 构造函数
- 旧构造函数标记为 `[Obsolete]`，内部委托给新构造函数（传入 null CsgFacade 时保持旧行为）

**验收**：
- [ ] 编译通过
- [ ] 布局计算逻辑不变
- [ ] CanonicalForm 输出包含正确的 Bounds 信息
- [ ] 向后兼容模式可用

---

#### 步骤 2.2：重构 StyleSystem

**文件**：`CsharpPrj/Operation/FrontEndDriven/ControlRender/Style/Systems/StyleSystem.cs`  
**依赖**：`CsgFacade`, `CanonicalFormBuilder`, `UIProtocol`  
**风险**：高（核心 System，影响样式）

**变化**：
```
当前：合并样式 → Adapter.SetFont/SetForeColor/SetBackColor(...)
目标：合并样式 → builder.AddNode(...) → _csg.Execute(...)
```

**具体修改**：
1. 构造函数新增 `CsgFacade _csg` 参数
2. `Update()` 方法中：
   - 保留样式合并逻辑
   - 移除 `ApplyStyle()` 中的 `Adapter.SetFont/SetForeColor/SetBackColor` 调用
   - 改为构建 CanonicalForm 节点，通过 `_csg.Execute()` 发布
3. `ApplyStyle()` 方法改为构建 CanonicalForm 属性

**验收**：
- [ ] 编译通过
- [ ] 样式合并逻辑不变
- [ ] CanonicalForm 输出包含正确的样式信息

---

#### 步骤 2.3：重构 EventBindingSystem

**文件**：`CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/Systems/EventBindingSystem.cs`  
**依赖**：`CsgFacade`, `CanonicalFormBuilder`, `UIProtocol`  
**风险**：中

**变化**：
```
当前：读取 EventBindingsComponent → _bindingDelegate(control, ...)
目标：读取 EventBindingsComponent → builder.AddEdge(...) → _csg.Execute(...)
```

**具体修改**：
1. 构造函数新增 `CsgFacade _csg` 参数
2. `Update()` 方法中：
   - 保留事件绑定读取逻辑
   - 移除 `BindEvent()` 中的 `_bindingDelegate` 直接调用
   - 改为构建 CanonicalForm 边（`EdgeType_Event`），通过 `_csg.Execute()` 发布
3. 事件绑定信息存储在边的 properties 中（`eventName`, `functionName`）

**验收**：
- [ ] 编译通过
- [ ] 事件绑定信息正确输出到 CanonicalForm
- [ ] 边类型和属性符合 UIProtocol 定义

---

#### 步骤 2.4：重构 SplitContainerSystem

**文件**：`CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/Systems/SplitContainerSystem.cs`  
**依赖**：`CsgFacade`, `CanonicalFormBuilder`, `UIProtocol`  
**风险**：中

**变化**：
```
当前：计算 SplitterDistance → Adapter.SetSplitterDistance(...)
目标：计算 SplitterDistance → builder.AddNode(...) → _csg.Execute(...)
```

**具体修改**：
1. 构造函数新增 `CsgFacade _csg` 参数
2. `Update()` 方法中：
   - 保留 SplitterDistance 计算逻辑
   - 移除 `Adapter.SetSplitterDistance()` 调用
   - 改为构建 CanonicalForm 节点，通过 `_csg.Execute()` 发布

**验收**：
- [ ] 编译通过
- [ ] SplitterDistance 计算逻辑不变
- [ ] CanonicalForm 输出正确

---

### 阶段 3：ComponentDataBridge 替换

#### 步骤 3.1：创建 EcsEventSystem

**文件**：`CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/Systems/EcsEventSystem.cs`  
**依赖**：`CsgFacade`, `CanonicalForm`, `UIProtocol`  
**风险**：低

**内容**：
- 订阅 `CsgFacade` 的 CanonicalForm 变更事件
- 将 CanonicalForm 中的事件边转换为 ECS Component 更新
- 实现 UI → ECS 的回传通道

**设计要点**：
- 监听 `EdgeType_Event` 类型的边
- 根据 `eventName` 和 `functionName` 更新对应的 ECS Component
- 通过 `EntityManager` 更新 Entity 状态

**验收**：
- [ ] 编译通过
- [ ] UI 事件能正确回传到 ECS

---

#### 步骤 3.2：重构 ComponentDataBridge

**文件**：`CsharpPrj/Operation/FrontEndDriven/ComponentDataIntegration/ComponentDataIntegration.cs`  
**依赖**：`WinFormsUIAdapter`, `UIReconciler`  
**风险**：高（涉及反射替换）

**变化**：
```
当前：订阅 ComponentUpdated → 反射读取属性 → Adapter/ValueWriter 设置控件
目标：订阅 ComponentUpdated → 构建 CanonicalForm → WinFormsUIAdapter.ApplyCanonical() 差异更新
```

**具体修改**：
1. 注入 `WinFormsUIAdapter` 和 `UIReconciler` 替代 `IWinFormsControlAdapter`
2. `OnComponentUpdated()` 方法中：
   - 移除反射读取属性的逻辑
   - 改为构建增量 CanonicalForm（只包含变更的节点）
   - 通过 `WinFormsUIAdapter.ApplyCanonical()` 执行差异更新
3. 移除 `_syncHandlers` 字典和所有 `Sync*Value` 方法
4. 移除 `RegisterSyncHandler()` 和 `TryLazyRegister()` 方法

**验收**：
- [ ] 编译通过
- [ ] 组件值变更能正确同步到控件
- [ ] 不再使用反射读取属性

---

### 阶段 4：管线集成与清理

#### 步骤 4.1：重构 EcsRenderPipeline

**文件**：`CsharpPrj/Operation/FrontEndDriven/RenderPipeline/EcsRenderPipeline.cs`  
**依赖**：所有阶段 1-3 的组件  
**风险**：高（管线入口，影响全局）

**变化**：
```
当前：_layoutSystem.Update() → _styleSystem.Update() → ...（直接操作控件）
目标：_layoutSystem.Update() → _styleSystem.Update() → ...（输出 CanonicalForm）
      → _csg.Execute() → WinFormsUIAdapter.ApplyCanonical()（差异更新）
```

**具体修改**：
1. 构造函数新增 `CsgFacade _csg` 参数
2. `Initialize()` 方法中：
   - 创建 `CsgFacade` 实例
   - 创建 `WinFormsUIAdapter` 实例
   - 创建 `UIReconciler` 实例
   - 将 `CsgFacade` 注入到所有 ECS System
3. `ExecutePipeline()` 方法中：
   - 保留 System 执行顺序
   - 在 System 执行后，调用 `UIReconciler.Apply()` 执行差异更新
4. 新增 `GetOutput()` 方法 — 获取 ECS 输出的 CanonicalForm（用于调试）

**验收**：
- [ ] 编译通过
- [ ] 管线执行后控件正确渲染
- [ ] `GetOutput()` 返回正确的 CanonicalForm
- [ ] 向后兼容模式可用

---

#### 步骤 4.2：更新项目引用

**文件**：`CsharpPrj/Operation/FrontEndDriven/FrontEndDriven.csproj`  
**风险**：低

**修改**：
- 添加 `CSG.Unified` 的项目引用：
  ```xml
  <ProjectReference Include="..\..\Protocol\CSG\CSG.Unified\CSG.Unified.csproj" />
  ```

**验收**：
- [ ] 编译通过
- [ ] 无循环引用

---

#### 步骤 4.3：标记旧接口为废弃

**文件**：
- `CsharpPrj/Operation/FrontEndDriven/Shared/Abstractions/IWinFormsControlAdapter.cs`
- `CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/WinFormsControlAdapter.cs`

**修改**：
- 在接口和类上添加 `[Obsolete("请使用 WinFormsUIAdapter (IConfiguration) 替代")]` 属性
- 不立即删除，保持向后兼容

**验收**：
- [ ] 编译通过（有 Obsolete 警告）
- [ ] 旧代码仍能工作

---

### 阶段 5：文档与知识记录

#### 步骤 5.1：更新架构文档

**文件**：`CsharpPrj/Structuring/Architecture.md`  
**修改**：更新 FrontEndDriven 的架构描述，反映协议层的引入

#### 步骤 5.2：记录知识图谱

使用 `create_entities` 和 `create_relations` 记录新的架构知识

---

## 四、文件清单

### 新建文件（4 个）

| # | 文件路径 | 阶段 |
|---|---------|------|
| 1 | `CsharpPrj/Operation/FrontEndDriven/Protocol/UIProtocol.cs` | 1 |
| 2 | `CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/WinFormsUIAdapter.cs` | 1 |
| 3 | `CsharpPrj/Operation/FrontEndDriven/RenderPipeline/UIReconciler.cs` | 1 |
| 4 | `CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/WinFormsEventAdapter.cs` | 1 |
| 5 | `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/Systems/EcsEventSystem.cs` | 3 |

### 修改文件（7 个）

| # | 文件路径 | 阶段 |
|---|---------|------|
| 1 | `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/Systems/LayoutSystem.cs` | 2 |
| 2 | `CsharpPrj/Operation/FrontEndDriven/ControlRender/Style/Systems/StyleSystem.cs` | 2 |
| 3 | `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/Systems/EventBindingSystem.cs` | 2 |
| 4 | `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/Systems/SplitContainerSystem.cs` | 2 |
| 5 | `CsharpPrj/Operation/FrontEndDriven/ComponentDataIntegration/ComponentDataIntegration.cs` | 3 |
| 6 | `CsharpPrj/Operation/FrontEndDriven/RenderPipeline/EcsRenderPipeline.cs` | 4 |
| 7 | `CsharpPrj/Operation/FrontEndDriven/FrontEndDriven.csproj` | 4 |

### 废弃文件（2 个）

| # | 文件路径 | 阶段 |
|---|---------|------|
| 1 | `CsharpPrj/Operation/FrontEndDriven/Shared/Abstractions/IWinFormsControlAdapter.cs` | 4 |
| 2 | `CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/WinFormsControlAdapter.cs` | 4 |

---

## 五、风险与缓解措施

| 风险 | 等级 | 说明 | 缓解措施 |
|------|------|------|---------|
| **CSG.Unified API 不满足需求** | 中 | CanonicalForm 的 Execute 批处理可能不支持 UI 更新场景 | 阶段 1 先做 POC 验证核心流程 |
| **性能退化** | 低 | CanonicalForm 序列化/反序列化带来额外开销 | 差异比较减少实际操作次数，整体性能可能提升 |
| **迁移中断** | 中 | 重构期间现有功能可能被破坏 | 逐步迁移，每个 System 保持向后兼容构造函数 |
| **ComponentDataBridge 反射替换** | 高 | 反射逻辑分散在多个 Sync*Value 方法中 | 先做 WinFormsUIAdapter 的 ApplyCanonical，验证可行后再替换 |
| **管线执行顺序变化** | 低 | CanonicalForm 的收集和执行分离可能改变时序 | 保持 System 执行顺序不变，只在最后统一 Apply |

---

## 六、验收标准

### 6.1 功能验收

- [ ] 现有 UI 渲染功能不变（回归测试通过）
- [ ] ECS System 输出 CanonicalForm，不再直接操作控件
- [ ] WinFormsUIAdapter 能正确解释 CanonicalForm 并更新控件
- [ ] UIReconciler 能正确执行差异更新（只更新变化的部分）
- [ ] ComponentDataBridge 不再使用反射同步值

### 6.2 架构验收

- [ ] ECS 层不依赖 WinForms（System.Windows.Forms 引用消除）
- [ ] CanonicalForm 可序列化为 JSON
- [ ] ECS 管线可脱离 WinForms 环境独立测试
- [ ] 旧接口标记为 `[Obsolete]` 但保持可用

### 6.3 测试验收

- [ ] LayoutSystem 的单元测试（验证 CanonicalForm 输出）
- [ ] StyleSystem 的单元测试（验证 CanonicalForm 输出）
- [ ] WinFormsUIAdapter 的单元测试（验证 ToCanonical / ApplyCanonical）
- [ ] UIReconciler 的单元测试（验证差异比较）
- [ ] 集成测试（验证完整管线流程）

---

## 附录：实施顺序依赖图

```
步骤 1.1 (UIProtocol.cs)
  │
  ▼
步骤 1.2 (WinFormsUIAdapter.cs) ─── 依赖 1.1
  │
  ├── 步骤 1.3 (UIReconciler.cs) ─── 依赖 CsgFacade
  │
  └── 步骤 1.4 (WinFormsEventAdapter.cs) ─── 依赖 1.1
  │
  ▼
步骤 2.1-2.4 (ECS System 重构) ─── 依赖 1.1, CsgFacade
  │
  ▼
步骤 3.1 (EcsEventSystem.cs) ─── 依赖 1.1, CsgFacade
  │
  ▼
步骤 3.2 (ComponentDataBridge 重构) ─── 依赖 1.2, 1.3
  │
  ▼
步骤 4.1 (EcsRenderPipeline 重构) ─── 依赖 所有前置步骤
  │
  ▼
步骤 4.2 (csproj 更新)
  │
  ▼
步骤 4.3 (标记废弃)
```
