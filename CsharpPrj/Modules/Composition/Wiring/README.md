# Modules.Wiring — 端口接盒通用框架（Wiring Fabric）

> layer=2。判据载体 = 键集合 / 形态 / 坐标（结构事实），不含执行者叙述。

## 六词

| 词 | 定义 | 承载 |
|---|---|---|
| Form | 值形态签名 | `Model/FormSpec` |
| Port | `{Owner, Name, Dir, Form, Required, Card, Scope}` | `Model/PortSpec` |
| Box | 实现体 + 端口集合（不含连接知识） | `Model/BoxSpec` |
| Wire | `From → To` + `Adapter/Judge/OnMissing/Merge/Bridge` | `Model/WireSpec` |
| Junction | 运行期唯一投递器 | `Runtime/Junction` |
| Profile | 接线表子集 | `Model/ProfileSpec` |

## 三法则

- **W1 形态闭合**：wire 两端 Form 相等，或存在已声明 Adapter（D2）。
- **W2 单写者**：同一 inport 恰一条 wire；汇聚须显式 `merge`（D4）。
- **W3 无隐式**：禁同名自动连接作为生产语义；连接只由 `[Wire]` 建立（D3 + 门禁）。

## 编译流水线

`Collect → Link → Verify → Plan → Freeze`；运行期 `Junction.Deliver` → `IWireLedger.Record`。
运行期零解析、零反射（`WiringPlanner` 生成委托 + 索引）。

## 声明边界（重要）

本工程**不解析 `.ushar`**：声明→模型 的唯一入口是 `Abstractions/IWiringDeclarationSource`。
ushar 适配器属上层（复用既有 `Usharp.*` / `DeclarationSpecLoader`），避免第二套行解析器（`Strategy.SingleImplementation`）。

## 依赖面

`ProjectReference = { Modules.KeySpace, Modules.Parsing.Keys }`。出现第三个引用须登记理由。

## 不复用项

- 不做工作流引擎（顺序归上层 `[Flow]`）
- 不做 DI 容器（`Junction` 只做投递，不解析服务）
- 不做序列化/DTO（载荷以 JSON/BCL 为界）
