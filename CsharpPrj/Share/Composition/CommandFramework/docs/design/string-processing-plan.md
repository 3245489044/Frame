# 字符串和参数处理逻辑集中整理方案

## 一、当前分布现状

### 1.1 文件分布概览

当前 CommandFramework 中与字符串和参数处理相关的代码分散在多个目录中：

| 文件路径 | 命名空间 | 核心职责 | 归类 |
|----------|----------|----------|------|
| `Execution/StringHelper.cs` | `CommandFramework.Execution` | 字符串拆分、转义处理 | 字符串处理 |
| `Execution/ArgumentBuilder.cs` | `CommandFramework.Execution` | 参数构建、类型转换 | 参数处理 |
| `CommandParser.cs` | `CommandFramework.Parsing` | 命令解析（含字符串拆分调用） | 命令解析 |
| `WorkflowExecution/Engine/ParameterResolver.cs` | `WorkflowExecution.Engine` | 参数解析器实现（参数合并、节点结果存储） | 参数处理 |
| `WorkflowExecution/Abstractions/IParameterResolver.cs` | `WorkflowExecution.Abstractions` | 参数解析器接口 | 参数处理 |

### 1.2 当前目录结构

```
CommandFramework/
├── Abstractions/                    # 抽象接口
│   ├── IExecutionContext.cs        # 包含 IParsedCommand 接口 + IExecutionContext 接口
│   └── ...
├── Execution/                      # 执行层（混合职责）
│   ├── StringHelper.cs             # 字符串处理（应迁移）
│   ├── ArgumentBuilder.cs          # 参数处理（应迁移）
│   ├── CommandEngine.cs            # 命令引擎（保留）
│   ├── FunctionExecution.cs        # 函数执行（保留）
│   ├── CommandResults.cs           # 命令结果（保留）
│   ├── StateStoreExtensions.cs     # 状态存储扩展（保留）
│   ├── InMemoryStateStore.cs       # 内存状态存储（保留）
│   ├── EnvironmentAdapterBase.cs   # 环境适配器（保留）
│   └── ControlValueAccessorBase.cs # 控件值访问器（保留）
├── WorkflowExecution/              # 工作流执行（应移除参数解析器）
│   ├── Engine/
│   │   └── ParameterResolver.cs    # 参数解析器（应迁移）
│   └── Abstractions/
│       └── IParameterResolver.cs   # 参数解析器接口（应迁移）
├── CommandParser.cs                # 命令解析器（应迁移）
└── ...
```

### 1.3 问题分析

| 问题 | 影响 | 严重程度 |
|------|------|----------|
| 字符串处理分散在 `Execution/` 和根目录 | 查找困难，职责不清 | 中 |
| 参数处理分散在 `Execution/` 和 `WorkflowExecution/` | 跨目录依赖，维护成本高 | 中 |
| `Execution/` 目录混合多种职责 | 命名空间与职责不匹配 | 低 |
| `CommandParser.cs` 位于根目录 | 与 `Parsing` 命名空间不符 | 低 |
| `ParameterResolver` 位于工作流目录 | 与参数处理核心逻辑分离 | 中 |

---

## 二、整理目标

### 2.1 目标

1. **统一位置**：将字符串处理和参数处理逻辑集中整理
2. **清晰职责**：按职责划分目录，每个文件职责单一
3. **命名空间一致性**：目录结构与命名空间保持一致
4. **最小改动**：保持对外 API 兼容性，最小化重构影响

### 2.2 整理后的目录结构

```
CommandFramework/
├── Abstractions/                    # 保持不变
│   ├── IExecutionContext.cs        # 保留 IParsedCommand 接口（与 IExecutionContext 紧密耦合）
│   └── ...
├── Execution/                      # 保持不变（移除字符串/参数相关文件）
├── Parsing/                        # 新增：字符串和命令解析
│   ├── StringUtils.cs              # 字符串处理工具
│   ├── CommandParser.cs            # 命令解析器
│   └── ParsedCommand.cs            # 解析结果模型（从 CommandParser.cs 提取）
├── Parameters/                     # 新增：参数处理
│   ├── Abstractions/
│   │   └── IParameterResolver.cs   # 参数解析器接口
│   ├── ArgumentBuilder.cs          # 参数构建器
│   └── ParameterResolver.cs        # 参数解析器实现
├── WorkflowExecution/              # 清理（移除参数解析器相关）
│   ├── Engine/                     # 移除 ParameterResolver.cs
│   └── Abstractions/               # 移除 IParameterResolver.cs
└── ...
```

### 2.3 目录职责划分

| 目录 | 职责描述 | 包含文件 |
|------|----------|----------|
| `Parsing/` | 字符串拆分、命令解析、解析结果模型 | `StringUtils.cs`, `CommandParser.cs`, `ParsedCommand.cs` |
| `Parameters/` | 参数构建、参数类型转换、参数解析与存储 | `ArgumentBuilder.cs`, `ParameterResolver.cs`, `IParameterResolver.cs` |

