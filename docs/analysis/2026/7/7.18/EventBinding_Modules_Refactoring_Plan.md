# EventBinding 模块化重构方案

> **目标**：以 `Modules.EventFramework` 为主，先扩展框架的通用性和可扩展性，再让 `Share.EventBindingOrchestrator` 基于新框架重新实现，最终消除重复代码。
>
> **原则**：
> - **先扩展，后迁移**：先完善 Modules 框架能力，再让 Share 层基于新框架实现
> - Modules 层（层2）放**纯接口定义**和**零依赖的通用实现**
> - Share 层（层3）保留**编排器、管线、WinForms 适配**等高层逻辑
> - 所有重复代码以 Modules 版本为准，Share 版本删除或委托

---

## 一、现状全景

### 1.1 项目结构

```
CsharpPrj/
├── Modules/EventFramework/          ← 层2：通用事件框架
│   ├── Abstractions/                ← 纯接口定义
│   ├── Core/                        ← 核心实现
│   ├── Models/                      ← 数据模型
│   ├── Bus/                         ← 事件总线
│   ├── Services/                    ← 服务接口
│   └── Adapters/                    ← 适配器
│
└── Share/EventBindingOrchestrator/  ← 层3：事件绑定编排器
    ├── Abstractions/                ← 接口定义（与 Modules 重叠）
    ├── Invoke/                      ← 实现类（与 Modules 重叠）
    ├── Utils/                       ← 工具类（应属于 Modules）
    ├── Services/                    ← 服务实现
    ├── Controls/                    ← WinForms 适配
    ├── Diagnostics/                 ← 诊断上下文
    ├── Mapping/                     ← 事件类型映射
    ├── Validation/                  ← 验证器
    ├── Transformers/                ← 管线转换器
    ├── Stores/                      ← 元数据存储
    ├── EventBus/                    ← 事件引擎封装
    └── Models/                      ← 结果模型
```

### 1.2 重复代码清单

| 编号 | 功能 | Modules 路径 | Share 路径 | 差异分析 |
|------|------|-------------|-----------|---------|
| **R1** | ReflectionEventBinder | `Core/ReflectionEventBinder.cs`（静态类） | `Invoke/ReflectionEventBinderStrategy.cs`（实例类） | 方法体完全一致；Modules 是静态类，Share 是实例类 |
| **R2** | ReflectionControlFinderStrategy | `Core/ReflectionControlFinderStrategy.cs`（内联实现） | `Invoke/ReflectionControlFinderStrategy.cs`（委托给 ReflectionChildControlProvider） | 逻辑相同，Share 多一层委托 |
| **R3** | DefaultEventMappingRegistry | `Core/DefaultEventMappingRegistry.cs`（实现 `TConfig.IEventMappingRegistry`） | `Invoke/DefaultEventMappingRegistry.cs`（实现 `Modules.EventFramework.Abstractions.IEventMappingRegistry`） | 实现不同接口，功能相同 |
| **R4** | EventBindingValidator | `EventBindingValidator.cs`（泛型，基于 Declarative） | `Invoke/EventBindingValidator.cs`（非泛型，手动验证 EventDescriptor） | 验证方式不同，功能相同 |
| **R5** | ReflectionChildControlProvider | 无 | `Utils/ReflectionChildControlProvider.cs` | Share 独有，Modules 需要 |
| **R6** | ReflectionCache | 无 | `Utils/ReflectionCache.cs` | Share 独有，Modules 需要 |

### 1.3 接口重叠清单

