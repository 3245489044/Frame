# 文档：JSON 统一复用 Plate/JsonMethod 与 Chain 通用骨架提取

> 日期：2026-08-30
> 范围：FileDriven 脚本框架、WorkFlowMethod.Chain（节点链执行骨架）、Plate.JsonMethod（JSON 基础设施）
> 结论一句话：**JSON 的读取、预处理、合并、校验、序列化统一收敛到** **`CsharpPrj/Plate/JsonMethod`；可上提到 WorkFlowMethod 的通用逻辑只剩"定义加载 / 执行分发 / 结果回写"三段骨架，其 JSON 基础一律内聚 JsonMethod。**

***

## 一、背景与目标

FileDriven 脚本框架（`Scripts/*.json` → 命令管线 → ECS 分发）与 WorkFlowMethod.Chain（JSON → 类型化节点链 → 管线执行）都是"读 JSON 定义 → 驱动执行"的框架。两者各自维护 JSON 处理逻辑，存在重复与分散：

* FileDriven 侧自建了模板展开门面（`JsonPipelineProcessor` / `GraphKeyFrameUsings`）与全局配置状态（`IsConfigured` / `TemplateConfiguration`）；

* WorkFlowMethod 侧用 `Newtonsoft.Json` 反序列化节点定义，与 Plate 的 `System.Text.Json` 序列化工厂不同源。

它们都依赖 Plate 层已经准备好的 `JsonMethod`，却只用了其中一部分。本文档的目标：

1. 明确 `Plate.JsonMethod` 为 JSON 能力的**单一来源**；
2. 盘点已复用与重复实现；
3. 给出收敛方案；
4. 承接前一轮分析，界定真正值得上提到 `WorkFlowMethod.Chain` 的通用骨架。

***

## 二、Plate.JsonMethod 能力全景

项目位置：`CsharpPrj/Plate/JsonMethod/`

| 能力  | 组件                                                                                                         | 说明                                                                                                            |
| --- | ---------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| 预处理 | `Utilities/JsonPreprocessor.cs`                                                                            | 注册 `Func<string,string>` 步骤链，**逐步骤失败回滚**（`Rollback`），支持 `PreprocessFileAsync` / `PreprocessAndDeserialize<T>` |
| 合并  | `Utilities/JsonMergeService.cs`                                                                            | 深/浅合并；数组模式 `Replace/Append/Merge/Deduplicate`；`MergeModels<T>` 类型级合并                                          |
| 校验  | `Validation/JsonValidationService.cs`                                                                      | 语法校验、泛型结构校验、文件校验（同步/异步）                                                                                       |
| 序列化 | `Serialization/JsonSerializerFactory.cs`、`JsonSerializationService.cs`、`DynamicObjectConverter.cs`         | 多套上下文（`Default/UIConfig/ScriptCommand/PrettyPrint/Compact/LogExport`），camelCase、宽松读、枚举字符串化                    |
| 扩展  | `Extensions/JsonExtensions.cs`                                                                             | `FromJson/ToJson/IsValidJson/PrettyPrint/Minify/DeepCloneViaJson`                                             |
| 其他  | `TemplateFormatter.cs`、`JsonPathHelper.cs`、`JsonValueConverter.cs`、`EnumConvert.cs`、诊断（`JsonSizeAnalyzer`） | 模板占位格式化、路径寻址、值转换等                                                                                             |

关键点：`JsonPreprocessor` 提供了**与领域无关的 JSON 预处理门面**，恰好覆盖 FileDriven 自建模板展开门面所承担的角色；`JsonMergeService`/`JsonValidationService`/`JsonSerializerFactory` 分别对应模板合并、配置校验、统一序列化。

***

## 三、现状盘点

### 3.1 已复用（保持碎片）

