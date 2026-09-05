# CodePathExplorer 分析报告

- **目标路径**: `D:\Project\Program\WorkFlowPrj\Utilitys\CodePathExplorer`
- **文件数**: 12
- **类数**: 22
- **调用链数**: 30
- **字段数据流数**: 9

## 函数调用链

### 入口: CallChainExplorer.ExploreAll

| Chain ID | 调用路径 |
|----------|----------|
| 1 | ExploreAll → ToList → Where → ExploreFromMethod (if (entryMethod == null)) |
| 2 | ExploreAll → ToList → Where → ExploreFromMethod (if (entryMethod == null)) |

### 入口: CallChainExplorer.ExploreFromMethod

| Chain ID | 调用路径 |
|----------|----------|
| 3 | ExploreFromMethod (if (entryMethod == null)) |
| 4 | ExploreFromMethod (if (entryMethod == null)) |

### 入口: FieldFlowExplorer.ExploreAll

| Chain ID | 调用路径 |
|----------|----------|
| 1 | ExploreAll → AnalyzeFieldFlow (if (classDecl == null)) |

### 入口: SimplificationAnalyzer.Analyze

| Chain ID | 调用路径 |
|----------|----------|
| 1 | Analyze → AnalyzeRedundantFields → Add |
| 2 | Analyze → AnalyzeRedundantFields → AnalyzeInternalizableFields → FirstOrDefault → SelectMany → Add |
| 3 | Analyze → AnalyzeRedundantFields → AnalyzeInternalizableFields → FirstOrDefault → SelectMany → AnalyzeRedundantMethods → Add → Add → Contains → Add → Contains → Add → Contains → LastIndexOf → Add → Contains → LastIndexOf |

### 入口: ChainPrinter.PrintReport

| Chain ID | 调用路径 |
|----------|----------|
| 1 | PrintReport → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → PrintCallChains (if (chains.Count == 0)) → AppendLine → AppendLine |
| 2 | PrintReport → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → PrintFieldFlows (if (flowSets.Count == 0)) → AppendLine → AppendLine |
| 3 | PrintReport → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → PrintSuggestions (if (suggestions.Count == 0)) → AppendLine → AppendLine |

### 入口: FormatHelper.FormatChainNode

| Chain ID | 调用路径 |
|----------|----------|
| 1 | FormatChainNode |

### 入口: FormatHelper.FormatChainPath

| Chain ID | 调用路径 |
|----------|----------|
| 2 | FormatChainPath → Join → Select |

### 入口: FormatHelper.GetOperationSymbol

| Chain ID | 调用路径 |
|----------|----------|
| 3 | GetOperationSymbol |

### 入口: FormatHelper.FormatFieldFlowNode

| Chain ID | 调用路径 |
|----------|----------|
| 4 | FormatFieldFlowNode → GetOperationSymbol |

### 入口: FormatHelper.FormatFieldFlowPath

| Chain ID | 调用路径 |
|----------|----------|
| 5 | FormatFieldFlowPath → Join → Select |

### 入口: FormatHelper.GetFieldSetStatus

| Chain ID | 调用路径 |
|----------|----------|
| 6 | GetFieldSetStatus |

### 入口: FormatHelper.GetSuggestionIcon

| Chain ID | 调用路径 |
|----------|----------|
| 7 | GetSuggestionIcon |

### 入口: FormatHelper.GetSuggestionMarkdownIcon

| Chain ID | 调用路径 |
|----------|----------|
| 8 | GetSuggestionMarkdownIcon |

### 入口: LogExporter.ExportToLog

| Chain ID | 调用路径 |
|----------|----------|
| 1 | ExportToLog → CreateDirectory → Add → GroupBy → Add → Count → Add → Add → Add → Add → ToDictionary → GroupBy → ToDictionary → GroupBy → ToDictionary → GroupBy → Combine → Serialize → WriteAllText |

### 入口: LogExporter.GenerateBatchId

| Chain ID | 调用路径 |
|----------|----------|
| 2 | GenerateBatchId |

### 入口: MarkdownExporter.ExportToMarkdown

