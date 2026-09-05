# PsRuntimeBinding 标准化 + 文件夹分析 → 标准存储 设计文档

> **日期**：2026-08-14
> **范围**：① 在 `FileDriven/DrivenConfig/Templates/AgentRun/task.md` 增加「自然语言 prompt 让 AI 创建任务」示例；② Cline 经 `Agent/PsRuntimeBinding` 标准化运行（统一 `Invoke-AgentStep -Action <verb.object>` 调用 + 结构化结果）；③ 读到的有价值文件在框架内直接转换为标准存储节点（`ur_resources`），不再额外"生成节点"。
> **关联**：`Agent/docs/plans/2026-08-13-ps1-runtime-framework-plan.md`（PsRuntime 引擎原始计划）、`Agent/src/Agent.Standardization/Dsl/storage-template.dsl.md`（标准存储权威定义）、`Agent/skills/*`（外部挂载技能框架）。
> **状态**：设计（待实施）

---

## 一、背景与目标

**诉求**：让用户用一句自然语言创建任务 → Cline 分析指定文件夹的逻辑 → 把分析结果**结构化返回为标准存储**。Cline 的一切确定性文件操作都应走 `PsRuntimeBinding`，且每次调用标准化；分析过程中读到的"有价值文件"能在框架内直接转成标准存储节点，无需再手动生成节点。

**三个目标**：

1. **模板示例**：`task.md` 增加一个自然语言 prompt 示例（`op=enqueue`，`input` 为自然语言），模板层零引擎改动即可落地。
2. **PsRuntimeBinding 标准化**：统一 `Invoke-AgentStep -Action <verb.object> -Params @{}` → 结构化 `{status,message,data,errors}`；补齐分析流程所需的确定性动作（文件夹枚举 / 标准存储写入）。
3. **读文件 → 标准存储节点**：读到的有价值文件在框架内（薄绑定/动作）直接转成 `ur_resources` 资源节点，单一写入路径、不复制 schema。

---

## 二、现状能力盘点（含文件目录）

### 2.1 任务创建链（已具备）

| 环节 | 位置 | 职责 |
|------|------|------|
| 任务模板 | `FileDriven/DrivenConfig/Templates/AgentRun/task.md` + `task.json` | `op=enqueue` 把自由文本 `input` 作为 `user_input` 入队 |
| 任务池 | `Agent/src/Agent.Core/TaskPool/`（`AgentTaskPoolAdapter` / `TaskGraphService`） | 写 `ur_tasks` 即激活消费循环 |
| 主循环 | `Agent/src/Agent.Core/`（`SnapshotDrivenLoopEngine` → `RunRound` 骨架五步） | `provide_prompt` → `cline_execute` → `rule_match` |
| 提示词装配 | `Agent/src/Agent.Core/Prompts/PromptFrameworkFunction.cs`（外部挂载技能注入点） | L0 + SkillBlock + L1 + L2 → system 槽位 |

> **结论**：`task.md` 的 `input` 已是自由文本，自然语言 prompt 可直接经此链路到达 Cline。模板层可行性 = ✅。

### 2.2 PsRuntimeBinding（已有地基）

| 文件 | 职责 |
|------|------|
| `Agent/PsRuntimeBinding/Invoke-AgentStep.ps1` | Agent 薄绑定：加载 PsRuntime + `Config/ps1-runtime.json`，暴露 `Invoke-AgentStep` |
| `Agent/PsRuntimeBinding/Invoke-AgentStep.Tests.ps1` | 加载/命中/边界 Pester 测试 |
| `Agent/Config/ps1-runtime.json` | 绑定配置（projectRoot / contractDirs / basemethodDeps / registry / usage） |

**调用形态（已标准化）**：

```powershell
Invoke-AgentStep -Action "read.file" -Params @{ Path = "..." }
# → { status:"success", message, data:{Path,TotalLines,Lines,Content}, errors:[] }
# 未命中 → failure + errors[0]="no_capability"；缺参 → failure
```

### 2.3 契约运行引擎（PsRuntime，已具备）

`PowerShellScript/PsRuntime/`：`_init.ps1`（加载器）· `Bootstrap/Register-PsContracts.ps1`（契约扫描）· `Runtime/Invoke-PsAction.ps1`（★LLM 单一入口）· `Runtime/Invoke-PsContract.ps1`（stage 化执行）· `Runtime/OutputSchema.psm1`（`{status,message,data,errors}` 信封）· `Extension/*`（拓展协议）。

### 2.4 现有确定性动作（BasicMethod 契约）