| 编号 | 接口 | Modules 路径 | Share 路径 | 差异分析 |
|------|------|-------------|-----------|---------|
| **I1** | IEventTriggerService | `Services/IEventTriggerService.cs`（基于 `IEventSourceNode`） | `Abstractions/IEventTriggerService.cs`（基于 `controlName` 字符串） | 两个不同接口，语义相同 |
| **I2** | IEventTriggerResult | `Services/IEventTriggerResult.cs`（继承 `TConfig.IEventTriggerResult`） | `Abstractions/IEventTriggerResult.cs`（独立接口） | Modules 继承 TConfig，Share 独立定义 |
| **I3** | IControlFinder | 无 | `Abstractions/IControlFinder.cs` | Share 独有，应属于 Modules |
| **I4** | IControlMetadataStore | 无 | `Abstractions/IControlMetadataStore.cs` | Share 独有，应属于 Modules |
| **I5** | IFunctionResolver | 无 | `Abstractions/IFunctionResolver.cs` | Share 独有，应属于 Modules |
| **I6** | IParameterBinder | 无 | `Abstractions/IParameterBinder.cs` | Share 独有，应属于 Modules |

### 1.4 Share 独有逻辑（不应迁移）

| 编号 | 文件 | 说明 | 保留理由 |
|------|------|------|---------|
| **S1** | `EventBindingOrchestrator.cs` | 编排器核心 | 依赖 Pipeline、FluentValidation、UniversalMiddleware |
| **S2** | `EventBindingPipelineFactory.cs` | 管线工厂 | 依赖 Pipeline 模块 |
| **S3** | `EventBindingService.cs` | 事件绑定服务 | 高层服务，依赖 Modules 接口 |
| **S4** | `EventTriggerService.cs` | 事件触发服务 | 高层服务，已整合 EventTriggerServiceBase |
| **S5** | `EventTriggerResult.cs` | 结果模型 | 实现两个接口的桥接类 |
| **S6** | `WinFormsControlTree.cs` | WinForms 控件树 | WinForms 平台适配 |
| **S7** | `WinFormsControlNode.cs` | WinForms 控件节点 | WinForms 平台适配 |
| **S8** | `WinFormsThreadDispatcher.cs` | WinForms 线程调度 | WinForms 平台适配 |
| **S9** | `ControlTreeWalker.cs` | 配置树遍历器 | 基于 `TreeWalker<ControlConfig>` |
| **S10** | `EventBindingHelper.cs` | 辅助方法 | Share 层特有逻辑 |
| **S11** | `EventBindingDiagnosticContext.cs` | 诊断上下文 | 依赖 DiagnosticContext 模块 |
| **S12** | `EventNames.cs` | 诊断事件名常量 | Share 层特有 |
| **S13** | `EventTypeMapper.cs` | 事件类型映射器 | 依赖 UniversalMiddleware |
| **S14** | `EventBindingContextValidator.cs` | FluentValidation 验证器 | 依赖 FluentValidation |
| **S15** | `EventEngine.cs` | 事件引擎封装 | 高层封装 |
| **S16** | `DictionaryControlMetadataStore.cs` | 字典元数据存储 | Share 层实现 |
| **S17** | `ControlTreeWalkerTransformer.cs` | 控件树遍历转换器 | 管线转换器 |

---

## 二、重构步骤

### 阶段一：扩展 Modules.EventFramework 框架

> 目标：让 Modules.EventFramework 具备足够的通用性和可扩展性，使 Share 层能基于它重新实现。

#### 步骤 1：新增 Utils 工具类目录

在 `Modules.EventFramework` 下新建 `Utils/` 目录，从 Share 迁入通用工具类。

**1a. 迁移 ReflectionChildControlProvider**
- **源**：`Share.EventBindingOrchestrator.Utils.ReflectionChildControlProvider`
- **目标**：`Modules.EventFramework.Utils.ReflectionChildControlProvider`
- **操作**：复制文件，更新命名空间为 `Modules.EventFramework.Utils`
- **影响**：Share 版改为 using 别名指向 Modules 版

**1b. 迁移 ReflectionCache**
- **源**：`Share.EventBindingOrchestrator.Utils.ReflectionCache`
- **目标**：`Modules.EventFramework.Utils.ReflectionCache`
- **操作**：同上

#### 步骤 2：扩展 Abstractions 接口层

将 Share 中通用的接口定义迁入 Modules，使 Modules 成为所有接口的唯一来源。

**2a. 迁移 IControlFinder**
- **源**：`Share.EventBindingOrchestrator.Abstractions.IControlFinder`
- **目标**：`Modules.EventFramework.Abstractions.IControlFinder`
- **操作**：复制接口定义，更新命名空间

