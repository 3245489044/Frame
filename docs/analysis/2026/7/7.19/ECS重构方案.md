# ECS 模块重构方案

## 1. 背景与目标

### 1.1 当前问题

`CsharpPrj/BasicMethod/ECS/` 作为 BasicMethod 的子项目，存在以下问题：

1. **跨层反向引用**：`ControlPool.cs` 引用了 `Modules.InternalStorage.Extensions`（`StorageLayer` 枚举），违反了 BasicMethod 层不能引用 Modules 层的架构约束。
2. **BasicMethod 内部耦合**：ECS 大量引用了 BasicMethod 内部的 `ComponentLifecycle`、`SkeletonFlow`、`FastReflection` 等类型，导致 ECS 无法独立于 BasicMethod 存在。
3. **职责边界模糊**：ECS 中的 Bridge/Systems 部分（`ControlFactory`、`ControlCreateSystem` 等）本质上是 UI 控件相关的 ECS 应用逻辑，与 ECS 核心引擎（Archetype、Chunk、EntityManager）混在一起。

### 1.2 重构目标

1. **ECS 核心引擎独立**：ECS 核心（Archetype、EntityManager、Chunk、Query 等）只依赖 TConfig 和 Modules.ComponentData，不依赖 BasicMethod。
2. **依赖方向正确**：BasicMethod → Modules.ComponentData → ECS，Modules 层可引用 ECS。
3. **职责分离**：ECS 核心引擎与 UI 控件应用逻辑分离。

### 1.3 架构依赖关系（重构后）

```
BasicMethod
  ├── ContextFrame（IContext 等接口实现）
  ├── FrameMethod（SkeletonFlow 等）
  └── BasicAlgorithm（ComponentLifecycle 等）
       ↓
Modules.ComponentData（ECS 依赖的抽象层）
  ├── Lifecycle/（从 BasicMethod 迁移）
  ├── Skeleton/（从 BasicMethod 迁移）
  └── Reflection/（从 BasicMethod 迁移）
       ↓
ECS（核心引擎，独立项目）
  ├── Core/（Archetype, EntityManager, Chunk, Query）
  ├── Bridge/（ControlPool 等，通过 IoC 解耦）
  └── Systems/（UI 控件 System）
       ↓
Modules（应用层）
  ├── ComponentData（ECS 组件数据处理框架）
  ├── InternalStorage（内部存储实现）
  └── ...
```

---

## 2. 详细迁移清单

### 2.1 从 BasicMethod 迁移到 Modules.ComponentData 的类型

#### A. ComponentLifecycle 体系

| 类型 | 源文件 | 目标目录 |
|------|-------|---------|
| `IComponentLifecycleHandler<T>` | `BasicMethod/BasicAlgorithm/ComponentLifecycle.cs` | `Modules/ComponentData/Lifecycle/` |
| `ComponentLifecycleManager` | `BasicMethod/BasicAlgorithm/ComponentLifecycle.cs` | `Modules/ComponentData/Lifecycle/` |
| `IComponentEventSource` | `BasicMethod/BasicAlgorithm/ComponentLifecycle.cs` | `Modules/ComponentData/Lifecycle/` |
| `ExecutionMode` | `BasicMethod/BasicAlgorithm/ComponentLifecycle.cs` | `Modules/ComponentData/Lifecycle/` |

#### B. SkeletonFlow 核心抽象

| 类型 | 源文件 | 目标目录 |
|------|-------|---------|
| `ISkeleton<TIn, TOut>` | `BasicMethod/FrameMethod/SkeletonFlow.cs` | `Modules/ComponentData/Skeleton/` |
| `SkeletonBase<TIn, TOut>` | `BasicMethod/FrameMethod/SkeletonFlow.cs` | `Modules/ComponentData/Skeleton/` |
| `SkeletonContext` | `BasicMethod/FrameMethod/SkeletonFlow.cs` | `Modules/ComponentData/Skeleton/` |
| `Unit` | `BasicMethod/FrameMethod/SkeletonFlow.cs` | `Modules/ComponentData/Skeleton/` |
| `IEcsSkeleton<TIn, TOut>` | `ECS/ECSFrame.cs` | `Modules/ComponentData/Skeleton/` |

#### C. FastReflection 体系

| 类型 | 源文件 | 目标目录 |
|------|-------|---------|
| `DelegateCompiler` | `BasicMethod/ElementCalculate_Script.cs` | `Modules/ComponentData/Reflection/` |
| `InvocationDescriptor` | `BasicMethod/ElementCalculate_Script.cs` | `Modules/ComponentData/Reflection/` |
| `MemberKind` | `BasicMethod/ElementCalculate_Script.cs` | `Modules/ComponentData/Reflection/` |

### 2.2 控制反转（IoC）解耦的部分

| 依赖 | 当前引用方式 | 解耦方式 |
|------|------------|---------|
| `BasicMethod.ContextFrame.Abstractions.IContext` | 直接 using | 通过构造函数注入，ECS 只依赖接口定义（在 TConfig 中） |
| `BasicMethod.ContextFrame.Core.ContextBase` | 直接 new | 通过工厂/委托注入 |
| `BasicMethod.ContextFrame.Locking.Lock` | 直接 using | 通过构造函数注入 `Lock` 的抽象 |
| `Modules.InternalStorage.Extensions.StorageLayer` | 直接 using | 在 ECS 内部自包含定义 |

