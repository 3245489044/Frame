# IFunctionRegistry 迁移整合计划

> 目标：将 `Modules.Registry` 命名空间下的 `IFunctionRegistry` 使用整合到 `TConfig.IFunctionRegistry`，消除命名冲突和编译错误。

---

## 1. 背景

当前存在两个 `IFunctionRegistry` 相关体系：

| 体系 | 位置 | 内容 |
|------|------|------|
| **TConfig** | `CsharpPrj/Plate/TConfig/Models/IFunctionRegistry.cs` | 精简接口（6 个方法） |
| **Modules.Registry** | `CsharpPrj/Modules/Registry/06_ConcreteRegistries.cs` | `FunctionRegistry` 实现类，实现 `TConfig.IFunctionRegistry` + `ITrConfigRegistry` |

### 1.1 问题

`FunctionStorageRegistry.cs` 中 `Registry` 属性声明为 `public static IFunctionRegistry Registry`，编译器解析为 `TConfig.IFunctionRegistry`。但代码中调用了 `GetRegisteredNames()`、`TryGetDescriptor()`、`TryGetDelegate()`、`TryGetConfig()`、`Clear()` 等方法，这些方法**不在** `TConfig.IFunctionRegistry` 接口中，导致 CS1061 编译错误。

### 1.2 现有接口定义

**`TConfig.IFunctionRegistry`**（`CsharpPrj/Plate/TConfig/Models/IFunctionRegistry.cs`）：
```csharp
public interface IFunctionRegistry
{
    void Register(string functionName, Delegate function);
    void Register(string functionName, Delegate function, string? description);
    Delegate? Find(string functionName);
    bool Contains(string functionName);
    void Unregister(string functionName);
    IEnumerable<string> GetAllFunctionNames();
}
```

**`TConfig.ITrConfigRegistry`**（`CsharpPrj/Plate/TConfig/TrConfig/Interfaces/ITrConfig.cs`）：
```csharp
public interface ITrConfigRegistry
{
    void Register(string name, ITrConfig config);
    void Register(string name, Delegate function, string? description = null);
    void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null);
    void Unregister(string name);
    bool TryGetConfig(string name, out ITrConfig config);
    bool Contains(string name);
    IEnumerable<string> GetRegisteredNames();
    void Clear();
}
```

**`FunctionRegistry` 类额外方法**（不在任何接口中）：
```csharp
// 在 FunctionRegistry 类中，但不在 IFunctionRegistry 或 ITrConfigRegistry 中
bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor);
bool TryGetDelegate(string name, out Delegate? function);
bool TryGetUnit(string name, out IFunctionUnit? unit);
```

---

## 2. 迁移方案

### 2.1 核心思路

将 `FunctionRegistry` 类中额外的方法提升到 `TConfig.IFunctionRegistry` 接口中，使 `TConfig.IFunctionRegistry` 成为完整的函数注册表抽象。同时将 `ITrConfigRegistry` 中与 `IFunctionRegistry` 重叠的方法统一。

### 2.2 步骤

#### 步骤 1：扩展 `TConfig.IFunctionRegistry` 接口

**文件**：`CsharpPrj/Plate/TConfig/Models/IFunctionRegistry.cs`

新增方法：
- `void Clear()` — 清空所有注册（从 `IRegistry` / `ITrConfigRegistry` 提升）
- `bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor)` — 尝试获取函数描述符
- `bool TryGetDelegate(string name, out Delegate? function)` — 尝试获取委托
- `bool TryGetConfig(string name, out ITrConfig? config)` — 尝试获取配置（从 `ITrConfigRegistry` 提升）
- `IEnumerable<string> GetRegisteredNames()` — 获取所有已注册名称（等价于 `GetAllFunctionNames()`，保留两个名称兼容）

**注意**：`GetAllFunctionNames()` 和 `GetRegisteredNames()` 语义相同，但为了兼容现有调用方，两个都保留。

#### 步骤 2：更新 `FunctionRegistry` 实现类

**文件**：`CsharpPrj/Modules/Registry/06_ConcreteRegistries.cs`

- `FunctionRegistry` 类已实现 `IFunctionRegistry` + `ITrConfigRegistry`
- 新增的接口方法（`TryGetDescriptor`、`TryGetDelegate`）已有实现，无需修改
- 确认 `Clear()`、`GetRegisteredNames()`、`TryGetConfig()` 已实现（来自 `ITrConfigRegistry`）

#### 步骤 3：更新 `FunctionStorageRegistry.cs`

**文件**：`CsharpPrj/Operation/StateBlockDriven/FunctionStorage/Framework/FunctionStorageRegistry.cs`

- 移除 `using Modules.Registry;`（不再需要）
- `Registry` 属性类型保持 `IFunctionRegistry`（现在解析为 `TConfig.IFunctionRegistry`，已有所有方法）
- 所有调用 `GetRegisteredNames()`、`TryGetDescriptor()` 等的地方自动编译通过

#### 步骤 4：更新 `FunctionStorageAdapter.cs`

