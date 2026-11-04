# MathematicalAlgorithms.FormulaFit

通用 sweep → 公式拟合框架（纯离线后处理，不是运行时热路径）。随 `Mathematics.dll` 烘焙，供离线工具与 EngineTest 直接消费。

## 公共 API

| 类型 | 作用 |
|---|---|
| `FormulaFitPlan` / `FormulaFitTargetPlan` | `formula-fit-plan-v1` 计划模型与校验 |
| `SweepDocument` / `SweepPoint` | `gameeditor-sweep-v1` 读取、JSON Pointer 取数、多轮聚合 |
| `FormulaFitRunner` | 按 target 抽取样本、调用 `Fitting` 拟合、生成 `FormulaSet` |
| `FormulaFitRunResult` / `FormulaFitTargetReport` | 显式成功/失败与逐 target 报告 |

内部实现：
- `FormulaFitSamples`：筛选、x/y 抽取、样本数下限、证据来源集合；
- `FormulaFitConditions`：`groupBy` 汇总、数值规范化排序；
- `JsonPointer`：RFC 6901 解析。

## 最小用法

```csharp
FormulaFitPlan plan = FormulaFitPlan.Load(planPath);
SweepDocument sweep = SweepDocument.Load(sweepPath);

FormulaFitRunResult result = FormulaFitRunner.Run(
    plan,
    new[] { sweep },
    generatedAt,
    TextWriter.Null);

if (!result.Success)
    throw new InvalidOperationException(string.Join("；", result.Errors));

result.FormulaSet!.Save(outputPath);
```

## 纪律

- 只做 `calibrated` 目标；常量/闭式/分段公式不在此拟合。
- 样本下限默认 5，计划不得低于 5。
- 拟合失败或任一 target 缺 x/y 时**不产出部分公式集**。
- 不夹取、不外推；域校验与越域三态由 `MathematicalAlgorithms.Formula` 负责。
- 输出按 `id` / 系数名 / 来源排序，固定 `generatedAt` 时逐字节确定。
- 签名复用 `MathematicalAlgorithms.Core.SignatureHash`，系数/域/分段变化都会改变签名。

## 测试

`Environment\CentralTest\EngineTest\FormulaFit.Tests`（MSTest，二进制消费 `Mathematics.dll`）。

```powershell
dotnet build "Frame\CsharpPrj\Plate\Mathematics\Mathematics.csproj" -c Debug
dotnet test  "Environment\CentralTest\EngineTest\FormulaFit.Tests\FormulaFit.Tests.csproj" -c Debug
```

## CLI

`Frame\CsharpPrj\Plate\Mathematics.Tools\FormulaFit`（独立 Exe，命名空间 `MathematicalAlgorithms.FormulaFit.Cli`，自有输出目录）。
