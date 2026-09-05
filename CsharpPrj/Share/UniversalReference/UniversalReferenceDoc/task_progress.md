# 代码优化任务进度

## 第一阶段（低风险）- 已完成
- [x] 修复 BatchManager.CreateBatchPipeline() 中 IBatchAware 重复转换问题
- [x] 修复 GraphDatabaseStorage 中日志格式化字符串错误（6处 {0} 占位符）
- [x] 修复 BatchRegistry.UpdateBatchAsync() 异步方法返回问题
- [x] 提取 BatchRegistry 中元数据字典构建方法（ConvertToMetadataDict）
- [x] 合并属性路径解析逻辑为共享工具类（FindMapping 按长度降序匹配）
- [x] 修复 BatchRegistry 批次数据源不一致问题（以 BatchManager 为唯一数据源）
- [x] 移除废弃接口和适配层（ReferenceChangeType 废弃枚举、LogHelper 无用包装）
- [x] 删除未使用的 MapParametersWithFieldMapping 方法
- [x] 删除 Pipeline 包装方法（WorkflowExecutor、ObservableReferenceManager）
- [x] 清理不再需要的 using 语句

## 第二阶段（结构性拆分，方案1）- 已完成
- [x] 将 `UniversalReferenceLayer.cs`（3521 行单体）按顶层 namespace 拆分为 15 个独立文件（namespace 不变，消费者零改动）
- [x] 数据访问逻辑集中到 `DataAccess/`：GraphStorage / ComplexData / MetaGraph / ModuleFramework（Core、Repositories、Services、UnitOfWork、Events、Catalog）
- [x] 引用关系逻辑独立到 `Reference/`：ReferenceManagement、FieldMapping、Relationship（Core、Exceptions、Dispatcher、Configuration）
- [x] 删除原单体文件；拆分脚本保留在 `PowerShellScript/Split-UniversalReferenceLayer.ps1`
- [x] 验证：`UniversalWorkflowFramework.csproj` 构建通过，0 警告 0 错误

### 提取引用/关系语义到 Reference（第二轮）
- [x] `ComplexDataAbstraction` 关系模型（`RelationshipType`/`RelationshipData`/`EntityEvent`/`RelationshipEvent`/事件枚举）→ `Reference/Relationship/Models/ComplexDataRelationshipModels.cs`（namespace 不变）
- [x] `MetaArchitecture` 图/关系模型（`NodeData`/`EdgeData`/`GroupManifest`/`ArtifactManifest`/`DependencyManifest`）→ `Reference/Relationship/Models/MetaGraphModels.cs`（namespace 不变）
- [x] DataAccess 移除对应类型，仅保留持久化代码；构建通过 0 警告 0 错误

### 提取剩余引用/关系语义到 Reference（第三轮）
- [x] `GraphStorageConstants` 内嵌套关系常量（`ReferenceDescriptions`/`RelationshipPrefixes`）解嵌套 → `Reference/Relationship/Models/RelationshipVocabulary.cs`
- [x] `GraphRelationshipRepository` 私有关系映射/RELID 编码方法 → `Reference/Relationship/Models/RelationshipCodec.cs`（`RelationshipCodec` 静态类），仓储改为调用
- [x] 模块关系领域模型（`UniversalModuleFramework.Core.RelationshipType` 枚举 + `IModuleRelationship`）→ `Reference/Relationship/Models/ModuleRelationshipModels.cs`
- [x] 引用寻址工厂（`ResourceIdentifierFactory`）→ `Reference/Relationship/Models/ResourceIdentifierFactory.cs`
- [x] 全部构建通过 0 警告 0 错误；DataAccess 仅剩持久化/领域代码

### 下沉关系编排到 Reference 编排器（第四轮）
- [x] 新增 `Reference/Relationship/Orchestration/GraphRelationshipOrchestrator.cs`：承接 `GraphService` 的图上关系编排（Contains/DependsOn 的 `AddArtifact`/`RemoveArtifact`/`AddDependency`/`RemoveDependency`/`GetGroupArtifactIds`/`GetGroupDependencies`/`GetGroupDependents`/`IsGroupComplete`）
- [x] 新增 `Reference/Relationship/Orchestration/ModuleDependencyOrchestrator.cs`：承接 `ModuleCatalog` 的模块依赖编排（`AddDependencyAsync`/`GetDependenciesAsync`/`GetDependentsAsync`）
- [x] `GraphService`/`ModuleCatalog` 保留公共方法与接口，改为委托到编排器（构造函数不变，消费方零改动）
- [x] 构建通过 0 警告 0 错误

