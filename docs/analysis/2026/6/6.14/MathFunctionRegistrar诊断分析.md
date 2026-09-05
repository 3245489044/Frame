# MathFunctionRegistrar 诊断分析

## 一、当前问题

### 1.1 逻辑过于分散

MathFunctionRegistrar 的注册逻辑分散在多个地方：

| 位置 | 职责 | 问题 |
|------|------|------|
| `MathFunctionRegistrar.cs` | 程序集加载 + 反射提取 + 委托包装 + 注册 | 职责过多，难以维护 |
| `MathLibrary.cs` | 基础数学运算注册 | 包含 `_registered` 去重标志 |
| `BuiltInUnitsLibrary.cs` | 类型转换单元注册 | 包含 `_registered` 去重标志 |
| `FunctionStorageLifecycle.Initialize()` | 统一注册入口 | 调用顺序硬编码 |

### 1.2 核心痛点

1. **添加新数学类需要修改 C# 代码**：每次新增数学类都要在 `MathFunctionRegistrar.cs` 中添加 `ProcessEntry` 调用
2. **委托包装逻辑重复**：参数解析、结果格式化逻辑散落在各处
3. **去重逻辑分散**：每个 Library 类都有自己的 `_registered` 标志
4. **配置与代码耦合**：类名、命名规则、过滤规则硬编码在 C# 中

## 二、重构方案：通用注册框架

### 2.1 架构设计

```
┌─────────────────────────────────────────────────────────────┐
│                    AutoRegisterEngine                        │
│  (通用注册引擎 - 从 JSON 配置到 IFunctionRegistry 一站式)    │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────────┐  ┌──────────────────┐                 │
│  │ AutoRegisterService│  │ IDelegateWrapper │                 │
│  │ (提取→过滤→命名)   │  │ (委托包装策略)    │                 │
│  └────────┬─────────┘  └────────┬─────────┘                 │
│           │                      │                           │
│           ▼                      ▼                           │
│  ┌─────────────────────────────────────────┐                │
│  │           IFunctionRegistry              │                │
│  │         (目标注册表)                      │                │
│  └─────────────────────────────────────────┘                │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 核心组件

#### 2.2.1 `IDelegateWrapper` — 委托包装器接口

```csharp
public interface IDelegateWrapper
{
    string WrapperType { get; }
    DelegateType Wrap(RegisteredItemInfo item);
}
```

- **PassthroughWrapper**：透传包装器，直接使用 `item.Executor`
- **MathWrapper**：数学函数包装器，支持参数解析和结果格式化
- **WrapperConfig**：JSON 配置中指定包装器类型

#### 2.2.2 `AutoRegisterEngine` — 通用注册引擎

```csharp
public class AutoRegisterEngine
{
    // 从配置文件处理并注册到 registry
    List<(RegistrationEntry, AutoRegisterResult)> ProcessConfig(
        string configPath, IFunctionRegistry registry);

    // 从配置目录批量处理
    Dictionary<string, List<...>> ProcessConfigDirectory(
        string configDir, IFunctionRegistry registry);
}
```

#### 2.2.3 JSON 配置格式

```json
{
  "registrations": [
    {
      "source": { "type": "assembly", "path": "Mathematics.dll" },
      "className": "MathematicalAlgorithms.Maths",
      "naming": { "convention": "NestedClass_MethodName", "separator": "_" },
      "description": { "source": "xmlDoc" },
      "methodFilters": {
        "includeStaticOnly": true,
        "excludeByReturnType": ["Void"],
        "excludeMethodNames": ["GetType", "ToString", "Equals", "GetHashCode"]
      },
      "wrapper": {
        "type": "math",
        "paramParsing": "auto",
        "resultFormat": "auto"
      }
    }
  ]
}
```

### 2.3 重构后的注册流程

```
MathFunctionRegistrar.RegisterAll()
    │
    ▼
查找 math-register-config.json
    │
    ▼
AutoRegisterEngine.ProcessConfig()
    │
    ├─ 1. 读取 JSON 配置
    ├─ 2. AutoRegisterService.ProcessBatch()
    │      ├─ 反射提取方法
    │      ├─ 过滤方法
    │      ├─ 生成注册名称
    │      └─ 创建 Executor 委托
    ├─ 3. IDelegateWrapper.Wrap()
    │      ├─ PassthroughWrapper（直接透传）
    │      └─ MathWrapper（参数解析 + 结果格式化）
    └─ 4. IFunctionRegistry.Register()
```

### 2.4 新增数学类的步骤

**只需在 JSON 配置中添加条目**，无需修改 C# 代码：

```json
{
  "registrations": [
    // 已有的 Maths 类
    { "className": "MathematicalAlgorithms.Maths", ... },
    // 新增的聚类算法类
    { "className": "MathematicalAlgorithms.Clustering.KMeans", ... },
    // 新增的矩阵运算类
    { "className": "MathematicalAlgorithms.Matrix.MatrixOps", ... }
  ]
}
```

## 三、重构成果

### 3.1 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Share/Registry/AutoRegister/Wrappers/IDelegateWrapper.cs` | 新增 | 委托包装器接口 |
| `Share/Registry/AutoRegister/Wrappers/DelegateWrappers.cs` | 新增 | 内置包装器实现 |
| `Share/Registry/AutoRegister/AutoRegisterEngine.cs` | 新增 | 通用注册引擎 |
| `Share/Registry/AutoRegister/AutoRegisterService.cs` | 修改 | 添加 Wrapper 配置字段 |
| `Operation/FunctionStorage/Registrations/MathFunctionRegistrar.cs` | 重构 | 使用 AutoRegisterEngine |
| `Operation/FunctionStorage/Registrations/math-register-config.json` | 修改 | 添加 wrapper 配置 |
| `Operation/FunctionStorage/Business/Registrations/MathLibrary.cs` | 简化 | 去掉 _registered 标志 |
| `Operation/FunctionStorage/Business/Registrations/BuiltInUnitsLibrary.cs` | 简化 | 去掉 _registered 标志 |
| `Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs` | 修改 | 更新注释 |

### 3.2 消除的重复逻辑

- ✅ `_registered` 去重标志：从 MathLibrary、BuiltInUnitsLibrary 中移除
- ✅ 程序集加载逻辑：统一到 AutoRegisterEngine
- ✅ 委托包装逻辑：统一到 IDelegateWrapper
- ✅ 参数解析逻辑：统一到 MathWrapper
- ✅ 结果格式化逻辑：统一到 MathWrapper

### 3.3 可扩展性

- **新增数学类**：只需在 JSON 配置中添加条目
- **新增包装器**：实现 IDelegateWrapper 接口即可
- **新增命名规则**：实现 INamingConvention 接口即可
- **新增描述提供者**：实现 IDescriptionProvider 接口即可

## 四、后续优化方向

### 4.1 短期优化

1. **配置热加载**：支持运行时重新加载配置文件
2. **配置验证**：启动时验证 JSON 配置的合法性
3. **注册冲突检测**：检测同名函数注册冲突

### 4.2 中期优化

1. **配置继承**：支持基础配置 + 子配置继承
2. **条件注册**：支持按条件（如平台、环境）选择性注册
3. **注册钩子**：注册前后执行自定义逻辑

### 4.3 长期优化

1. **可视化配置编辑器**：提供 GUI 配置管理工具
2. **注册性能分析**：统计注册耗时、内存占用
3. **动态卸载**：支持运行时卸载指定类的注册
