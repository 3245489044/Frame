# F0 基线冻结包 —— 2026-09-18

> **定位**：F0 批次的产出物（判据母表 + 基线冻结）。后续每一批收口都以此包为**对拍基准**：
> 红集合须与本节冻结值**逐条同名**；差集只减不增；台账 / 夹具指标只减不增。
> **口径**：全部数字为 2026-09-18 本机实测（命令见 §4）；与《DataCenter-声明式框架补完…》v1.0 文档口径的差异见 §3。

---

## §1 六向完备性矩阵（F0 判据母表）

| 指标 | 冻结值 |
|---|---|
| 词总数（族 = 13） | **127** |
| 差集（任一轴未落地） | **127** |
| ② 结构门：确定缺失 | **0**（F2-b 收口：aggOp 4 + message 2 已核销，见 §6） |
| ② 结构门：无法判定（词表登记待补） | **5**（`Separator` / `Parts` 已补登记；余 5 为宿主内直取成员，见 §6） |
| ③ 读取器解析：无分派（承担方 = 结构门 enum） | **51** |
| ③ 读取器解析：无探针登记 | **62**（aggOp 4 + message 2 已补探针并加读取器分派） |
| ⑤ 实现 / ⑥ 自证：无外来证据 | **127 / 127** |

> 逐行矩阵 = `completeness-matrix.psv`；逐条差异 = `completeness-gaps.txt`；汇总 = `completeness-summary.txt`。

### 1.1 ② 结构门 · 确定缺失（**0 条 = 已清零**）

| 词 | 条数 | 原缺口 | 核销方式 |
|---|---|---|---|
| `aggOp`（sum/max/min/avg） | 4 | 结构门无 enum 绑定（词表 `AggOps` 未被结构门强制） | 词表派生绑定 `$defs/expr/properties/aggOp/enum` + 结构门补 enum（见 §6） |
| `message`（Message/TypeName） | 2 | 结构门节点无 enum；族指针 `$defs/deliver/capture/...` 因 `deliver.capture` 为 `$ref` 而**悬空** | 指针修正为 `$defs/capture/properties/message` + 绑定 `.../enum` + 补 enum（见 §6） |

### 1.2 ② 结构门 · 无法判定（5 条 = 宿主内直取成员，显式登记）

`expr` 族中**无独立顶层 JSON 键**的成员（键在宿主对象内读取，或直取取值来源键），结构门对该层为浅校验：
`Source` · `FirstWhere` · `FirstPath` · `FirstOrNull` · `FoldAs`

> `Separator`（顶层键 `separator`）与 `Parts`（顶层键 `concat`）**已于 F2-b 补登记**；该补登记顺带修正了
> **词表与结构门的不一致**：结构门早已声明 `separator`，而词表 `ExprKeys` 未登记 ⇒ 表达式层白名单此前会拒该键。
> 核销前 7 条 → 现 5 条（5 条均在守卫 `RegisteredSchemaUnregistered` 显式登记）。

### 1.3 ③ 读取器解析 · 承担方口径（本批新登记的事实）

- **有读取器分派**：仅 `impl.kind`（`NormalizeKind` 白名单）与 `dep` 形态位（三选一）；
- **无读取器分派，承担方 = 结构门 enum 绑定**：51 个词位（`convert`/`from`/`select`/`deliver.kind`/`scope.kind`/`fake`/`onBlankFirstKey`/`onDuplicate`）；
- **无探针登记**：62 个词位（`expr` 62；`aggOp` 4 + `message` 2 已于 F2-b 补探针 + 加读取器词位校验）。

> 结论：**标准层读取器只在 `kind` 与 `dep` 形态位做分派，其余词位由结构门承担** ——
> 本事实此前无任何单点登记，F0 起由矩阵固化（F2-b 据此决定「补探针」或「记豁免」）。

---

## §2 其余冻结值

