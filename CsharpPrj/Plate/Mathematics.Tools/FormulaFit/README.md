# FormulaFit

通用公式拟合框架（**非运行时面**）：读取 `gameeditor-sweep-v1` 的 sweep 结果与 `formula-fit-plan-v1` 拟合计划，调用 `Plate\Mathematics\Fitting` 的通用拟合器，写出 `metric-formula-v1` 公式集。

## 分层

| 层 | 位置 | 说明 |
|---|---|---|
| 通用框架 | `Frame\CsharpPrj\Plate\Mathematics\FormulaFit` | 命名空间 `MathematicalAlgorithms.FormulaFit`，随 `Mathematics.dll` 烘焙；供离线工具/测试直接消费 |
| CLI 外壳 | `Frame\CsharpPrj\Plate\Mathematics.Tools\FormulaFit` | 命名空间 `MathematicalAlgorithms.FormulaFit.Cli`，独立 Exe，自有输出目录 |

## 用法

```powershell
FormulaFit.exe `
  --input "Runtime\Data\Metrics\sweep-20261104.json" `
  --defs  "Runtime\Data\Metrics\fit-plan.json" `
  --out   "Runtime\Data\Metrics\formulas\metric-formulas.json" `
  --now   "2026-11-04T12:00:00+08:00"
```

- `--input` 可重复，多个 sweep 会合并参与拟合。
- `--out` 以 `.json` 结尾时视为文件，否则视为目录并写 `metric-formulas.json`。
- `--now` 固定后，除该字段外输出逐字节确定。
- `--verbose` 输出每个 target 的拟合日志，异常时输出完整堆栈。
- 退出码：`0` 成功；`1` 参数错误；`2` 数据/拟合/校验失败。
- 任一 target 失败时不写出部分公式集。

## 构建与测试

```powershell
# 1) 通用框架（随 Mathematics.dll）
dotnet build "Frame\CsharpPrj\Plate\Mathematics\Mathematics.csproj" -c Debug

# 2) 框架测试（EngineTest）
dotnet test  "Environment\CentralTest\EngineTest\FormulaFit.Tests\FormulaFit.Tests.csproj" -c Debug

# 3) 需要命令行工具时
dotnet build "Frame\CsharpPrj\Plate\Mathematics.Tools\FormulaFit\FormulaFit.csproj" -c Debug
```

- 通用框架随 `Mathematics.dll` 进入 CsharpPrj 权威烘焙目录，`CentralTest` 测试工程按二进制引用直接消费，不依赖 CLI 构建顺序。
- CLI 自有输出目录：`Frame\CsharpPrj\Plate\Mathematics.Tools\FormulaFit\BuildOutput\$(Configuration)\`。
- CLI 不进 `Plate` 伞聚合、不进 `MainProj.sln`。

## sweep 输入（`gameeditor-sweep-v1`）

```json
{
  "schema": "gameeditor-sweep-v1",
  "points": [
    {
      "conditions": { "visible": 128, "chunkSizeCm": 32, "cacheState": "hot" },
      "metrics": {
        "frame.sync":   { "p95Ms": [ 1.76, 1.78, 1.80 ] },
        "frame.stream": { "p95Ms": [ 3.87, 3.91, 3.95 ] },
        "mesh.build":   { "p95Ms": [ 98304.4, 98304.5, 98304.6 ] }
      }
    }
  ]
}
```

- 只接受 `schema = gameeditor-sweep-v1` 且 `points` 非空。
- 数值可以是 `number`、可解析字符串、`value` 对象、`runs` 数组或数字数组。
- 数组/多轮通过 `aggregate` 聚合：`median`（默认）/`mean`/`min`/`max`/`first`/`last`。
- 不接受 `MetricHistogram` 的桶上界近似；请提供精确分位/原始轮次。

## 拟合计划（`formula-fit-plan-v1`）

```json
{
  "schema": "formula-fit-plan-v1",
  "planId": "engine-v1",
  "window": {
    "from": "2026-11-04T11:20:00+08:00",
    "to": "2026-11-04T11:30:00+08:00",
    "conditions": { "build": "Debug", "machine": "local" }
  },
  "targets": [
    {
      "id": "frame.sync",
      "version": 1,
      "kind": "calibrated",
      "shape": "linear",
      "target": "frame.sync",
      "unit": "ms",
      "x": "/conditions/visible",
      "y": "/metrics/frame.sync/p95Ms",
      "aggregate": "median",
      "groupBy": [ "/conditions/chunkSizeCm", "/conditions/cacheState" ],
      "filter": [ { "path": "/conditions/cacheState", "equals": "hot" } ],
      "inputs": [ { "name": "visible", "unit": "count", "min": 16, "max": 256 } ],
      "minimumSamples": 5
    }
  ]
}
```

- `x` / `y` / `groupBy` / `filter.path` 使用 **JSON Pointer（RFC 6901）**；指标名中的点（如 `frame.sync`）是普通字符，无需转义。
- `shape` 支持：`linear`、`cubic-offset`、`power-law`、`rational-saturation`。
- `inputs` 必须恰好一个（单变量）；`name` 会成为公式输入名（如 `visible`、`chunkSizeCm`）。
- `groupBy` 的值会汇总进 `fit.window.conditions`，数值优先按数值排序并规范化为 `R` 格式。
- `filter` 全部命中才参与拟合；未命中静默跳过，命中但缺 x/y 则显式失败。
- `minimumSamples` 默认 5，且不允许低于 5（设计稿硬规则）；低于该值直接失败，不产出公式。

## 输出

- 文档 schema：`metric-formula-v1`。
- 每条公式包含 `id/version/kind/shape/target/unit/inputs/expression/coefficients/fit/residual/domain/status/signature`。
- `status` 由 `FormulaAcceptance` 判定：样本 ≥5、有来源、`maxRel ≤ 20%`、`rmseLog ≤ 0.25` 才为 `active`，否则 `provisional`。
- `fit.sources[].sha256` 为输入 sweep 文件的 SHA-256，便于追溯。
- 签名复用 `Plate\Mathematics\Core\SignatureHash`，系数/域/分段变化都会改变签名。

## 边界

- 本工具只做 **calibrated** 公式；常量/闭式/分段公式由计划文件或后续工具产出。
- 不接运行时热路径，不做在线滚动拟合，不做字符串表达式解析。
- `frame.render` 不纳入；`height.query` 因分辨率不足不拟合；`chunk.visible` 是配额上限，不拟合。
