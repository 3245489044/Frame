# 场景指南
> 工具映射、使用流程与通用后处理。
> 完整规则本体参见 `core.md`。

## 一、工具名映射

### 能力→工具映射

| 能力（core.md 用词） | 实际工具 |
|----------------------|----------|
| `search` | `search_files` / `search_code` |
| `process` / `sandbox_processing` | `ctx_execute` / `ctx_execute_file` |
| `batch_process` / `batch_processing` | `ctx_batch_execute` |
| `batch_read` | `ctx_batch_execute`（批量读取文件） |
| `index` | `ctx_fetch_and_index` |
| `retrieve` | `ctx_search` |
| `edit` | `replace_in_file` / `write_to_file` |
| `assess` / `impact_analysis` | `impact_analysis` |
| `analyze` | `ctx_execute`（数据分析） |
| `verify` | `execute_command`（构建/测试） |
| `structured_reasoning` | `sequentialthinking_tools` |
| `record` / `persist` | `create_entities` / `create_relations` / `note` |
| `direct_file_ops` | `read_file` / `write_to_file` / `replace_in_file` |
| `builtin` | 标准工具（`read_file` / `write_to_file` / `replace_in_file` / `execute_command` / `search_files` / `list_files` / `list_code_definition_names` / `ask_followup_question` / `attempt_completion`） |
| `external` | MCP 工具（`ctx_*` / `sequentialthinking_tools` / `impact_analysis` 等） |
| `cli` | `execute_command` |

### 执行环境→路径格式映射

| 执行环境（core.md 用词） | 路径格式 | 示例 |
|--------------------------|----------|------|
| `direct_api` | 绝对路径 + 反斜杠 | `d:\Project\Program\WorkFlowPrj\...` |
| `cli` | 绝对路径 + 反斜杠 | `d:\Project\Program\WorkFlowPrj\...` |
| `sandbox` | 双反斜杠或正斜杠 | `d:\\Project\\Program\\WorkFlowPrj\\...` 或 `d:/Project/Program/WorkFlowPrj/...` |

### 抽象动作→语言实现映射
> `core.md` §二 [ConflictStrategy] 引用的抽象动作语言特定实现。

| 抽象动作 | C# 实现 | TypeScript 实现 | Python 实现 |
|----------|---------|-----------------|-------------|
| `move_and_exclude` | `_deprecated/` + `<Compile Remove>` | `_deprecated/` + tsconfig exclude | `_deprecated/` + `__init__.py` 排除 |
| `deprecate_in_place` | `[Obsolete]` 特性 | `@deprecated` JSDoc | `@deprecated` docstring |
| `delete` | 删除文件 | 删除文件 | 删除文件 |

### 拓扑结构参考
> 拓扑结构定义参见 `core.md` §三 [SCA/VB/CommunicationMode]。三种类型可嵌套组合：面→线→点。

| 类型 | 定义 | 适用场景 | 示例 |
|------|------|----------|------|
| **点聚合** | 分散同类操作收敛到单一入口 | 外部依赖/数据流边界收敛 | EcsInputPort/CsgEntryPoint |
| **线聚合** | 有序步骤编排为管线 | 流程编排/阶段式处理 | EcsRenderPipeline.ExecutePipeline() |
| **面聚合** | 模块公开能力收敛到入口面 | 模块能力出口收敛 | WorkflowMethodUsage |

### 压缩步骤→工具映射

| 步骤（core.md 用词） | 实际工具 |
|----------------------|----------|
| `record_plan_to_graph` | `create_entities` / `create_relations` |
| `index_structure_to_kb` | `ctx_index` |
| `note_changes` | `note` |
| `persist_reasoning` | `write_to_file`（写入推理链持久化文件，追加模式） |
| `update_progress` / `update_progress_marker` | `task_progress` 参数 |
| `mark_phase_done` | `task_progress` 参数 |
| `clear_context` | `attempt_completion` |
| `record_module_summary` | `create_entities` |
| `record_notes` | `note` |
| 紧急转储写入物理文件 | `write_to_file`（写入 `/tmp/context_snapshot.json`） |


## 二、场景→工具链映射表（Scene Macro）
> `core.md` §二 [ToolStrategy] 引用的映射表。场景即配置，直接绑定 `{tools, timeout, mode}` 三元组。

| 场景 | 工具链 | 超时 | 模式 | 说明 |
|------|--------|------|------|------|
| 代码分析 | `[search, process]` | 30s | IO | 搜索+处理 |
| 知识构建 | `[index, retrieve]` | 120s | Compute | 索引+检索 |
| 问题解决 | `[batch_process]` | 120s | Compute | 批量处理 |
| 代码审查 | `[@parallel batch_read, process]` | 30s | IO | 并行批量读取+处理 |
| 性能优化 | `[process, analyze, @parallel edit, verify]` | 60s | Hybrid | 处理+分析+并行编辑+验证 |
| 重构 | `[search, assess, @parallel edit, verify]` | 60s | Hybrid | 搜索+评估+并行编辑+验证 |

> `@parallel` 标注：该步骤与前一步无数据依赖，调度器可并发执行。无标注的步骤按顺序执行。
> 超时和模式值来自 project.md §一 [1.2 执行环境配置]，场景宏直接展开无需独立路由。

## 三、使用流程

> 使用流程由 `core.md` §二 [ToolStrategy] 和 §五 [PostProcess] 定义。本指南仅提供场景映射参考，不重复流程定义。
> 核心流程：场景识别 → 工具链展开 → Guard 校验 → 执行 → 后处理（推理→记录知识）。
