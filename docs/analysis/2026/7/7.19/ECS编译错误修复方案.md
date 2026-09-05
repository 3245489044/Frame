# ECS 编译错误修复方案

> 日期：2026-07-19
> 目标：消除 `ECS.csproj` 的 57 个编译错误，同时保持架构整洁

---

## 1. 问题根因

`ECS.csproj` 当前仅引用了 `TConfig.csproj`：

```xml
<ItemGroup>
    <ProjectReference Include="..\..\Plate\TConfig\TConfig.csproj" />
</ItemGroup>
```

但 `ECSFrame.cs` 和 `RelationComponent.cs` 使用了来自以下项目的类型：

| 源项目 | 位置 | 使用的类型 | 能否引用 |
|--------|------|-----------|---------|
| `ContextFrame.csproj` | BasicMethod 下（同层） | `IContext`, `ContextBase`, `Locking.Lock` | ❌ 禁止 |
| `ComponentData.csproj` | Modules 下（层2） | `ISkeleton<>`, `SkeletonBase<>`, `SkeletonContext`, `Unit`, `IEcsSkeleton<>`, `ComponentLifecycleManager`, `InvocationDescriptor`, `DelegateCompiler`, `MemberKind` | ❌ 禁止 |

**架构约束**：
- BasicMethod 下的子项目之间不能相互引用
- BasicMethod 下的子项目也不能引用 Modules 层
- ECS 只能引用 Plate/TConfig

---

## 2. 修复策略

**迁移到 Modules/ComponentData**：把 ECS 中依赖外部类型的代码迁移到 `Modules/ComponentData`，ECS 只保留纯 ECS 核心。

### 依赖链变化

```
迁移前：ECS → TConfig + (隐式依赖 ContextFrame + ComponentData)
迁移后：ECS → TConfig（仅此而已）
        Modules/ComponentData → ECS + ContextFrame
```

### 迁移内容

#### 从 ECSFrame.cs 迁移（`#region ECS.ContextDriven`，第378-681行）

| 类型 | 说明 | 目标文件 |
|------|------|---------|
| `EntityManagerContextExtensions` | EntityManager 上下文扩展方法 | `ContextDriven/EntityManagerContextExtensions.cs` |
| `EcsSkeletonContext` | 继承 `SkeletonContext` 的 ECS 上下文 | `ContextDriven/EcsSkeletonContext.cs` |
| `IEcsSkeleton<>` | 继承 `ISkeleton<>` 的接口 | `ContextDriven/IEcsSkeleton.cs` |
| `ContextAwareSystem` | 上下文感知系统基类 | `ContextDriven/ContextAwareSystem.cs` |
| `ReadContextDataSkeleton<T>` | 读取上下文数据的 Skeleton | `ContextDriven/ReadContextDataSkeleton.cs` |
| `WriteContextDataSkeleton<T>` | 写入上下文数据的 Skeleton | `ContextDriven/WriteContextDataSkeleton.cs` |
| `UpdateContextDataSkeleton<T>` | 更新上下文数据的 Skeleton | `ContextDriven/UpdateContextDataSkeleton.cs` |
| `IContextReactor<T>` | 上下文反应器接口 | `ContextDriven/IContextReactor.cs` |
| `ContextReactionSystem` | 上下文反应器系统 | `ContextDriven/ContextReactionSystem.cs` |

#### 从 RelationComponent.cs 迁移（`#region ECSEnhanced`，第91-1119行）

| 类型 | 说明 | 目标文件 |
|------|------|---------|
| `RelationIndex<TKey>` | 关系索引（internal） | `Enhanced/RelationIndex.cs` |
| `RelationKey` | 关系键 | `Enhanced/RelationKey.cs` |
| `InternalRelationCore` | 关系核心（internal） | `Enhanced/InternalRelationCore.cs` |
| `IndexedAttribute` | 索引特性 | `Enhanced/IndexedAttribute.cs` |
| `ExecutionContext` | 执行上下文枚举 | `Enhanced/ExecutionContext.cs` |
| `RelationManager` | 关系管理器 | `Enhanced/RelationManager.cs` |
| `IndexManager` | 索引管理器 | `Enhanced/IndexManager.cs` |
| `NotificationCenter` | 通知中心 | `Enhanced/NotificationCenter.cs` |
| `CommandBase<TResult>` | 命令基类 | `Enhanced/CommandBase.cs` |
| `CommandSystem<TCommand,TResult>` | 命令系统 | `Enhanced/CommandSystem.cs` |
| `EcsContext` | ECS 上下文入口 | `Enhanced/EcsContext.cs` |
| `EntityManagerEventSource` | 事件源（internal） | `Enhanced/EntityManagerEventSource.cs` |

---