**2b. 迁移 IControlMetadataStore**
- **源**：`Share.EventBindingOrchestrator.Abstractions.IControlMetadataStore`
- **目标**：`Modules.EventFramework.Abstractions.IControlMetadataStore`
- **操作**：同上

**2c. 迁移 IFunctionResolver**
- **源**：`Share.EventBindingOrchestrator.Abstractions.IFunctionResolver`
- **目标**：`Modules.EventFramework.Abstractions.IFunctionResolver`
- **操作**：同上

**2d. 迁移 IParameterBinder**
- **源**：`Share.EventBindingOrchestrator.Abstractions.IParameterBinder`
- **目标**：`Modules.EventFramework.Abstractions.IParameterBinder`
- **操作**：同上

#### 步骤 3：统一 Core 实现

**3a. 统一 ReflectionEventBinder**
- Modules 版保留静态类 `ReflectionEventBinder`
- Share 版 `ReflectionEventBinderStrategy` 改为委托包装，内部调用 Modules 版
- 确保 Modules 版包含所有方法：`BindEvent`/`UnbindEvent`/`UnbindAll`/`BindEventFromConfig`/`CanHandle`

**3b. 统一 ReflectionControlFinderStrategy**
- Modules 版改为委托给 `Modules.EventFramework.Utils.ReflectionChildControlProvider`
- Share 版删除，改为 using 别名指向 Modules 版

**3c. 统一 DefaultEventMappingRegistry**
- Modules 版同时实现 `TConfig.IEventMappingRegistry` 和 `Modules.EventFramework.Abstractions.IEventMappingRegistry`
- Share 版删除，Share 中引用方改为 using 别名指向 Modules 版

**3d. 统一 EventBindingValidator**
- Modules 版新增非泛型 `EventBindingValidator` 类，验证 `EventDescriptor`
- Share 版删除，改为 using 别名指向 Modules 版

#### 步骤 4：新增通用服务实现

> 目标：Modules 提供可直接使用的通用实现，Share 层无需重复实现。

**4a. 新增 DefaultFunctionResolver**
- 位置：`Modules.EventFramework.Services.DefaultFunctionResolver`
- 实现 `Modules.EventFramework.Abstractions.IFunctionResolver`
- 功能：基于 `IEventBindingResolver` 的默认函数解析实现

**4b. 新增 DefaultParameterBinder**
- 位置：`Modules.EventFramework.Services.DefaultParameterBinder`
- 实现 `Modules.EventFramework.Abstractions.IParameterBinder`
- 功能：基于 `IParameterBuilder<string>` 的默认参数绑定实现

**4c. 新增 DefaultControlFinder**
- 位置：`Modules.EventFramework.Services.DefaultControlFinder`
- 实现 `Modules.EventFramework.Abstractions.IControlFinder`
- 功能：基于 `IControlFinderStrategy` 的默认控件查找实现

**4d. 新增 DefaultControlMetadataStore**
- 位置：`Modules.EventFramework.Services.DefaultControlMetadataStore`
- 实现 `Modules.EventFramework.Abstractions.IControlMetadataStore`
- 功能：基于 `ConcurrentDictionary` 的默认元数据存储实现

### 阶段二：Share 层基于新框架重新实现

> 目标：Share 层基于扩展后的 Modules 框架重新实现，消除重复代码。

#### 步骤 5：更新 Share 层引用

- Share 中所有引用重复接口/实现的地方，改为引用 Modules 版本
- 使用 `using` 别名或完全限定名
- 删除 Share 中已由 Modules 替代的文件

