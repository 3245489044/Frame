# MCP 工具集成逻辑（MCP Tool Integration Logic）

> **层级**：L2-L3 | **职责**：MCP 工具的分类、选择策略、调用规范与后处理集成
> **源规则**：`[CR: rules.md §五 §八]` | `[CR: engine.md §六]` | `[CR: project.md §一]`
> **MCP 配置**：`docs/CLine_MCP_配置指南.md` | `docs/环境配置指南.md#4-CLine-MCP-配置`

---

## §一、MCP 工具分类体系

### 1.1 按职责分类

| 分类 | MCP 服务器 | 工具/能力 | 对应策略 |
|------|-----------|-----------|---------|
| **执行沙箱** | context-mode | `ctx_execute`, `ctx_batch_execute`, `ctx_index` | ToolSelect → sandbox_processing |
| **推理分解** | mcp-sequentialthinking-tools | `sequentialthinking_tools`, `get_thinking_history` | ToolSelect → structured_reasoning |
| **知识图谱** | memory | `create_entities`, `create_relations`, `search_nodes`, `add_observations`, `note` | PostProcess → record_knowledge |
| **文档查询** | context7-mcp | `resolve-library-id`, `query-docs` | ToolSelect → doc_web |
| **代码搜索** | lexis-mcp | `search_code`, `get_symbol`, `find_references`（28+ 工具） | 批量/精确代码搜索 |
| **文件系统** | filesystem | 读写搜索（13 工具） | PathStrategy + direct_file_ops |
| **MCP 桥接** | MCPBridge | `invoke_ps_script` / `context7-mcp` / `csproj-context` | 宿主机关调用 |

### 1.2 按操作类型分类 [CM]

`McpOperationType(taskType) [CM]` Provides: 任务类型 → MCP 操作分类

```
taskType ∈ {file_io}         → filesystem
taskType ∈ {analysis,process} → context-mode (ctx_execute / ctx_batch_execute)
taskType ∈ {reasoning}       → mcp-sequentialthinking-tools
taskType ∈ {knowledge}       → memory
taskType ∈ {doc_query}       → context7-mcp
taskType ∈ {code_search}     → lexis-mcp
taskType ∈ {ps_script}       → MCPBridge (invoke_ps_script)
otherwise                    → fallback to CLine builtin
```

### 1.3 MCP 工具优先级 [CM]

`McpToolPriority(hasMcp, hasBuiltin) [CD]`：
```
hasMcp ∧ autoApprove → mcp_direct
hasMcp ∧ ¬autoApprove → mcp_with_confirm
¬hasMcp ∧ hasBuiltin → builtin_fallback
otherwise → cli_command
```

> 派生自 ToolPriority：`[CR: rules.md §5.1 ToolPriority]`

---

## §二、工具选择映射（ToolSelect → MCP）

### 2.1 任务到 MCP 的映射矩阵

| 任务类型 | MCP 工具 | 参数规范 |
|---------|---------|---------|
| `file_read` | `ctx_execute → BasicMethod-FileAccess.psm1` | 文件路径使用绝对路径（双反斜杠或正斜杠） |
| `file_write` | `ctx_execute → BasicMethod-FileAccess.psm1` | 包含备份、校验、差异预览安全机制 |
| `code_execution` | `ctx_execute` | summary_only 模式，避免输出过长 |
| `batch_operation` | `ctx_batch_execute` | `queries` 参数可一次返回多个结果 |
| `reasoning` | `sequentialthinking_tools` | 替代官方 sequential-thinking，分解复杂问题 |
| `doc_query` | `context7-mcp` | `resolve-library-id` / `query-docs` |
| `code_search` | `lexis-mcp` | `search_code` / `get_symbol` / `find_references` |
| `knowledge_record` | `memory` | `create_entities` + `create_relations` |
| `knowledge_search` | `memory` | `search_nodes` / `add_observations` |
| `ps_script_invoke` | `MCPBridge.invoke_ps_script` | 宿主机 PowerShell 脚本调用 |

### 2.2 文件操作安全规范

> 优先使用 MCP 工具（`invoke_ps_script` → `BasicMethod-FileAccess.psm1` 中的 `BatchRead-File`、`BatchWrite-File`、`BatchReplace-InFile`），而非 CLine 内置工具。

**原因**：脚本层提供编码检测、备份、校验、差异预览、交互确认等安全机制，CLine 内置工具不具备这些能力。