| Chain ID | 调用路径 |
|----------|----------|
| 1 | ExportToMarkdown → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → ExportCallChainsMarkdown (if (chains.Count == 0)) → AppendLine → AppendLine |
| 2 | ExportToMarkdown → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → ExportFieldFlowsMarkdown (if (flowSets.Count == 0)) → AppendLine → AppendLine |
| 3 | ExportToMarkdown → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → AppendLine → ExportSuggestionsMarkdown (if (suggestions.Count == 0)) → AppendLine → AppendLine |

### 入口: CallChainNode.ToString

| Chain ID | 调用路径 |
|----------|----------|
| 1 | ToString |

### 入口: CallChain.ToString

| Chain ID | 调用路径 |
|----------|----------|
| 2 | ToString |

### 入口: FieldFlowNode.ToString

| Chain ID | 调用路径 |
|----------|----------|
| 1 | ToString |

### 入口: FieldFlow.ToString

| Chain ID | 调用路径 |
|----------|----------|
| 2 | ToString |

### 入口: FieldFlowSet.InvalidateCache

| Chain ID | 调用路径 |
|----------|----------|
| 3 | InvalidateCache |

### 入口: CodebaseScanner.Scan

| Chain ID | 调用路径 |
|----------|----------|
| 1 | Scan (if (!Directory.Exists(directoryPath))) |

## 字段数据流

### CallChainExplorer._context `AnalysisContext`

| Flow ID | 数据流路径 |
|---------|------------|
| 1 | [声明] (字段声明) → [写] CallChainExplorer |
| 2 | [声明] (字段声明) → [读] CollectAllMethods |
| 3 | [声明] (字段声明) → [读] ResolveMethodTarget |

### CallChainExplorer._allMethods `Dictionary<string, MethodDeclarationSyntax>`

| Flow ID | 数据流路径 |
|---------|------------|
| 1 | [声明] (字段声明) → [读] CollectAllMethods |
| 2 | [声明] (字段声明) → [读] CollectAllMethods |
| 3 | [声明] (字段声明) → [读] ExploreAll |
| 4 | [声明] (字段声明) → [读] ExploreFromMethod |
| 5 | [声明] (字段声明) → [读] ProcessSequentialCalls |
| 6 | [声明] (字段声明) → [读] ProcessInvocationsInPath |

### CallChainExplorer._builtInMethods `HashSet<string>` ⚡ 可内化

| Flow ID | 数据流路径 |
|---------|------------|
| 1 | [声明] (字段声明) → [读] IsBuiltIn |

### CallChainExplorer._chainCounter `int`

| Flow ID | 数据流路径 |
|---------|------------|
| 1 | [声明] (字段声明) → [写] ExploreAll |
| 2 | [声明] (字段声明) → [写] CreateChainFromPath |
| 3 | [声明] (字段声明) → [读] CreateChainFromPath |

### FieldFlowExplorer._context `AnalysisContext`

| Flow ID | 数据流路径 |
|---------|------------|
| 1 | [声明] (字段声明) → [写] FieldFlowExplorer |
| 2 | [声明] (字段声明) → [读] CollectAllFields |
| 3 | [声明] (字段声明) → [读] IsFieldReference |
| 4 | [声明] (字段声明) → [读] IsFieldReference |

### FieldFlowExplorer._classFields `Dictionary<string, List<FieldDeclarationSyntax>>`

| Flow ID | 数据流路径 |
|---------|------------|
| 1 | [声明] (字段声明) → [读] CollectAllFields |
| 2 | [声明] (字段声明) → [读] ExploreAll |

### FieldFlowSet._isUnused `bool?`

| Flow ID | 数据流路径 |
|---------|------------|
| 1 | [声明] (字段声明) → [读] IsUnused |
| 2 | [声明] (字段声明) → [读] IsUnused |
| 3 | [声明] (字段声明) → [写] IsUnused |
| 4 | [声明] (字段声明) → [读] IsUnused |
| 5 | [声明] (字段声明) → [写] InvalidateCache |

### FieldFlowSet._isSingleMethodUse `bool?`

| Flow ID | 数据流路径 |
|---------|------------|
| 1 | [声明] (字段声明) → [读] IsSingleMethodUse |
| 2 | [声明] (字段声明) → [读] IsSingleMethodUse |
| 3 | [声明] (字段声明) → [写] IsSingleMethodUse |
| 4 | [声明] (字段声明) → [读] IsSingleMethodUse |
| 5 | [声明] (字段声明) → [写] InvalidateCache |