**文件**：`CsharpPrj/Operation/StateBlockDriven/FunctionStorage/Framework/FunctionStorageAdapter.cs`

- 移除 `using Modules.Registry;`
- `FunctionRegistryAdapterImpl` 中的 `_innerRegistry.TryGetDelegate()`、`_innerRegistry.TryGetConfig()` 等调用自动编译通过

#### 步骤 5：更新 `Core.cs`

**文件**：`CsharpPrj/Operation/StateBlockDriven/FunctionStorage/Framework/Core.cs`

- 移除对 `Modules.Registry` 的依赖
- 修复 `ExecutionResult` → `ExecutionResult<TResult>` 转换错误（CS0266）
- 修复 `ExecuteAsync` 调用二义性（CS0121）

#### 步骤 6：更新 `NonEventCommandCenter.cs`

**文件**：`CsharpPrj/Operation/StateBlockDriven/FunctionStorage/Framework/NonEventCommandCenter.cs`

- 移除对 `Modules.Registry` 的依赖

#### 步骤 7：更新 `RegistryStateBlock.cs`

**文件**：`CsharpPrj/Operation/StateBlockDriven/Blocks/RegistryStateBlock.cs`

- 修复 `TrConfigRegistry` 类型引用（CS0246）

#### 步骤 8：更新 `UnifiedRegistryCenter.cs`

**文件**：`CsharpPrj/Modules/Registry/09_UnifiedRegistryCenter.cs`

- 确认 `IFunctionRegistry` 引用解析为 `TConfig.IFunctionRegistry`
- 添加必要的 using 声明

#### 步骤 9：更新 `AutoRegisterEngine.cs`

**文件**：`CsharpPrj/Modules/Registry/AutoRegister/AutoRegisterEngine.cs`

- 确认 `IFunctionRegistry` 引用解析为 `TConfig.IFunctionRegistry`

#### 步骤 10：更新 `FunctionRegistryExtensions.cs`

**文件**：
- `CsharpPrj/Modules/Registry/FunctionRegistryExtensions.cs`
- `CsharpPrj/Modules/Registry/14_RegistryExtensions.cs`

- 确认扩展方法基于 `TConfig.IFunctionRegistry`

#### 步骤 11：更新 `FunctionLibraryLifecycle.cs`

**文件**：`CsharpPrj/Modules/Registry/11_FunctionLibraryLifecycle.cs`

- 确认 `IFunctionRegistry` 引用解析为 `TConfig.IFunctionRegistry`

---

## 3. 影响范围

### 3.1 直接修改的文件

| 文件 | 修改内容 |
|------|----------|
| `CsharpPrj/Plate/TConfig/Models/IFunctionRegistry.cs` | 新增 5 个方法 |
| `CsharpPrj/Operation/StateBlockDriven/FunctionStorage/Framework/FunctionStorageRegistry.cs` | 移除 `using Modules.Registry` |
| `CsharpPrj/Operation/StateBlockDriven/FunctionStorage/Framework/FunctionStorageAdapter.cs` | 移除 `using Modules.Registry` |
| `CsharpPrj/Operation/StateBlockDriven/FunctionStorage/Framework/Core.cs` | 修复 CS0266、CS0121 |
| `CsharpPrj/Operation/StateBlockDriven/Blocks/RegistryStateBlock.cs` | 修复 CS0246 |

### 3.2 需要确认编译的文件（无需修改，但需验证）

- `CsharpPrj/Modules/Registry/09_UnifiedRegistryCenter.cs`
- `CsharpPrj/Modules/Registry/AutoRegister/AutoRegisterEngine.cs`
- `CsharpPrj/Modules/Registry/FunctionRegistryExtensions.cs`
- `CsharpPrj/Modules/Registry/14_RegistryExtensions.cs`
- `CsharpPrj/Modules/Registry/11_FunctionLibraryLifecycle.cs`

---

## 4. 风险与注意事项

1. **`GetAllFunctionNames()` vs `GetRegisteredNames()`**：两者语义相同，保留两个方法避免破坏现有调用方。后续可考虑废弃一个。

2. **`TryGetUnit` 方法**：`FunctionRegistry` 类中有 `TryGetUnit()` 方法，但它返回 `IFunctionUnit`（来自 `FunctionStorage.Abstractions`），不适合放入 `TConfig.IFunctionRegistry`（TConfig 不应依赖 FunctionStorage）。此方法保持为 `FunctionRegistry` 类的专有方法。

3. **`IFunctionDescriptor` 和 `ITrConfig` 的关系**：`FunctionDescriptor` 类同时实现了 `IFunctionDescriptor` 和 `ITrConfig`，所以 `TryGetDescriptor` 和 `TryGetConfig` 本质上是同一数据的不同视图。

4. **编译顺序**：TConfig 项目需要先编译，然后 Modules.Registry，最后 StateBlockDriven。

---

## 5. 验证方法

修改完成后，执行以下命令验证：

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln" 2>&1 | findstr /R "error CS"
```

期望结果：0 个错误。
