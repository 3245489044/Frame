# 重构实施状态清单（JsonMethod 复用与 Chain 通用骨架提取）

> 依据：`docs/8.30/JsonMethod复用与Chain通用骨架提取.md`（五阶段迁移方案）
> 状态日期：2026-08-31 | 当前 PR-A（阶段 0-4）推进中

## 现状速览

| 阶段 | 内容 | 状态 | 验证 |
|------|------|------|------|
| 0 | 验收基线 | ⏳ 部分（无基线文档，已跑测试） | FrontEndDriven 93 通过 |
| 1 | 建 JSON 门面（受控 JsonPreprocessor） | ✅ 完成 | FileDriven.sln 构建 0 错；FrontEndDriven 93 通过 |
| 2 | 序列化单轨化 | 🔶 部分（见下方结论） | WorkFlowMethod.csproj 构建 0 错 |
| 3 | 校验切分 | ✅ 完成 | FileDriven.sln 构建 0 错；FrontEndDriven 93 通过 |
| 4 | 上提 Chain（ChainDefinitionLoadSkeleton） | ✅ 完成（骨架落地 + NodeChainSkeleton 采纳） | UniversalMiddleware / WorkFlowMethod 构建 0 错 |
| 5 | 上提执行端口（IChainExecutionPort+ResultSync） | ⛔ 未开始（PR-B） | — |

## 阶段 1 交付（已完成）

- 新增 `FileDriven/FileDriven.Utilities/ScriptPipeline/ScriptJsonPipeline.cs`
  - 消费方侧受控门面：把「名称前缀化」「模板展开」注册到 `Plate.JsonMethod.Utilities.JsonPreprocessor` 为业务步骤。
  - `Process(path)` 行为与原 `JsonPipelineProcessor.Process` 完全一致（文件缺失/未配置/展开失败 → null，sourcePath 透传）。
  - 依赖方向正确：消费方（FileDriven）注册业务步骤到 Plate，Plate 不反向依赖业务（JsonMethod.csproj 未新增引用）。
- `JsonPipelineProcessor.cs` 收敛为薄委托 → `ScriptJsonPipeline.Process`，两个调用方（`ScriptConverter.cs:14`、`ScriptRunner.cs:275`）零改动。
- 验证：FileDriven.sln 构建 0 错 0 警；FrontEndDriven.Tests **93 通过 / 0 失败**。

## 阶段 3 交付（已完成）

- `FileDriven/FileDriven.JsonRefer/Validation/FileDrivenConfigValidator.cs`
  - 新增 `ParseSyntax` 辅助：通用 JSON 语法校验统一走 `Plate.JsonMethod.Validation.JsonValidationService.ValidateSyntax`（通用校验单源）。
  - `Validate`/`ValidateAsync` 改用 `ParseSyntax`：语法合法后解析 `JsonDocument` 供领域校验引擎（上层适配器，ParameterResolver.Validation）使用。
  - 行为不变：Loose/Silent 语法失败 → 返回 null（跳过校验）；Strict → 抛 `ConfigValidationException`。
  - 验证：FileDriven.sln 构建 0 错；FrontEndDriven 93 通过。

## 阶段 4 交付（已完成）

- 新增 `CsharpPrj/Share/UniversalMiddleware/Chain/ChainDefinitionLoadSkeleton.cs`
  - `ChainDefinitionLoadSkeleton<TDefinition>`：加载源（文件/字符串）→ 可选 JsonMethod 规范化 → 可选校验 → 产出 TDefinition → 统一 `ChainLoadResult<TDefinition>`。
  - JSON 工程一律内聚 Plate.JsonMethod，骨架仅注入依赖（无 ChainInputNormalizer）。
  - `ForJson` 便捷工厂默认以 `JsonValidationService.ValidateSyntax` 为通用校验。
  - **落点说明**：置于 UniversalMiddleware（FileDriven.Execution 与 WorkFlowMethod 均引用），避免 FileDriven 向上依赖 WorkFlowMethod.Chain（符合层纪律）。
- `WorkFlowMethod/NodeProcess/NodeChainSkeleton.cs` 采纳骨架：parse 委托 `NodeChainService.CreateChain`，不注入 normalize/validate（行为保持）；非法 JSON 经 Load 原样上抛（与迁移前一致）。
- 验证：UniversalMiddleware / WorkFlowMethod 构建 0 错。



- **反序列化其实早已单轨化**：`UniversalMiddlewareEntry.FromJson<T>` → `UnifiedJsonSerializer.Deserialize<T>` → `Plate.JsonMethod.JsonSerializationService`（System.Text.Json）。文档所述"WorkFlowMethod 用 Newtonsoft 反序列化"已过时。
- **已改**：`WorkflowDeserializer.cs` 移除 `NewtonsoftJson.JsonException` 死 catch 与别名（行为等价：带路径诊断的 STJ `JsonException` 原样上抛）；`WorkflowDeserializer` 不再引用 Newtonsoft。
- **推迟（兼容性敏感）**：`NodeChainService.DeepCloneNode`（`JsonConvert` 克隆）与模型 `[JsonProperty]`（如 `NodeConfig.cs` 的 `using Newtonsoft.Json`）——对应 2026/8/8.2 报告 **D7**「模型级依赖，需全量兼容性验证」，须先做契约对拍/全量验证再切换，不盲改。

## 已知既有问题（与本次改动无关）

- `CentralTest/CSharpTest/Operation.Tests/WorkFlowMethod.Tests/WorkFlowMethod.Test.csproj` 编译失败：引用不存在的命名空间 `WorkFlowMethod.Orchestration.Refactored`（`RefactoredExecutionUsageTests.cs`）、`WorkFlow.Config`（`WorkFlowsSetUsageTests.cs`）。主源码中不存在这两个命名空间 → 该测试项目陈旧/损坏，属既有问题。

## 待办（后续 PR-A 续 + PR-B）

- [ ] 阶段 2 续：`NodeChainService.DeepCloneNode` 契约对拍后切 STJ；清理模型层 `[JsonProperty]`（D7，需全量兼容性验证）。
- [ ] 阶段 4 续：`ScriptLoadSkeleton` 采纳 `ChainDefinitionLoadSkeleton` 适配器（.json/.txt 双路径较复杂，需行为保持契约，单独处理）。
- [ ] 阶段 5（PR-B）：`IChainExecutionPort` + `ResultSync`。
- [ ] 阶段 0：补基线文档 / 冒烟入口记录。