## 第三阶段（重复/冗余/错误逻辑清理）- 已完成
> 本轮基于全量分析（2026-08 复盘），聚焦错误逻辑修复 + 死代码收敛；构建 0 警告 0 错误。

### P0 错误逻辑修复
- [x] **修复构建断链**：`Modules/Pipeline/Registry.cs` 删除引用已移除类型 `SequentialStrategy` 的死方法 `ExecuteAll`/`ExecuteAllAsync` 及孤立接口 `IExecutionStrategy`（全库 0 消费方；7.13 重构遗留悬空引用）→ 依赖链恢复编译
- [x] **修复 `FieldMappingReferenceResolver.GetMappingForPath` 空值缓存崩溃**：`ConcurrentDictionary.GetOrAdd` 遇 null 抛 `ArgumentNullException`，改为 `TryGetValue` + 守卫 `TryAdd`，恢复友好错误路径
- [x] **消除 `Resource` 的 `new` 字段隐藏**：删除重复声明的 `CreatedAt`/`LastModified`，改由基类 `ResourceData` 继承（消除双时间戳隐患）

### 死代码收敛（全库 0 消费方，保留 CellFrame 复用契约类型）
- [x] 删除 `Reference/ReferenceInterfaces.cs`（7 个死接口：`IReferenceable`/`IReferenceEventBus`/`IReferenceAnalysisService`/`IReferenceValidationService`/`IUnifiedCacheService`/`IUnifiedEventPublisherService`/`IUnifiedResourceIdentifierService`）
- [x] 删除 `Reference/Models/ReferenceRequests.cs`（6 个死请求 DTO）
- [x] `Reference/Models/ReferenceResults.cs` 收敛为仅保留 `CircularDependencyResult`/`CircularDependency`（删除 7 个死结果类）
- [x] `Reference/Models/ReferenceModels.cs` 删除 4 个死模型（`ReferenceOperation`/`ReferenceOperationResult`/`ProcessedReference`/`ReferenceAnalysisItem`）
- [x] `Reference/Models/Enums.cs` 收敛重复枚举：删除死枚举 `ReferenceOperationType`/`ChangeType`/`RelationshipChangeType`/`ProcessingStatus`/`AnalysisScope`/`OptimizationStrategy`（保留 `ReferenceType`/`FilterType`/`ReferenceIssueType`/`Severity`/`ReferenceChangeType`）→ 消除与 `Core.Models.ProcessingStatus` 的同名冲突

### 冗余收敛
- [x] `PersistentUniversalReferenceManager` 提取 `ApplyLoadedFiles`，消除 `LoadFromDiskAsync`/`LoadFromDiskSync` 重复锁块

### 图算法收敛到 GraphProcessing（项 4，行为重构）
- [x] `ReferenceManagerBase.FindShortestPathAsync`（手写 BFS）→ `GraphProcessing.Algorithms.DefaultShortestPathFinder`（语义一致，含 `start==end` 与不可达返回空）
- [x] `ReferenceManagerBase.FindPathsAsync`（手写 DFS）→ `GraphProcessing.Algorithms.DefaultPathFinder.FindAllPaths`，**并修正原 `FindPathsRecursive` 环守卫缺陷**（`path.Skip(1).Any(...)` 恒为真导致深度>1 永远无法返回真实路径）
- [x] 新增 `GetOutgoingKeys`/`KeyToResourceIdentifier`/`BuildKey` 桥接（键=ResourceType:Id）；`GetResourceKey` 收敛复用 `BuildKey`
- [x] 删除已下沉的死方法 `FindPathsRecursive`
- [x] 保留 `GetDependenciesAsync`/`GetDependentsAsync` 手写深度受限 DFS：`GraphProcessing.IPathFinder.GetReachableNodes` 为「无深度上限」可达集，无法表达 `maxDepth` 语义（已在代码注释说明）
- [x] 构建通过 0 警告 0 错误；`FindPathsAsync`/`FindShortestPathAsync` 全库 0 消费方，行为变更无影响
