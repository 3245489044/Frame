# ParameterResolver 配置校验扩展规划

> **目标**：扩展 `ParameterResolver` 模块，使其从"参数映射校验"升级为"通用配置结构校验"，覆盖 JSON 配置的结构正确性、类型匹配、引用完整性等场景。

---

## 目录

1. [文档规划目标](#一文档规划目标)
2. [引用的模块](#二引用的模块)
3. [实现步骤](#三实现步骤)

---

## 一、文档规划目标

### 1.1 背景与动机

#### 1.1.1 当前问题

在 `FileDriven` 框架中，JSON 配置文件（如 `MathFunctions.unified.json`）的结构正确性完全依赖反序列化器的隐式行为，缺乏显式校验。典型问题包括：

- **值类型不匹配**：`events.click` 的值是字符串 `"Add"`，但反序列化器期望的是数组 `["Add"]`，导致 `EventsDictionary` 为 null，事件绑定静默跳过
- **必填字段缺失**：控件缺少 `name` 或 `type` 字段，导致后续逻辑异常
- **模板引用断裂**：`template` 字段指向不存在的模板定义
- **交叉引用失效**：`bindings.parameters[].source` 引用的控件名在配置树中不存在

这些问题在运行时表现为"静默失败"——没有异常、没有警告，只有通过仔细分析日志才能发现绑定数量为 0。

#### 1.1.2 为什么扩展 ParameterResolver 而非另起炉灶

- `ParameterResolver` 已有 `Validation/` 目录和 `IConstraint` 校验体系
- 校验逻辑与参数解析紧密相关（参数映射校验 → 配置结构校验 → 运行时校验）
- 避免引入新的模块/项目，降低维护成本

---

### 1.2 扩展目标

#### 1.2.1 核心目标

将 `ParameterResolver.Validation` 从"参数映射校验"扩展为"配置结构校验"，使其能够：

1. **校验 JSON 配置的结构正确性** — 字段存在性、值类型、格式规范
2. **校验配置的引用完整性** — 模板引用、控件引用、函数引用
3. **校验配置的语义一致性** — 事件绑定格式、参数映射格式
4. **输出结构化诊断报告** — 包含位置信息、严重级别、修复建议

#### 1.2.2 设计原则

- **渐进增强**：不破坏现有 `ParameterMappingValidator` 的接口和行为
- **声明式规则**：校验规则用声明式方式定义，易于理解和扩展
- **可组合**：规则可以组合、嵌套、复用
- **可定位**：诊断信息包含配置文件的精确位置（路径 + 行号/列号）
- **可分级**：诊断结果分 Error / Warning / Info 三级

---

### 1.3 校验范围

#### 1.3.1 结构校验（Structure Validation）

校验 JSON 配置的字段存在性和值类型正确性：

- **必填字段检查**：控件必须包含 `name`、`type` 字段
- **值类型检查**：`events.xxx` 的值必须是 `string[]` 而非 `string`
- **字段格式检查**：`layout` 字段的百分比值在 0~1 范围内
- **枚举值检查**：`sourceType` 必须是 `control` / `literal` / `expression` 之一

> **可借用的能力**：
> - `JsonValidationService.ValidateSyntax()` 作为 JSON 语法预校验的前置步骤
> - `RequiredConstraint` / `TypeConstraint` / `RangeConstraint` 直接实现必填/类型/范围校验
> - `JsonPathHelper.ResolveJsonPath()` 精确定位校验失败的配置节点

#### 1.3.2 引用校验（Reference Validation）

校验配置中的交叉引用完整性：

- **模板引用检查**：`template` 字段指向的模板必须存在于模板注册表中
- **控件引用检查**：`bindings.parameters[].source` 引用的控件名必须在配置树中存在
- **函数引用检查**：`events.xxx` 引用的函数名必须在函数注册表中存在
- **输出目标检查**：`bindings.output` 引用的控件名必须在配置树中存在

> **可借用的能力**：
> - `JsonPathHelper.ExtractUnexpandedTemplates()` 提取未展开的模板引用
> - `JsonPathHelper.GetAllPaths()` 获取配置树所有路径，用于引用存在性检查
> - `TemplateFormatter.ExtractParams()` 提取模板参数，辅助模板参数完整性校验

#### 1.3.3 语义校验（Semantic Validation）

校验配置的业务语义正确性：

- **事件绑定格式检查**：`events` 对象中非 `bindings` 的键值对，值必须是 `string[]`
- **参数映射格式检查**：`bindings.parameters` 的每个条目必须包含 `target` 和 `source`
- **工作流定义检查**：工作流节点必须包含 `functionName` 和有效的参数映射
- **输出适配器检查**：`outputAdapter.mode` 必须是已知的适配模式

> **可借用的能力**：
> - `IRelation` + `Select`/`Project` 对配置树进行关系查询和字段筛选
> - `JsonPathHelper.ExtractContextSnippet()` 在校验失败时输出周围上下文
> - `JsonErrorDiagnostics.DescribeExpectedType()` 生成类型不匹配时的友好提示

#### 1.3.4 运行时校验（Runtime Validation）

校验运行时状态与配置的一致性：

- **控件存在性检查**：配置中声明的控件在运行时 UI 树中是否存在
- **类型兼容性检查**：参数源值的运行时类型与目标参数类型是否兼容
- **事件兼容性检查**：配置中声明的事件在运行时控件上是否支持

---

### 1.4 非目标

以下内容不在本次扩展范围内：

- **不实现通用 JSON Schema 校验**（如 JSON Schema 标准）
- **不实现跨文件校验**（如 A 配置引用 B 配置的内容）
- **不实现自动修复**（只检测和报告，不修改配置）
- **不改变现有 `ParameterMappingValidator` 的接口和行为**
- **不引入新的外部依赖**

---

## 二、引用的模块

### 2.1 现有基础

#### 2.1.1 已有的校验能力

| 组件 | 能力 | 局限 |
|------|------|------|
| `ParameterMappingValidator` | 校验 `ParameterMapping` 列表的 Target 唯一性、SourceType 合法性 | 只校验参数映射层面，不校验 JSON 配置结构 |
| `IConstraint<T>` | 谓词约束接口，支持组合和链式调用 | 当前只用于 `ParameterMapping` 类型 |
| `ConstraintResult` / `ValidationResult` | 约束检查结果和验证结果聚合 | 结果格式简单，缺少位置信息和严重级别 |
| `EventBindingContextValidator` (FluentValidation) | 校验 `EventBindingContext` 输入参数 | 只校验运行时上下文，不校验配置源 |

#### 2.1.2 可复用的基础设施

- `BasicMethod.Declarative` 框架的 `IConstraint<T>` 接口体系
- `CompositeConstraint` 组合约束模式（AND/OR/NOT）
- `PredicateConstraint` 谓词约束（快速定义简单规则）
- `RangeConstraint` 范围约束（如 layout 百分比值 0~1 校验）
- `TypeConstraint` 类型约束（如 events.xxx 值类型必须是 string[]）
- `RequiredConstraint` 必填约束（如控件 name/type 必填字段）
- `ValidationResult` / `ConstraintResult` 结果聚合
- `IRelation` 关系代数接口（Select/Project/Join/Rename），可用于配置树的关系查询——筛选所有控件、投影字段列表、连接配置段
- `IDeclarativeMapper<TDec, TRt>` 声明→运行时映射器，可将校验规则声明映射为可执行校验器
- `IDeclarationResolver<TSrc, TDec>` 配置解析器，可将 JSON 配置解析为声明对象后再校验
- `IDeclarationCollection<TDec>` 声明集合管理，可用于管理校验规则集合的注册、查找和动态调整

---

### 2.2 可借用的外部能力

> 校验框架的构建不必从零开始。`BasicMethod.Map` 和 `Plate/JsonMethod` 两个模块提供了大量可直接复用或间接参考的基础能力。

#### 2.2.1 BasicMethod.Map — 映射与编排基础框架

`BasicMethod.Map` 是 WorkFlowPrj 基础方法库的核心子体系，提供 **9 个并行的通用基础模块**。以下四个模块对校验框架的构建最具参考价值：

##### Declarative（声明式框架）— 约束体系核心

> 数学基础：关系代数 + 谓词逻辑 + 约束满足问题

| 组件 | 对应校验场景 | 复用方式 |
|------|-------------|---------|
| `IConstraint<T>` | 所有校验规则的基础接口 | 直接引用 `BasicMethod.Declarative` 命名空间 |
| `CompositeConstraint<T>` (AND/OR/NOT) | 复杂条件校验（如"必填 + 类型 + 范围"组合） | `ParameterMappingValidator` 已在使用 |
| `PredicateConstraint<T>` | 快速定义简单校验规则 | |
| `RangeConstraint<T>` | `layout` 百分比值 0~1 校验 | |
| `TypeConstraint` | `events.xxx` 值类型必须是 `string[]` | |
| `RequiredConstraint` | 控件 `name`/`type` 必填字段 | |
| `IRelation` + `Select`/`Project`/`Join` | 配置树的关系查询（筛选控件、投影字段、连接配置段） | |
| `IDeclarativeMapper<TDec, TRt>` | 将 JSON Schema 风格的规则声明映射为 `IConstraint` | |
| `IDeclarationResolver<TSrc, TDec>` | 将 JSON 配置解析为声明对象后再校验 | |
| `IDeclarationCollection<TDec>` | 注册/查找/动态调整校验规则 | |
| `DeclarationValidator<T>` | 验证声明是否满足所有约束 | |
| `ConstraintResult` / `ValidationResult` | 校验结果统一封装 | |

##### Transformers（函数变换框架）— 校验管线与横切关注点

> 数学基础：函数 `f: A → B`

| 组件 | 对应校验场景 | 复用方式 |
|------|-------------|---------|
| `Validator<T>` | 单个字段的快速验证 | 直接引用 `BasicMethod.Transformers` 命名空间 |
| `TryTransformer` | 宽松模式下捕获校验异常，以 `Result<TOutput>` 返回 | 通过 `Then()` 扩展方法组合校验步骤 |
| `ConditionalTransformer` | 根据配置模式选择不同校验规则 | |
| `Pipeline<TInput, TOutput>` | 结构校验→引用校验→语义校验 的管线化执行 | |
| `Decorator` 体系（Log/Retry/Timeout） | 校验日志记录、失败重试、超时控制 | |
| `CacheTransformer` | 相同配置的重复校验结果缓存 | |
| `TransformContext` (AsyncLocal) | 校验过程中传递注册表、控件树等上下文数据 | |
| `Result<TOutput>` | 校验结果统一封装（成功/失败/错误信息） | |

##### Block（分块执行框架）— 校验执行引擎

> 数学基础：分段函数 `f(x) = { f₁(x), ..., fₙ(x) }`

| 组件 | 对应校验场景 | 复用方式 |
|------|-------------|---------|
| `ExecutionPipeline` | 多步骤校验的编排执行 | 直接引用 `BasicMethod.Block` 命名空间 |
| `BlockContext` | 校验中间结果传递和诊断记录 | 通过 `ExecutionPipeline` 编排校验流程 |
| `BlockResult` / `PipelineResult` | 结构化校验报告输出 | |
| `DiagnosticBlock` | 校验前置条件检查 | |
| `ConditionalBlock` | 条件性校验规则 | |
| `TimeoutBlock` | 防止校验耗时过长 | |
| `SequenceBlock` | 嵌套校验规则组 | |

##### Container（通用执行容器框架）— 弹性策略

> 数学基础：命令模式 + 请求/响应

| 组件 | 对应校验场景 | 复用方式 |
|------|-------------|---------|
| `IExecutionCell` | 单个校验规则的原子执行 | 引用 `Container` 命名空间 |
| `IExecutionEngine` | 校验规则的统一调度执行 | 通过 `PolicyFactory` 创建弹性策略 |
| Behaviors（Retry/CircuitBreaker/Timeout/Log） | 校验失败重试、熔断保护、超时控制、日志记录 | |
| Decorators | 校验行为的横切关注点 | |

#### 2.2.2 Plate/JsonMethod — JSON 处理工具集

`Plate/JsonMethod` 是从 `BasicMethod/JsonProcess` 迁移至 Plate 层的 JSON 处理工具集，提供 JSON 验证、诊断、预处理、序列化等能力。

##### Validation（JSON 验证）

| 组件 | 对应校验场景 | 复用方式 |
|------|-------------|---------|
| `JsonValidationService.ValidateSyntax()` | 配置加载前的 JSON 语法检查 | 直接使用 `JsonValidationService.Instance` 单例 |
| `JsonValidationService.ValidateStructure<T>()` | 配置结构正确性检查 | 在配置加载阶段前置调用 |
| `JsonValidationService.ValidateFileAsync()` | 配置文件存在性和语法检查 | |
| `JsonValidationResult`（含 Warnings） | 校验结果统一格式（与 ParameterResolver 的 ValidationResult 互补） | |

##### Diagnostics（错误诊断）

| 组件 | 对应校验场景 | 复用方式 |
|------|-------------|---------|
| `JsonErrorDiagnostics.Enrich()` | 校验失败时的精确定位（路径+行号+期望类型） | 在 `ValidationDiagnostic` 中调用增强诊断信息 |
| `JsonErrorDiagnostics.DescribeExpectedType()` | 类型不匹配时的友好提示 | |
| `JsonSizeAnalyzer.AnalyzeJsonSize()` | 配置大小预警 | |

##### Utilities（工具方法）

| 组件 | 对应校验场景 | 复用方式 |
|------|-------------|---------|
| `JsonPathHelper.ResolveJsonPath()` | 精确定位校验失败的配置节点 | 直接引用 `Plate.JsonMethod.Utilities` 命名空间 |
| `JsonPathHelper.ExtractContextSnippet()` | 校验失败时输出周围上下文 | 在校验规则中调用路径解析和模板提取方法 |
| `JsonPathHelper.ExtractUnexpandedTemplates()` | 模板引用完整性校验 | |
| `JsonPathHelper.GetAllPaths()` | 遍历配置树进行全量校验 | |
| `JsonPreprocessor` | 校验前的 JSON 标准化（注释移除、模板展开） | |
| `JsonMergeService.DeepMerge()` | 合并后配置的结构校验 | |
| `TemplateFormatter.ExtractParams()` | 模板引用参数完整性校验 | |

##### Serialization（序列化）

| 组件 | 对应校验场景 | 复用方式 |
|------|-------------|---------|
| `JsonSerializationService.DeserializeWithValidation()` | 配置反序列化时的结构校验 | 使用 `JsonSerializationService.Instance` 进行序列化操作 |
| `JsonSerializationService.SafeSerialize()` | 校验报告的 JSON 序列化输出 | 使用 `JsonSerializerFactory.Contexts` 获取预定义选项 |
| `JsonSerializerFactory` 多上下文 | 校验报告的美化/紧凑输出 | |
| `DynamicObjectConverter` | 配置树的动态类型解析 | |

##### Extensions（扩展方法）

| 组件 | 对应校验场景 | 复用方式 |
|------|-------------|---------|
| `IsValidJson()` | 配置加载前的快速预检 | 直接调用字符串扩展方法，如 `json.IsValidJson()` |
| `PrettyPrintJson()` / `MinifyJson()` | 校验报告的美化输出 | |

---

### 2.3 涉及的文件目录

#### 2.3.1 校验核心目录（ParameterResolver.Validation）

校验逻辑的主体位于 `ParameterResolver` 模块的 `Validation/` 目录下，扩展后该目录结构如下：

```
CsharpPrj/Modules/ParameterResolver/Validation/
├── ParameterMappingValidator.cs      # 已有：参数映射校验（保持不变）
├── ConfigStructureValidator.cs       # 新增：配置结构校验入口
├── ConfigValidationContext.cs        # 新增：校验上下文（承载注册表等外部数据）
├── Rules/                            # 新增：校验规则目录
│   ├── IValidationRule.cs            # 新增：校验规则接口
│   ├── RequiredFieldRule.cs          # 新增：必填字段规则
│   ├── EventValueTypeRule.cs         # 新增：events.xxx 值类型规则
│   ├── TemplateRefRule.cs            # 新增：模板引用存在性规则
│   ├── ControlRefRule.cs             # 新增：控件引用存在性规则
│   ├── FunctionRefRule.cs            # 新增：函数引用存在性规则
│   └── ParameterMappingFormatRule.cs # 新增：参数映射格式规则
└── Diagnostics/                      # 新增：诊断结果目录
    ├── ValidationDiagnostic.cs       # 新增：诊断信息（含位置、严重级别）
    ├── ValidationReport.cs           # 新增：校验报告（聚合所有诊断）
    └── Severity.cs                   # 新增：严重级别枚举
```

#### 2.3.2 校验规则目录（ParameterResolver.Validation.Rules）

所有校验规则集中放在 `Rules/` 子目录下，每条规则实现统一的 `IValidationRule` 接口：

```
CsharpPrj/Modules/ParameterResolver/Validation/Rules/
├── IValidationRule.cs                # 校验规则接口
├── Structure/                        # 结构校验规则
│   ├── RequiredFieldRule.cs          # 必填字段检查
│   ├── EventValueTypeRule.cs         # events.xxx 值类型检查
│   ├── LayoutFormatRule.cs           # layout 字段格式检查
│   └── EnumValueRule.cs             # 枚举值合法性检查
├── Reference/                        # 引用校验规则
│   ├── TemplateRefRule.cs            # 模板引用存在性检查
│   ├── ControlRefRule.cs             # 控件引用存在性检查
│   ├── FunctionRefRule.cs            # 函数引用存在性检查
│   └── OutputTargetRule.cs          # 输出目标存在性检查
└── Semantic/                         # 语义校验规则
    ├── EventBindingFormatRule.cs     # 事件绑定格式检查
    ├── ParameterMappingFormatRule.cs # 参数映射格式检查
    ├── WorkflowDefinitionRule.cs     # 工作流定义检查
    └── OutputAdapterRule.cs         # 输出适配器模式检查
```

#### 2.3.3 校验上下文目录（ParameterResolver.Validation）

校验上下文承载校验过程中需要的外部数据，如模板注册表、函数注册表、控件树等：

```
CsharpPrj/Modules/ParameterResolver/Validation/
├── ConfigValidationContext.cs        # 校验上下文（主类）
├── Providers/                        # 上下文数据提供者
│   ├── ITemplateRegistryProvider.cs  # 模板注册表提供者
│   ├── IFunctionRegistryProvider.cs  # 函数注册表提供者
│   └── IControlTreeProvider.cs       # 控件树提供者
```

#### 2.3.4 诊断结果目录（ParameterResolver.Validation.Diagnostics）

诊断结果相关的类型定义：

```
CsharpPrj/Modules/ParameterResolver/Validation/Diagnostics/
├── ValidationDiagnostic.cs           # 单条诊断信息
├── ValidationReport.cs               # 校验报告（聚合多条诊断）
├── Severity.cs                       # 严重级别枚举
└── SourceLocation.cs                 # 源码位置信息
```

> **诊断增强**：`ValidationDiagnostic` 可调用 `JsonErrorDiagnostics.Enrich()` 增强错误信息，包含路径、行号、期望类型等上下文。

#### 2.3.5 被校验的配置文件目录

校验的目标配置文件位于以下目录：

```
FileDriven/DrivenConfig/
├── JsonConfig/                       # JSON 格式配置（ConfigSelector.json 等）
│   └── *.json
├── TempletConfig/                    # 模板配置（被 JsonConfig 引用）
│   ├── styles.json
│   └── composites.json
└── UnifiedConfig/                    # 统一配置（UI + 函数注册合并）
    └── *.unified.json
```

#### 2.3.6 校验集成点目录

校验逻辑需要集成到以下现有模块中：

```
FileDriven/FileDriven.JsonRefer/      # 配置加载阶段集成
├── PreprocessConfig.cs               # 预处理配置（校验触发点 1）
└── JsonTemplateProcessor.cs          # 模板处理（校验触发点 1）

CsharpPrj/Share/EventBindingOrchestrator/  # 事件绑定阶段集成
├── EventBindingPipelineFactory.cs    # 事件绑定管线（校验触发点 2）
└── EventBindingContextValidator.cs   # 上下文验证器（校验触发点 2）

CsharpPrj/Modules/ParameterResolver/      # 参数解析阶段集成
├── Builder/ParameterBuilder.cs       # 参数构建器（校验触发点 3）
└── Validation/ParameterMappingValidator.cs  # 已有校验器（校验触发点 3）
```

---

## 三、实现步骤

> **实现顺序说明**：先扩展（Phase 1-4），再 debug（Phase 5）。Phase 1-4 按依赖关系依次推进，Phase 5 在所有扩展完成后进行测试验证。

### 3.1 集成方式（前置说明）

#### 3.1.1 校验触发时机

校验应在以下阶段自动触发：

1. **配置加载阶段**：`PreprocessConfig` 或 `ProcessJsonWithTemplate` 执行后，对解析后的配置树执行结构校验和引用校验
   - 前置步骤：`JsonValidationService.ValidateSyntax()` 做 JSON 语法预检
   - 主校验：使用 `ExecutionPipeline`（Block 模块）或 `Pipeline`（Transformers 模块）编排校验流程
2. **事件绑定阶段**：`EventBindingPipelineFactory` 的 `ValidateInput` 步骤中，对 `ControlConfig.Events` 执行语义校验
3. **参数解析阶段**：`ParameterBuilder` 构建参数时，对 `ParameterMapping` 执行运行时校验

#### 3.1.2 校验结果输出

校验结果应通过以下渠道输出：

1. **日志系统**：Error/Warning 级别的诊断写入 `ILogger`
2. **诊断上下文**：所有诊断记录到 `EventBindingDiagnosticContext`
3. **结构化报告**：可选的 JSON 格式校验报告，用于 CI/CD 集成
   - 使用 `JsonSerializationService.SafeSerialize()` 序列化校验报告
   - 使用 `JsonSerializerFactory.Contexts.PrettyPrint` 美化输出

#### 3.1.3 校验行为控制

- **严格模式**：校验失败时抛出异常，阻止后续执行
  - 可基于 `Container` 的 `IExecutionEngine` + Behaviors 实现弹性控制
- **宽松模式**：校验失败时仅记录警告，继续执行（默认）
  - 可基于 `TryTransformer` 容错包装实现安全降级
- **静默模式**：校验失败时不输出任何信息（仅用于性能敏感场景）

---

### 3.2 Phase 1 — 基础设施扩展

> **目标**：构建校验框架的基础类型体系，为后续规则实现提供类型支撑。
> **预估工作量**：中（约 5-8 个类型定义）
> **依赖**：无（纯新增类型，不依赖现有代码修改）

#### 步骤 1.1：定义严重级别枚举（Severity）

**位置**：`ParameterResolver/Validation/Diagnostics/Severity.cs`

**职责**：定义校验诊断的严重级别分级，支持诊断分类和过滤。

| 级别 | 含义 | 行为影响 |
|------|------|---------|
| `Error` | 配置错误，必须修复 | 严格模式下阻止执行 |
| `Warning` | 配置不规范，建议修复 | 宽松模式下仅记录日志 |
| `Info` | 配置信息提示 | 仅用于诊断报告 |

**设计要点**：
- 枚举类型，支持按大小比较（Error > Warning > Info）
- 提供 `IsBlocking()` 扩展方法，根据当前模式判断是否阻止执行
- 提供 `ToString()` 本地化描述

**验收标准**：
- 可创建指定级别的诊断
- 支持级别比较和过滤
- 与校验行为控制（严格/宽松/静默模式）联动

---

#### 步骤 1.2：定义源码位置信息（SourceLocation）

**位置**：`ParameterResolver/Validation/Diagnostics/SourceLocation.cs`

**职责**：精确定位校验失败在配置文件中的位置，支持快速定位问题。

| 字段 | 类型 | 说明 |
|------|------|------|
| `FilePath` | `string` | 配置文件路径（相对或绝对） |
| `JsonPath` | `string` | JSON 路径（如 `$.controls[0].name`） |
| `LineNumber` | `int?` | 行号（可选，由 JsonErrorDiagnostics 提供） |
| `ColumnNumber` | `int?` | 列号（可选） |
| `ContextSnippet` | `string?` | 周围上下文片段（可选，由 JsonPathHelper 提供） |

**设计要点**：
- 不可变 record 类型
- 提供 `ToString()` 格式化为 `"文件路径:行号:列号 - JSON路径"` 的可读形式
- 与 `JsonErrorDiagnostics.Enrich()` 集成，自动填充行号/列号
- 与 `JsonPathHelper.ExtractContextSnippet()` 集成，自动提取上下文片段

**验收标准**：
- 可创建包含文件路径和 JSON 路径的位置信息
- 行号/列号/上下文片段为可选字段，不强制要求
- 格式化输出可读性强，便于日志记录

---

#### 步骤 1.3：定义单条诊断信息（ValidationDiagnostic）

**位置**：`ParameterResolver/Validation/Diagnostics/ValidationDiagnostic.cs`

**职责**：封装单条校验诊断的全部信息，是校验结果的最小单元。

| 字段 | 类型 | 说明 |
|------|------|------|
| `RuleName` | `string` | 触发诊断的规则名称 |
| `Severity` | `Severity` | 严重级别 |
| `Message` | `string` | 诊断描述信息 |
| `SourceLocation` | `SourceLocation?` | 源码位置（可选） |
| `RepairSuggestion` | `string?` | 修复建议（可选） |
| `Timestamp` | `DateTime` | 诊断产生时间 |

**设计要点**：
- 不可变 record 类型
- 提供工厂方法 `CreateError()` / `CreateWarning()` / `CreateInfo()` 便捷创建
- 提供 `WithLocation()` 链式方法附加位置信息
- 提供 `WithSuggestion()` 链式方法附加修复建议
- 提供 `ToLogString()` 格式化为日志友好的单行文本

**验收标准**：
- 可通过工厂方法快速创建不同级别的诊断
- 支持链式调用附加位置和修复建议
- 日志输出格式统一，便于日志分析

---

#### 步骤 1.4：定义校验报告（ValidationReport）

**位置**：`ParameterResolver/Validation/Diagnostics/ValidationReport.cs`

**职责**：聚合一次校验操作的所有诊断结果，提供整体校验结论。

| 字段 | 类型 | 说明 |
|------|------|------|
| `TargetName` | `string` | 被校验的目标名称（如配置文件名） |
| `IsValid` | `bool` | 整体校验是否通过（无 Error 级别诊断） |
| `Diagnostics` | `IReadOnlyList<ValidationDiagnostic>` | 所有诊断列表 |
| `ErrorCount` | `int` | Error 级别诊断数量 |
| `WarningCount` | `int` | Warning 级别诊断数量 |
| `InfoCount` | `int` | Info 级别诊断数量 |
| `ExecutedAt` | `DateTime` | 校验执行时间 |
| `Duration` | `TimeSpan` | 校验耗时 |

**设计要点**：
- 提供 `AddDiagnostic()` 方法添加诊断
- 提供 `MergeFrom()` 方法合并多个子报告（用于分步校验的汇总）
- 提供 `ToJson()` 方法序列化为 JSON 格式（使用 `JsonSerializationService`）
- 提供 `ToDiagnosticString()` 方法格式化为可读文本（参考 Block 模块的 `PipelineResult.ToDiagnosticString()`）

**验收标准**：
- 可聚合多条诊断并自动统计各级别数量
- 支持多个子报告合并
- 支持 JSON 序列化输出

---

#### 步骤 1.5：定义校验上下文（ConfigValidationContext）

**位置**：`ParameterResolver/Validation/ConfigValidationContext.cs`

**职责**：承载校验过程中需要的外部数据，作为校验规则与外部环境的桥梁。

| 数据 | 类型提供者 | 用途 |
|------|-----------|------|
| 模板注册表 | `ITemplateRegistryProvider` | 模板引用校验 |
| 函数注册表 | `IFunctionRegistryProvider` | 函数引用校验 |
| 控件树 | `IControlTreeProvider` | 控件引用校验、运行时控件存在性校验 |
| 配置原始 JSON | `JsonElement` | 路径解析、上下文提取 |
| 校验模式 | `ValidationMode` | 严格/宽松/静默模式控制 |

**设计要点**：
- 数据提供者接口定义在 `Providers/` 子目录下
- 提供者通过构造函数注入，运行时通过 DI 容器解析
- 校验模式通过 `ValidationMode` 枚举控制（`Strict` / `Loose` / `Silent`）
- 提供 `CreateScope()` 方法创建校验作用域（参考 `TransformContext.BeginScope()`）

**验收标准**：
- 可注入所有必要的数据提供者
- 支持校验模式切换
- 提供者接口设计合理，便于 mock 测试

---

#### 步骤 1.6：定义统一校验入口（IConfigValidator）

**位置**：`ParameterResolver/Validation/IConfigValidator.cs`

**职责**：统一配置校验入口，对外提供一致的校验调用方式。

```csharp
// 接口设计示意
public interface IConfigValidator<TConfig>
{
    string Name { get; }
    Task<ValidationReport> ValidateAsync(TConfig config, ConfigValidationContext context);
}
```

**设计要点**：
- 泛型接口，`TConfig` 为被校验的配置类型
- 同步/异步双版本（异步为主，同步通过 `AsyncTransformerAdapter` 适配）
- 与现有 `ParameterMappingValidator` 的关系：`ParameterMappingValidator` 保持不变，`IConfigValidator` 作为新的统一入口
- 提供 `CompositeConfigValidator` 组合多个校验器

**验收标准**：
- 可对指定配置类型执行校验
- 支持组合多个校验器
- 与现有 `ParameterMappingValidator` 互不干扰

---

#### 步骤 1.7（可选）：构建校验执行引擎

**选型分析**：两种方案对比

| 维度 | Block 方案（ExecutionPipeline） | Transformers 方案（Pipeline） |
|------|-------------------------------|------------------------------|
| 核心抽象 | 每个校验规则 = 一个 Block | 每个校验规则 = 一个 Transformer |
| 数据传递 | `BlockContext.Data` 字典 | `TransformContext` AsyncLocal |
| 诊断报告 | 内置 `PipelineResult.ToDiagnosticString()` | 需自行构建 |
| 条件执行 | `ConditionalBlock` 内置 | `ConditionalTransformer` |
| 超时控制 | `TimeoutBlock` 内置 | `TimeoutDecorator` |
| 嵌套执行 | `SequenceBlock` 内置 | 需自行组合 |
| 适用场景 | 校验步骤多、需要丰富诊断报告 | 校验步骤少、需要轻量管线 |

**建议**：优先使用 **Block 方案**，因为：
- 内置诊断报告格式与校验需求高度匹配
- `BlockContext` 的 `Data` + `Diagnostics` 天然适合校验数据传递和诊断记录
- 内置块类型（ConditionalBlock、TimeoutBlock、DiagnosticBlock）直接对应校验需求

**验收标准**：
- 可编排多个校验规则按顺序执行
- 支持条件跳过、超时控制
- 输出结构化诊断报告

---

### 3.3 Phase 2 — 核心校验规则

> **目标**：实现 12 条核心校验规则，覆盖结构/引用/语义三类校验场景。
> **预估工作量**：大（约 12 个规则类）
> **依赖**：Phase 1 的基础类型体系

#### 步骤 2.1：定义校验规则接口（IValidationRule）

**位置**：`ParameterResolver/Validation/Rules/IValidationRule.cs`

**职责**：所有校验规则的统一接口，确保规则可被校验引擎统一调度。

```csharp
// 接口设计示意
public interface IValidationRule
{
    string RuleName { get; }
    string Description { get; }
    ValidationRuleCategory Category { get; } // Structure / Reference / Semantic
    Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config, ConfigValidationContext context);
}
```

**设计要点**：
- `Category` 用于规则分类和选择性执行（如只执行结构校验）
- 输入为 `JsonElement`（解析后的 JSON 配置树），而非反序列化的强类型对象
- 输出为诊断列表，每条诊断独立，便于精确定位多个问题
- 提供 `ValidationRuleBase` 抽象基类，封装公共逻辑（规则名称、分类、日志记录）

**验收标准**：
- 所有规则实现同一接口
- 支持按分类选择性执行
- 基类封装公共逻辑，减少重复代码

---

#### 步骤 2.2：实现结构校验规则（Structure Rules）

**规则清单**：

| 规则 | 输入 | 判定条件 | 失败时输出 |
|------|------|---------|-----------|
| `RequiredFieldRule` | 控件 JSON 对象 | 必填字段列表中的每个字段都存在且非空 | `Error: 控件缺少必填字段 'name'` |
| `EventValueTypeRule` | `events` 对象 | 非 `bindings` 的键值对，值类型为 `JsonValueKind.Array` | `Error: events.click 的值类型应为 string[]，当前为 string` |
| `LayoutFormatRule` | `layout` 字段 | 百分比值在 0~1 范围内 | `Warning: layout.width 的值 1.5 超出 0~1 范围` |
| `EnumValueRule` | 枚举字段（如 `sourceType`） | 值在允许的枚举列表中 | `Error: sourceType 的值 'invalid' 不在允许范围内` |

**实现路径**：
1. 创建 `Rules/Structure/` 目录，每个规则一个独立文件
2. 每条规则继承 `ValidationRuleBase`，设置 `Category = ValidationRuleCategory.Structure`
3. 使用 `JsonElement.TryGetProperty()` 遍历配置树，检查字段存在性和值类型
4. 复用 `RequiredConstraint` / `TypeConstraint` / `RangeConstraint` 作为底层判定逻辑
5. 失败时通过 `ValidationDiagnostic.CreateError()` 创建诊断，通过 `JsonPathHelper.ResolveJsonPath()` 填充位置信息

**与 Plate/JsonMethod 的集成**：
- 前置步骤：在校验管线入口调用 `JsonValidationService.ValidateSyntax()` 做 JSON 语法预检
- 若语法预检失败，直接返回 `ValidationReport`（含语法错误诊断），跳过后续结构校验

**验收标准**：
- 对包含必填字段的配置，缺失字段时正确报告 Error
- 对 events 值类型错误的配置，正确检测类型不匹配
- 对 layout 值超出范围的配置，正确报告 Warning
- 对枚举值不合法的配置，正确报告 Error
- 诊断信息包含精确的 JSON 路径

---

#### 步骤 2.3：实现引用校验规则（Reference Rules）

**规则清单**：

| 规则 | 输入 | 判定条件 | 失败时输出 |
|------|------|---------|-----------|
| `TemplateRefRule` | `template` 字段值 | 值在 `ITemplateRegistryProvider` 提供的模板名列表中 | `Error: 模板 'MyTemplate' 未在模板注册表中定义` |
| `ControlRefRule` | `bindings.parameters[].source` 值 | 值在配置树的控件名列表中 | `Error: 参数 'result' 的 source 引用控件 'nonexistent' 不存在` |
| `FunctionRefRule` | `events.xxx` 数组中的函数名 | 值在 `IFunctionRegistryProvider` 提供的函数名列表中 | `Error: 事件 'click' 引用的函数 'Add' 未在函数注册表中定义` |
| `OutputTargetRule` | `bindings.output` 值 | 值在配置树的控件名列表中 | `Error: output 目标控件 'nonexistent' 不存在` |

**实现路径**：
1. 创建 `Rules/Reference/` 目录，每个规则一个独立文件
2. 每条规则继承 `ValidationRuleBase`，设置 `Category = ValidationRuleCategory.Reference`
3. 通过 `ConfigValidationContext` 获取对应的数据提供者（`ITemplateRegistryProvider` 等）
4. 使用 `JsonPathHelper.ExtractUnexpandedTemplates()` 提取所有模板引用
5. 使用 `JsonPathHelper.GetAllPaths()` 获取配置树所有路径，构建控件名列表
6. 使用 `TemplateFormatter.ExtractParams()` 提取模板参数，辅助参数完整性校验

**验收标准**：
- 对引用不存在模板的配置，正确报告 Error
- 对引用不存在控件的配置，正确报告 Error
- 对引用不存在函数的配置，正确报告 Error
- 诊断信息包含引用路径和可用列表（如"可用模板：A、B、C"）

---

#### 步骤 2.4：实现语义校验规则（Semantic Rules）

**规则清单**：

| 规则 | 输入 | 判定条件 | 失败时输出 |
|------|------|---------|-----------|
| `EventBindingFormatRule` | `events` 对象 | 非 `bindings` 的键值对，值类型为 `string[]` | `Error: events.click 的值应为函数名数组，当前为字符串` |
| `ParameterMappingFormatRule` | `bindings.parameters` 数组 | 每个条目包含 `target` 和 `source` 字段 | `Error: parameters[2] 缺少必填字段 'source'` |
| `WorkflowDefinitionRule` | 工作流节点对象 | 包含 `functionName` 且参数映射有效 | `Error: 工作流节点 'step1' 缺少 functionName` |
| `OutputAdapterRule` | `outputAdapter` 对象 | `mode` 字段值为已知适配模式 | `Warning: outputAdapter.mode 'unknown' 不是已知适配模式` |

**实现路径**：
1. 创建 `Rules/Semantic/` 目录，每个规则一个独立文件
2. 每条规则继承 `ValidationRuleBase`，设置 `Category = ValidationRuleCategory.Semantic`
3. 使用 `IRelation` + `Select`/`Project` 对配置树进行关系查询：
   - `Select` 筛选所有 `events` 对象中的非 `bindings` 键
   - `Project` 投影 `parameters` 数组中的 `target` 和 `source` 字段
4. 使用 `JsonPathHelper.ExtractContextSnippet()` 在校验失败时输出周围上下文
5. 使用 `JsonErrorDiagnostics.DescribeExpectedType()` 生成类型不匹配时的友好提示

**验收标准**：
- 对事件绑定格式错误的配置，正确报告 Error
- 对参数映射缺少字段的配置，正确报告 Error
- 对工作流定义不完整的配置，正确报告 Error
- 对输出适配器模式未知的配置，正确报告 Warning
- 诊断信息包含上下文片段和期望格式说明

---

### 3.4 Phase 3 — 集成与适配

> **目标**：将校验框架集成到现有模块的三个触发点中，使校验在配置加载、事件绑定、参数解析阶段自动执行。
> **预估工作量**：中（约 3 个集成点，每个 1-2 个文件修改）
> **依赖**：Phase 2 的校验规则

#### 步骤 3.1：集成到配置加载阶段

**集成点**：`FileDriven/FileDriven.JsonRefer/PreprocessConfig.cs` 和 `JsonTemplateProcessor.cs`

**触发时机**：`PreprocessConfig` 或 `ProcessJsonWithTemplate` 执行后，对解析后的配置树执行校验。

**校验数据流**：
```
原始 JSON
  → JsonValidationService.ValidateSyntax()
    → [语法错误] → 返回 ValidationReport
    → [语法正确] JsonPreprocessor 预处理
      → JsonTemplateProcessor 模板展开
        → ConfigStructureValidator.ValidateAsync()
          ├─ 结构校验规则（RequiredField / EventValueType / LayoutFormat / EnumValue）
          └─ 引用校验规则（TemplateRef / ControlRef / FunctionRef / OutputTarget）
            → ValidationReport → 日志 / 诊断上下文 / 可选 JSON 报告
```

**实现路径**：
1. 在 `PreprocessConfig.ProcessAsync()` 中，模板展开后调用 `ConfigStructureValidator.ValidateAsync()`
2. 前置调用 `JsonValidationService.ValidateSyntax()` 做 JSON 语法预检
3. 根据校验模式决定行为：
   - 严格模式：`ValidationReport.IsValid == false` 时抛出 `ConfigValidationException`
   - 宽松模式：仅通过 `ILogger` 记录 Error/Warning 诊断
   - 静默模式：不执行任何校验
4. 将诊断写入 `EventBindingDiagnosticContext`（如果可用）

**验收标准**：
- 配置加载时自动触发结构校验和引用校验
- 语法错误在结构校验前被捕获
- 严格模式下校验失败阻止配置加载
- 宽松模式下校验失败仅记录日志，配置继续加载

---

#### 步骤 3.2：集成到事件绑定阶段

**集成点**：`CsharpPrj/Share/EventBindingOrchestrator/EventBindingPipelineFactory.cs`

**触发时机**：`EventBindingPipelineFactory` 的 `ValidateInput` 步骤中，对 `ControlConfig.Events` 执行语义校验。

**校验数据流**：
```
ControlConfig.Events
  → EventBindingFormatRule.ValidateAsync()
    → ValidationReport → EventBindingDiagnosticContext
      → 根据校验模式决定是否阻止绑定
```

**实现路径**：
1. 在 `EventBindingPipelineFactory.ValidateInput` 步骤中，添加对 `ControlConfig.Events` 的语义校验
2. 创建 `EventBindingConfigValidator`（实现 `IConfigValidator<ControlConfig>`），内部调用 `EventBindingFormatRule`
3. 校验结果写入 `EventBindingDiagnosticContext`
4. 与现有 `EventBindingContextValidator`（FluentValidation）协作：`EventBindingContextValidator` 校验运行时输入参数，`EventBindingConfigValidator` 校验配置源

**验收标准**：
- 事件绑定时自动触发语义校验
- 校验结果写入诊断上下文
- 与现有 FluentValidation 校验器互不干扰

---

#### 步骤 3.3：集成到参数解析阶段

**集成点**：`CsharpPrj/Modules/ParameterResolver/Builder/ParameterBuilder.cs`

**触发时机**：`ParameterBuilder` 构建参数时，对 `ParameterMapping` 执行运行时校验。

**校验数据流**：
```
ParameterMapping
  → ParameterMappingFormatRule.ValidateAsync()
    → ValidationReport → ILogger
      → 根据校验模式决定是否使用默认值降级
```

**实现路径**：
1. 在 `ParameterBuilder.BuildParametersAsync()` 中，对 `ParameterMapping` 列表执行运行时校验
2. 创建 `ParameterMappingConfigValidator`（实现 `IConfigValidator<ParameterMapping>`），内部调用 `ParameterMappingFormatRule`
3. 校验失败时，根据校验模式决定行为：
   - 严格模式：抛出 `ParameterValidationException`
   - 宽松模式：使用默认值降级，记录 Warning
4. 与现有 `ParameterMappingValidator` 协作：`ParameterMappingValidator` 校验参数映射的静态正确性（Target 唯一性等），`ParameterMappingConfigValidator` 校验运行时一致性

**验收标准**：
- 参数解析时自动触发运行时校验
- 严格模式下校验失败抛出异常
- 宽松模式下使用默认值降级并记录日志
- 与现有 `ParameterMappingValidator` 互不干扰

---

### 3.5 Phase 4 — 完善与优化

> **目标**：补充运行时校验规则，优化校验性能，完善报告输出和弹性策略。
> **预估工作量**：中（约 3 条运行时规则 + 性能优化 + 报告输出 + 弹性策略）
> **依赖**：Phase 3 的集成

#### 步骤 4.1：实现运行时校验规则（Runtime Rules）

**规则清单**：

| 规则 | 输入 | 判定条件 | 失败时输出 |
|------|------|---------|-----------|
| `ControlExistenceRule` | 控件名列表 | 每个控件名在运行时 UI 树中存在 | `Error: 控件 'resultLabel' 在运行时 UI 树中不存在` |
| `TypeCompatibilityRule` | 参数源值 + 目标参数类型 | 源值类型可隐式转换为目标参数类型 | `Warning: 参数 'value' 的源类型 int 不能隐式转换为目标类型 string` |
| `EventCompatibilityRule` | 事件名 + 控件类型 | 事件在运行时控件上支持 | `Warning: 控件 'Button1' 不支持事件 'onHover'` |

**实现路径**：
1. 创建 `Rules/Runtime/` 目录，每个规则一个独立文件
2. 通过 `IControlTreeProvider` 获取运行时 UI 树信息
3. 通过 `IFunctionRegistryProvider` 获取函数签名信息（用于类型兼容性检查）
4. 运行时校验仅在配置加载完成后、事件绑定前执行

**验收标准**：
- 对运行时 UI 树中不存在的控件，正确报告 Error
- 对类型不兼容的参数映射，正确报告 Warning
- 对控件不支持的事件，正确报告 Warning

---

#### 步骤 4.2：校验性能优化

**优化策略**：

| 策略 | 实现方式 | 适用场景 |
|------|---------|---------|
| 增量校验 | 缓存上次校验的配置哈希，仅当配置变更时重新校验 | 同一配置多次加载 |
| 规则选择性执行 | 根据 `ValidationRuleCategory` 选择性执行特定类别的规则 | 只关心结构校验的场景 |
| 校验结果缓存 | 复用 `CacheTransformer` 缓存校验结果 | 相同配置的重复校验 |
| 并行校验 | 无依赖的规则并行执行（如结构校验和引用校验可并行） | 配置树较大时 |

**实现路径**：
1. **增量校验**：在 `ConfigValidationContext` 中存储配置文件的 `ContentHash`（SHA256），校验前比较哈希值
2. **规则选择性执行**：`ConfigStructureValidator` 接受 `ValidationRuleCategory` 参数，只执行指定类别的规则
3. **校验结果缓存**：使用 `CacheTransformer` 包装 `IConfigValidator`，以配置哈希为缓存键
4. **并行校验**：基于 Block 模块的 `ExecutionPipeline`，无依赖的 Block 可并行执行

**验收标准**：
- 相同配置重复加载时，增量校验跳过已校验的规则
- 选择性执行时，只执行指定类别的规则
- 缓存命中时直接返回缓存结果

---

#### 步骤 4.3：结构化报告输出

**报告格式**：

```json
{
  "targetName": "MathFunctions.unified.json",
  "isValid": false,
  "executedAt": "2026-07-18T12:00:00Z",
  "duration": "00:00:00.123",
  "summary": {
    "errorCount": 2,
    "warningCount": 1,
    "infoCount": 0
  },
  "diagnostics": [
    {
      "ruleName": "RequiredFieldRule",
      "severity": "Error",
      "message": "控件缺少必填字段 'name'",
      "sourceLocation": {
        "filePath": "DrivenConfig/UnifiedConfig/MathFunctions.unified.json",
        "jsonPath": "$.controls[0]",
        "lineNumber": 42,
        "columnNumber": 10,
        "contextSnippet": "  \"controls\": [\n    { \"type\": \"button\" }  ← 缺少 name\n  ]"
      },
      "repairSuggestion": "添加 name 字段，如 \"name\": \"myButton\""
    }
  ]
}
```

**实现路径**：
- `ValidationReport.ToJson()` 使用 `JsonSerializationService.SafeSerialize()` 序列化
- 使用 `JsonSerializerFactory.Contexts.PrettyPrint` 美化输出
- 提供 `ValidationReportJsonConverter` 自定义序列化（控制字段命名风格、排除可选空字段）
- 报告输出到以下渠道：
  - **文件系统**：`Storage/ValidationReports/{timestamp}_{targetName}.json`
  - **日志系统**：`ILogger` 输出摘要（ErrorCount / WarningCount）
  - **诊断上下文**：`EventBindingDiagnosticContext` 记录详细诊断

**验收标准**：
- 校验报告可序列化为 JSON 格式
- 报告包含完整的诊断信息（位置、严重级别、修复建议）
- 支持美化输出和紧凑输出两种格式

---

#### 步骤 4.4：弹性策略支持

**三种模式的行为定义**：

| 模式 | 结构校验失败 | 引用校验失败 | 语义校验失败 | 运行时校验失败 |
|------|-------------|-------------|-------------|---------------|
| 严格模式 | 抛出异常，阻止加载 | 抛出异常，阻止加载 | 抛出异常，阻止绑定 | 抛出异常，阻止执行 |
| 宽松模式 | 记录 Error，继续加载 | 记录 Error，继续加载 | 记录 Warning，继续绑定 | 记录 Warning，使用默认值 |
| 静默模式 | 不执行 | 不执行 | 不执行 | 不执行 |

**实现路径**：
- **严格模式**：基于 `Container` 的 `IExecutionEngine` + Behaviors：
  - 使用 `RetryBehavior` 在校验失败时重试（最多 1 次，避免瞬态错误）
  - 使用 `CircuitBreakerBehavior` 在连续失败时熔断（防止级联故障）
  - 使用 `LogBehavior` 记录所有校验操作
- **宽松模式**：基于 `TryTransformer` 容错包装：
  - `TryTransformer` 捕获校验异常，以 `Result<ValidationReport>` 返回
  - 调用方检查 `Result.IsSuccess`，失败时仅记录日志
- **静默模式**：基于 `ConditionalTransformer` 条件分支：
  - 校验模式为 `Silent` 时，跳过整个校验管线
  - 返回空的 `ValidationReport`（`IsValid = true`）

**验收标准**：
- 严格模式下，任何 Error 级别诊断都抛出异常
- 宽松模式下，校验失败仅记录日志，不阻止执行
- 静默模式下，不执行任何校验，返回空报告

---

### 3.6 Phase 5 — 测试与验证

> **目标**：确保校验框架的正确性、稳定性和向后兼容性。
> **预估工作量**：中（约 30-50 个测试用例）
> **依赖**：Phase 1-4 全部完成

#### 步骤 5.1：单元测试策略

**测试范围**：

| 测试目标 | 测试内容 | 测试数量 |
|---------|---------|---------|
| 诊断类型体系 | Severity 比较、SourceLocation 格式化、ValidationDiagnostic 工厂方法、ValidationReport 聚合 | 约 8 个 |
| 校验上下文 | 提供者注入、模式切换、作用域创建 | 约 5 个 |
| 结构校验规则 | 每条规则的正向/负向/边界测试 | 约 12 个（4 规则 × 3 场景） |
| 引用校验规则 | 每条规则的正向/负向/边界测试 | 约 12 个（4 规则 × 3 场景） |
| 语义校验规则 | 每条规则的正向/负向/边界测试 | 约 12 个（4 规则 × 3 场景） |
| 运行时校验规则 | 每条规则的正向/负向/边界测试 | 约 9 个（3 规则 × 3 场景） |
| 校验执行引擎 | 管线编排、条件跳过、超时控制 | 约 6 个 |
| 弹性策略 | 三种模式的行为验证 | 约 6 个 |

**测试工具**：使用 xUnit + FluentAssertions（与项目现有测试框架一致）

**Mock 策略**：
- `ITemplateRegistryProvider` / `IFunctionRegistryProvider` / `IControlTreeProvider` 使用 Moq 模拟
- 配置 JSON 使用内联 `JsonDocument.Parse()` 构造，避免依赖文件系统

**验收标准**：
- 所有规则的正向测试通过（正确配置通过校验）
- 所有规则的负向测试通过（错误配置正确报告诊断）
- 所有规则的边界测试通过（空值、空数组、特殊字符等）

---

#### 步骤 5.2：集成测试策略

**测试范围**：

| 测试场景 | 测试内容 | 测试数量 |
|---------|---------|---------|
| 配置加载阶段集成 | 模拟 `PreprocessConfig` 调用，验证校验自动触发 | 约 4 个 |
| 事件绑定阶段集成 | 模拟 `EventBindingPipelineFactory` 调用，验证语义校验 | 约 4 个 |
| 参数解析阶段集成 | 模拟 `ParameterBuilder` 调用，验证运行时校验 | 约 4 个 |
| 端到端校验 | 从配置加载到参数解析的完整流程校验 | 约 3 个 |

**测试数据**：
- 正常配置：`MathFunctions.unified.json` 的正确版本
- 异常配置：包含各类错误的配置变体（缺失字段、类型错误、引用断裂等）
- 边界配置：空配置、超大配置、特殊字符配置

**验收标准**：
- 集成点正确触发校验
- 校验结果正确传递到下游（日志、诊断上下文、报告文件）
- 端到端流程中，校验不破坏现有功能

---

#### 步骤 5.3：回归测试

**测试目标**：确保扩展后的校验框架不破坏现有 `ParameterMappingValidator` 的行为。

**测试内容**：
- 现有 `ParameterMappingValidator` 的所有测试用例继续通过
- 现有 `EventBindingContextValidator` 的所有测试用例继续通过
- 现有 `EventBindingPipelineFactory` 的所有测试用例继续通过
- 现有 `ParameterBuilder` 的所有测试用例继续通过

**验收标准**：
- 所有现有测试用例在扩展后继续通过
- 新增的校验逻辑不影响现有功能的执行路径

---

#### 步骤 5.4：测试用例设计示例

**结构校验 — RequiredFieldRule 测试用例**：

| 用例 | 输入配置片段 | 期望结果 |
|------|-------------|---------|
| 正向 | `{"controls": [{"name": "btn1", "type": "button"}]}` | 无诊断 |
| 负向-缺 name | `{"controls": [{"type": "button"}]}` | 1 个 Error：缺少 name |
| 负向-缺 type | `{"controls": [{"name": "btn1"}]}` | 1 个 Error：缺少 type |
| 负向-全缺 | `{"controls": [{}]}` | 2 个 Error：缺少 name 和 type |
| 边界-空数组 | `{"controls": []}` | 无诊断 |
| 边界-空对象 | `{}` | 无诊断（无 controls 时跳过） |

**引用校验 — ControlRefRule 测试用例**：

| 用例 | 输入配置片段 | 期望结果 |
|------|-------------|---------|
| 正向 | source 引用存在的控件名 | 无诊断 |
| 负向 | source 引用不存在的控件名 | 1 个 Error：控件不存在 |
| 边界 | source 为空字符串 | 1 个 Error：source 为空 |
| 边界 | parameters 为空数组 | 无诊断 |

**语义校验 — EventBindingFormatRule 测试用例**：

| 用例 | 输入配置片段 | 期望结果 |
|------|-------------|---------|
| 正向 | `{"events": {"click": ["Add"], "bindings": {...}}}` | 无诊断 |
| 负向-值类型错误 | `{"events": {"click": "Add", "bindings": {...}}}` | 1 个 Error：值类型应为数组 |
| 负向-空数组 | `{"events": {"click": [], "bindings": {...}}}` | 1 个 Warning：事件绑定数组为空 |
| 边界-无 events | `{}` | 无诊断 |

---

### 文档版本

| 版本 | 日期 | 主要变更 |
|------|------|---------|
| v0.1.0 | — | 初始版本 |
| v0.2.0 | 2026-07-18 | 新增"可借用的外部能力"章节，系统分析 BasicMethod.Map 和 Plate/JsonMethod 对校验框架的辅助能力 |
| v0.3.0 | 2026-07-18 | 将"实施路线"从 4 个 Phase 的概要描述展开为 5 个 Phase 的详细步骤，每个步骤包含位置、职责、设计要点、验收标准、实现路径等完整信息 |
| v0.4.0 | 2026-07-18 | 模块化整理：分为"文档规划目标"、"引用的模块"、"实现步骤"三大块，精简外部能力表格格式，集成方式前置为步骤说明 |