| 动作 | 契约文件 | 能力 |
|------|----------|------|
| `read.file` | `PowerShellScript/BasicMethod/FileOps/FileAccess.contracts.json` | 读文件（行号区间/原始模式） |
| `write.file` | 同上 | 写文件（追加/编码/备份/校验） |
| `replace.file.batch` | `PowerShellScript/BasicMethod/FileOps/FileBatch.contracts.json` | 批量替换 |
| `resolve.path` | `PowerShellScript/BasicMethod/Core/Path.contracts.json` | 路径解析（容器/宿主机转换） |

### 2.5 标准存储权威定义

`Agent/src/Agent.Standardization/Dsl/storage-template.dsl.md`：

```
[Template] resource: 确定性产物(代码/文档/配置) | kind=resource | table=ur_resources | mode=upsert | fields=symbol,metadata
```

**标准存储节点** = `ur_resources` 一行 `{ResourceType, Id, symbol, metadata}`，经 `IResourceReferenceStore.AddResourceAsync(ResourceIdentifier, metadata, symbol)` 写入（SQLite 真源）。分析结论与读到的文件均为"确定性产物" → 落 `resource`。

---

## 三、核心设计：PsRuntimeBinding 标准化 + 「读文件→标准存储节点」

### 3.1 三层架构总览

```
Cline
  │ 统一调用：Invoke-AgentStep -Action <verb.object> -Params @{}
  ▼
① 标准化动作集（BasicMethod 契约，零 LLM，确定性）
   read.file        ← 增加可选 Persist 开关（命中后自动把文件转为资源节点）
   list.dir         ← 新增：枚举文件夹（Path / Recurse / Pattern）
   store.resource   ← 新增：写标准资源节点（resourceType, id, symbol, metadata → ur_resources）
   resolve.path / write.file / replace.file.batch（已有）
  ▼
② 存储桥（单一写入路径，SingleImplementation）
   store.resource 与 read.file-Persist 的落库走同一个确定性 C# 写入入口
   （复用 ur_resources 既有写路径，PS 不复制 schema，避免双真源）
  ▼
③ 流程 skill（外部挂载框架，作为 Cline 的操作指南）
   analyze-folder：resolve.path → list.dir → read.file(-Persist) → 组结构化分析
                 → store.resource → 返回节点引用
```

### 3.2 关键原则

| 原则 | 说明 |
|------|------|
| **统一入口** | Cline 唯一调用 `Invoke-AgentStep -Action <verb.object> -Params @{}`，动作在前、参数在后（对齐 LLM 语序） |
| **结构化信封** | 每个动作返回 `{status,message,data,errors}`；`no_capability` / 缺参 / 执行失败均有明确错误码 |
| **确定性动作** | `list.dir` / `store.resource` / `read.file` 均为确定性模块（零 LLM），LLM 只做意图解析与参数选择 |
| **单一写入路径** | 标准存储写入统一走一个 C# 入口，PowerShell 不复制 `ur_resources` schema |
| **有价值才落库** | `read.file -Persist` 显式开关（或策略谓词），不自动全量落库，避免污染存储 |

### 3.3 新增/扩展动作定义

#### 3.3.1 `list.dir`（新增，文件夹枚举）

```jsonc
// PowerShellScript/BasicMethod/FileOps/FileListing.contracts.json
{
  "identity": { "action": "list.dir", "verb": "list", "object": "dir",
    "purpose": "枚举文件夹内容（文件+子目录，支持递归/通配）", "function": "List-Dir",
    "path": "PowerShellScript\\BasicMethod\\FileOps\\FileListing.psm1", "version": "1.0" },
  "params": [
    { "name": "Path", "type": "string", "required": true, "constraint": "非空，相对项目根或绝对路径" },
    { "name": "Recurse", "type": "switch", "required": false, "default": false, "constraint": "递归子目录" },
    { "name": "Pattern", "type": "string", "required": false, "default": "*", "constraint": "通配模式（如 *.cs）" }
  ],
  "output_schema": { "status": "success|failure", "message": "string",
    "data": { "Path": "string", "Files": "array", "Dirs": "array" }, "errors": "array" }
}
```

#### 3.3.2 `store.resource`（新增，写标准资源节点）