**删除文件清单：**
```
Share/EventBindingOrchestrator/Invoke/ReflectionEventBinderStrategy.cs
Share/EventBindingOrchestrator/Invoke/ReflectionControlFinderStrategy.cs
Share/EventBindingOrchestrator/Invoke/DefaultEventMappingRegistry.cs
Share/EventBindingOrchestrator/Invoke/EventBindingValidator.cs
Share/EventBindingOrchestrator/Utils/ReflectionChildControlProvider.cs
Share/EventBindingOrchestrator/Utils/ReflectionCache.cs
Share/EventBindingOrchestrator/Abstractions/IControlFinder.cs
Share/EventBindingOrchestrator/Abstractions/IControlMetadataStore.cs
Share/EventBindingOrchestrator/Abstractions/IFunctionResolver.cs
Share/EventBindingOrchestrator/Abstractions/IParameterBinder.cs
```

#### 步骤 6：更新 Share 中所有引用

将 Share 中引用上述文件的代码改为引用 Modules 版本。

**影响文件搜索范围：**
- `EventBindingOrchestrator.cs` — 引用 `IFunctionResolver`、`IParameterBinder`
- `EventBindingPipelineFactory.cs` — 引用 `ReflectionControlFinderStrategy`、`IFunctionResolver`、`IParameterBinder`
- `EventBindingService.cs` — 引用 Modules 接口
- `EventTriggerService.cs` — 引用 Modules 接口
- `WinFormsControlNode.cs` — 引用 `ReflectionChildControlProvider`
- 其他引用方

### 阶段三：Share 层清理

#### 步骤 7：删除 Share 中的重复文件

删除阶段二中列出的 10 个文件。

#### 步骤 8：编译验证

- 确保 Modules 先编译，Share 后编译
- 检查 `.csproj` 依赖关系是否正确
- 搜索外部模块（WdUI、Operation、FileDriven、Agent）是否直接引用 Share 中的待删除文件

---

## 三、架构变化

### 3.1 重构后 Modules.EventFramework 结构

```
Modules.EventFramework/
├── Abstractions/
│   ├── IEventSourceNode.cs              （已有）
│   ├── IEventTriggerService.cs          （已有）
│   ├── IEventTriggerResult.cs           （已有）
│   ├── IControlFinder.cs                ★ 从 Share 迁入
│   ├── IControlMetadataStore.cs         ★ 从 Share 迁入
│   ├── IFunctionResolver.cs             ★ 从 Share 迁入
│   └── IParameterBinder.cs              ★ 从 Share 迁入
├── Core/
│   ├── ReflectionEventBinder.cs         （统一版本）
│   ├── ReflectionControlFinderStrategy.cs（统一版本，委托给 Utils）
│   └── DefaultEventMappingRegistry.cs   （统一版本，实现两个接口）
├── Utils/                               ★ 新增
│   ├── ReflectionChildControlProvider.cs ★ 从 Share 迁入
│   └── ReflectionCache.cs               ★ 从 Share 迁入
├── Services/
│   ├── IEventTriggerService.cs          （已有）
│   ├── IFunctionExecutor.cs             （已有）
│   ├── IValueWriter.cs                  （已有）
│   ├── EnhancedValueWriter.cs           （已有）
│   ├── DefaultFunctionResolver.cs       ★ 新增
│   ├── DefaultParameterBinder.cs        ★ 新增
│   ├── DefaultControlFinder.cs          ★ 新增
│   └── DefaultControlMetadataStore.cs   ★ 新增
├── EventBindingValidator.cs             （统一版本，新增非泛型重载）
├── Models/                              （已有）
├── Bus/                                 （已有）
└── Adapters/                            （已有）
```

### 3.2 重构后 Share.EventBindingOrchestrator 结构