**回退规则**：当 `filesystem` MCP 不可用或路径解析失败时，回退到 CLine 内置的 `read_file` / `write_to_file` / `replace_in_file`。

### 2.3 路径规范

`McpPathStrategy(execEnv) [CM]`：
```
execEnv = sandbox → escaped_or_forward_slash（双反斜杠 \\ 或正斜杠 /）
execEnv = host    → absolute_with_backslash
```

> 派生自 PathStrategy：`[CR: rules.md §5.1 PathStrategy]` | `[CR: project.md §1.4]`

**核心规则**：
- ⚠️ filesystem MCP 必须使用绝对路径（如 `d:\Project\Program\WorkFlowPrj\...`）
- 路径解析失败时，尝试使用项目根目录 `d:\Project\Program\WorkFlowPrj\` 作为前缀
- `ctx_execute` 中的文件路径使用双反斜杠 `\\` 或正斜杠 `/`
- 默认不设置 timeout（由 MCP host 的 RPC 超时控制）

---

## §三、MCP 在引擎循环中的角色

### 3.1 五步模型中的 MCP 位置

```
① Execute       ← MCP 工具执行（根据 ToolSelect 选择具体 MCP）
   ↓ ExecutionFailureGate
② Collect [CM]  ← 收集 MCP 返回结果（extract output + 模式提取）
   ↓
③ Evaluate [QA] ← 评估 MCP 执行质量（pass/partial/fail/parse_error）
   ↓
④ Decide [CD]   ← 决定下一步（TS/TO/TM/switch_strategy/continue）
   ↓
⑤ Adjust [SM]   ← 调整参数/切换 MCP 工具/修复
```

> 全五步模型：`[CR: engine.md §四]`

### 3.2 Execute 阶段的 MCP 调用

`McpExecute(toolName, params) [SM]`：
```
1. 根据 ToolSelect 确定 MCP 工具
2. 路径格式转换（McpPathStrategy）
3. 调用 MCP 工具（含 timeout 和 retry 策略）
4. 收集结果到 Execute.output
```

### 3.3 批量轮询模式 [SM]

`McpBatchPoll(items, batchConfig) [SM]`：
```
1. 使用 ctx_batch_execute 并行处理 items
2. @parallel 标注可并发步骤
3. 遵循 StateSnapshot + StateMerge 公理
4. 合并时按声明顺序，非完成顺序
```

> 循环模式定义：`[CR: engine.md §6.3.3]`

---

## §四、后处理与知识持久化中的 MCP 调用

### 4.1 PostProcess 流程中的 MCP 调用 [SM]

```
PostProcess [SM]:
  ① sequentialthinking_tools → 分析执行结果（推理）
  ② KnowledgeDedup(entity, kb) → 去重检查（仅当 kb.hashes 已初始化时，否则先 search_nodes）
  ③ create_entities / create_relations → 持久化关键发现
  ④ note → 记录观测（搜索后若存在则 add_observations，不存在则 record）