### 2.4 文件职责映射

| 新文件 | 来源 | 职责 |
|--------|------|------|
| `Parsing/StringUtils.cs` | `Execution/StringHelper.cs` | 字符串拆分、转义处理 |
| `Parsing/CommandParser.cs` | 根目录 `CommandParser.cs` | 命令解析器 |
| `Parsing/ParsedCommand.cs` | `CommandParser.cs` 内部类 | 解析结果模型（提取） |
| `Parameters/Abstractions/IParameterResolver.cs` | `WorkflowExecution/Abstractions/` | 参数解析器接口 |
| `Parameters/ArgumentBuilder.cs` | `Execution/ArgumentBuilder.cs` | 参数构建、类型转换 |
| `Parameters/ParameterResolver.cs` | `WorkflowExecution/Engine/` | 参数解析器实现 |

### 2.5 不迁移的文件

| 文件 | 原因 |
|------|------|
| `Abstractions/IExecutionContext.cs` | `IParsedCommand` 与 `IExecutionContext` 紧密耦合（`IExecutionContext.ParsedCommand` 属性），提取会碎片化 |

---

## 三、迁移步骤

### Phase 1：创建新目录结构

**步骤 1.1**：创建 `Parsing/` 和 `Parameters/Abstractions/` 目录

```
mkdir Parsing
mkdir Parameters
mkdir Parameters/Abstractions
```

**步骤 1.2**：移动文件到新位置

| 源文件 | 目标文件 | 操作 |
|--------|----------|------|
| `Execution/StringHelper.cs` | `Parsing/StringUtils.cs` | 移动 + 重命名 |
| `CommandParser.cs` | `Parsing/CommandParser.cs` | 移动 |
| `Execution/ArgumentBuilder.cs` | `Parameters/ArgumentBuilder.cs` | 移动 |
| `WorkflowExecution/Abstractions/IParameterResolver.cs` | `Parameters/Abstractions/IParameterResolver.cs` | 移动 |
| `WorkflowExecution/Engine/ParameterResolver.cs` | `Parameters/ParameterResolver.cs` | 移动 |

### Phase 2：重构命令解析器

**步骤 2.1**：从 `Parsing/CommandParser.cs` 提取 `ParsedCommand` 类到 `Parsing/ParsedCommand.cs`

**步骤 2.2**：更新 `Parsing/CommandParser.cs` 移除内部类定义，添加对 `ParsedCommand.cs` 的引用

**步骤 2.3**：保持 `IParsedCommand` 接口在 `Abstractions/IExecutionContext.cs` 中的位置不变

### Phase 3：更新命名空间

**步骤 3.1**：更新 `Parsing/StringUtils.cs` 的命名空间

```csharp
// 旧
namespace CommandFramework.Execution

// 新
namespace CommandFramework.Parsing
```

**步骤 3.2**：`CommandParser.cs` 命名空间已为 `CommandFramework.Parsing`，无需变更

**步骤 3.3**：更新 `Parsing/ParsedCommand.cs` 的命名空间

```csharp
namespace CommandFramework.Parsing
```

**步骤 3.4**：更新 `Parameters/ArgumentBuilder.cs` 的命名空间

```csharp
// 旧
namespace CommandFramework.Execution

// 新
namespace CommandFramework.Parameters
```

**步骤 3.5**：更新 `Parameters/Abstractions/IParameterResolver.cs` 的命名空间

```csharp
// 旧
namespace WorkflowExecution.Abstractions

// 新
namespace CommandFramework.Parameters.Abstractions
```

**步骤 3.6**：更新 `Parameters/ParameterResolver.cs` 的命名空间和 using 语句

```csharp
// 旧
namespace WorkflowExecution.Engine

// 新
namespace CommandFramework.Parameters

// 更新 using
// 旧：using WorkflowExecution.Abstractions;
// 新：using CommandFramework.Parameters.Abstractions;
```

### Phase 4：更新引用

**步骤 4.1**：全局搜索并更新所有 `using CommandFramework.Execution.StringUtils` → `using CommandFramework.Parsing.StringUtils`

**步骤 4.2**：全局搜索并更新所有 `using CommandFramework.Execution.ArgumentBuilder` → `using CommandFramework.Parameters.ArgumentBuilder`

**步骤 4.3**：全局搜索并更新所有 `using WorkflowExecution.Abstractions.IParameterResolver` → `using CommandFramework.Parameters.Abstractions.IParameterResolver`

**步骤 4.4**：全局搜索并更新所有 `using WorkflowExecution.Engine.ParameterResolver` → `using CommandFramework.Parameters.ParameterResolver`

**步骤 4.5**：更新 `CommandParser.cs` 和 `ParsedCommand.cs` 中的内部引用

### Phase 5：清理旧文件

**步骤 5.1**：删除旧文件

```
rm Execution/StringHelper.cs
rm Execution/ArgumentBuilder.cs
rm WorkflowExecution/Engine/ParameterResolver.cs
rm WorkflowExecution/Abstractions/IParameterResolver.cs
```