```jsonc
// PowerShellScript/BasicMethod/Storage/StorageNode.contracts.json
{
  "identity": { "action": "store.resource", "verb": "store", "object": "resource",
    "purpose": "写标准资源节点到 ur_resources（resourceType,id,symbol,metadata）", "function": "Store-Resource",
    "path": "PowerShellScript\\BasicMethod\\Storage\\StorageNode.psm1", "version": "1.0" },
  "params": [
    { "name": "ResourceType", "type": "string", "required": true, "default": "node", "constraint": "资源类型（分析结论=analysis；文件=file；一般=node）" },
    { "name": "Id", "type": "string", "required": true, "constraint": "资源唯一 id" },
    { "name": "Symbol", "type": "string", "required": false, "constraint": "符号名（如 analysis:xxx / 文件名）" },
    { "name": "Metadata", "type": "object", "required": false, "constraint": "结构化元数据（分析结论 JSON / 文件元数据）" }
  ],
  "output_schema": { "status": "success|failure", "message": "string",
    "data": { "ResourceType": "string", "Id": "string", "Upserted": "bool" }, "errors": "array" }
}
```

#### 3.3.3 `read.file`（扩展：增加 `Persist` 开关）

`PowerShellScript/BasicMethod/FileOps/FileAccess.contracts.json` 的 `read.file` 增加一个可选参数：

```jsonc
{ "name": "Persist", "type": "string", "required": false, "default": "off",
  "constraint": "off=不落库 | on=读后写入 ur_resources 资源节点 | auto=按策略谓词(扩展名/大小)自动落库" }
```

- `Persist=on` / 命中 `auto` 谓词：命中成功后，薄绑定组合 `read.file` 结果 → `store.resource`，把文件转成 `file:<path>` 资源节点（`symbol=文件名`，`metadata={path,totalLines,contentHash}`）。

### 3.4 存储桥（第②层，单一写入路径）

> **目标**：`store.resource` 与 `read.file-Persist` 落 `ur_resources`，只走一个确定性 C# 写入口，PowerShell 不复制 schema。

| 桥接形态 | 说明 | 取舍 |
|----------|------|------|
| **推荐**：`store.resource` 函数委托给既有 C# 资源写入入口 | 复用 `IResourceReferenceStore.AddResourceAsync` 同一写路径（如 `Agent` 注册/`AgentUi` 的 `addResource` 后端） | 符合 SingleImplementation；PS 只发请求不写库 |
| 备选：PS 直接写 SQLite | 复制 `ur_resources` 建表/upsert | 双真源漂移风险，不推荐 |

> **调用约定**：`store.resource` 契约的 `function` 内部经一条稳定的 CLI/接口调用既有 C# 写入口；接口契约由 `Agent/Config/ps1-runtime.json` 的 `storage` 段配置（`storeEndpoint` / `resourceType` 默认）。

---

## 四、task.md 自然语言示例（待落地）

在 `FileDriven/DrivenConfig/Templates/AgentRun/task.md` 末尾新增一节「示例（op=enqueue，自然语言 prompt，Cline 经 PsRuntimeBinding 分析文件夹并结构化入库）」。

### 4.1 示例 JSON

```json
{
  "action": "task",
  "op": "enqueue",
  "input": "分析指定文件夹的逻辑并按标准存储入库。目标文件夹：D:\\Project\\Program\\WorkFlowPrj\\Storage\\xxx。请严格按 PsRuntimeBinding 标准化动作执行：resolve.path 定位 → list.dir 枚举 → 对有价值文件 read.file（带 Persist，读后自动转 ur_resources 资源节点）→ 分析依赖/职责/数据流 → 将分析结论作为标准资源节点 store.resource 写入（symbol=analysis:xxx，metadata 为结构化 JSON）→ 返回该节点引用。",
  "taskKey": "tp_analyze_folder_xxx",
  "mode": "Hybrid",
  "priority": 0,
  "maxRetries": 0
}
```

### 4.2 字段语义

| 字段 | 值 | 说明 |
|------|----|------|
| `op` | `enqueue` | 入队即激活消费（写 `ur_tasks`） |
| `input` | 自然语言 prompt | 成为 `user_input`，指导 Cline 走 PsRuntimeBinding 标准化动作 + 标准存储入库 |
| `mode` | `Hybrid` | LLM 导演（Cline 自由分析）；如需纯规则可换 `Config` + `schedulerRulesKey` |
| `taskKey` | `tp_analyze_folder_xxx` | 业务幂等键（去重 / 暂停恢复 / 查询） |

### 4.3 自然语言 prompt 要点（写入 `input`）

1. **明确目标文件夹**（绝对路径）。
2. **强制标准化工具调用**：点名 `PsRuntimeBinding` / `Invoke-AgentStep` 与动作序列 `resolve.path → list.dir → read.file(-Persist)`。
3. **明确落库契约**：读到的有价值文件 `read.file -Persist` 自动转 `ur_resources` 资源节点；分析结论 `store.resource` 写入 `analysis:<folder>` 节点（`metadata` 为结构化 JSON）。
4. **明确返回**：返回标准存储节点引用（`ResourceType:Id`），供主循环/后续任务消费。

