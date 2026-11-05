# ContractFramework — 契约框架（以契约为语义的通用模块）

> **layer** 1（`Modules` 根）· **依赖** BCL-only（零 ProjectReference）· **落点** `Frame/CsharpPrj/Modules/Foundations/ContractFramework/`

把「契约」升为一等公民：**一切规则 / 门禁 / 校验 / 断言，都必须携带契约身份**（不变量族 + 载体 + 层级 + 处置 + 证据），否则判 `⊥`（不可判定，不放行）。

> 契约 = **声明（`contracts.json`）** ⊕ **执行体（外部）** ⊕ **裁决（三值 + 处置）** ⊕ **取证（可复算命令）**。
> 本框架只负责 **身份 · 注册 · 对账 · 执行 · 落盘**，**不重造规则引擎**（校验逻辑仍由 `ValidationFramework` / Roslyn 分析器 / PowerShell 门禁 / 运行期探针承担）。

## 一 语义模型

```
Contract ≜ ⟨Id, Semantics, Carrier, Tier, Effect⟩ ⊗ Evidence
  Id        : 契约标识（Name + Version，稳定、可反向引用）
  Semantics : 不变量族 + 违规谓词（Violation(n) 可判定）
  Carrier   : 判据载体（结构事实；见下"载体铁律"）
  Tier      : 执行层级 ∈ {Build, Assembly, Gate, Runtime}
  Effect    : 处置 ∈ {FailFast, Block, Heal, Log}
  Evidence  : 可复算证据（recomputeCmd + 正/负对照样本）
```

**载体铁律**：允许 工具名 / 命令形态 / 参数 / 路径起点与深度 / 计数 / 已声明文本 / 内容指纹 / 已注册符号集；
禁止 动机 / 必要性 / 收益 / 副作用 / 风险 / "顺手" / "我已修复" / "应该没问题" —— 含语义词 ⇒ 契约不合格 ⇒ 裁决恒 `⊥`。

**三值裁决**：`T` 成立放行 · `F` 违例（按 Effect）· `⊥` 不可判定（载体不合格 / 证据缺失 / 未注册执行体）⇒ **fail-closed 不放行**。`⊥ ≠ T`。

**元契约（自举）**：`wellformed(C) ↔ Id≠∅ ∧ Semantics≠∅ ∧ Carrier∈StructuralFact ∧ Tier∈{…} ∧ Effect∈{…} ∧ Evidence.recomputeCmd≠∅ ∧ 有正/负对照`；`¬wellformed → ⊥`。

## 二 目录

| 目录 | 内容 |
|---|---|
| `Abstractions/` | 语义模型：`ContractId` / `ContractCarrier` / `ContractHostRef` / `ContractEvidence` / `ContractDescriptor` / `ContractResult` / `IContractHost` / `IContractContext` … |
| `Catalog/` | `ContractsDocument`（POCO）+ `ContractCatalog`（加载/合并/去重） |
| `Registry/` | `ContractRegistry`（注册/查询/反向引用/域分组） |
| `Gate/` | `ContractWellFormedness`（元契约）+ `ContractGate`（唯一执行入口 + 单一抛出点） |
| `Ledger/` | `ContractLedger`（裁决落盘 JSONL + 对账报告） |
| `Diagnostics/` | `ContractDiagnostics`（结构化出口 + 未判定清单） |
| `Verify/` | `ContractFrameworkVerify`（正/负对照自证） |

## 三 接入四步（对齐仓库既有机制）

1. `Frame/CsharpPrj/modules.json` 的 `projects` 增 `"ContractFramework": "Modules\\ContractFramework\\ContractFramework.csproj"`；
2. 运行 `Frame/PowerShellScript/Share/Solution/Generate-ModulePaths.ps1`（生成 `Proj_ContractFramework` / `Mod_ContractFramework`）；
3. `Frame/CsharpPrj/Modules/Modules.csproj` 增 `ProjectReference` + `Compile Remove="ContractFramework\\**\\*.cs"`；
4. 声明：`Frame/CsharpPrj/contracts.index.json` 登记各 `contracts.json`；门禁 `Frame/PowerShellScript/Operation/DSL/Meta/Check-ContractRegistry.ps1`（G0–G4 + 对照）。

> `MainProj.sln` 无需改动（只引聚合工程 `Modules\\Modules.csproj`，子模块经 ProjectReference 传递）。

## 四 消费方式

- **C# 侧（装配期/运行期）**：`ContractCatalog.LoadIndex(index, repoRoot)` → `ContractRegistry.FromCatalog(catalog, hosts)` → `ContractGate.Enforce(contracts, ContractTier.Assembly, ctx, registry)`。
- **PowerShell 侧（门禁期）**：`Check-ContractRegistry.ps1 -Check`（与 C# 同读一份 `contracts.json`）。
- **适配器（执行体）放消费侧**，不放框架内（保持 BCL-only）：`ValidationRuleContractHost` / `RoslynContractHost` / `PowerGateContractHost` / `ProbeContractHost`。

## 五 自证

`ContractFrameworkVerify.Run(out summary)` —— 正/负对照（缺 carrier ⇒ ⊥ / 语义载体 ⇒ ⊥ / 执行体违例 ⇒ F / 未注册执行体 ⇒ ⊥ / 目录去重）。

## 六 诚实边界

消灭的是「**契约缺失导致的静默漂移**」；**不**承诺"人永不写错契约"，只保证——**契约一旦缺失/不合格/实现漂移，必被机器看见（红 / ⊥ / 落盘），而非静默放行**。