| 面 | 指标 | 冻结值 |
|---|---|---|
| 内核 | `Ports\` 文件数 / 行数 | **41 / 6,153** |
| 夹具 | `[PortImplementation]` / 行数 / 夹内 `private static` | **69 / 3,243 / 215** |
| 台账 | 键数 / 分片 | **110 / 13** |
| 台账 | `impl.kind` 分布 | `scripted=85` · `reflective=20` · `composed=5` · `delegate=0` |
| 台账 | 派生工厂键（`deps[].factory`） | **70** |
| 构建 | `DataCenter.slnx` / `MainProj.sln` | **0 错 0 警 / 0 错 0 警** |
| 测试 | `DataCenter.Standards.Tests` | **101 总 / 99 通过 / 2 跳过 / 0 失败**（F2-b 收口新增 `StandardRelationsTests` 4 例 + ② 无法判定登记判据 1 例） |
| 测试 | `Observability.Tests`（全量，含 `PortKernelDisciplineGuardTests`） | **215 / 215** |
| 测试 | `Agent.Spec.Guard.Tests` | **80 / 80** |
| 台账工具 | `Split-SpecPortLedger.ps1 -Check` | **OK（110 键 / 13 分片）** |
| 测试 | `Declarative.Ledger.Tests`（台账机制 + 文本口径 `PsJsonText` + 区间改写 `LedgerTextEditor`） | **33 / 33** |
| 台账工具 | `LedgerForge check`（F2-a 离线工具，输出与上者逐字一致） | **OK（110 键 / 13 分片）** |
| 台账工具 | `LedgerForge check --bytes` / `compose` / `split`（dry-run 与磁盘文件**逐字节一致**） | **OK（13 分片 + _meta + 产物）** |
| 台账工具 | `LedgerForge set-impl`（与 PS 工具产物**逐字节一致** + 幂等） | **OK（150,215 字节同比特 / 二次运行零变更）** |
| 台账工具 | `LedgerForge normalize-key --check`（与 PS 工具输出**逐字一致**） | **OK（46 份用例 / 零残留）** |
| 台账工具 | `LedgerForge derive-schema`（词表 ↔ 结构门对账） | **OK（enum 绑定 18 / 键集绑定 1）** |
| 台账工具 | `LedgerForge emit-declaration`（声明化骨架 + 缺失落点清单） | **OK（10 族 / 69 键全落点）** |

---

## §3 与方案文档 v1.0 口径的差异（实测校正）

| 项 | 文档 v1.0 | 实测 | 处置 |
|---|---|---|---|
| 夹内私有 helper | 146 | **215** | 口径统一为「`PortFixtures.*.cs` 内 `(?m)^\s*private static `」；F3 递减基准取 **215** |
| `Protocol\Standards` 文件数 | 20 | **25**（含 5 partial + `ExprLayout` + `OutFieldContract`） | 以实测为准 |
| `convert` 词表差 1（`dep`） | 差 1 未被门发现 | **已消**：`Conversions` = 11（含 `DependencyConversion`） | 矩阵 ② 上 `convert` 11 词全绿 |
| 内核行数 | 6,153 | **6,153** | 一致 |
| 夹具行数 / 标注 | 3,243 / 69 | **3,243 / 69** | 一致 |

---

## §4 复算命令

```powershell
# ① 矩阵与差集重生成（显式通道；默认跳过不写盘）
$env:DC_COMPLETENESS_DUMP='1'
dotnet test D:\Project\Main\Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Standards.Tests\DataCenter.Standards.Tests.csproj `
  --filter "FullyQualifiedName~StandardCompletenessTests"
Remove-Item Env:\DC_COMPLETENESS_DUMP

# ② 构建门禁
dotnet build D:\Project\Main\Frame\DataCenter\DataCenter.slnx -clp:ErrorsOnly
dotnet build D:\Project\Main\Frame\CsharpPrj\MainProj.sln -clp:ErrorsOnly

# ③ 测试面
pwsh D:\Project\Main\Environment\CentralTest\Run-CSharpTests.ps1 -Project 'DataCenter.Standards.Tests'
pwsh D:\Project\Main\Environment\CentralTest\Run-CSharpTests.ps1 -Project 'Observability.Tests' -Class 'PortKernelDisciplineGuardTests|E22'
pwsh D:\Project\Main\Environment\CentralTest\Run-CSharpTests.ps1 -Project 'Agent.Spec.Guard.Tests'

# ④ 台账一致性（PS 与离线工具须逐字一致；写侧须逐字节一致）
#    离线工具 = LedgerForge（**自有输出目录**，不进 CsharpPrj 权威烘焙目录）
$LF = 'D:\Project\Main\Frame\CsharpPrj\Plate\DeclarativeLedger.Tools\LedgerForge\BuildOutput\Debug\LedgerForge.dll'
pwsh D:\Project\Main\Environment\CentralTest\Tools\Split-SpecPortLedger.ps1 -Check
dotnet $LF check                  # 语义对拍（输出与上者逐字一致）
dotnet $LF check --bytes          # 追加「产物 + 分片 + _meta」逐字节对拍
dotnet $LF compose                # dry-run：分片 → 产物（与磁盘产物逐字节比较，差异即红）
dotnet $LF split                  # dry-run：产物 → 分片（与磁盘分片逐字节比较，差异即红）
dotnet $LF set-impl --impl <d.json>          # impl 区间改写 dry-run（其余字节逐字保留；--write 才落盘）
dotnet $LF normalize-key --project Agent.Core.Tests --check   # 步骤 kind → 端口键残留检查
dotnet $LF derive-schema          # 词表 → 结构门只读对账（空 = 绿；--write 幂等写回）
dotnet $LF emit-declaration --family <PortFixtures.X.cs>      # 声明化骨架草稿 + 缺失落点清单（哨兵 __BIND__）

# ⑤ 体量指标
$f='D:\Project\Main\Environment\CentralTest\AgentTest\Agent.TestShared\Fixtures\Agent'
(Select-String -Path "$f\PortFixtures.*.cs" -Pattern '\[PortImplementation\(' -AllMatches).Matches.Count
(Get-ChildItem $f -Recurse -Filter '*.cs' | Get-Content | Measure-Object -Line).Lines
(Get-ChildItem "$f\PortFixtures.*.cs" | ForEach-Object {
  ([regex]::Matches((Get-Content $_.FullName -Raw),'(?m)^\s*private static ')).Count } | Measure-Object -Sum).Sum
$k='D:\Project\Main\Frame\CsharpPrj\Strategy\Observability\Ports'
$pf=Get-ChildItem -Recurse $k -Filter '*.cs'; "$($pf.Count) / $((($pf | Get-Content | Measure-Object -Line).Lines))"
```