---

## 五、analyze-folder 操作指南 skill（待落地）

新增挂载 skill `D:\Project\Program\WorkFlowPrj\Agent\skills\analyze-folder\SKILL.md`，让 Cline 在运行时获得该流程的标准化操作指南。

```yaml
---
name: analyze-folder
description: 分析指定文件夹逻辑并结构化入库——经 PsRuntimeBinding 标准化动作（resolve.path/list.dir/read.file(-Persist)/store.resource）把结论与有价值文件写入标准存储 ur_resources
kind: operation
tags: [analyze, folder, psruntime, resource, ur_resources, list.dir, store.resource]
trigger: analyze-folder
layer: L2
schema: ResourceIdentifier
---
```

**skill 正文内容**：
- **何时用**：用户要求"分析某文件夹的逻辑/结构/依赖"。
- **标准化动作序列**（零 LLM 确定性）：
  1. `resolve.path` 定位文件夹绝对路径；
  2. `list.dir -Path <folder> -Recurse` 枚举文件/子目录；
  3. 对有价值文件 `read.file -Persist on`（自动转 `file:<path>` 资源节点）；
  4. 组结构化分析结论（依赖/职责/数据流，JSON）；
  5. `store.resource -ResourceType analysis -Id <folder> -Symbol analysis:<folder> -Metadata <结论JSON>`；
  6. 返回 `analysis:<folder>` 节点引用。
- **校验**：`listReferences` / `getResource` 确认节点已建、无悬空。
- **职责边界**：LLM 只做意图解析与参数选择；写图/落库走确定性动作（`list.dir` / `read.file` / `store.resource`）。

---

## 六、具体文件目录清单（新增 / 修改）

> 路径均为绝对目录；`[新增]` / `[修改]` 标注。分 P0/P1/P2/P3 四阶段（见 §七）。

### P0：模板示例 + 操作指南 skill（立即可落地，无引擎改动）

| 动作 | 类型 | 具体文件目录 |
|------|------|--------------|
| 加自然语言示例 | `[修改]` | `D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\Templates\AgentRun\task.md` |
| 加流程 skill | `[新增]` | `D:\Project\Program\WorkFlowPrj\Agent\skills\analyze-folder\SKILL.md` |

### P1：PsRuntimeBinding 动作补齐（标准化动作集）

| 动作 | 类型 | 具体文件目录 |
|------|------|--------------|
| `list.dir` 契约 + 实现 | `[新增]` | `D:\Project\Program\WorkFlowPrj\PowerShellScript\BasicMethod\FileOps\FileListing.contracts.json`、`D:\Project\Program\WorkFlowPrj\PowerShellScript\BasicMethod\FileOps\FileListing.psm1` |
| `store.resource` 契约 + 实现 | `[新增]` | `D:\Project\Program\WorkFlowPrj\PowerShellScript\BasicMethod\Storage\StorageNode.contracts.json`、`D:\Project\Program\WorkFlowPrj\PowerShellScript\BasicMethod\Storage\StorageNode.psm1` |
| `read.file` 加 `Persist` 参数 | `[修改]` | `D:\Project\Program\WorkFlowPrj\PowerShellScript\BasicMethod\FileOps\FileAccess.contracts.json`、`D:\Project\Program\WorkFlowPrj\PowerShellScript\BasicMethod\FileOps\FileAccess.psm1` |

### P2：存储桥（单一写入路径）

| 动作 | 类型 | 具体文件目录 |
|------|------|--------------|
| 绑定配置加 `storage` 段 | `[修改]` | `D:\Project\Program\WorkFlowPrj\Agent\Config\ps1-runtime.json` |
| `store.resource`/`read.file-Persist` 委托既有 C# 写入口 | `[修改]` | `D:\Project\Program\WorkFlowPrj\Agent\PsRuntimeBinding\Invoke-AgentStep.ps1`（薄绑定组合/委托） |
| 复用既有资源写路径（SingleImplementation） | `[复用]` | `IResourceReferenceStore.AddResourceAsync`（`ur_resources` SQLite 真源，C# 侧） |

### P3：测试

