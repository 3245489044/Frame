# CELL框架设计文档

---

## 文档信息

| 项目 | 内容 |
| :--- | :--- |
| **文档版本** | v2.0 |
| **更新日期** | 2026-05-10 |
| **作者** | CELL框架开发团队 |
| **状态** | 已整理 |

---

## 目录

1. [框架概述](#1-框架概述)
2. [架构设计](#2-架构设计)
3. [引用管理层（L1）](#3-引用管理层-l1)
4. [入口控制层（L2）](#4-入口控制层-l2)
5. [模块层（L3）](#5-模块层-l3)
6. [离散变化追踪](#6-离散变化追踪)
7. [引用记录机制](#7-引用记录机制)
8. [部署与集成](#8-部署与集成)
9. [扩展能力](#9-扩展能力)
10. [附录：术语表](#10-术语表)

---

## 1. 框架概述

### 1.1 设计目标

CELL框架是一个基于模块构建的.NET 10项目容器框架，核心目标是实现**引用的全生命周期管理**。每个CELL包含三个核心模块：

| 模块类型 | 职责 | 变更触发条件 |
| :--- | :--- | :--- |
| **SQL存储模块** | 数据持久化管理 | 增删改查操作 |
| **代码功能模块** | 业务逻辑执行 | 代码变更、方法调用 |
| **JSON配置模块** | 配置数据管理 | 配置文件变更 |

### 1.2 核心设计原则

1. **完全入口控制**：所有模块操作必须经过统一入口，确保变更可追踪
2. **引用驱动**：每个模块变更都建立引用关系，支持追溯分析
3. **改进量化**：基于变更记录计算模块改进程度

---

## 2. 架构设计

### 2.1 三层架构结构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          CELL Framework                                │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                【引用管理层】ReferenceManagementLayer           │   │
│  │  - 引用关系存储与查询                                           │   │
│  │  - 变更记录管理                                                 │   │
│  │  - 依赖关系分析                                                 │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                              ↑                                         │
│                              │ 记录引用关系                             │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                  【入口控制层】CellGateway                        │   │
│  │  - 统一操作入口                                                 │   │
│  │  - 操作拦截与记录                                               │   │
│  │  - 权限与验证控制                                               │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                              ↑                                         │
│                              │ 执行操作                               │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    【模块层】CellModules                          │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │   │
│  │  │ SqlStorage   │  │   CodeModule │  │ ConfigModule │          │   │
│  │  │   Module     │  │              │  │              │          │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘          │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.2 架构层次说明

| 层级 | 名称 | 职责描述 |
| :--- | :--- | :--- |
| **L1** | 引用管理层 | 基于UniversalReference扩展，管理模块间引用关系、变更记录、依赖分析 |
| **L2** | 入口控制层 | 统一入口网关，拦截所有操作，触发引用记录，验证权限 |
| **L3** | 模块层 | 具体功能实现，包含SQL存储、代码执行、JSON配置三个子模块 |

---

## 3. 引用管理层（L1）

### 3.1 核心接口：IReferenceManagementLayer

| 功能分类 | 方法名 | 参数 | 返回值 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| **引用关系管理** | CreateReference | `source: string`, `target: string`, `type: RelationType` | `Guid` | 创建引用关系 |
| | RemoveReference | `source: string`, `target: string`, `type: RelationType` | `bool` | 删除引用关系 |
| | GetReferences | `resourceId: string`, `direction: Direction` | `List<ReferenceRelation>` | 查询引用列表 |
| | GetDependencies | `resourceId: string`, `depth: int` | `List<ReferenceRelation>` | 获取依赖树 |
| **变更记录管理** | RecordChange | `moduleId: string`, `operation: OperationType`, `before: Dictionary`, `after: Dictionary` | `Guid` | 记录变更 |
| | GetChangeRecord | `recordId: Guid` | `ChangeRecord` | 获取变更记录详情 |
| | QueryChanges | `filter: ChangeFilter` | `List<ChangeRecord>` | 条件查询变更记录 |
| | ExportChanges | `from: DateTime`, `to: DateTime` | `Stream` | 导出变更记录 |
| **分析计算** | CalculateImprovement | `moduleId: string` | `double` | 计算改进程度 |
| | AnalyzeDependencyGraph | 无 | `DependencyGraph` | 分析依赖图 |
| | DetectCircularDependencies | 无 | `List<CircularDependency>` | 检测循环依赖 |

### 3.2 核心数据模型

#### ChangeRecord（变更记录）

| 字段名 | 类型 | 说明 |
| :--- | :--- | :--- |
| `Id` | Guid | 记录唯一标识 |
| `ModuleId` | string | 所属模块ID |
| `ModuleType` | enum | 模块类型(Sql/Code/Config) |
| `Operation` | enum | 操作类型(Create/Update/Delete/Execute) |
| `ReferenceId` | Guid | 关联的引用ID |
| `BeforeState` | Dictionary | 操作前状态快照 |
| `AfterState` | Dictionary | 操作后状态快照 |
| `Timestamp` | DateTime | 操作时间 |
| `Operator` | string | 操作执行者 |
| `Reason` | string | 操作原因描述 |

#### ReferenceRelation（引用关系）

| 字段名 | 类型 | 说明 |
| :--- | :--- | :--- |
| `Id` | Guid | 引用唯一标识 |
| `SourceModuleId` | string | 源模块ID |
| `TargetModuleId` | string | 目标模块ID |
| `RelationType` | enum | 关系类型(Dependency/Uses/Extends/Requires) |
| `Strength` | double | 关系强度(0-1) |
| `CreatedAt` | DateTime | 创建时间 |

### 3.3 依赖与集成

| 组件 | 版本 | 说明 |
| :--- | :--- | :--- |
| UniversalReference | 1.0+ | 引用管理基础 |
| Newtonsoft.Json | 13.0+ | JSON序列化 |

---

## 4. 入口控制层（L2）

### 4.1 核心接口：ICellGateway

| 功能分类 | 方法名 | 参数 | 返回值 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| **统一操作入口** | ExecuteOperation | `operation: CellOperation` | `OperationResult` | 执行单个操作 |
| | BatchExecute | `operations: List<CellOperation>` | `List<OperationResult>` | 批量执行操作 |
| **操作拦截与记录** | RegisterInterceptor | `interceptor: IOperationInterceptor` | `void` | 注册拦截器 |
| | EnableAuditLogging | `enabled: bool` | `void` | 启用/禁用审计日志 |
| | GetOperationHistory | `moduleId: string` | `List<OperationRecord>` | 获取操作历史 |
| **权限与验证** | ValidateAccess | `moduleId: string`, `operation: string` | `ValidationResult` | 验证访问权限 |
| | CheckModuleHealth | `moduleId: string` | `HealthStatus` | 检查模块健康状态 |

### 4.2 操作模型

#### CellOperation（操作封装）

| 字段名 | 类型 | 说明 |
| :--- | :--- | :--- |
| `OperationId` | Guid | 操作唯一标识 |
| `ModuleId` | string | 目标模块ID |
| `ModuleType` | enum | 目标模块类型 |
| `Action` | string | 操作动作名称 |
| `Parameters` | Dictionary | 操作参数 |
| `TransactionId` | Guid | 事务ID（用于批量操作） |
| `RequireAudit` | bool | 是否需要审计 |

#### OperationResult（操作结果）

| 字段名 | 类型 | 说明 |
| :--- | :--- | :--- |
| `OperationId` | Guid | 操作ID |
| `Success` | bool | 是否成功 |
| `Result` | object | 执行结果 |
| `Error` | Exception | 错误信息 |
| `Timestamp` | DateTime | 执行时间 |

### 4.3 入口控制流程

```
用户请求
    ↓
┌─────────────────────┐
│  CellGateway        │  ← 统一入口
│  ├─ 权限验证        │
│  ├─ 参数校验        │
│  └─ 创建Operation   │
└─────────────────────┘
    ↓
┌─────────────────────┐
│  拦截器链           │  ← 可扩展拦截
│  ├─ AuditInterceptor│
│  ├─ LogInterceptor  │
│  └─ ...             │
└─────────────────────┘
    ↓
┌─────────────────────┐
│  ReferenceManager   │  ← 记录引用
│  ├─ 创建引用记录    │
│  └─ 更新依赖关系    │
└─────────────────────┘
    ↓
┌─────────────────────┐
│  目标模块执行       │  ← 实际操作
└─────────────────────┘
    ↓
┌─────────────────────┐
│  变更记录           │  ← 记录状态变化
│  ├─ 保存ChangeRecord│
│  └─ 触发事件通知    │
└─────────────────────┘
    ↓
返回结果
```

### 4.4 依赖与集成

| 组件 | 版本 | 说明 |
| :--- | :--- | :--- |
| CellReferenceManagement | 1.0+ | 引用管理 |
| ICellModule | 1.0+ | 模块抽象接口 |

### 4.5 拦截器扩展点

支持注册自定义拦截器：
- 审计日志拦截器
- 性能监控拦截器
- 权限验证拦截器
- 自定义业务拦截器

---

## 5. 模块层（L3）

### 5.1 模块抽象接口：ICellModule

| 功能分类 | 方法名 | 参数 | 返回值 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| **生命周期管理** | Initialize | `config: Dictionary<string, object>` | `void` | 初始化模块 |
| | Start | 无 | `void` | 启动模块 |
| | Stop | 无 | `void` | 停止模块 |
| **操作执行** | Execute | `action: string`, `parameters: Dictionary<string, object>` | `object` | 通用执行入口 |
| | Query | `query: string` | `object` | 通用查询入口 |
| | Update | `data: Dictionary<string, object>` | `bool` | 通用更新入口 |
| **状态管理** | GetState | 无 | `Dictionary<string, object>` | 获取模块状态 |
| | SetState | `state: Dictionary<string, object>` | `void` | 设置模块状态 |

### 5.2 SQL存储模块（CellSqlStorageModule）

#### 核心接口：ISqlStorageModule

| 方法名 | 参数 | 返回值 | 说明 |
| :--- | :--- | :--- | :--- |
| ExecuteQuery | `sql: string`, `parameters: Dictionary<string, object>` | `DataTable` | 执行SQL查询 |
| ExecuteNonQuery | `sql: string`, `parameters: Dictionary<string, object>` | `int` | 执行SQL非查询操作 |
| ExecuteScalar | `sql: string`, `parameters: Dictionary<string, object>` | `object` | 执行标量查询 |
| BeginTransaction | 无 | `Guid` | 开启事务 |
| CommitTransaction | `transactionId: Guid` | `bool` | 提交事务 |
| RollbackTransaction | `transactionId: Guid` | `bool` | 回滚事务 |
| GetConnectionState | 无 | `ConnectionState` | 获取连接状态 |

#### 配置参数

| 参数名 | 类型 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| ConnectionString | string | 无 | 数据库连接字符串 |
| ProviderName | string | SQLite | 数据库提供程序 |
| MaxPoolSize | int | 10 | 连接池最大连接数 |
| CommandTimeout | int | 30 | 命令超时时间(秒) |
| EnableTransaction | bool | true | 是否启用事务支持 |

#### 变更触发场景

| 场景 | 触发条件 | 变更类型 |
| :--- | :--- | :--- |
| INSERT操作 | ExecuteNonQuery执行INSERT语句 | Create |
| UPDATE操作 | ExecuteNonQuery执行UPDATE语句 | Update |
| DELETE操作 | ExecuteNonQuery执行DELETE语句 | Delete |
| 事务提交 | CommitTransaction调用 | Update |
| 事务回滚 | RollbackTransaction调用 | Update |
| 连接状态变化 | 连接打开/关闭 | Update |

#### 依赖组件

| 组件 | 版本 | 说明 |
| :--- | :--- | :--- |
| SQLite | 3.45+ | 数据库引擎 |
| System.Data.SQLite | 1.0+ | ADO.NET提供程序 |

### 5.3 代码功能模块（CellCodeModule）

#### 核心接口：ICodeModule

| 方法名 | 参数 | 返回值 | 说明 |
| :--- | :--- | :--- | :--- |
| Initialize | `config: Dictionary<string, object>` | `void` | 初始化模块，注入工作流函数 |
| RegisterFunctions | `functions: IWorkflowFunctions` | `void` | 注册工作流函数实例 |
| GetRegisteredFunctions | 无 | `IWorkflowFunctions` | 获取已注册的函数实例 |
| CallFunction | `functionName: string`, `args: object[]` | `object` | 调用指定工作流函数 |
| CallFunctionAsync | `functionName: string`, `args: object[]` | `Task<object>` | 异步调用工作流函数 |
| GetAvailableFunctions | 无 | `IEnumerable<string>` | 获取所有可调用的函数列表 |
| HasFunction | `functionName: string` | `bool` | 检查函数是否存在 |
| IsAsyncFunction | `functionName: string` | `bool` | 检查函数是否为异步 |

#### 配置参数

| 参数名 | 类型 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| FunctionPrefix | string | Workflow. | 函数注册前缀 |
| EnableAsyncSupport | bool | true | 是否启用异步函数支持 |

#### 支持的工作流函数（基于WorkFlowMethod）

| 分类 | 函数 | 说明 |
| :--- | :--- | :--- |
| 基础运算 | Plus, Int, Float, Double, String | 数学运算和类型转换 |
| 字符串处理 | MergePrefix, Concat | 字符串操作 |
| 字典操作 | GetPathValue, SetPathValue | 嵌套字典路径操作 |
| 流程控制 | ExecuteWorkflow, ExecuteWorkflowAsync | 工作流执行 |

#### 变更触发场景

| 场景 | 触发条件 | 变更类型 |
| :--- | :--- | :--- |
| 函数注册 | RegisterFunctions调用 | Create |
| 函数调用 | CallFunction/CallFunctionAsync调用 | Execute |
| 模块启动 | Start调用 | Update |
| 模块停止 | Stop调用 | Update |

#### 依赖组件

| 组件 | 版本 | 说明 |
| :--- | :--- | :--- |
| WorkFlowMethod | 1.0+ | 工作流函数核心实现 |
| WorkFlowMethod.Contracts | 1.0+ | 工作流函数接口定义 |

### 5.4 JSON配置模块（CellConfigModule）

#### 核心接口：IConfigModule

| 方法名 | 参数 | 返回值 | 说明 |
| :--- | :--- | :--- | :--- |
| GetConfig | `key: string` | `object` | 获取配置值 |
| GetConfig\<T\> | `key: string` | `T` | 获取配置值(泛型) |
| SetConfig | `key: string`, `value: object` | `bool` | 设置配置值 |
| LoadConfig | `filePath: string` | `bool` | 从文件加载配置 |
| SaveConfig | `filePath: string` | `bool` | 保存配置到文件 |
| ReloadConfig | 无 | `bool` | 重新加载配置 |
| ValidateConfig | `schema: object` | `ValidationResult` | 验证配置合法性 |
| GetAllKeys | 无 | `List<string>` | 获取所有配置键 |
| ClearConfig | `key: string` | `bool` | 清除配置项 |

#### 配置参数

| 参数名 | 类型 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| ConfigPath | string | ./config/ | 配置文件目录 |
| ConfigFileName | string | config.json | 主配置文件名 |
| EnableAutoReload | bool | false | 是否自动重新加载 |
| AutoReloadInterval | int | 60 | 自动重载间隔(秒) |
| EnableValidation | bool | true | 是否启用配置验证 |
| SchemaFile | string | schema.json | 验证schema文件路径 |

#### 变更触发场景

| 场景 | 触发条件 | 变更类型 |
| :--- | :--- | :--- |
| 配置项设置 | SetConfig调用 | Update |
| 配置文件加载 | LoadConfig调用 | Create |
| 配置文件保存 | SaveConfig调用 | Update |
| 配置重新加载 | ReloadConfig调用 | Update |
| 配置验证 | ValidateConfig调用 | Execute |
| 配置清除 | ClearConfig调用 | Delete |

#### 依赖组件

| 组件 | 版本 | 说明 |
| :--- | :--- | :--- |
| Newtonsoft.Json | 13.0+ | JSON序列化 |

---

## 6. 离散变化追踪

### 6.1 核心设计思想

引用状态是**离散的**——每个引用要么存在（1），要么不存在（0）。使用**集合论**和**位运算**来精确追踪变化。

```
引用状态 = 离散集合 S = {r₁, r₂, r₃, ..., rₙ}

每个引用 rᵢ = (SourceModuleId, TargetModuleId, RelationType, Properties)
```

### 6.2 离散变化分类

| 变化类型 | 集合运算 | 含义 |
| :--- | :--- | :--- |
| **新增** | S₁ - S₀ | T1有但T0没有的引用 |
| **删除** | S₀ - S₁ | T0有但T1没有的引用 |
| **未变** | S₀ ∩ S₁ | T0和T1都存在的引用 |
| **修改** | (S₀ ∩ S₁) 中属性变化的引用 | 引用存在但属性改变 |

### 6.3 位向量表示

使用位向量来高效表示离散状态：

```
位向量 B = [b₁, b₂, b₃, ..., bₙ]

其中 bᵢ ∈ {0, 1}，表示第i个引用是否存在：
- bᵢ = 0: 引用不存在
- bᵢ = 1: 引用存在
```

**位运算变化检测**：

| 变化类型 | 位运算 | 说明 |
| :--- | :--- | :--- |
| 新增 | B₁ XOR (B₀ AND B₁) | B₁中有但B₀中没有的位 |
| 删除 | B₀ XOR (B₀ AND B₁) | B₀中有但B₁中没有的位 |
| 未变 | B₀ AND B₁ | 两者都为1的位 |
| 修改 | 需要额外属性比较 | 位相同但属性不同 |

### 6.4 核心接口：IReferenceChangeTracker

| 方法名 | 参数 | 返回值 | 说明 |
| :--- | :--- | :--- | :--- |
| GetChanges | `from: DateTime`, `to: DateTime` | `DiscreteChange` | 获取指定时间范围的所有变化 |
| GetReferenceHistory | `referenceId: string` | `List<ChangeRecord>` | 获取指定引用的变化历史 |
| GetModuleChanges | `moduleId: string`, `from: DateTime`, `to: DateTime` | `DiscreteChange` | 获取指定模块的变化 |
| QueryAddedReferences | `from: DateTime`, `to: DateTime` | `List<Reference>` | 查询新增引用 |
| QueryRemovedReferences | `from: DateTime`, `to: DateTime` | `List<Reference>` | 查询删除引用 |
| QueryModifiedReferences | `from: DateTime`, `to: DateTime` | `List<ModifiedReference>` | 查询修改引用 |
| CompareStates | `t0: DateTime`, `t1: DateTime` | `DiscreteChange` | 比较两个时间点的状态 |
| GenerateChangeReport | `from: DateTime`, `to: DateTime` | `ChangeReport` | 生成变化分析报告 |

### 6.5 核心数据结构

#### DiscreteChange（离散变化记录）

| 字段名 | 类型 | 说明 |
| :--- | :--- | :--- |
| Added | HashSet\<Reference\> | 新增引用集合 |
| Removed | HashSet\<Reference\> | 删除引用集合 |
| Unchanged | HashSet\<Reference\> | 未变引用集合 |
| Modified | HashSet\<ModifiedReference\> | 修改引用集合 |
| Timestamp | DateTime | 变化时间 |
| OperationSource | string | 操作来源 |

#### ModifiedReference（修改引用）

| 字段名 | 类型 | 说明 |
| :--- | :--- | :--- |
| Reference | Reference | 原始引用 |
| OldProperties | Dictionary | 旧属性 |
| NewProperties | Dictionary | 新属性 |

#### Reference（引用结构）

| 字段名 | 类型 | 说明 |
| :--- | :--- | :--- |
| SourceModuleId | string | 源模块ID |
| TargetModuleId | string | 目标模块ID |
| RelationType | RelationType | 关系类型 |
| Properties | Dictionary\<string, object\> | 属性集合 |

### 6.6 离散变化计算流程

```
┌─────────────────────────────────────────────────────────────────┐
│                    离散变化计算流程                            │
├─────────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌──────────────┐      ┌──────────────┐                      │
│  │   T0集合 S0  │      │   T1集合 S1  │                      │
│  └──────┬───────┘      └──────┬───────┘                      │
│         │                     │                               │
│         └─────────┬───────────┘                               │
│                   ↓                                           │
│         ┌─────────────────────┐                               │
│         │   集合差运算        │                               │
│         │                     │                               │
│         │  Added = S1 - S0    │                               │
│         │  Removed = S0 - S1  │                               │
│         │  Common = S0 ∩ S1   │                               │
│         └──────────┬──────────┘                               │
│                    ↓                                          │
│         ┌─────────────────────┐                               │
│         │   属性比较          │                               │
│         │                     │                               │
│         │  遍历 Common 中的   │                               │
│         │  每个引用，比较     │                               │
│         │  属性是否变化       │                               │
│         │                     │                               │
│         │  Unchanged = 属性   │                               │
│         │    未变的引用       │                               │
│         │  Modified = 属性    │                               │
│         │    变化的引用       │                               │
│         └──────────┬──────────┘                               │
│                    ↓                                          │
│         ┌─────────────────────┐                               │
│         │   构建DiscreteChange│                               │
│         │   返回精确变化记录   │                               │
│         └─────────────────────┘                               │
│                                                               │
└─────────────────────────────────────────────────────────────────┘
```

### 6.7 变化报告输出

```
引用变化分析报告
───────────────────────────────────────────────────────────────

【时间范围】
T0: 2024-01-01 10:00:00
T1: 2024-01-02 10:00:00

【变化摘要】
┌─────────────┬───────┐
│   变化类型  │ 数量  │
├─────────────┼───────┤
│   新增引用  │   2   │
│   删除引用  │   1   │
│   修改引用  │   1   │
│   未变引用  │   5   │
│   总计      │   9   │
└─────────────┴───────┘

【新增引用详情】
┌─────────────────────────────────────────────────────────────┐
│ 引用ID │ 源模块 │ 目标模块 │ 关系类型 │ 属性                   │
├────────┼────────┼──────────┼──────────┼───────────────────────┤
│  R5    │   A    │    E     │ Extends  │ {Strength: 0.7}       │
│  R6    │   A    │    F     │ Uses     │ {Strength: 0.8}       │
└────────┴────────┴──────────┴──────────┴─────────────────────┘

【删除引用详情】
┌─────────────────────────────────────────────────────────────┐
│ 引用ID │ 源模块 │ 目标模块 │ 关系类型 │ 属性                   │
├────────┼────────┼──────────┼──────────┼───────────────────────┤
│  R2    │   A    │    C     │ Uses     │ {Strength: 0.8}       │
└────────┴────────┴──────────┴──────────┴─────────────────────┘

【修改引用详情】
┌─────────────────────────────────────────────────────────────┐
│ 引用ID │ 属性名   │    旧值    │    新值    │    变化量       │
├────────┼──────────┼────────────┼────────────┼─────────────────┤
│  R3    │ Strength │    0.9     │    0.95    │    +0.05       │
└────────┴──────────┴────────────┴────────────┴─────────────────┘

【未变引用列表】
R1(A→B, Dependency), R4(A→D, Requires), R7(A→G, Other), ...
```

### 6.8 存储结构设计

```sql
-- 引用表
CREATE TABLE References (
    Id TEXT PRIMARY KEY,
    SourceModuleId TEXT NOT NULL,
    TargetModuleId TEXT NOT NULL,
    RelationType TEXT NOT NULL,
    Properties TEXT NOT NULL,  -- JSON格式
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

-- 变化记录表
CREATE TABLE ReferenceChanges (
    Id TEXT PRIMARY KEY,
    ChangeType TEXT NOT NULL,  -- Added/Removed/Modified
    ReferenceId TEXT NOT NULL,
    OldProperties TEXT,        -- JSON格式，删除和修改时有值
    NewProperties TEXT,        -- JSON格式，新增和修改时有值
    ChangedAt TEXT NOT NULL,
    OperationSource TEXT
);
```

### 6.9 依赖组件

| 组件 | 版本 | 说明 |
| :--- | :--- | :--- |
| CellReferenceManagement | 1.0+ | 引用管理基础 |
| SQLite | 3.45+ | 数据库引擎 |

---

## 7. 引用记录机制

### 7.1 完全入口控制模式

```
┌─────────────────────────────────────────────┐
│              完全入口控制机制                │
├─────────────────────────────────────────────┤
│                                             │
│  ┌─────────────────────────────────────────┐│
│  │           禁止直接访问                    ││
│  │   module.Execute()  →  拒绝访问         ││
│  └─────────────────────────────────────────┘│
│                                             │
│  ┌─────────────────────────────────────────┐│
│  │           必须通过Gateway                ││
│  │   gateway.Execute(moduleId, action)     ││
│  │       ↓                                 ││
│  │   1. 记录引用                           ││
│  │   2. 执行操作                           ││
│  │   3. 记录变更                           ││
│  └─────────────────────────────────────────┘│
│                                             │
└─────────────────────────────────────────────┘
```

### 7.2 引用关系建立规则

| 场景 | 源模块 | 目标模块 | 关系类型 |
| :--- | :--- | :--- | :--- |
| SQL查询 | CodeModule | SqlStorageModule | Uses |
| 配置读取 | CodeModule | ConfigModule | Requires |
| 配置驱动SQL | SqlStorageModule | ConfigModule | DependsOn |
| 函数调用 | CodeModule | CodeModule | Invokes |

---

## 8. 部署与集成

### 8.1 依赖关系

```
CELL Framework
    ├─ UniversalReference (引用管理基础)
    ├─ DataManage (数据存储抽象)
    ├─ SQLite (数据库引擎)
    └─ Newtonsoft.Json (配置处理)
```

### 8.2 配置示例

```json
{
  "CellFramework": {
    "Cells": [
      {
        "Id": "cell-001",
        "Name": "业务处理单元",
        "Modules": {
          "SqlStorage": {
            "ConnectionString": "Data Source=./data/example.db"
          },
          "Code": {
            "FunctionPrefix": "Workflow.",
            "EnableAsyncSupport": true
          },
          "Config": {
            "ConfigPath": "./config/",
            "ConfigFileName": "config.json",
            "EnableAutoReload": true,
            "AutoReloadInterval": 60
          }
        }
      }
    ],
    "AuditEnabled": true,
    "ReferenceTracking": true,
    "ImprovementCalculationInterval": "1h"
  }
}
```

---

## 9. 扩展能力

### 9.1 拦截器扩展

支持通过注册自定义拦截器扩展功能：
- 审计日志拦截器
- 性能监控拦截器
- 权限验证拦截器
- 自定义业务拦截器

### 9.2 模块扩展

支持动态加载自定义模块：
- 实现ICellModule接口
- 在配置中注册模块类型
- 通过Gateway统一管理

### 9.3 验证器扩展

支持自定义配置验证器扩展：
- 实现 `IConfigValidator` 接口添加自定义验证逻辑

### 9.4 数据库提供程序扩展

支持自定义数据库提供程序扩展：
- 实现 `IDbProvider` 接口接入其他数据库

---

## 10. 附录：术语表

| 术语 | 定义 |
| :--- | :--- |
| **CELL** | 模块容器单元，包含SQL/Code/Config三个子模块 |
| **Gateway** | 统一入口控制器，负责操作路由和拦截 |
| **Reference** | 模块间的依赖关系记录 |
| **ChangeRecord** | 操作变更的完整记录 |
| **ImprovementScore** | 基于变更记录计算的模块改进分数 |
| **DiscreteChange** | 离散变化记录（新增/删除/修改/未变） |
| **StateSnapshot** | 状态快照（时间戳 + 数据集） |

---

*文档版本: v2.0*  
*最后更新: 2026-05-10*