```
Share.EventBindingOrchestrator/
├── EventBindingOrchestrator.cs          （保留）
├── Services/
│   ├── EventBindingService.cs           （保留）
│   └── EventTriggerService.cs           （保留）
├── Models/
│   └── EventTriggerResult.cs            （保留）
├── Controls/
│   ├── WinFormsControlTree.cs           （保留）
│   ├── WinFormsControlNode.cs           （保留）
│   └── WinFormsThreadDispatcher.cs      （保留）
├── Diagnostics/
│   ├── EventBindingDiagnosticContext.cs  （保留）
│   └── EventNames.cs                    （保留）
├── Mapping/
│   └── EventTypeMapper.cs               （保留）
├── Validation/
│   └── EventBindingContextValidator.cs   （保留）
├── Transformers/
│   ├── EventBindingPipelineFactory.cs    （保留）
│   └── ControlTreeWalkerTransformer.cs   （保留）
├── Stores/
│   └── DictionaryControlMetadataStore.cs（保留）
├── EventBus/
│   └── EventEngine.cs                   （保留）
├── Invoke/                              （清理后）
│   └── EventBindingHelper.cs            （保留）
├── Abstractions/                        （清理后，全部删除）
│   ├── IControlFinder.cs                → 已迁至 Modules
│   ├── IControlMetadataStore.cs         → 已迁至 Modules
│   ├── IFunctionResolver.cs             → 已迁至 Modules
│   ├── IParameterBinder.cs              → 已迁至 Modules
│   ├── IEventTriggerService.cs          → 保留（Share 特有重载）
│   └── IEventTriggerResult.cs           → 保留（Share 特有重载）
└── Utils/                               （清理后，全部删除）
    ├── ReflectionChildControlProvider.cs → 已迁至 Modules
    └── ReflectionCache.cs               → 已迁至 Modules
```

### 3.3 依赖关系变化

```
重构前：
  Share → Modules（部分依赖）
        → 自包含（重复实现）

重构后：
  Share → Modules（完全依赖，无重复）
        → 仅保留高层编排逻辑
```

---

## 四、风险与注意事项

### 4.1 接口兼容性风险

| 风险 | 说明 | 缓解措施 |
|------|------|---------|
| `DefaultEventMappingRegistry` 实现两个接口 | 方法签名可能冲突 | 使用显式接口实现 |
| `IControlFinder` 等接口迁移 | Share 中引用方需更新 using | 使用 `global using` 或别名 |
| 编译顺序 | Modules 先编译，Share 后编译 | 确保 `.csproj` 依赖正确 |

### 4.2 外部调用方检查

重构前需搜索以下外部模块是否直接引用 Share 中的待删除文件：

- `WdUI` 层
- `Operation` 层
- `FileDriven` 项目
- `Agent` 项目

### 4.3 执行顺序建议

```
阶段一（扩展框架）→ 阶段二（重新实现）→ 阶段三（清理）
```

每个步骤完成后应编译验证，确保不破坏现有功能。

---

## 五、附录：文件映射表

| 原 Share 路径 | 目标 Modules 路径 | 操作 |
|-------------|-----------------|------|
| `Utils/ReflectionChildControlProvider.cs` | `Utils/ReflectionChildControlProvider.cs` | 复制 + 更新命名空间 |
| `Utils/ReflectionCache.cs` | `Utils/ReflectionCache.cs` | 复制 + 更新命名空间 |
| `Invoke/ReflectionEventBinderStrategy.cs` | `Core/ReflectionEventBinder.cs` | 委托到 Modules 版 |
| `Invoke/ReflectionControlFinderStrategy.cs` | `Core/ReflectionControlFinderStrategy.cs` | 统一实现 |
| `Invoke/DefaultEventMappingRegistry.cs` | `Core/DefaultEventMappingRegistry.cs` | 统一实现 |
| `Invoke/EventBindingValidator.cs` | `EventBindingValidator.cs` | 统一实现 |
| `Abstractions/IControlFinder.cs` | `Abstractions/IControlFinder.cs` | 复制 + 更新命名空间 |
| `Abstractions/IControlMetadataStore.cs` | `Abstractions/IControlMetadataStore.cs` | 复制 + 更新命名空间 |
| `Abstractions/IFunctionResolver.cs` | `Abstractions/IFunctionResolver.cs` | 复制 + 更新命名空间 |
| `Abstractions/IParameterBinder.cs` | `Abstractions/IParameterBinder.cs` | 复制 + 更新命名空间 |
| — | `Services/DefaultFunctionResolver.cs` | ★ 新增 |
| — | `Services/DefaultParameterBinder.cs` | ★ 新增 |
| — | `Services/DefaultControlFinder.cs` | ★ 新增 |
| — | `Services/DefaultControlMetadataStore.cs` | ★ 新增 |
