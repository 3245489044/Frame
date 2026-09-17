# DataCenter 标准框架通用化重构 —— 下一步执行清单（M2 → M3 → M4）

> 承接 `B5 + B6 同批交付`（v1.3）。本文档把**剩余工作**整理为可逐批执行的三段，每段含改动面、判据、回退与验收命令。三段串行依赖，收口后跑 §5 全量回归。
> 依赖序（完整）：`B4 ∥ (A1→A2→A3) → B5 → B6 → M2(Boundary 收尾) → M3(B7) → M4(B8)`。

---

## §0 当前状态快照（B5+B6 已交付，作为 M 线起点）

| 维度 | 已达成（B5+B6 v1.3） | 证据 |
|---|---|---|
| 内核退场重复解析 | 删 `Spec\PortLedger*.cs` 三件（1,126 行，grep 归零后删） | `Ports\**` `JsonDocument.Parse` **=2**（ComposedArgs.cs:170 / Value\ScriptedValue.cs:310 专属运行时；旧规格位=0） |
| 接线归位（B6 中立 DTO） | 解析源改**标准目录投影**；中立 DTO 落 SpecHost（`SpecPortStandard.Dto.cs`+`DtoBinding.cs`）；投影方 Agent.TestShared（`AgentPortStandardProjection`+ModuleInitializer）；SpecHost 零新增 `DataCenter.*` 引用 | `SpecHost.csproj` 0 错；`Agent.Spec.Guard.Tests` **74/74** |
| 编译 / 测试全面绿 | — | Observability `0/0` + 守卫+E22 `51/51`；`MainProj.sln` `0/0`；`DataCenter.slnx` `0/0`；`Ports\` 行数 **6,126** ≤ 6,173 |

**尚未收口的偏差（本文档 M2 目标）**：判据②原口径「`Ports\**` 内 `JsonDocument.Parse` 命中=0」因 **Boundary 顺延**暂为 **2**。
Boundary 顺延项目前仍骑旧解析，须在 **M2 改纯投影**后归 0。

---

## §1 下一步总览

| 段 | 主题 | 一句话目标 | 完成标志 |
|---|---|---|---|
| **M2** | Boundary 顺延收尾 | `Boundary\BoundaryContractJson*.cs` 改纯投影 | **已交付（v1.5）**：`Boundary\**` 解析位 **0**；`Ports\**` **2**（运行期已登记，守卫固化）；`Ports\` **6,133** 行；守卫/自证全绿（见 `下一阶段实施计划` §10.1 注记） |
| **M3** | B7 诊断口径归一 | 装载期统一 `StandardViolationException`（含判据号） | 同一份坏台账**只报一次且带判据号**；既有守卫失败信息不退化 |
| **M4** | B8 守卫/清单/文档同步（收口） | 棘轮预算下调 + 清单/文档 100% 对齐 | `MainProj.sln` 与 `DataCenter.slnx` 0 错 0 警；README 与实作一致 |

每段收口都跑 §5 对应验收命令再交下一段；不得跨段跳过。

---

## §2 M2 —— Boundary 顺延收尾（B5 遗留）

| 项 | 内容 |
|---|---|
| **背景** | v1.3 本批顺延 `Boundary\BoundaryContractJson*.cs` 改纯投影，故判据② `JsonDocument.Parse` 暂 =2。本轮专收此口。 |
| **改动面** | `Ports\**\Boundary\BoundaryContractJson*.cs` 两个文件：解析源由内核旧解析改**标准目录投影**（同 B6 所用 `StandardCatalog.Load` 投影侧），消费方改经投影；不动路由面。 |
| **判据** | `Ports\**` 内 `JsonDocument.Parse` 命中 **=0**；`Ports\` 行数 **≤6,173**；`Boundary` 相关既有测试全绿。 |
| **回退** | 单提交可撤（旧件删除 + 引用新增同批）。 |
| **验收** | 见 §5 命令：①(内核) ③ ④ ⑤（B5 判据）全绿；`JsonDocument.Parse`=0。 |

> 注意：本次 Boundary 收尾须**复用已建的投影侧**（Agent.TestShared 投影适配器 / SpecHost Dto 之外的 Boundary 专属 Dto 视需要新增，保持 SpecHost 零新增数据层引用）。勿另起第二套解析。

---

## §3 M3 —— B7 诊断口径归一

| 项 | 内容 |
|---|---|
| **改动面** | 装载期统一 `StandardViolationException`（**含判据号**）；运行期保留内核既有异常，不强行合并。 |
| **判据** | ① 同一份坏台账**只报一次**且带判据号（C1–C13）；② 守卫失败信息**不退化**—— 逐条比对既有守卫的失败文案基线，逐条反压；③ `PortKernelDisciplineGuardTests` 等守卫仍全绿。 |
| **回退** | 文案回滚（异常类型 + 消息体逐项可撤）。 |
| **验收** | 见 §5：③④ 全绿；坏台账自证（同一失败只报 1 条、带判据号）。 |

> 陷阱：装配期 vs 运行期的异常**不得互相污染**——运行期内核异常保留原样，仅装载期口径收敛到统一异常。

---

## §4 M4 —— B8 守卫 / 清单 / 文档同步（收口）

| 项 | 内容 |
|---|---|
| **改动面** | ① `PortKernelDisciplineGuardTests` **棘轮预算下调**至目标值；② `KernelRedLineTests` **公开面清单**；③ `CsharpBakeRefs.props` 烘焙清单（含 M2/M3 新增引用）；④ `DataCenter.slnx` 收录；⑤ 两侧 README / `ModuleRule_*` 对齐 —— `DataCenter.Standards.csproj` 的 `ModuleRule_Description` 补 **C1–C13** 与 **impl 词表**职责。 |
| **判据** | `MainProj.sln` 与 `DataCenter.slnx` **0 错 0 警**；守卫（①–④）全绿；README 与实际 **100% 对齐**（逐条比对）。 |
| **回退** | 逐项可撤（每项独立提交）。 |
| **验收** | 见 §5 全量回归。 |

---

## §5 验收命令（每批收口都跑）

```powershell
# ① 标准层与内核各自编译
dotnet build Frame\DataCenter\DataCenter.slnx -clp:ErrorsOnly
dotnet build Frame\CsharpPrj\Strategy\Observability\Observability.csproj -clp:ErrorsOnly

