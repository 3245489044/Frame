# 标识标准（Fact ID Standard）

> 唯一权威：`Projects/Plug/Agent/doc/会话事实标准与唯一标识-设计与实施方案.md` §1；本 README 与 §1 同源。
> 定位：**事实归属轴**的唯一载体（回答「这条记录属于哪一轮 / 哪一步 / 哪次调用」）。
> 落位（2026-09-24 S1 下沉）：`Agent.Session\Ids` → **`Frame/CsharpPrj/Share/Frameworks/Conversation/Ids`**（平台层，零 Agent 依赖）；Agent 侧目录只留迁移提示（类型转发 shim 实测不可用：C# 编译器不解析 `TypeForwardedTo`）。

## 1 文法

```
RunKey     ::= SessionKey                          // run 级 = 会话 = 任务（runId == TaskKey == HistoryId）
RoundKey   ::= SessionKey "#R" ROUND_NO            // default#R1、tp_cline_toolcall#R4
StepKey    ::= RoundKey "/" STEP_NAME ["#" ATTEMPT] // default#R1/cline_execute#1
CallKey    ::= StepKey "/" ITERATION "/" CALL_ID    // default#R1/cline_execute#1/2/call_00_FJLmIZn
ID         ::= [A-Za-z0-9_.\-]+
```

唯一正则（解析器与机检脚本共用，见 `FactIdGrammar.RoundKeyPattern` / `AnyKeyPattern`；数值段从 1 起，拒前导 0）：

```
^[A-Za-z0-9_.\-]+#R[1-9][0-9]*(?:\/[A-Za-z0-9_.\-]+#[1-9][0-9]*(?:\/[1-9][0-9]*\/[A-Za-z0-9_.\-]+)?)?$
```

## 2 文件职责

| 件 | 职责 |
|---|---|
| `FactIdGrammar.cs` | 文法常量与正则 —— **唯一字面量来源**；其他件禁写 `"#R"` / `"/"` 拼接串（机检 C1） |
| `FactId.cs` | 值对象：段 + `Kind` + `Parse`/`TryParse`/`CompareTo` |
| `FactIds.cs` | **唯一生成器**：`Run`/`Round`/`Step`/`Call`（含入参校验） |
| `ExternalRef.cs` | 外部代号容器（`TsSdk`/`TsDaemon`/`Ecs`/`Lease`）——**只作属性，禁参与关联** |
| `FactIdMigration.cs` | 存量形态归一（纯函数；读侧一次性，**不回改历史行**） |

## 3 不变量

| 编号 | 内容 |
|---|---|
| I1 | 一个 `RoundKey` 恰属一个 `SessionKey`；`StepKey`/`CallKey` 为**纯派生**（不引入新随机源） |
| I2 | 键**不可变**：轮内 `RoundKey` 恒定，重派发不产生新键（代次降为文档属性） |
| I3 | 键**可解析**：任一键可反解出全部段，无需外部查表（run 级键即裸会话键，自身即其会话键） |
| I4 | 键**可排序**：`CompareTo` 数值段感知（`#R10` > `#R2`）；**禁**对键串做字典序比较 |

## 4 废除清单（旧标识资格废除）

| 原形态 | 处置 | 归一 / 落点 |
|---|---|---|
| `default@1` | E2 归一 | `default#R1`（`FactIdMigration.Normalize`） |
| `default#1#1`（lease） | E1+E3 移除标识资格 | 未归因 + `ExternalRef{Lease}`（`gen/seq` 入轮次事实属性） |
| `defaultR1`（obs） | E2 归一 | `default#R1` |
| `opId=GUID` | E1 | 仅 ECS 幂等位，**不进事实面** |
| `conv_*` / `1790*` | E1 | `ExternalRef{TsSdk}`，禁作会话键 |
| `req-1` | E3 | daemon 请求 id 直接用 `RoundKey` |
| `worker-1` | E1 降级 | `actor=agent/cline@worker-1` |
| `{taskKey}@1`（threadId） | E2 归一 | 键名统一 `traceKey`，值 = `RoundKey` |

## 5 三轴边界（防「第六套 ID」）

| 轴 | 回答 | 载体 |
|---|---|---|
| 运行模式轴 | 用什么方式跑 | `ExecutionFlag`（`执行标识符框架设计.md`） |
| 实体定位轴 | 是哪个实体 | `EntityRef`（同上） |
| **事实归属轴** | 属于哪一轮/步/调用 | **`FactId`（本目录）** |

三轴可组合为三元组，**禁合并为一个 ID**（合并即把「值」与「键」混同）。
