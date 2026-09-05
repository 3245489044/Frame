# PS 日志/模块框架 — owner-aware 化与日志冗余治理 · 下一步实施规划

> 日期：2026-09-02
> 范围：PowerShellScript 加载框架（Modules / PScriptManager）、日志消息注册校验与冗余清理
> 目标文件：`Projects/Agent/src/Agent.Core/Diagnostics/agent_log_messages.json`

---

## 1. 背景与已达成状态

### 1.1 已实现（本阶段完成，可作基线）

**路径无关引导**（解决 `Operation\DSL` 深移一层后 `..\Modules\Import-Modules.ps1` 断链的根因）：
消费脚本以 `.git` 哨兵自锚定定位，不再用带深度的 `..`。

**owner-aware 加载层**（新增，位于 `PowerShellScript/PScriptManager/Framework/`）：

| 文件 | 职责 | 是否入库 |
|---|---|---|
| `Build-FunctionIndex.ps1` | 扫描真实 .ps1/.psm1 定义点 + `# @exports`，产 owner 索引 | 源码 ✓ |
| `ps-function-index.json` | JSON 编译表（159 文件 / 786 函数 / 133 多属主歧义） | **不入库**（.gitignore）|
| `FrameworkInvoke.psm1` | owner-aware 逻辑调用 API（Get-FrameworkOwners / Invoke-FrameworkFunction / Import-FrameworkRequires） | 源码 ✓ |
| `FrameworkRequires.ps1` | 点源入口：把属主函数装进消费脚本自身作用域 | 源码 ✓ |

核心语义：**函数一律按 (owner 文件, function) 限定**；同名多属主不丢弃、不静默覆盖；多属主缺省 `-Owner` 即抛错强制消歧。

**参考消费方接入**：`Operation/DSL/Check-LogMessages.ps1` 改为经新框架引导并跑通
（owner 指定 `Get-ProjectRoot→ReadProjectConfig.ps1`；单属主 `Get-FileList`、`Get-MsgId*` 自动选）。

**PScriptManager 注册表重建**：`psm-registry.json` 用 `Build-Registry.ps1 -ProjectRoot <仓库根>` 重建为
778 函数、无失效路径（原 Install/Core/Executor 等死条目消失）。该文件在 .gitignore 内。

**Modules 侧已还原**：新增散件已删、`Import-Modules.ps1` git 还原、`BuildOutputCleaner.psm1` 未触碰。

### 1.2 遗留现状（下一步处理）

- `agent_log_messages.json` 存在**冗余注册候选 52/160**（只读子串判据：代码无 `"<键>"` 字面量引用）。
- 单向校验缺口：`Check-LogMessages.ps1` 只查「代码引用→是否注册」，**不查「注册→是否有代码调用」**（孤儿）。
- `StringTools.LogMessages.psm1` 的 `IdentLike` 正则**括号不配对**（offset 49 Too many )'s）
  → `Get-MsgIdSourceIndex` 抛错 → `Remove-RedundantLogMessages.ps1` 当前不可运行。
- 同名逻辑函数多处重复（`Get-ProjectRoot` 7 个定义点；owner 索引标记 133 个多属主）。
- `Build-Registry.ps1` / `_init.ps1` 的 `..\..` 仓库根定位少一级（现用显式 `-ProjectRoot` 绕过）。
- `Check-ClinerulesReferences.ps1`、`Remove-RedundantLogMessages.ps1` 仍走旧加载器（路径无关引导已修，未切新框架）。

---

## 2. 方向判据（决策基线）

- **路径无关**：一切以 `.git`/文件名哨兵自锚定，禁止 `..` 深度硬编码。
- **扫描→编译表**：运行时只读 JSON 编译表，不活扫描；表不入库、由脚本重建。
- **不静默冲突**：同名不同文件实现不互相覆盖；歧义显式报告/消歧。
- **不误删**：冗余清理须防 keyPrefix 拆分与运行时拼接误判。
- **最小侵入**：不动既有模块函数；改动集中在框架与消费方引导。

---

## 3. 下一步实施阶段

### Phase A — 修复工具链（阻断项，优先）

**A1 修 `StringTools.LogMessages.psm1` 的 `IdentLike` 正则**
- 现状：`[\"]((?:[A-Z][A-Za-z0-9_]*)_)+[A-Z][A-Za-z0-9_]*?)[\"]` 括号不配对。
- 目标正则（配平，捕获组 1 = 候选 msgId）：`[\"]((?:[A-Z][A-Za-z0-9_]*_)+[A-Z][A-Za-z0-9_]*)[\"]`
- 验证：跑通 `Get-MsgIdSourceIndex`；补单测（CentralTest/PowerShellTests StringTools）。