### 2.3 保留在 ECS 中的类型（无需迁移）

以下类型只依赖 TConfig 和标准库，保持不动：

- `ECS.Core.Vec3`
- `ECS.Core.EntityManager`
- `ECS.Core.ArchetypeManager`
- `ECS.Core.Archetype`
- `ECS.Core.ArchetypeSignature`
- `ECS.Core.Chunk`
- `ECS.Core.ComponentTypeId`
- `ECS.Core.ArchetypeQueryResult`
- `ECS.Core.ChunkAccessor<T>`
- `ECS.Core.EntityQueryBuilder`
- `ECS.Core.ChunkJob`
- `ECS.Core.ISystem` / `SystemBase` / `SystemManager`
- `ECS.Core.EcsWorld`

### 2.4 需要调整引用的类型

以下类型当前引用了 BasicMethod 内部类型，迁移后改为引用 Modules.ComponentData：

| 类型 | 当前引用 | 改为引用 |
|------|---------|---------|
| `ECSFrame.cs` 中的 `EcsSkeletonContext` | `SkeletonFlow`, `ComponentLifecycle` | `Modules.ComponentData.Skeleton`, `Modules.ComponentData.Lifecycle` |
| `RelationComponent.cs` 中的 `RelationManager` | `ComponentLifecycle`, `SkeletonFlow`, `FastReflection` | `Modules.ComponentData.Lifecycle`, `Modules.ComponentData.Skeleton`, `Modules.ComponentData.Reflection` |
| `ControlPool.cs` | `Modules.InternalStorage.Extensions` | 内部定义 `StorageLayer` |

---

## 3. 文件变更清单

### 3.1 新建文件

```
Modules/ComponentData/Lifecycle/
  ├── IComponentLifecycleHandler.cs    # 从 BasicMethod 迁移
  ├── ComponentLifecycleManager.cs     # 从 BasicMethod 迁移
  ├── IComponentEventSource.cs         # 从 BasicMethod 迁移
  └── ExecutionMode.cs                 # 从 BasicMethod 迁移

Modules/ComponentData/Skeleton/
  ├── ISkeleton.cs                     # 从 BasicMethod 迁移
  ├── SkeletonBase.cs                  # 从 BasicMethod 迁移
  ├── SkeletonContext.cs               # 从 BasicMethod 迁移
  ├── Unit.cs                          # 从 BasicMethod 迁移
  └── IEcsSkeleton.cs                  # 从 ECS/ECSFrame.cs 迁移

Modules/ComponentData/Reflection/
  ├── DelegateCompiler.cs              # 从 BasicMethod 迁移
  ├── InvocationDescriptor.cs          # 从 BasicMethod 迁移
  └── MemberKind.cs                    # 从 BasicMethod 迁移
```

### 3.2 修改文件

| 文件 | 修改内容 |
|------|---------|
| `ECS/ECS.csproj` | 移除对 BasicMethod.csproj 的引用，改为引用 Modules.ComponentData.csproj |
| `ECS/ECSFrame.cs` | 将 `using ComponentLifecycle` 改为 `using Modules.ComponentData.Lifecycle`，将 `using SkeletonFlow` 改为 `using Modules.ComponentData.Skeleton` |
| `ECS/Relations/RelationComponent.cs` | 同上，更新命名空间引用 |
| `ECS/Bridge/ControlPool.cs` | 移除 `using Modules.InternalStorage.Extensions`，内部定义 `StorageLayer` 枚举 |
| `ECS/GlobalUsings.cs` | 移除对 BasicMethod 内部类型的全局 using |
| `Modules/ComponentData/ComponentData.csproj` | 添加对 BasicMethod.csproj 的引用（因为迁移过来的类型原本在 BasicMethod 中） |

### 3.3 删除文件（BasicMethod 中的原始文件）

迁移完成后，BasicMethod 中以下文件中的重复类型应标记为废弃或删除：

- `BasicMethod/BasicAlgorithm/ComponentLifecycle.cs` 中的 ComponentLifecycle 相关类型
- `BasicMethod/FrameMethod/SkeletonFlow.cs` 中的 SkeletonFlow 核心抽象
- `BasicMethod/ElementCalculate_Script.cs` 中的 FastReflection 相关类型

> **注意**：删除前需确认 BasicMethod 中其他部分是否仍在使用这些类型。如果仍有使用，则保留原始定义，Modules.ComponentData 中的定义为独立副本。

---

## 4. 实施步骤

### Step 1: 在 Modules.ComponentData 中创建目标目录结构

```
Modules/ComponentData/Lifecycle/
Modules/ComponentData/Skeleton/
Modules/ComponentData/Reflection/
```

### Step 2: 迁移 ComponentLifecycle 体系

从 `BasicMethod/BasicAlgorithm/ComponentLifecycle.cs` 中提取以下类型到 `Modules/ComponentData/Lifecycle/`：

