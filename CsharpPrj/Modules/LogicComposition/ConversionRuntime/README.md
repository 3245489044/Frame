
# ConversionRuntime — 运行期 JSON 出口（对接 Plate.JsonMethod）

## 为什么需要这一层

转换内核 `Conversion\`（`LogicComposition.Conversion`）是 **netstandard2.0 零依赖**工程 —— 这是 Roslyn 源生成器（分析器）
能引用它的前提，因此它**不能**引用平台级 JSON 框架 `Plate.JsonMethod`（net10.0-windows）。

但仓库既有纪律是「所有上层模块应通过 `Plate.JsonMethod` 处理 JSON」。二者冲突的解法是把**运行期出口**单列一层：

| 侧 | 出口 | 适用 | 理由 |
|---|---|---|---|
| 分析器侧（编译期） | `Conversion\Emitters\JsonTextEmitter` | Roslyn 源生成器 / MSBuild Task 内联 | netstandard2.0 零依赖，分析器进程可加载 |
| **运行期** | **本层 → `Plate.JsonMethod`** | 宿主/CLI/测试基础设施 | 平台 JSON 单一出口：缩进/编码/诊断/计量/路径/合并全部复用 |

## 组成

| 类型 | 职责 |
|---|---|
| `ConversionNodeJsonNodeBridge` | IR ↔ `System.Text.Json.Nodes.JsonNode` 双向桥（键序保序，往返不重排） |
| `JsonMethodJsonEmitter` | IR → JSON 文本；缩进/编码取自 `JsonSerializerFactory.GetOptions(JsonContext)`（`PrettyPrint` / `Compact`） |
| `JsonMethodJsonArtifact` | 运行期产物：`Text`（含头注释）/ `Json`（正文）/ `Node` / `Header` / `Size`（`JsonSizeInfo`）/ `TotalBytes` |
| `JsonMethodConversionDiagnostics` | `JsonException` → 内核诊断：消息复用 `JsonErrorDiagnostics`（路径/行号/列位置/实际值/期望类型），位置还原为 `SourceSpan` |
| `ConversionJsonRuntime` | 门面：`Emit` / `EmitPretty` / `EmitCompact` / `Parse` / `FromJsonNode` / `Analyze` |

## 复用的 JsonMethod 能力（不重复实现）

| 需求 | 用哪个（**唯一来源**） |
|---|---|
| JSON 文本解析 | `JsonSerializationService.ParseJsonNode` / `TryParseNode` |
| 缩进/紧凑上下文 | `JsonSerializerFactory.GetOptions(JsonContext.PrettyPrint / Compact)` |
| 解析错误富化 | `JsonErrorDiagnostics.Enrich`（路径、行号、列位置、实际值、期望类型） |
| 体积/元素计量 | `JsonSizeAnalyzer.AnalyzeJsonSize`（`RawBytes` / `ElementCount` / `EstimatedKb`） |
| 路径导航（读取/写入/枚举/字符串映射） | `JsonPathHelper`（`ResolveJsonPath` / `SetJsonPath` / `GetAllPaths` / `ReadStringMap`） |
| 深/浅合并、数组策略 | `JsonMergeService` |
| JSON 语法/结构校验 | `JsonValidationService` |
| 标量换算 | `JsonValueConverter` / `EnumConvert` |

> 上表即「运行期 JSON 能力仅在 JsonMethod 实现一次」的落点；转换内核不再新增 JSON 读写/合并/路径实现。

## 使用示例

### IR → JSON（运行期）

```csharp
var root = new ConversionObject()
    .Add("suite", "axioms.strategy-guard")
    .Add("cases", new ConversionArray().Add(new ConversionObject().Add("id", "a.b")));

var pretty  = ConversionJsonRuntime.EmitPretty(root, "生成物，勿手改（由 compact.specs 展开）");
var compact = ConversionJsonRuntime.EmitCompact(root);

if (!pretty.IsSuccess) { Console.WriteLine(pretty.GetDiagnosticsString()); return; }

Console.WriteLine($"正文 {pretty.Artifact!.JsonBytes} B / 产物 {pretty.Artifact.TotalBytes} B / 元素 {pretty.Artifact.ElementCount}");
File.WriteAllText(targetPath, pretty.Artifact.Text);   // 正文用 .Json（无头注释）
```

### 既有 JSON → IR（对拍「展开产物 ≡ 现行 JSON」）

```csharp
var ir = ConversionJsonRuntime.Parse(File.ReadAllText(existingSpecPath), existingSpecPath);
if (!ir.IsSuccess) { Console.WriteLine(ir.GetDiagnosticsString()); return; }   // 含 文件(行,列)

// 之后可套用内核规则/发射器做语义等价比较
```

### 解析失败定位

```csharp
var bad = ConversionJsonRuntime.Parse("{ \"a\": }", "Cases\\x.spec.json");
// 诊断形如：Cases\x.spec.json(1,7): JSON-PARSE JSON 反序列化失败: 目标类型=JsonNode … 路径: $.a …
```

## 设计边界

- 本层**只做桥接与出口**，不新增 JSON 语义（解析/合并/路径/校验一律委托 JsonMethod）。
- 头注释用 JSONC（`// …`），消费方需以 `JsonCommentHandling.Skip` 读取（`TestInfrastructure.DataDriven.SpecJson` 即如此）。
- `JsonMethodConversionDiagnostics` 的列位置是**字节**偏移（`JsonException.BytePositionInLine`），与字符列存在多字节偏差（如中文行）。