**A2 修 `Build-Registry.ps1`/`_init.ps1` 仓库根定位**
- 把固定 `..\..` 改为 `.git` 哨兵上溯（自锚定），去掉显式 `-ProjectRoot` 依赖。

**验收**：`Remove-RedundantLogMessages.ps1 -WhatIf` 可运行且不报正则错；`Build-Registry.ps1` 无参数即可自举重建注册表。

### Phase B — 补齐「冗余注册」校验方向

目标：`Check-LogMessages.ps1` 增加**检查 5（注册但无代码引用，孤儿）**，默认 Warn / `-StrictOrphans` 时 Error。

- 复用 A1 修好后的 `Get-MsgIdSourceIndex` + `Test-MsgIdReferenced`（含 keyPrefix 前缀候选）。
- 输出形态对齐既有检查 4：按 msgId + 注册文件 + 详情，附行:列（JSON 无，可给键序）。
- 退出码规则并入现有结构（`-StrictOrphans`）。

**验收**：
- `Check-LogMessages.ps1` 对 `agent_log_messages.json` 报出 52 个孤儿候选（与 1.2 只读结果一致）。
- `-StrictOrphans` 时 52 项作为错误退出（供 CI 决策）。

### Phase C — 冗余清理（经人工复核后）

- 用修好的 `Remove-RedundantLogMessages.ps1`（或新检查 5 清单）跑 `-WhatIf` 复核 52 候选。
- 复核要点：确认非「变量/运行时拼接引用」「keyPrefix 前缀形态」误报。
- 确认后执行删除；删除走既有备份 + `-Verify` 回归，失败自动回退。

**验收**：删除后 `Check-LogMessages.ps1` 不再报相应孤儿项；`Remove-*` 日志无回归。

### Phase D — 加载框架收敛（owner-aware 全面化）

- 把 `Operation/DSL` 另两个校验器（`Check-ClinerulesReferences.ps1`、`Remove-RedundantLogMessages.ps1`）
  的引导切到 `FrameworkRequires.ps1`（与 Check-LogMessages 一致）。
- 明确 `.psm1` 跨模块同名的模块命名空间包装方案，使 `FrameworkInvoke` 的模块路径不依赖裸名。
- 评估把 `Modules/Import-Modules.ps1`（旧加载器）的引导统一收敛到 Framework 层，旧产物只读兼容、逐步退役。

**验收**：三个 DSL 校验器均经新框架运行；`Modules/Import-Modules.ps1` 从「新消费脚本」引用面移除（旧引用仅兼容）。

### Phase E — 重复函数治理（可选，破坏性，需单独确认）

- 用 owner 索引导出「多属主清单」（133 个）作为治理输入。
- 按 `Strategy.SingleImplementation`：为 `Get-ProjectRoot` 等定唯一权威模块，其余改薄委托/删除。
- 仅在权威选型经评审后执行，单独 PR。

---

## 4. 风险与护栏

| 风险 | 护栏 |
|---|---|
| 冗余误删（前缀/拼接） | keyPrefix 候选 + `-WhatIf` 复核 + `-Verify` 回退 |
| 孤儿检查误报 | 报告为「候选」非自动删；输出供人工复核 |
| 改名/删函数后编译表陈旧 | 编译表不入库 + `Build-FunctionIndex` 幂等重建 + CI 跑 Check |
| 旧加载器回归 | Modules/Import-Modules.ps1 保持 git 基线，仅读兼容 |
| 正则/路径改动影响既有调用 | 每个 Phase 以跑通 `Check-LogMessages.ps1` + 相关单测为验收门 |

---

## 5. 依赖与文件清单（改动面）

| 阶段 | 主要改动文件 |
|---|---|
| A | `Modules/StringTools/StringTools.LogMessages.psm1`、`PScriptManager/Commands/Build-Registry.ps1`、`PScriptManager/_init.ps1` |
| B | `Operation/DSL/Check-LogMessages.ps1`（+`StringTools.LogMessages` 判据复用）|
| C | 执行产物（`*_log_messages.json` + `backups/`）|
| D | `Operation/DSL/*`（3 个）、`PScriptManager/Framework/*`、旧 `Modules/Import-Modules.ps1` 引用审计 |
| E | 重复实现模块（待清单定）|
| — | `docs/` 本规划、`PowerShellScript/PScriptManager/Framework/ps-function-index.json`（.gitignore）|

---

## 6. 立即可做的第一步（推荐顺序）

1. **A1**（正则配平，解锁冗余工具与检查 5）——改动小、风险低、收益大。
2. **A2**（自锚定根定位）。
3. **B**（补孤儿检查方向），对 `agent_log_messages.json` 得 52 孤儿清单。
4. **C** 复核并清理（如需）。
5. **D/E** 视资源与评审推进。