### CodebaseScanner.ExcludedDirectories `string[]` ⚡ 可内化

| Flow ID | 数据流路径 |
|---------|------------|
| 1 | [声明] (字段声明) → [读] Scan |

## 简化建议

- ⚡ **字段 '_builtInMethods' 仅被 'IsBuiltIn' 使用，可内化为局部变量**
  - 类型: HashSet<string>, 类: CallChainExplorer, 使用方法: IsBuiltIn

- ⚡ **字段 'ExcludedDirectories' 仅被 'Scan' 使用，可内化为局部变量**
  - 类型: string[], 类: CodebaseScanner, 使用方法: Scan

- ⚠️ **方法 'ExploreAll' 未被其他方法调用（孤立入口）**
  - 完整名称: CallChainExplorer.ExploreAll

- ⚠️ **方法 'ExploreFromMethod' 未被其他方法调用（孤立入口）**
  - 完整名称: CallChainExplorer.ExploreFromMethod

- ⚠️ **方法 'ExploreAll' 未被其他方法调用（孤立入口）**
  - 完整名称: FieldFlowExplorer.ExploreAll

- ⚠️ **方法 'Analyze' 未被其他方法调用（孤立入口）**
  - 完整名称: SimplificationAnalyzer.Analyze

- ⚠️ **方法 'PrintReport' 未被其他方法调用（孤立入口）**
  - 完整名称: ChainPrinter.PrintReport

- ⚠️ **方法 'FormatChainNode' 未被其他方法调用（孤立入口）**
  - 完整名称: FormatHelper.FormatChainNode

- ⚠️ **方法 'FormatChainPath' 未被其他方法调用（孤立入口）**
  - 完整名称: FormatHelper.FormatChainPath

- ⚠️ **方法 'GetOperationSymbol' 未被其他方法调用（孤立入口）**
  - 完整名称: FormatHelper.GetOperationSymbol

- ⚠️ **方法 'FormatFieldFlowNode' 未被其他方法调用（孤立入口）**
  - 完整名称: FormatHelper.FormatFieldFlowNode

- ⚠️ **方法 'FormatFieldFlowPath' 未被其他方法调用（孤立入口）**
  - 完整名称: FormatHelper.FormatFieldFlowPath

- ⚠️ **方法 'GetFieldSetStatus' 未被其他方法调用（孤立入口）**
  - 完整名称: FormatHelper.GetFieldSetStatus

- ⚠️ **方法 'GetSuggestionIcon' 未被其他方法调用（孤立入口）**
  - 完整名称: FormatHelper.GetSuggestionIcon

- ⚠️ **方法 'GetSuggestionMarkdownIcon' 未被其他方法调用（孤立入口）**
  - 完整名称: FormatHelper.GetSuggestionMarkdownIcon

- ⚠️ **方法 'ExportToLog' 未被其他方法调用（孤立入口）**
  - 完整名称: LogExporter.ExportToLog

- ⚠️ **方法 'GenerateBatchId' 未被其他方法调用（孤立入口）**
  - 完整名称: LogExporter.GenerateBatchId

- ⚠️ **方法 'ExportToMarkdown' 未被其他方法调用（孤立入口）**
  - 完整名称: MarkdownExporter.ExportToMarkdown

- ⚠️ **方法 'ToString' 未被其他方法调用（孤立入口）**
  - 完整名称: CallChainNode.ToString

- ⚠️ **方法 'ToString' 未被其他方法调用（孤立入口）**
  - 完整名称: CallChain.ToString

- ⚠️ **方法 'ToString' 未被其他方法调用（孤立入口）**
  - 完整名称: FieldFlowNode.ToString

- ⚠️ **方法 'ToString' 未被其他方法调用（孤立入口）**
  - 完整名称: FieldFlow.ToString

- ⚠️ **方法 'InvalidateCache' 未被其他方法调用（孤立入口）**
  - 完整名称: FieldFlowSet.InvalidateCache

- ⚠️ **方法 'Scan' 未被其他方法调用（孤立入口）**
  - 完整名称: CodebaseScanner.Scan