| 动作 | 类型 | 具体文件目录 |
|------|------|--------------|
| `list.dir` / `store.resource` / `read.file-Persist` 单测 | `[新增]` | `D:\Project\Program\WorkFlowPrj\CentralTest\PowerShellTests\PsRuntimeBinding\`（对齐既有 `Invoke-AgentStep.Tests.ps1`） |
| 端到端「分析文件夹→标准存储节点」演示 | `[新增]` | `D:\Project\Program\WorkFlowPrj\FileDriven\DrivenConfig\Scripts\Agent\`（对齐既有 `agent_task_node_loop_test.json`） |

---

## 七、分阶段实施计划

| 阶段 | 内容 | 验收 |
|------|------|------|
| **P0** | `task.md` 加自然语言示例；新增 `analyze-folder/SKILL.md` | 文档示例完整；skill 能被挂载框架解析（YAML front-matter 合规） |
| **P1** | 新增 `list.dir` / `store.resource`；`read.file` 加 `Persist` | `Invoke-Pester` 新动作单测全绿；构建不影响 C#（纯 PS） |
| **P2** | 存储桥：`ps1-runtime.json` 加 `storage` 段；薄绑定组合 read→store | `store.resource` 落 `ur_resources`；`read.file -Persist on` 自动生成 `file:<path>` 节点 |
| **P3** | 端到端验证：分析文件夹 → `ur_resources` 出现 `analysis` + 文件节点 | `dotnet build Agent/Agent.sln` 0 警告 0 错误；端到端脚本通过 |

> 每阶段结束保持：`Invoke-Pester`（PowerShell 测试）与 `dotnet build`（C# 侧，若涉及）全绿。

---

## 八、验证标准

1. **模板示例可用**：`task.md` 自然语言示例可直接贴入 `AgentUi_RunJson`，`action=task` + `op=enqueue` 入队即激活。
2. **PsRuntimeBinding 标准化**：Cline 全程只发 `Invoke-AgentStep -Action <verb.object>`；每个动作返回 `{status,message,data,errors}`；`no_capability` / 缺参 / 执行失败有明确错误码。
3. **动作补齐**：`list.dir` 枚举文件夹；`store.resource` 写 `ur_resources`；`read.file -Persist on` 自动转资源节点。
4. **标准存储单一写入**：读文件与分析结论都经同一 C# 写入口落 `ur_resources`，PowerShell 不复制 schema（无双真源）。
5. **端到端闭环**：Cline 分析 `Storage\xxx` → `ur_resources` 出现 `analysis:<folder>` 节点（`metadata` 为结构化 JSON）+ 各有价值文件的 `file:<path>` 节点 → 无悬空引用。

---

## 九、关键取舍

| 取舍 | 选择 | 理由 |
|------|------|------|
| 读文件自动落库 vs 显式 | **显式 `-Persist` + 可选 `auto` 策略谓词** | 避免每个临时读污染标准存储；"有价值"需策略而非全量 |
| 落库路径 | **复用单一 C# 写入入口（SingleImplementation）**，PS 不写 SQLite | 消除双真源漂移，符合 RedundancyElimination |
| `store.resource` 放 BasicMethod vs 薄绑定 | 放 **BasicMethod 契约**（`store.resource`），薄绑定只做配置与组合 | 保持 PsRuntime 通用、Agent 侧只留薄绑定（与现有架构一致） |
| task.md 示例 `mode` | **Hybrid**（示例）+ 注明可换 Config | 分析需 LLM；规则化流程留作可选 |
| 标准存储目标表 | **`ur_resources`（`resource` 模板，确定性产物）** | 分析结论与读到的文件均属确定性产物；`ur_outputs` 为主循环执行结果，不混用 |

---

## 十、与现有资产的关系

| 资产 | 关系 |
|------|------|
| `Agent/docs/plans/2026-08-13-ps1-runtime-framework-plan.md` | 本设计复用它确立的 PsRuntime 引擎（verb.object / 契约 / 信封 / 拓展协议），只扩展动作集与薄绑定 |
| `Agent/skills/*`（外部挂载技能框架） | `analyze-folder` 是挂载框架的第四个技能，运行时注入 system 槽位作为 Cline 操作指南 |
| `Agent/src/Agent.Standardization/Dsl/storage-template.dsl.md` | 标准存储节点（`resource`→`ur_resources`）的权威来源，本设计只引用不改 |
| `FileDriven/DrivenConfig/Templates/AgentRun/*` | `task.md` 加示例；`resource.md`/`reference.md` 为确定性兜底模板，可复用 |

---

> **一句话总结**：`task.md` 加自然语言示例（P0 立即可落地）→ Cline 经 `PsRuntimeBinding` 统一 `Invoke-AgentStep -Action <verb.object>` 标准化运行 → 补齐 `list.dir` / `store.resource` / `read.file-Persist` 三个确定性动作 → 经单一 C# 写入口把读到的有价值文件与分析结论直接转为 `ur_resources` 标准存储节点 → `analyze-folder` 挂载 skill 指导整个流程。



