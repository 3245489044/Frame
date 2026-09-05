# FileDriven → FrontEndDriven 旧体系迁移方案

> **版本：** v1.0
> **更新日期：** 2026-07-19
> **维护者：** WorkFlowPrj Team
> **前置文档：** [`ECS_Based_Control_Refactoring.md`](ECS_Based_Control_Refactoring.md)（ECS 重构方案）
> **核心原则：** FrontEndDriven 承载核心逻辑，FileDriven 只负责调用

---

## 目录

1. [迁移目标与原则](#1-迁移目标与原则)
2. [现状全景图](#2-现状全景图)
3. [分阶段迁移计划](#3-分阶段迁移计划)
   - [阶段一：安全清理（已委托代码）](#阶段一安全清理已委托代码)
   - [阶段二：核心逻辑迁移（Pipeline 层）](#阶段二核心逻辑迁移pipeline-层)
   - [阶段三：FileDriven.UI 精简](#阶段三filedrivenui-精简)
   - [阶段四：FileDriven.Execution 精简](#阶段四filedrivenexecution-精简)
4. [依赖关系图](#4-依赖关系图)
5. [验证策略](#5-验证策略)
6. [附录：文件清单](#6-附录文件清单)

---

## 1. 迁移目标与原则

### 1.1 核心目标

```
迁移前：FileDriven 承载核心逻辑 + UI 调用
         ↓
迁移后：FrontEndDriven 承载核心逻辑
        FileDriven 只负责调用和编排
```

### 1.2 具体目标

| 目标 | 说明 |
|------|------|
| **消除重复实现** | FileDriven 中已委托到 FrontEndDriven 的代码直接删除 |
| **核心逻辑下沉** | Pipeline、EventBinding、ControlFinder 等核心逻辑迁移到 FrontEndDriven |
| **接口统一** | FileDriven 通过接口调用 FrontEndDriven，不直接依赖实现 |
| **保持向后兼容** | 迁移过程中不破坏现有调用方 |
| **可验证** | 每个迁移步骤都有明确的验证策略 |

### 1.3 设计原则

```
FrontEndDriven（核心层）：
  ├── ECS 集成（FrontEndEcsIntegration）
  ├── 容器管理（WdUIContainerManager）
  ├── 控件查找（ControlRegistryAwareFinder）
  ├── 事件管理（EventSubscriptionManager）
  ├── 控件服务（WinFormsControlService）
  ├── 配置加载（ConfigLoader）
  └── 日志系统（FrontEndDrivenLog）

FileDriven（调用层）：
  ├── MainForm（UI 入口）
  ├── PipelineEntry（管线入口）
  ├── FileDrivenWdUIBridge（WdUI 调用桥接）
  ├── EcsPipelineOrchestrator（ECS 管线编排 → 委托到 FrontEndDriven）
  └── UICommandManager（UI 命令 → 委托到 FrontEndDriven）
```

---

## 2. 现状全景图

### 2.1 FrontEndDriven 现有能力

| 模块 | 文件 | 能力 |
|------|------|------|
| **ECS 集成** | `ECS/FrontEndEcsIntegration.cs` | EcsWorld 生命周期、System 管线编排、事件桥接 |
| **ECS 桥接** | `ECS/EcsControlRegistryAdapter.cs` | IControlRegistry 桥接 |
| **ECS 桥接** | `ECS/EcsEventBridge.cs` | 事件绑定桥接 |
| **ECS 桥接** | `ECS/EcsLayoutStyleBridge.cs` | 布局/样式同步桥接 |
| **容器管理** | `Implementation/WdUIContainerManager.cs` | 容器创建/查找/跟踪/释放 |
| **控件查找** | `Implementation/ControlRegistryAwareFinder.cs` | 控件查找（替代 WinFormsControlFinder） |
| **事件管理** | `Implementation/EventSubscriptionManager.cs` | 事件订阅管理 |
| **控件服务** | `Implementation/WinFormsControlService.cs` | 控件值读写 |
| **控件服务** | `Implementation/WinFormsControlValueWriter.cs` | 控件值写入 |
| **控件服务** | `Implementation/CompositeValueWriter.cs` | 复合值写入 |
| **控件服务** | `Implementation/UnifiedControlService.cs` | 统一控件服务 |
| **配置加载** | `Loading/ConfigLoader.cs` | 配置加载 |
| **日志** | `Diagnostics/FrontEndDrivenLog.cs` | 统一日志入口 |
| **工具** | `Utils/CommandHandler.cs` | 命令处理 |
| **工具** | `Utils/UILogger.cs` | UI 日志工具 |
| **工具** | `Utils/FormInitializer.cs` | Form 初始化 |
| **工具** | `Utils/FormDisposer.cs` | Form 释放 |
| **WinForms** | `WinForms/Implementation/ControlRegistry.cs` | ControlRegistry 实现 |
| **WinForms** | `WinForms/EventBinding/UIEventBinder.cs` | 事件绑定 |
| **WinForms** | `WinForms/Registration/WinFormsEventRegistrar.cs` | 事件注册 |
| **WinForms** | `WinForms/Loading/ConfigLoader.cs` | WinForms 配置加载 |

### 2.2 FileDriven 待处理文件清单

#### A 类：已委托但未删除（可直接安全删除）

| 文件 | 委托目标 | 风险 |
|------|---------|------|
| `FileDriven.Pipeline/Core/WinFormsControlFinder.cs` | `FrontEndDriven.Implementation.ControlRegistryAwareFinder` | 已标记 `[Obsolete]`，无引用 |
| `FileDriven.UI/WinForms/UIControlAbstractions.cs` | `FrontEndDriven.WinForms.Implementation.ControlRegistry` | 仅接口转发，无独立逻辑 |

#### B 类：与 FrontEndDriven 重复（需委托后删除）

| 文件 | 重复目标 | 策略 |
|------|---------|------|
| `FileDriven.UI/ECS/EcsPipelineOrchestrator.cs` | `FrontEndDriven.ECS.FrontEndEcsIntegration` | 委托到 FrontEndEcsIntegration |
| `FileDriven.UI/WinForms/CommandLayer.cs` 中的 `ControlValueHelper` | `FrontEndDriven.Implementation.WinFormsControlValueWriter` | 委托到 FrontEndDriven |
| `FileDriven.UI/WinForms/CommandLayer.cs` 中的 `VirtualControlStore` | `FrontEndDriven.Implementation` | 委托到 FrontEndDriven |

#### C 类：被 PipelineEntry 引用（需先重构 PipelineEntry）

| 文件 | 被引用方式 | 策略 |
|------|----------|------|
| `FileDriven.Pipeline/Core/EventBindingInfo.cs` | `PipelineEntry.cs` 直接引用 | 重构 PipelineEntry → 使用 TConfig 接口 |
| `FileDriven.Pipeline/Core/LoadedConfigInfo.cs` | `PipelineEntry.cs` 直接引用 | 重构 PipelineEntry → 使用 FrontEndDriven 版本 |
| `FileDriven.Pipeline/Contracts/ControlTreeWalker.cs` | `PipelineEntry.cs` 直接引用 | 重构 PipelineEntry → 使用 Share 版本 |
| `FileDriven.Pipeline/Contracts/ControlConfigTarget.cs` | `ContractValidator.cs` 引用 | 保持不动（Schema 校验框架） |
| `FileDriven.Pipeline/Contracts/ContractBuilder.cs` | `ContractRegistry.cs` 引用 | 保持不动 |
| `FileDriven.Pipeline/Contracts/ContractRegistry.cs` | `ContractValidator.cs` 引用 | 保持不动 |
| `FileDriven.Pipeline/Contracts/ContractValidator.cs` | 外部调用 | 保持不动 |

#### D 类：独立工具类（保持不动）

| 文件 | 说明 |
|------|------|
| `FileDriven.UI/WinForms/UIInspector.cs` | UI 检查器（诊断工具，与 ECS 无关） |
| `FileDriven.UI/WinForms/LayoutJsonExporter.cs` | JSON 布局导出（诊断工具，与 ECS 无关） |
| `FileDriven.UI/WinForms/WinFormsControlMetadataStore.cs` | IControlMetadataStore 实现（元数据存储） |
| `FileDriven.Execution/EventPipeline/EventHub.cs` | IEventHub 实现（事件中心） |
| `FileDriven.Execution/EventPipeline/LoadedConfigInfo.cs` | 配置信息（与 Pipeline 版本重复但独立） |

---

## 3. 分阶段迁移计划

### 阶段一：安全清理（已委托代码）

> **目标：** 删除已标记 `[Obsolete]` 且无引用的代码
> **风险：** 低
> **验证：** 编译通过 + 现有测试通过

#### 步骤 1.1：删除 `WinFormsControlFinder.cs`

**文件：** `FileDriven/FileDriven.Pipeline/Core/WinFormsControlFinder.cs`

```diff
- 删除整个文件（82 行）
```

**验证：**
- 搜索 `FileDriven.Pipeline.Core.WinFormsControlFinder` 确认无引用
- 搜索 `using FileDriven.Pipeline.Core` 确认无其他引用
- 编译 `FileDriven.Pipeline.csproj` 通过

#### 步骤 1.2：删除 `UIControlAbstractions.cs`

**文件：** `FileDriven/FileDriven.UI/WinForms/UIControlAbstractions.cs`

```diff
- 删除整个文件（46 行）
```

**验证：**
- 搜索 `FileDriven.UI.WinForms.Abstractions.IControlRegistry` 确认无引用
- 搜索 `FileDriven.UI.WinForms.Abstractions.ControlRegistry` 确认无引用
- 编译 `FileDriven.UI.csproj` 通过

---

### 阶段二：核心逻辑迁移（Pipeline 层）

> **目标：** 重构 `PipelineEntry.cs`，消除对旧文件的直接引用
> **风险：** 中
> **验证：** 编译通过 + 集成测试通过

#### 步骤 2.1：分析 `PipelineEntry.cs` 的引用关系

**文件：** `FileDriven/FileDriven.Pipeline/PipelineEntry.cs`

需要识别以下引用：
1. `FileDriven.Pipeline.Core.LoadedConfigInfo` — 配置信息类
2. `FileDriven.Pipeline.Core.EventBindingInfo` — 事件绑定信息类
3. `FileDriven.Pipeline.Contracts.ControlTreeWalker` — 控件树遍历

#### 步骤 2.2：重构 `LoadedConfigInfo` 引用

**策略：** 将 `PipelineEntry.cs` 中对 `FileDriven.Pipeline.Core.LoadedConfigInfo` 的引用改为使用 `FileDriven.Execution.EventPipeline.LoadedConfigInfo`（如果功能相同）或迁移到 FrontEndDriven。

**具体操作：**
1. 检查两个 `LoadedConfigInfo` 的差异
2. 如果功能相同，统一使用 `FileDriven.Execution.EventPipeline.LoadedConfigInfo`
3. 更新 `PipelineEntry.cs` 的 using 语句

#### 步骤 2.3：重构 `EventBindingInfo` 引用

**策略：** `EventBindingInfo` 实现 `TConfig.IEventBindingInfo` 接口，将 `PipelineEntry.cs` 改为使用接口类型。

**具体操作：**
1. 将 `PipelineEntry.cs` 中直接引用 `EventBindingInfo` 的地方改为使用 `TConfig.IEventBindingInfo`
2. 删除 `FileDriven.Pipeline/Core/EventBindingInfo.cs`

#### 步骤 2.4：重构 `ControlTreeWalker` 引用

**策略：** `ControlTreeWalker` 已委托到 `Share.EventBindingOrchestrator.Utils.ControlTreeWalker`，更新引用。

**具体操作：**
1. 将 `PipelineEntry.cs` 中 `using FileDriven.Pipeline.Contracts` 改为 `using Share.EventBindingOrchestrator.Utils`
2. 删除 `FileDriven.Pipeline/Contracts/ControlTreeWalker.cs`

#### 步骤 2.5：删除已无引用的文件

**删除文件：**
- `FileDriven.Pipeline/Core/EventBindingInfo.cs`
- `FileDriven.Pipeline/Core/LoadedConfigInfo.cs`（如果不再被引用）
- `FileDriven.Pipeline/Contracts/ControlTreeWalker.cs`

---

### 阶段三：FileDriven.UI 精简

> **目标：** 将 FileDriven.UI 中的核心逻辑委托到 FrontEndDriven
> **风险：** 中
> **验证：** 编译通过 + GUI 功能测试通过

#### 步骤 3.1：委托 `EcsPipelineOrchestrator` 到 `FrontEndEcsIntegration`

**文件：** `FileDriven/FileDriven.UI/ECS/EcsPipelineOrchestrator.cs`

**分析：**
- `EcsPipelineOrchestrator` 与 `FrontEndEcsIntegration` 功能高度重叠
- 两者都管理 ECS System 管线（ConfigToEntity → ControlCreate → Layout → Style → EventBinding → ControlSync）
- `EcsPipelineOrchestrator` 多了 `controlFactoryCallback` 和 `eventBindCallback` 回调参数

**策略：** 将 `EcsPipelineOrchestrator` 改为委托到 `FrontEndEcsIntegration`

```csharp
// 重构后：EcsPipelineOrchestrator 委托到 FrontEndEcsIntegration
public class EcsPipelineOrchestrator : IDisposable
{
    private readonly FrontEndEcsIntegration _integration;
    
    public EcsPipelineOrchestrator(
        IECSStorage ecsStorage,
        IStorageRegistry storageRegistry,
        Func<string, string, Control>? controlFactoryCallback = null,
        Action<string, string, object?>? eventBindCallback = null)
    {
        _integration = new FrontEndEcsIntegration(ecsStorage, storageRegistry);
        _integration.Initialize();
        
        // 连接回调
        if (controlFactoryCallback != null)
            _integration.ControlCreated += (entity, type, name) => { ... };
        if (eventBindCallback != null)
            _integration.EventBindRequested += (entity, eventName, funcName, parameters, outputTarget) => { ... };
    }
    
    public void ExecutePipeline(UIConfiguration config) => _integration.LoadConfiguration(config);
    public void OnWindowResized() => _integration.RecalculateLayout();
    public void Clear() => _integration.Clear();
    public void Dispose() => _integration.Dispose();
}
```

#### 步骤 3.2：委托 `CommandLayer` 中的工具类

**文件：** `FileDriven/FileDriven.UI/WinForms/CommandLayer.cs`

**分析：**
- `ControlValueHelper` 实现 `IControlValueAccessor` 接口
- `FrontEndDriven.Implementation.WinFormsControlValueWriter` 已有相同功能

**策略：** 将 `ControlValueHelper` 改为委托到 `WinFormsControlValueWriter`

#### 步骤 3.3：精简 `FileDrivenWdUIBridge`

**文件：** `FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs`

**分析：**
- 生命周期管理（Initialize/Shutdown）→ 已委托到 `WdUIContainerManager`
- 容器管理（CreateContainer/GetContainer 等）→ 已委托到 `WdUIContainerManager`
- `FindAndBindContainers` → 仍有实际业务逻辑（fallback 诊断）
- `FindConfigSelectorControls` → 仍有实际业务逻辑
- `ClearSideContainer` → 仍有实际业务逻辑

**策略：** 保持不动（仍有实际业务逻辑，不属于重复代码）

---

### 阶段四：FileDriven.Execution 精简

> **目标：** 清理 Execution 层中已迁移的代码
> **风险：** 低-中
> **验证：** 编译通过 + 功能测试通过

#### 步骤 4.1：分析 `EventHub.cs` 的引用

**文件：** `FileDriven/FileDriven.Execution/EventPipeline/EventHub.cs`

**分析：**
- 实现 `IEventHub` 接口
- 被 `MainForm.cs` 通过接口引用
- 被 `FileDrivenGuiInitializerOrchestrator.cs` 引用

**策略：** 保持不动（活跃使用，且是 IEventHub 的唯一实现）

#### 步骤 4.2：分析 `LoadedConfigInfo.cs`（Execution 版本）

**文件：** `FileDriven/FileDriven.Execution/EventPipeline/LoadedConfigInfo.cs`

**分析：**
- 与 `FileDriven.Pipeline/Core/LoadedConfigInfo.cs` 功能重复
- 被 `EventHub.cs` 引用

**策略：** 阶段二完成后，如果两个 `LoadedConfigInfo` 功能相同，统一保留 Execution 版本，删除 Pipeline 版本

---

## 4. 依赖关系图

### 4.1 文件依赖关系

```
PipelineEntry.cs
  ├── FileDriven.Pipeline.Core.LoadedConfigInfo  ← 阶段二重构
  ├── FileDriven.Pipeline.Core.EventBindingInfo  ← 阶段二重构
  ├── FileDriven.Pipeline.Contracts.ControlTreeWalker  ← 阶段二重构
  └── FileDriven.Pipeline.Contracts.ContractBuilder  ← 保持不动

MainForm.cs
  ├── IEventHub (由 EventHub.cs 实现)  ← 保持不动
  ├── IWinFormsEventRegistrar (由 WinFormsEventRegistrar 实现)  ← 保持不动
  ├── IUIEventBinder (由 UIEventBinder 实现)  ← 保持不动
  └── FileDrivenWdUIBridge  ← 保持不动

EcsPipelineOrchestrator.cs
  └── FrontEndEcsIntegration  ← 阶段三委托
```

### 4.2 迁移顺序

```
阶段一（安全清理）
  └── 删除 WinFormsControlFinder.cs
  └── 删除 UIControlAbstractions.cs

阶段二（Pipeline 层重构）
  └── 重构 PipelineEntry.cs
      ├── LoadedConfigInfo → 统一使用 Execution 版本
      ├── EventBindingInfo → 使用 TConfig.IEventBindingInfo 接口
      └── ControlTreeWalker → 使用 Share 版本
  └── 删除已无引用的文件

阶段三（UI 层精简）
  └── EcsPipelineOrchestrator → 委托到 FrontEndEcsIntegration
  └── CommandLayer 工具类 → 委托到 FrontEndDriven

阶段四（Execution 层清理）
  └── 清理重复的 LoadedConfigInfo
```

---

## 5. 验证策略

### 5.1 编译验证

每个阶段完成后，执行编译验证：

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\FileDriven\FileDriven.sln"
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln"
```

### 5.2 测试验证

```bash
# ECS 相关测试
dotnet test "d:\Project\Program\WorkFlowPrj\CentralTest\CSharpTest\BasicMethod.Tests\ECS.Tests\ECS.Tests.csproj"

# 全量测试
dotnet test "d:\Project\Program\WorkFlowPrj\CentralTest\CSharpTest\BasicMethod.Tests\BasicMethod.Tests.csproj"
```

### 5.3 引用验证

每个文件删除前，使用搜索工具确认无引用：

```bash
# 搜索类名引用
# 例如：搜索 "WinFormsControlFinder" 确认无引用
```

### 5.4 功能验证

| 阶段 | 验证内容 |
|------|---------|
| 阶段一 | 编译通过，无警告 |
| 阶段二 | PipelineEntry 功能正常，配置加载/事件绑定不受影响 |
| 阶段三 | GUI 模式启动正常，控件创建/布局/样式/事件绑定正常 |
| 阶段四 | 编译通过，无警告 |

---

## 6. 附录：文件清单

### 6.1 待删除文件

| 阶段 | 文件路径 | 行数 | 说明 |
|------|---------|------|------|
| 一 | `FileDriven.Pipeline/Core/WinFormsControlFinder.cs` | 82 | 已标记 `[Obsolete]` |
| 一 | `FileDriven.UI/WinForms/UIControlAbstractions.cs` | 46 | 仅接口转发 |
| 二 | `FileDriven.Pipeline/Core/EventBindingInfo.cs` | 26 | 重构后删除 |
| 二 | `FileDriven.Pipeline/Core/LoadedConfigInfo.cs` | - | 重构后删除（如果不再被引用） |
| 二 | `FileDriven.Pipeline/Contracts/ControlTreeWalker.cs` | 100 | 重构后删除 |

### 6.2 待修改文件

| 阶段 | 文件路径 | 修改内容 |
|------|---------|---------|
| 二 | `FileDriven.Pipeline/PipelineEntry.cs` | 更新引用到新位置 |
| 三 | `FileDriven.UI/ECS/EcsPipelineOrchestrator.cs` | 委托到 FrontEndEcsIntegration |
| 三 | `FileDriven.UI/WinForms/CommandLayer.cs` | 委托到 FrontEndDriven |

### 6.3 保持不动的文件

| 文件路径 | 原因 |
|---------|------|
| `FileDriven.Pipeline/Contracts/ContractBuilder.cs` | Schema 校验框架核心 |
| `FileDriven.Pipeline/Contracts/ContractRegistry.cs` | Schema 校验框架核心 |
| `FileDriven.Pipeline/Contracts/ContractValidator.cs` | Schema 校验框架核心 |
| `FileDriven.Pipeline/Contracts/ControlConfigTarget.cs` | Schema 校验框架核心 |
| `FileDriven.UI/WinForms/UIInspector.cs` | 独立诊断工具 |
| `FileDriven.UI/WinForms/LayoutJsonExporter.cs` | 独立导出工具 |
| `FileDriven.UI/WinForms/WinFormsControlMetadataStore.cs` | 元数据存储实现 |
| `FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs` | 仍有实际业务逻辑 |
| `FileDriven.Execution/EventPipeline/EventHub.cs` | IEventHub 唯一实现 |
| `FileDriven.Execution/EventPipeline/LoadedConfigInfo.cs` | 活跃使用 |
| `FileDriven.UI/Core/MainForm.cs` | UI 主入口 |
| `FileDriven.UI/WinForms/CommandLayer.cs`（UICommandManager） | 活跃使用 |

---

## 附录：迁移进度追踪

### 阶段一：安全清理

- [ ] 1.1 删除 `WinFormsControlFinder.cs`
- [ ] 1.2 删除 `UIControlAbstractions.cs`
- [ ] 1.3 编译验证

### 阶段二：Pipeline 层重构

- [ ] 2.1 分析 `PipelineEntry.cs` 引用关系
- [ ] 2.2 重构 `LoadedConfigInfo` 引用
- [ ] 2.3 重构 `EventBindingInfo` 引用
- [ ] 2.4 重构 `ControlTreeWalker` 引用
- [ ] 2.5 删除已无引用的文件
- [ ] 2.6 编译验证 + 测试验证

### 阶段三：FileDriven.UI 精简

- [ ] 3.1 委托 `EcsPipelineOrchestrator` 到 `FrontEndEcsIntegration`
- [ ] 3.2 委托 `CommandLayer` 工具类到 FrontEndDriven
- [ ] 3.3 编译验证 + GUI 功能测试

### 阶段四：FileDriven.Execution 精简

- [ ] 4.1 清理重复的 `LoadedConfigInfo`
- [ ] 4.2 编译验证