- `IComponentLifecycleHandler<T>` → `IComponentLifecycleHandler.cs`
- `ComponentLifecycleManager` → `ComponentLifecycleManager.cs`
- `IComponentEventSource` → `IComponentEventSource.cs`
- `ExecutionMode` → `ExecutionMode.cs`

### Step 3: 迁移 SkeletonFlow 核心抽象

从 `BasicMethod/FrameMethod/SkeletonFlow.cs` 中提取以下类型到 `Modules/ComponentData/Skeleton/`：

- `ISkeleton<TIn, TOut>` → `ISkeleton.cs`
- `SkeletonBase<TIn, TOut>` → `SkeletonBase.cs`
- `SkeletonContext` → `SkeletonContext.cs`
- `Unit` → `Unit.cs`

从 `ECS/ECSFrame.cs` 中提取 `IEcsSkeleton<TIn, TOut>` 到 `Modules/ComponentData/Skeleton/IEcsSkeleton.cs`。

### Step 4: 迁移 FastReflection 体系

从 `BasicMethod/ElementCalculate_Script.cs` 中提取以下类型到 `Modules/ComponentData/Reflection/`：

- `DelegateCompiler` → `DelegateCompiler.cs`
- `InvocationDescriptor` → `InvocationDescriptor.cs`
- `MemberKind` → `MemberKind.cs`

### Step 5: 更新 Modules.ComponentData.csproj

添加对 BasicMethod.csproj 的 ProjectReference（因为迁移过来的类型原本在 BasicMethod 中定义）。

### Step 6: 更新 ECS.csproj

移除对 BasicMethod.csproj 的 ProjectReference，改为引用 Modules.ComponentData.csproj。

### Step 7: 更新 ECS 中的 using 语句

- `ECSFrame.cs`：`using ComponentLifecycle` → `using Modules.ComponentData.Lifecycle`，`using SkeletonFlow` → `using Modules.ComponentData.Skeleton`
- `RelationComponent.cs`：同上
- `ControlPool.cs`：移除 `using Modules.InternalStorage.Extensions`，内部定义 `StorageLayer`

### Step 8: 处理 ControlPool.cs 的 StorageLayer 依赖

在 ECS 项目内部定义 `StorageLayer` 枚举：

```csharp
namespace ECS.Core
{
    /// <summary>
    /// 存储层枚举 — 用于 IStorageCell.GetKeys 等操作
    /// 此定义为自包含副本，避免对 Modules.InternalStorage.Extensions 的依赖
    /// </summary>
    public enum StorageLayer
    {
        /// <summary>会话层（当前运行会话）</summary>
        Session,
        /// <summary>持久层（跨会话持久化）</summary>
        Persistent,
        /// <summary>缓存层（临时缓存）</summary>
        Cache
    }
}
```

### Step 9: 验证构建

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln"
```

---

## 5. 风险与注意事项

### 5.1 命名冲突风险

迁移后，`Modules.ComponentData` 和 `BasicMethod` 中会存在同名的类型（如 `ComponentLifecycleManager`）。需要确保：
- ECS 中的 using 明确指向 `Modules.ComponentData.Lifecycle`
- BasicMethod 中其他部分仍使用 BasicMethod 内部的原始定义

### 5.2 删除原始类型的时机

BasicMethod 中的原始类型不应立即删除，而应：
1. 先完成迁移并验证构建通过
2. 检查 BasicMethod 中是否有其他代码使用这些类型
3. 确认无引用后再删除

### 5.3 ECSFrame.cs 的特殊性

`ECSFrame.cs` 是一个 688 行的巨型文件，包含多个命名空间（`ECS.Core`、`ECS.ContextDriven`、`ECSEnhanced`）。重构时建议：
- 保持 `ECS.Core` 部分不动（只改 using）
- `ECS.ContextDriven` 部分中的 `IEcsSkeleton` 迁移到 Modules.ComponentData
- `ECSEnhanced` 部分中的 `RelationManager`、`IndexManager` 等保持不动（只改 using）

---

## 6. 重构后的依赖关系验证

重构完成后，各项目的依赖关系应为：

```
BasicMethod.csproj
  ├── TConfig (NuGet)
  ├── ContextFrame/ContextFrame.csproj (子项目)
  └── ECS/ECS.csproj (子项目)  ← 不再引用 BasicMethod

ECS/ECS.csproj
  ├── TConfig (NuGet)
  └── Modules/ComponentData/ComponentData.csproj (ProjectReference)

Modules/ComponentData/ComponentData.csproj
  ├── TConfig (NuGet)
  ├── BasicMethod/BasicMethod.csproj (ProjectReference) ← 迁移过来的类型
  └── System.Reactive (NuGet)
```

> **注意**：`Modules.ComponentData` 引用 `BasicMethod` 是合理的，因为 ComponentData 属于 Modules 层，可以引用 BasicMethod 层。而 ECS 作为 BasicMethod 的子项目，通过引用 Modules.ComponentData 来获取 Lifecycle/Skeleton/Reflection 等抽象，不再直接依赖 BasicMethod。
