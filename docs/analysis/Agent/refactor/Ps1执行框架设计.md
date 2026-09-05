# Ps1 执行框架设计（脚本执行模块）

> 日期：2026-08-08
> 状态：设计阶段（待评审）
> 参考：
> - [契约式脚本生成：Agent 工具复用的结构化设计理念](../docs/KnowLedge/契约式脚本生成：Agent工具复用的结构化设计理念.md)
> - [边界即智能：论最优性迷思的终结](../docs/KnowLedge/边界即智能：论最优性迷思的终结.md)
> - [从"省着用"到"循环用"：Agent Token 成本优化的逻辑重构与工程全景](../docs/KnowLedge/从“省着用”到“循环用”：Agent%20Token成本优化的逻辑重构与工程全景.md)
> - 配套：[规划智能体循环框架设计](./规划智能体循环框架设计.md)

## 摘要

构建 **Ps1 执行框架（脚本执行模块）**：让 Cline 执行智能体的每个操作都落到「已有的确定性 Ps1 框架」；Ps1 实现不了 → 先拓展 Ps1 → 再用 Ps1 实现。本框架是调度侧（规划智能体循环框架）的**执行底座**，二者通过任务契约衔接。

**核心原则**：所有操作通过运行 ps1 脚本实现；文件读写优先走 BasicMethod；Ps1 能力不足时，通过**拓展协议（边界重构）**先拓展 Ps1 再实现，禁止退化为临时 Cline 内置工具/手写命令。

---

## 一、知识文档 → 设计原则映射

| 文档 | 原则 | 落点 |
|---|---|---|
| 契约式脚本生成 | 脚本自带四维契约；检索优先于生成；两层检索；复用正反馈 | §三 契约化 + §四 能力解析 + §五 拓展协议 |
| 边界即智能 | 边界不足 → 重构边界而非在错误空间内强解 | §五 CanHandle + 拓展协议 |
| 省着用/循环用 | 复用高频、压缩长尾；能力飞轮 | §五.6 能力飞轮；Ps1 = 复用资产 |

---

## 二、现状盘点（可复用资产）

| 资产 | 位置 | 复用方式 |
|------|------|----------|
| 脚本注册表 | `PowerShellScript/PScriptManager`（`psm-registry.json`、元数据标签） | Ps1 能力注册/解析底座 |
| 能力库 | `PowerShellScript/BasicMethod`（FileOps/Core/DevOps/Tooling） | 确定性文件/命令操作 |
| 决策框架 | `PowerShellScript/AIDecisionFramework` | 固化「所有操作通过运行 ps1 脚本实现」+ N/T 矩阵 |
| MCP 桥 | `MCPBridge invoke_ps_script` | 宿主机 Ps1 调用通道 |
| 规范层 | `.clinerules` + `Instruction/` | 固化「Ps1 优先 + 拓展协议」 |

---

## 三、契约元数据规范（《契约式脚本生成》落地）

在 PScriptManager 头部元数据标签基础上，扩展契约四维 `@ps-contract`，复杂输出结构用可选 sidecar `*.contract.json`：

```powershell
# @ps-contract: file.ops.batch_replace
# @category: FileOps
# @alias: batch-replace
# @requires: BasicMethod
```

```json
{
  "identity":  { "id": "file.ops.batch_replace", "category": "FileOps", "purpose": "SEARCH/REPLACE 块精确替换" },
  "params":    [ { "name": "Path",   "type": "string", "required": true, "constraint": "glob" },
                 { "name": "Blocks", "type": "array[SEARCH/REPLACE]", "required": true } ],
  "output_schema": { "status": "string(success|failure)", "message": "string", "data": "object", "errors": "array" },
  "env_tags":  { "ps_version": "5.1+", "admin": false, "os": "windows", "modules": ["BasicMethod"] }
}
```

## 四、能力解析器（两层检索）

1. **精确过滤**：按任务动作类别 + 环境条件（category/env_tags）过滤注册表 → 候选集（确定性）。
2. **语义精匹配**：候选集内语义匹配 → 最优 Ps1 函数，经 `psm-registry.json` 解析到物理路径。
3. 命中 → 执行；未命中 → §五 边界判定。

## 五、能力边界判定与拓展协议

### 5.1 能力边界判定 `CanHandle(task, capability)`

确定性门控：任务可脚本化（逻辑闭合）∧ 存在匹配契约 ∧ 参数/环境可满足 → `true`（执行）；否则 `false`（进入拓展协议）。**禁止**在 `false` 时退化为临时 Cline 内置工具/手写命令（防「开环自治」反模式）。

### 5.2 确定性执行包装

统一入口（复用 `invoke_ps_script` / `ClineClient` 内置）：参数校验（对照 params）→ 环境校验（对照 env_tags）→ 执行 → 结构化输出（status/message/data/errors）。文件类操作强制经 BasicMethod（编码检测/备份/差异预览/交互确认），不直接走 Cline 内置读写。

### 5.3 拓展协议（边界重构，《边界即智能》落地）

1. **检测**：`CanHandle=false` 且任务逻辑闭合（可脚本化）。
2. **决策**：按 AIDecisionFramework N/T 决策矩阵判断是否值得固化（一次性但极耗时 → 也应生成一次性脚本）。
3. **生成**：规划器（SK）依据契约模板生成合规 Ps1（含四维契约 + 结构化输出）。
4. **注册**：PScriptManager 扫描注册 + contract 入库。
5. **执行**：用新脚本执行当前任务。
6. **升级**：同类别达到入库阈值（≥3 次/30 天）→ 正式固化复用；否则标记一次性，避免污染库。

### 5.4 能力飞轮

使用越多 → 注册表越丰富 → 命中率越高 → 生成减少 → 效率越高（冷启动即首脚本可用）。

---

## 六、规范模式（固化层）

- `.clinerules/project.md` 增补「Ps1 优先 + 边界拓展协议」小节（操作一律走 Ps1、Ps1 实现不了先拓展 Ps1）。
- Cline 系统提示词（Instruction）纳入：能力解析 → 命中执行 / 未命中走拓展协议，杜绝临时命令。
- 与现有 `AX.FailureBudgetEscalation` / `rule_match` 语义对齐，不新增逻辑算子。

## 七、文件结构（增量）

```
PowerShellScript/PScriptManager/{contract-schema.json, manifest.json 增标签}
PowerShellScript/PScriptManager/Contract/*.ps1             (契约生成脚手架)
.clinerules/project.md                                     (规范固化)
Agent/docs/Ps1执行框架设计.md                               (本文件)
```

## 八、验证与验收标准

1. **Ps1 优先**：任务可被既有 Ps1 契约满足时，执行路径不落任何 Cline 内置工具。
2. **边界拓展闭环**：`CanHandle=false` 且逻辑闭合时，能生成合规契约 Ps1 并入库复用。
3. **确定性输出**：执行结果符合 output_schema（status/message/data/errors）。
4. **参数/环境预校验**：不合规参数或环境在执行前被拦截。

## 九、待决问题（实现阶段确认）

1. L0 系统提示词「Ps1 优先」规范的具体措辞与死命令（需按任务领域填充，与调度侧文档联动）。
2. Ps1 契约 schema 的版本号与 sidecar 文件命名约定。
3. 拓展协议生成的 Ps1 是否需要人工确认后入库（安全边界）。