---

## §5 变更规则

| 场景 | 允许动作 |
|---|---|
| 差集下降（缺项被补齐） | 允许，且须同步更新本包 §1 冻结值（只减不增） |
| 差集上升 | **红**（除 F2-b 门化前的过渡批，须附逐条理由） |
| 夹具 / 派生工厂 / 内核行数上升 | **红**（F2-d / F3 的棘轮口径） |
| 台账键数变化 | 仅声明面变更批，须附逐条理由 + golden 再生 |

---

## §6 收口记录（F2-b · 2026-09-18）

> 本节按 §5「差集下降须同步更新 §1 冻结值」登记**已核销**的缺项；冻结值变动只减不增。

| 项 | 收口前 | 收口后 | 判据 |
|---|---|---|---|
| ② 结构门确定缺失 | 6（aggOp 4 + message 2） | **2**（message） | 新增词表派生绑定 `ports.schema.json#$defs/expr/properties/aggOp/enum`（`SchemaEnumSynchronizer`）+ 结构门补 enum 声明；`LedgerForge derive-schema` 对账绿（enum 绑定 17） |
| `message` 族结构门指针 | `$defs/deliver/capture/properties/message`（`$ref` 嵌套 → 悬空） | **`$defs/capture/properties/message`**（可达节点） | `StandardRelationsTests.DanglingDetector_...`：旧指针判悬空（反证检测器非空转）、当前图悬空 = 0 |
| 关系图能力（A5 并入 F2-b） | 无（仅逐格矩阵） | `StandardRelations` / `RelationGraph`：单跳关系 + **影响分析**（传递闭包）+ 悬空检测 + 词表绑定视图 | `StandardRelationsTests` 4 例（非空转 / 检测器 / 影响闭包 / 确定性） |
| ③ 解析面（`aggOp` / `message`） | 无探针（68 个词位无登记） | **62**（两族补探针 + **读取器词位校验**：`IsKnownAggOp` / `IsKnownMessageMode`） | 探针纪律自证：哨兵被读取器拒（分派成立）+ 合法词被接受 ⇒ ③ 转 Present |
| ② 无法判定（`expr` 族） | 7（成员未登记承载键） | **5**（`Separator` → `separator`、`Parts` → `concat` 已补登记） | `RegisteredSchemaUnregistered` 显式登记余 5 条（宿主内直取），僵尸条目即红 |
| 基线冻结值 | — | §1 ② 缺失 = **0** / ② 无法判定 = **5** / ③ 无探针 = **62**；§2 测试 **101 / 99 / 2 / 0**；`derive-schema` enum 绑定 **18** | 矩阵 / 差集 / 汇总已按 §4 ① 重生成 |

**未做（显式登记，不静默）**：`expr` 族的**成员级**探针（表达式键由结构门 `$defs/expr/properties` 闭合、读取器不分派词位，登记于守卫 `RegisteredParseProbeGaps`）；
`expr` 族 5 个宿主内直取成员的 ② 无法判定（登记于 `RegisteredSchemaUnregistered`，结构门对该层为浅校验）；
PS 命令补口（`new-ledger` / `emit-spec`）；F2-d 二期与 F3 消费侧退场。