## 3. 详细设计

### 3.1 ComponentData.csproj 修改

添加对 ECS 的引用：

```xml
<ItemGroup>
    <!-- 依赖 ECS 核心（EntityManager、SystemBase 等） -->
    <ProjectReference Include="..\..\BasicMethod\ECS\ECS.csproj" />
</ItemGroup>
```

添加新文件的 Compile 项。

### 3.2 迁移后的文件结构

```
Modules/ComponentData/
├── ComponentData.csproj
├── Abstractions/
│   ├── IComponentDataStore.cs
│   └── IEcsComponentReactor.cs
├── Core/
│   └── ComponentDataStore.cs
├── Lifecycle/
│   ├── ComponentLifecycleManager.cs
│   └── ExecutionMode.cs
├── Reflection/
│   ├── FastReflection.cs
│   ├── MemberKind.cs
│   ├── InvocationDescriptor.cs
│   ├── IMemberResolver.cs
│   ├── DefaultMemberResolver.cs
│   ├── ExpressionBuilder.cs
│   └── DelegateCompiler.cs
├── Skeleton/
│   ├── ISkeleton.cs
│   ├── Unit.cs
│   ├── SkeletonContext.cs
│   ├── SkeletonBase.cs
│   └── IEcsSkeleton.cs
├── Reactor/
│   ├── ComponentReactorSystem.cs
│   └── EcsComponentReactorPipeline.cs
├── Extensions/
│   └── EntityManagerExtensions.cs
├── ContextDriven/          ← 新增（从 ECSFrame.cs 迁移）
│   ├── EntityManagerContextExtensions.cs
│   ├── EcsSkeletonContext.cs
│   ├── IEcsSkeleton.cs
│   ├── ContextAwareSystem.cs
│   ├── ReadContextDataSkeleton.cs
│   ├── WriteContextDataSkeleton.cs
│   ├── UpdateContextDataSkeleton.cs
│   ├── IContextReactor.cs
│   └── ContextReactionSystem.cs
├── Enhanced/               ← 新增（从 RelationComponent.cs 迁移）
│   ├── RelationIndex.cs
│   ├── RelationKey.cs
│   ├── InternalRelationCore.cs
│   ├── IndexedAttribute.cs
│   ├── ExecutionContext.cs
│   ├── RelationManager.cs
│   ├── IndexManager.cs
│   ├── NotificationCenter.cs
│   ├── CommandBase.cs
│   ├── CommandSystem.cs
│   ├── EcsContext.cs
│   └── EntityManagerEventSource.cs
└── UsageExamples.cs
```

### 3.3 ECSFrame.cs 修改

删除 `#region ECS.ContextDriven` 整个区域（第378-681行）。

### 3.4 RelationComponent.cs 修改

删除 `#region ECSEnhanced` 整个区域（第91-1119行）。

### 3.5 ECS.csproj

保持不变，仅引用 TConfig：

```xml
<ItemGroup>
    <ProjectReference Include="..\..\Plate\TConfig\TConfig.csproj" />
</ItemGroup>
```

---

## 4. 影响范围

| 文件 | 修改类型 |
|------|---------|
| `Modules/ComponentData/ComponentData.csproj` | **修改** — 添加 ECS 引用 + 新文件 Compile 项 |
| `Modules/ComponentData/ContextDriven/*.cs` | **新建**（9个文件） |
| `Modules/ComponentData/Enhanced/*.cs` | **新建**（12个文件） |
| `BasicMethod/ECS/ECSFrame.cs` | **修改** — 删除 ECS.ContextDriven 区域 |
| `BasicMethod/ECS/Relations/RelationComponent.cs` | **修改** — 删除 ECSEnhanced 区域 |
| `BasicMethod/ECS/ECS.csproj` | **不变** |

---

## 5. 实施顺序

```
步骤 1: 修改 ComponentData.csproj — 添加 ECS 引用
步骤 2: 新建 ContextDriven/ 目录下的 9 个文件
步骤 3: 新建 Enhanced/ 目录下的 12 个文件
步骤 4: 修改 ComponentData.csproj — 添加新文件的 Compile 项
步骤 5: 从 ECSFrame.cs 删除 ECS.ContextDriven 区域
步骤 6: 从 RelationComponent.cs 删除 ECSEnhanced 区域
步骤 7: 编译验证
```

---

## 6. 风险评估

- **低风险**：代码原样迁移，不修改逻辑
- **低风险**：ECS.csproj 引用不变，不影响外部项目
- **低风险**：Modules/ComponentData 已引用 ContextFrame，Skeleton 类型可用
- **注意**：迁移后需要确认 `EcsContext` 中使用的 `EcsWorld` 类型在 ECS 项目中是否可访问