### Phase 6：验证

**步骤 6.1**：编译项目验证

**步骤 6.2**：运行单元测试验证

---

## 四、命名空间变更对照表

| 旧命名空间 | 新命名空间 | 影响范围 |
|------------|------------|----------|
| `CommandFramework.Execution.StringUtils` | `CommandFramework.Parsing.StringUtils` | 所有字符串处理调用方 |
| `CommandFramework.Execution.ArgumentBuilder` | `CommandFramework.Parameters.ArgumentBuilder` | 所有参数构建调用方 |
| `CommandFramework.Parsing` | `CommandFramework.Parsing` | 无变更 |
| `WorkflowExecution.Abstractions.IParameterResolver` | `CommandFramework.Parameters.Abstractions.IParameterResolver` | 工作流引擎 |
| `WorkflowExecution.Engine.ParameterResolver` | `CommandFramework.Parameters.ParameterResolver` | 工作流引擎 |

---

## 五、风险评估

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| 命名空间变更导致编译错误 | 高 | 高 | 全局搜索替换，逐个验证 |
| `ParsedCommand` 提取导致类型引用错误 | 中 | 高 | 先提取类，再更新引用 |
| 文件移动导致 IDE 引用失效 | 中 | 中 | 使用 IDE 重构功能（移动文件） |
| `StringUtils` 重命名影响外部代码 | 低 | 中 | 保留旧命名空间的兼容层（可选） |
| `ParameterResolver` 迁移影响工作流引擎 | 中 | 高 | 先更新引用，再编译验证 |

### 可选：兼容层方案

如果需要保持向后兼容性，可以在旧目录保留兼容包装类：

```csharp
// Execution/StringUtils.cs — 兼容层（可选）
namespace CommandFramework.Execution
{
    public static class StringUtils
    {
        public static List<string> SplitWithQuotesAndEscape(string input, char separator, bool preserveQuotes = false, bool restoreUnclosedQuote = false)
            => Parsing.StringUtils.SplitWithQuotesAndEscape(input, separator, preserveQuotes, restoreUnclosedQuote);
        
        public static List<string> SplitEscaped(string input, char separator = ',')
            => Parsing.StringUtils.SplitEscaped(input, separator);
        
        public static string[] SplitEscapedToArray(string input, char separator = ',')
            => Parsing.StringUtils.SplitEscapedToArray(input, separator);
        
        public static List<string> SplitCommand(string command, bool restoreUnclosedQuote = true)
            => Parsing.StringUtils.SplitCommand(command, restoreUnclosedQuote);
        
        public static List<string> SplitArguments(string rawArgs)
            => Parsing.StringUtils.SplitArguments(rawArgs);
    }
}

// Execution/ArgumentBuilder.cs — 兼容层（可选）
namespace CommandFramework.Execution
{
    public static class ArgumentBuilder
    {
        public static string?[] SplitArguments(string rawArgs, int expectedCount = -1)
            => Parameters.ArgumentBuilder.SplitArguments(rawArgs, expectedCount);
        
        public static object?[]? BuildFromDescriptor(IFunctionDescriptor descriptor, object? parameters)
            => Parameters.ArgumentBuilder.BuildFromDescriptor(descriptor, parameters);
        
        public static object?[] BuildFromDelegate(Delegate del, object? parameters)
            => Parameters.ArgumentBuilder.BuildFromDelegate(del, parameters);
    }
}
```

---

## 六、验证清单

- [ ] 所有文件已移动到新位置
- [ ] 所有命名空间已更新
- [ ] 所有 `using` 语句已更新
- [ ] `ParsedCommand` 类已提取到独立文件
- [ ] `ParameterResolver` 及其接口已迁移到 `Parameters/`
- [ ] 项目编译通过
- [ ] 单元测试全部通过
- [ ] 旧文件已删除
- [ ] 兼容层（如果需要）已实现

---

## 七、总结

通过本次整理，将实现：

1. **字符串处理集中化**：`Parsing/StringUtils.cs` 作为唯一入口
2. **命令解析集中化**：`Parsing/CommandParser.cs` 和 `Parsing/ParsedCommand.cs` 统一管理命令解析逻辑
3. **参数构建集中化**：`Parameters/ArgumentBuilder.cs` 统一管理参数构建和类型转换
4. **参数解析集中化**：`Parameters/ParameterResolver.cs` 和 `Parameters/Abstractions/IParameterResolver.cs` 统一管理参数存储和合并
5. **命名空间一致性**：`CommandFramework.Parsing` 和 `CommandFramework.Parameters` 分别覆盖字符串解析和参数处理逻辑

**边界清晰**：
- `Parsing/` 负责「解析阶段」：字符串拆分、命令解析、解析结果
- `Parameters/` 负责「执行阶段」：参数构建、参数存储、参数合并

建议按照 **Phase 1 → Phase 6** 的顺序逐步执行，每完成一个阶段后进行编译验证，确保迁移过程可控。