```

> 完整 PostProcess：`[CR: rules.md §八 PostProcess]`

### 4.2 CompactProcedure 中 MCP 调用 [SM]

| 阶段变更 | 动作 | MCP 工具 |
|---------|------|---------|
| `P_plan → P_exec_prep` | `record_plan_to_graph` | `memory.create_entities` |
| `P_exec_prep → P_exec_core` | `index_structure_to_kb` | `ctx_execute → ctx_index` |
| `P_exec_core → P_exec_verify` | `note_changes + persist_reasoning` | `memory.note` / `sequentialthinking_tools` |

> 完整 CompactProcedure：`[CR: rules.md §八 CompactProcedure]`

### 4.3 知识去重策略

`KnowledgeDedup(entity, kb) [CD]`（复用 rules.md 定义）：
```
hash(entity.name + entity.content) ∈ kb.hashes → skip
otherwise → record + kb.hashes.add(hash)
```
---meta
dr = "kb.hashes 未初始化时先 search_nodes(entity.name)，存在则 add_observations，不存在则 record"
---

---

## §五、MCP 错误处理与熔断

### 5.1 MCP 调用错误分类

`McpError(error) [CM]` Provides: MCP 调用错误分类
```
error ∈ {timeout}               → retry (max 2次)
error ∈ {parse_error}           → switch_strategy（换工具/方法）
error ∈ {irrecoverable}         → halt + report
error ∈ {partial_output}        → continue（记录警告）
error ∈ {resource_exhausted}    → report + halt
```

### 5.2 MCP 工具切换策略

`McpSwitchStrategy(toolName, error, counter) [CD]`：
```
counter ≥ MCP_RETRY_LIMIT(3)         → forcehalt
counter < MCP_RETRY_LIMIT ∧ retryable → switch_to_alternative_mcp
counter < MCP_RETRY_LIMIT ∧ fatal     → forcehalt
```

> 与 OnError 循环版协同：`[CR: engine.md §七]`

### 5.3 超时与重试配置

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `MCP_EXEC_TIMEOUT` | `60s` | 单次 MCP 调用超时 |
| `MCP_BATCH_TIMEOUT` | `120s` | 批量操作超时 |
| `MCP_RETRY_LIMIT` | `3` | 最大重试次数 |
| `MCP_RETRY_INTERVAL` | `2s` | 重试间隔 |

---

## §六、MCPBridge（宿主机桥接）

### 6.1 MCPBridge 架构

MCPBridge 是运行在宿主机上的 MCP 服务器，通过 `invoke_ps_script` 工具暴露 PowerShell 脚本/模块调用能力：

```
CLine (Agent) → MCPBridge (子进程) → PowerShell (宿主机)
```

### 6.2 MCPBridge 生命周期

| 方式 | 触发时机 | 适用场景 |
|------|---------|---------|
| **懒加载** | 首次调用 `mcp_call_tool` / `mcp_list_tools` 时 | FileDriven 启动快，Agent 功能按需加载 |
| **预加载** | FileDriven 初始化时一并启动 | 需要 Agent 功能立即可用 |

### 6.3 MCPBridge 注册函数（From Agent 层）

Agent 层通过 FunctionStorageRegistry 注册的 MCP 相关函数：

| 函数名 | 来源 | 功能 |
|-------|------|------|
| `mcp_call_tool` | `Agent.McpTools.McpToolFunction` | 调用 MCP 工具 |
| `mcp_list_tools` | `Agent.McpTools.McpToolFunction` | 列出所有可用 MCP 工具 |

> Agent 注册详情：`docs/analysis/2026/7/7.8/FileDriven集成Agent方案.md §4`

---

## §七、上下文预算策略（与 MCP 协作）

`CB_MCP(actionType) [CM]` Provides: MCP 调用时的上下文预算策略
```
file_read        → partial_read（仅缓存结果摘要）
code_execution   → summary_only（不保留原始输出全文）
batch_operation  → batch_with_queries（ctx_batch_execute 的多查询合并）
doc_web          → index_no_content（索引后检索，不保留原文）
reasoning        → keep_conclusion（仅保留推理结论）
```

> 派生自 CB 策略：`[CR: rules.md §5.2 CB]`

---

## §八、MCP 特有的反模式

| 反模式 | 严重 | 表现 → 正确做法 |
|--------|------|----------------|
| **MCP 路径格式错误** | 高 | 使用正斜杠却被 filesystem 拒绝 → 始终使用绝对路径 `d:\Project\...` |
| **MCP 调用顺序错乱** | 中 | 资源未准备好就调用依赖工具 → 遵循 ToolchainMap 的场景定义 |
| **MCP 结果未校验** | 高 | 盲目信任 MCP 返回结果 → 调用后更新 fileCache 并触发 StateValidity |
| **MCP 调用上下文膨胀** | 中 | 保留 MCP 返回的原始长文本 → 应用 CB_MCP 做摘要 |
| **MCP 工具选择不当** | 中 | 小文件读取也走 MCP 桥接 → ReadStrategy 判断档位（<50KB 直接读取） |
| **MCP 重试无熔断** | 高 | 连续失败仍无限重试 → 超过 MCP_RETRY_LIMIT 时 forcehalt |
| **MCPBatch 状态污染** | 高 | 并行步骤修改同一状态变量 → 标记 parallel_write_conflict |

---

## §九、工具使用总优先级

```
MCP 工具（优先）→ CLine 内置工具（备选）→ CLI 命令（最后手段）
```

详细优先级决策见 ToolPriority：`[CR: rules.md §5.1 ToolPriority]`

---

> **映射关系**：本文档是 clinerules 体系中 MCP 逻辑的单一来源。具体 MCP 服务器配置、安装流程和工具列表见 `docs/CLine_MCP_配置指南.md` 和 `docs/环境配置指南.md`。