* [FileDriven/GlobalUsings/Usings.Plate.cs](file:///d:/Project/Program/WorkFlowPrj/FileDriven/GlobalUsings/Usings.Plate.cs#L6-L7)：全局引用 `Plate.JsonMethod.Serialization / Utilities`；

* [FileDriven.Utilities/Configuration/ConfigurationMerger.cs](file:///d:/Project/Program/WorkFlowPrj/FileDriven/FileDriven.Utilities/Configuration/ConfigurationMerger.cs#L20)：合并直接用 `JsonMergeService.MergeModels`；

* [Share/UniversalMiddleware/](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Share/UniversalMiddleware/UniversalMiddleware.cs#L95)：`UnifiedJsonSerializer.Preprocessor` 暴露 `JP.Utilities.JsonPreprocessor`。

说明：序列化与合并已部分落入 JsonMethod，但**预处理门面、校验、反序列化入口**仍未收敛。

### 3.2 重复 / 未收敛（应整改）

* **模板展开门面自建全局状态**：`JsonPipelineProcessor`（`FileDriven.Utilities/ScriptPipeline/JsonPipelineProcessor.cs`）与 `JsonTemplateProcessor`（`FileDriven.JsonRefer/JsonPipeline/JsonTemplateProcessor.cs`）都维护 `IsConfigured` + `TemplateConfiguration.InitializeAll(...)` 全局一次性配置，且各自存在一份 `PreprocessJson` 前置处理（`FileDrivenAdapter.ConvertBareNamesToPrefixed`）。这些"门面 + 全局状态"应退化为注册到 `JsonMethod.JsonPreprocessor` 的**若干步骤**，消费者得到单一 JSON 入口。

* **反序列化双轨**：WorkFlowMethod 的 `WorkflowDeserializer`/`NodeChainService` 走 `Newtonsoft.Json`；Plate/FileDriven 走 `System.Text.Json`。节点定义反序列化应统一到 `JsonSerializerFactory` 上下文（脚本场景为 `ScriptCommand`）。

* **校验分散**：`FileDrivenConfigValidator`（领域语法校验）与 `JsonValidationService`（通用语法/结构校验）职责未切分，前者应建立在后者之上。

***

## 四、收敛方案：JSON 处理统一到 JsonMethod

统一原则：**领域只保留"业务规则"，不保留 JSON 工程**。业务规则（模板展开、名称前缀化、领域校验）通过适配器注册到 JsonMethod 门面上。

### 4.1 预处理 —— `JsonPreprocessor` + 业务步骤

将模板展开（Placeholder：extends/style/template/defaults/param）包装为一段 step，将名称前缀化（`FileDrivenAdapter.ConvertBareNamesToPrefixed`）包装为另一段 step：

```csharp
pre.RegisterStep(json => PlaceholderExpand(json, templateStorage),  "placeholder-expand");
pre.RegisterStep(json => BareNameToPrefixed(json),                     "bare-name-prefix");
pre.RegisterStep(json => FileDrivenConfigValidator.Validate(json).ToString(), "validate");
var normalized = pre.Preprocess(rawScript);
```

废除 `JsonPipelineProcessor.IsConfigured` / `JsonTemplateProcessor.EnsurePipelineConfigured` 的全局一次性状态，改为在 `JsonMethod` 工厂处组装一次受控实例，或按场景惰性创建。

### 4.2 模板合并 —— `JsonMergeService`

模板继承（extends）的合并基元直接用 `JsonMergeService.DeepMerge` / `MergeModels<T>`（FileDriven 侧 `ConfigurationMerger` 已示范），不新建合并实现。

### 4.3 校验 —— `JsonValidationService` + 领域校验

通用语法/结构校验走 `JsonValidationService`；领域语义校验（如命令 action/target 合法、配置字段约束）作为**上层适配器**在其结果之上叠加，保持 `JsonValidationResult` 统一形态。

### 4.4 序列化 —— `JsonSerializerFactory`

脚本/命令/节点定义反序列化统一使用 `JsonSerializerFactory.Contexts.ScriptCommand`（宽松读、camelCase）。WorkFlowMethod 节点定义解析由 `Newtonsoft.Json` 迁移到该上下文，收敛双轨。

### 4.5 适配层落点

| 原实现                                                                         | 迁移去向                                               |
| --------------------------------------------------------------------------- | -------------------------------------------------- |
| `JsonPipelineProcessor`                                                     | 一组 `JsonPreprocessor` 步骤                           |
| `JsonTemplateProcessor` / `GraphKeyFrameUsings.CreateReferencePreprocessor` | `JsonPreprocessor` 的模板展开 step + 引用解析适配器            |
| `FileDrivenAdapter.ConvertBareNamesToPrefixed`                              | 名称前缀化 step                                         |
| `FileDrivenConfigValidator`                                                 | 建立在 `JsonValidationService` 之上的领域校验适配器             |
| `ConfigurationLoaderBase` / `ConfigurationMerger`                           | 保持复用 `JsonMergeService`/`JsonSerializationService` |

***

## 五、Chain 通用骨架提取（承接前一轮分析）

JSON 基础归 JsonMethod 后，FileDriven 脚本框架真正**值得上提到 WorkFlowMethod.Chain** 的只剩三段进程：

### 5.1 `ChainDefinitionLoadSkeleton<TDefinition>`（P0）

泛化 [ScriptLoadSkeleton](file:///d:/Project/Program/WorkFlowPrj/FileDriven/FileDriven.Execution/Pipeline/ScriptLoadSkeleton.cs#L12-L105) 与 [NodeChainSkeleton](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/NodeChainSkeleton.cs#L14-L72)：加载源（`.json` 结构化 / `.txt` 读行）→ `JsonMethod` 规范化 → 校验 → 产出 `TDefinition`，并统一 `LoadResult`/失败标记（对应现有 `MarkScriptLoadFailure`）。

### 5.2 `IChainExecutionPort` + `ResultSync`（P1）

泛化 [CommandInputPort](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/BackEndDriven/CommandExecution/CommandInputPort.cs#L15-L68) 的 `Load → ExecuteAll → SyncCommandResults`：接收已解析 `TStep`，按注册表/ECS 分发，逐项回写结果（对应 `NodeExecutionStage` + coordinator）。

### 被判定的非目标（不提取、不搬 JSON 工程）

* **不把 PlaceholderFramework 提到 Chain**：模板展开是业务规则，保留并注册为 JsonMethod 步骤；

* **不做** **`ChainInputNormalizer`** **内复制 JSON**：JSON 规范化的唯一实现是 `Plate.JsonMethod`，Chain 仅注入依赖。

***

## 六、迁移步骤与影响面

按依赖顺序推进，每步可独立验收：

1. **建 JSON 门面**：在 Plate.JsonMethod 组装受控 `JsonPreprocessor` 工厂，注册模板展开 / 名称前缀化步骤（P0 前置，无行为变更，只是收敛入口）。
2. **序列化单轨化**：FileDriven 脚本加载与 WorkFlowMethod 节点解析切到 `JsonSerializerFactory.Contexts.ScriptCommand`；删除 `Newtonsoft.Json` 反序列化分支（保留 `NodeChainService` 的 JSON 序列化契约一致性）。
3. **校验切分**：`JsonValidationService` 兜底通用校验，领域校验改建其上。
4. **上提 Chain**：落地 `ChainDefinitionLoadSkeleton<T>`，先让 `ScriptLoadSkeleton` 与 `NodeChainSkeleton` 作为其两个适配器（W facet 不丢）。
5. **上提执行端口**：落地 `IChainExecutionPort` + `ResultSync`，ECS 分发与 `NodeExecutionStage` 各自接入。

验收标准：两条入口（`ScriptRunner` 生成的 `.txt` 与 GUI 直传 `.json`）在收敛后行为不变，命令管线 92 项 FrontEndDriven 测试保持通过；WorkFlowMethod 的节点链执行相关测试保持通过。

***

## 七、附录：关键文件清单

* Plate.JsonMethod：`Utilities/JsonPreprocessor.cs`、`Utilities/JsonMergeService.cs`、`Validation/JsonValidationService.cs`、`Serialization/JsonSerializerFactory.cs`、`Extensions/JsonExtensions.cs`

* FileDriven 复用点：`GlobalUsings/Usings.Plate.cs`、`Utilities/Configuration/ConfigurationMerger.cs`

* FileDriven 待整改：`Utilities/ScriptPipeline/JsonPipelineProcessor.cs`、`JsonRefer/JsonPipeline/JsonTemplateProcessor.cs`、`JsonRefer/Validation/`

* WorkFlowMethod 目标宿主：`NodeProcess/NodeChainSkeleton.cs`、`NodeProcess/Execution/NodeExecutionStage.cs`

* 框架入口调用链（前一轮分析）：`ScriptRunner` → `FileDriven.Launcher` → `ScriptExecutionPort` → `FileDrivenPipelineRunner` → `CentralPipeline` → `CommandInputPort` → ECS 分发