# ② 聚合回归（同时刷新烘焙副本 → 满足 Check-BakeFramework #6 同比特）
dotnet build Frame\CsharpPrj\MainProj.sln -clp:ErrorsOnly

# ③ 标准层判据（C1–C13 全绿，用例数不降）
# ④ 守卫（含 SpecHost / Guard.Tests / 对拍单侧断言）
dotnet test Environment\CentralTest\AgentTest\Agent.Spec.Guard.Tests\Agent.Spec.Guard.Tests.csproj
dotnet test Environment\CentralTest\CSharpTest\Strategy.Tests\Observability.Tests\Observability.Tests.csproj --filter "FullyQualifiedName~PortKernelDisciplineGuardTests|FullyQualifiedName~E22"

# ⑤ 单点判据（grep 型）
#  M2：Ports\ 内 JsonDocument.Parse 命中 = 0
#      Ports\ 行数 ≤ 6,173
#  M3：坏台账自证 —— 同一失败只报 1 条且带判据号
#  M4：双方 0 错 0 警；README 与实作 100% 对齐
```

---

## §6 变更记录

| 日期 | 版本 | 说明 |
|---|---|---|
| 2026-09-17 | v1.0 | 承接 B5+B6（v1.3），整理剩余为 M2/Boundary 收尾 → M3/B7 → M4/B8 三段执行清单。 |
| 2026-09-17 | v1.1 | 补前置引用（无内容变更）：M3/M4 的前置收敛路线见《DataCenter-标准单源化-重复逻辑收敛-设计与实施.md》P0–P2（词表单源 / 键闭包 / 结构门接线）。 |