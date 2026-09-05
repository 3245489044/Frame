# CELL框架复用指南

## 1. 概述

本指南详细说明如何基于现有 **UniversalReference** 项目构建 **CELL框架**，通过最大化复用现有代码资产，实现快速落地。

---

## 2. 现有项目架构分析

### 2.1 核心组件

| 组件 | 命名空间 | 核心职责 |
| :--- | :--- | :--- |
| **资源管理** | `UniversalWorkflowFramework.Core` | 资源的增删改查 |
| **引用管理** | `UniversalWorkflowFramework.Reference` | 引用关系管理 |
| **图存储** | `ReferenceFrame.GraphStorage` | 底层图数据库操作 |
| **事件驱动** | `UniversalWorkflowFramework.EventDriven` | 变化事件通知 |
| **模块管理** | `UniversalModuleFramework` | 模块实体管理 |

### 2.2 关键接口

```csharp
// 核心图存储接口
public interface IGraphReferenceManager {
    Task AddResourceAsync(ResourceIdentifier, Dictionary<string, object>);
    Task AddReferenceAsync(ResourceIdentifier, ResourceIdentifier, ReferenceType);
    Task<List<...>> GetReferencesAsync(ResourceIdentifier);
}

// 事件驱动接口
public interface IEventDrivenReferenceManager {
    IObservable<ReferenceChangeEvent> ReferenceChanges { get; }
}
```

---

## 3. 可复用组件清单

### 3.1 数据模型复用

| 现有模型 | CELL框架用途 | 复用方式 |
| :--- | :--- | :--- |
| `Reference` | 引用数据模型 | 直接复用 |
| `ReferenceType` | 关系类型枚举 | 直接复用 |
| `ReferenceChange` | 变化记录基础 | 扩展使用 |
| `ResourceIdentifier` | Cell标识 | 扩展使用 |
| `ResourceData` | 资源元数据 | 直接复用 |

### 3.2 接口复用

| 现有接口 | CELL框架用途 | 复用方式 |
| :--- | :--- | :--- |
| `IGraphReferenceManager` | 底层存储引擎 | 直接调用 |
| `IEventDrivenReferenceManager` | 变化通知 | 订阅触发 |
| `IModuleRepository` | Cell管理基础 | 扩展 |
| `IRelationshipRepository` | 引用关系管理 | 直接复用 |

### 3.3 存储层复用

| 现有组件 | CELL框架用途 | 复用方式 |
| :--- | :--- | :--- |
| `GraphDatabaseStorage` | 图数据库存储 | 直接复用 |
| `SqliteRelate` | SQLite适配 | 直接复用 |
| `LiteDBRelate` | LiteDB适配 | 直接复用 |

---

## 4. CELL框架三层架构复用方案

### 4.1 架构映射

```
┌──────────────────────────────────────────────────────────────┐
│                      CELL框架三层架构                        │
├──────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────┐     ┌─────────────────────────────┐    │
│  │  入口控制层     │     │        CellGateway          │    │
│  │   (新增)        │     │  - 统一操作入口             │    │
│  │                 │     │  - 权限验证                 │    │
│  │                 │     │  - 操作拦截                 │    │
│  └────────┬────────┘     └─────────────────────────────┘    │
│           │                                                  │
│           ▼                                                  │
│  ┌─────────────────┐     ┌─────────────────────────────┐    │
│  │  引用管理层     │     │   ReferenceManagementLayer   │    │
│  │  (复用+扩展)    │     │  - 引用关系管理(复用)       │    │
│  │                 │     │  - 离散变化计算(新增)       │    │
│  │                 │     │  - 状态快照(新增)           │    │
│  └────────┬────────┘     └─────────────────────────────┘    │
│           │                                                  │
│           ▼                                                  │
│  ┌─────────────────┐     ┌─────────────────────────────┐    │
│  │    模块层       │     │         CellModule          │    │
│  │   (复用+封装)   │     │  - SQL存储模块(复用)        │    │
│  │                 │     │  - 代码模块(复用)           │    │
│  │                 │     │  - JSON配置模块(复用)       │    │
│  └─────────────────┘     └─────────────────────────────┘    │
│                                                             │
└──────────────────────────────────────────────────────────────┘
```

### 4.2 入口控制层（新增）

```csharp
public class CellGateway {
    private readonly IReferenceManagementLayer _referenceLayer;
    
    public async Task<OperationResult> Execute(CellOperation operation) {
        // 1. 权限验证
        if (!await ValidatePermission(operation)) 
            return OperationResult.Forbidden();
        
        // 2. 获取操作前状态快照
        var beforeSnapshot = await _referenceLayer.TakeSnapshot();
        
        // 3. 执行操作（路由到对应模块）
        var result = await RouteOperation(operation);
        
        // 4. 获取操作后状态快照
        var afterSnapshot = await _referenceLayer.TakeSnapshot();
        
        // 5. 计算变化并记录
        var change = _referenceLayer.CalculateChange(beforeSnapshot, afterSnapshot);
        await _referenceLayer.RecordChange(change);
        
        return result;
    }
}
```

### 4.3 引用管理层（复用+扩展）

```csharp
public class ReferenceManagementLayer : IReferenceManagementLayer {
    private readonly IGraphReferenceManager _graphManager;  // 复用现有接口
    private readonly IEventDrivenReferenceManager _eventManager;  // 复用
    
    // 复用现有方法
    public async Task AddReference(Reference reference) {
        await _graphManager.AddReferenceAsync(
            new ResourceIdentifier { Id = reference.SourceId },
            new ResourceIdentifier { Id = reference.TargetId },
            Enum.Parse<ReferenceType>(reference.RelationshipType)
        );
    }
    
    // 新增：状态快照
    public ReferenceSnapshot TakeSnapshot() {
        var allReferences = _graphManager.GetAllReferencesAsync().Result;
        return new ReferenceSnapshot {
            Timestamp = DateTime.UtcNow,
            References = allReferences.ToList()
        };
    }
    
    // 新增：离散变化计算
    public DiscreteChange CalculateChange(ReferenceSnapshot before, ReferenceSnapshot after) {
        var beforeSet = new HashSet<ReferenceKey>(before.References.Select(r => r.ToKey()));
        var afterSet = new HashSet<ReferenceKey>(after.References.Select(r => r.ToKey()));
        
        return new DiscreteChange {
            Added = after.References.Where(r => !beforeSet.Contains(r.ToKey())).ToList(),
            Removed = before.References.Where(r => !afterSet.Contains(r.ToKey())).ToList(),
            Unchanged = before.References.Where(r => afterSet.Contains(r.ToKey())).ToList(),
            Modified = FindModifiedReferences(before, after)
        };
    }
}
```

### 4.4 模块层（复用+封装）

```csharp
public class CellModule {
    // 复用现有资源管理
    private readonly IGraphReferenceManager _storage;
    
    public CellModule(IGraphReferenceManager storage) {
        _storage = storage;
    }
    
    // SQL存储模块操作（复用现有存储）
    public async Task ExecuteSqlOperation(SqlOperation operation) {
        var resource = new ResourceIdentifier { 
            ResourceType = "CellSql", 
            Id = operation.CellId 
        };
        await _storage.AddResourceAsync(resource, new Dictionary<string, object> {
            ["SqlContent"] = operation.SqlContent,
            ["Timestamp"] = DateTime.UtcNow
        });
    }
    
    // 代码模块操作（复用现有存储）
    public async Task ExecuteCodeOperation(CodeOperation operation) {
        var resource = new ResourceIdentifier { 
            ResourceType = "CellCode", 
            Id = operation.CellId 
        };
        await _storage.AddResourceAsync(resource, new Dictionary<string, object> {
            ["CodeContent"] = operation.CodeContent,
            ["Language"] = operation.Language
        });
    }
    
    // JSON配置模块操作（复用现有存储）
    public async Task ExecuteConfigOperation(ConfigOperation operation) {
        var resource = new ResourceIdentifier { 
            ResourceType = "CellConfig", 
            Id = operation.CellId 
        };
        await _storage.AddResourceAsync(resource, operation.ConfigData);
    }
}
```

---

## 5. 变化追踪复用实现

### 5.1 复用现有事件机制

```csharp
public class ChangeTracker {
    private readonly IEventDrivenReferenceManager _eventManager;
    
    public ChangeTracker(IEventDrivenReferenceManager eventManager) {
        _eventManager = eventManager;
        
        // 订阅现有事件，转换为CELL变化记录
        _eventManager.ReferenceChanges.Subscribe(HandleReferenceChange);
    }
    
    private void HandleReferenceChange(ReferenceChangeEvent @event) {
        // 将现有事件转换为DiscreteChange记录
        var discreteChange = new DiscreteChange {
            Added = @event.Type == ReferenceChangeType.Added 
                ? new List<Reference> { @event.Reference } 
                : new List<Reference>(),
            // ... 其他变化类型处理
        };
        
        // 持久化变化记录
        PersistChange(discreteChange);
    }
}
```

### 5.2 复用现有变化模型

```csharp
// 扩展现有 ReferenceChange 为 DiscreteChange
public class DiscreteChange {
    // 复用现有类型
    public List<Reference> Added { get; set; } = new();
    public List<Reference> Removed { get; set; } = new();
    public List<Reference> Unchanged { get; set; } = new();
    
    // 扩展：包含属性变化的引用
    public List<ModifiedReference> Modified { get; set; } = new();
    
    // 复用现有字段
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string OperationSource { get; set; } = string.Empty;
}

// 扩展的修改引用模型
public class ModifiedReference {
    public Reference Reference { get; set; } = new();
    
    // 扩展：记录属性变化
    public Dictionary<string, object> OldProperties { get; set; } = new();
    public Dictionary<string, object> NewProperties { get; set; } = new();
}
```

---

## 6. 存储层复用

### 6.1 复用现有数据库结构

```sql
-- 复用现有 References 表
CREATE TABLE References (
    Id TEXT PRIMARY KEY,
    SourceModuleId TEXT NOT NULL,
    TargetModuleId TEXT NOT NULL,
    RelationType TEXT NOT NULL,
    Properties TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

-- 扩展：变化记录表（复用现有结构并扩展）
CREATE TABLE CellChanges (
    Id TEXT PRIMARY KEY,
    ChangeType TEXT NOT NULL,  -- Added/Removed/Modified/Unchanged
    ReferenceId TEXT NOT NULL,
    OldProperties TEXT,
    NewProperties TEXT,
    ChangedAt TEXT NOT NULL,
    OperationSource TEXT,
    CellId TEXT NOT NULL  -- 新增：Cell标识
);
```

### 6.2 复用现有存储接口

```csharp
public class CellStorage : ICellStorage {
    // 复用现有存储
    private readonly IGraphReferenceManager _graphStorage;
    
    public CellStorage(IGraphReferenceManager graphStorage) {
        _graphStorage = graphStorage;
    }
    
    public async Task SaveCellAsync(Cell cell) {
        // 保存SQL模块
        await _graphStorage.AddResourceAsync(
            new ResourceIdentifier { ResourceType = "CellSql", Id = cell.Id },
            new Dictionary<string, object> { ["Content"] = cell.SqlContent }
        );
        
        // 保存代码模块
        await _graphStorage.AddResourceAsync(
            new ResourceIdentifier { ResourceType = "CellCode", Id = cell.Id },
            new Dictionary<string, object> { ["Content"] = cell.CodeContent }
        );
        
        // 保存配置模块
        await _graphStorage.AddResourceAsync(
            new ResourceIdentifier { ResourceType = "CellConfig", Id = cell.Id },
            cell.ConfigData
        );
    }
}
```

---

## 7. 实现步骤

### 7.1 Phase 1: 基础架构搭建（1-2周）

| 任务 | 描述 | 复用内容 |
| :--- | :--- | :--- |
| 创建CellGateway | 统一入口控制 | 无（新增） |
| 创建ReferenceManagementLayer | 引用管理扩展 | IGraphReferenceManager |
| 创建CellModule | 模块封装 | ResourceIdentifier |

### 7.2 Phase 2: 变化追踪实现（2-3周）

| 任务 | 描述 | 复用内容 |
| :--- | :--- | :--- |
| 实现DiscreteChange | 离散变化模型 | Reference、ReferenceChange |
| 实现状态快照 | 快照机制 | GetAllReferencesAsync |
| 实现变化计算 | 集合差运算 | 现有引用查询 |

### 7.3 Phase 3: 模块层实现（2-3周）

| 任务 | 描述 | 复用内容 |
| :--- | :--- | :--- |
| SQL模块封装 | SQL存储操作 | IGraphReferenceManager |
| 代码模块封装 | 代码执行管理 | ModuleRepository |
| 配置模块封装 | JSON配置管理 | ResourceData |

### 7.4 Phase 4: 集成测试（1-2周）

| 任务 | 描述 | 复用内容 |
| :--- | :--- | :--- |
| 端到端测试 | 完整流程验证 | 现有测试框架 |
| 性能测试 | 变化计算性能 | 现有存储引擎 |

---

## 8. 代码复用示例

### 8.1 示例1：复用引用模型

```csharp
// 直接复用现有 Reference 模型
using UniversalWorkflowFramework.Reference.Models;

public class CellReference : Reference {
    // 扩展：添加Cell特定属性
    public string CellId { get; set; } = string.Empty;
}
```

### 8.2 示例2：复用图存储

```csharp
// 直接注入现有接口
public class CellService {
    private readonly IGraphReferenceManager _graphManager;
    
    public CellService(IGraphReferenceManager graphManager) {
        _graphManager = graphManager;
    }
    
    public async Task AddCellReference(string cellId, Reference reference) {
        await _graphManager.AddReferenceAsync(
            new ResourceIdentifier { ResourceType = "Cell", Id = cellId },
            new ResourceIdentifier { ResourceType = "Module", Id = reference.TargetId },
            Enum.Parse<ReferenceType>(reference.RelationshipType)
        );
    }
}
```

### 8.3 示例3：复用事件驱动

```csharp
// 订阅现有事件
public class CellChangeMonitor {
    public CellChangeMonitor(IEventDrivenReferenceManager eventManager) {
        eventManager.ReferenceChanges
            .Where(e => e.Source.ResourceType == "Cell")
            .Subscribe(HandleCellChange);
    }
    
    private void HandleCellChange(ReferenceChangeEvent e) {
        // 处理Cell相关变化
        Console.WriteLine($"Cell {e.Source.Id} reference changed");
    }
}
```

---

## 9. 迁移路径

### 9.1 数据迁移

```
现有资源 → Cell封装
    │
    ├─ ResourceType="Module" → ResourceType="Cell"
    ├─ 元数据扩展 → 添加CellId、CellType等字段
    └─ 引用关系保持不变
```

### 9.2 代码迁移

```
现有代码 → CELL框架
    │
    ├─ IGraphReferenceManager → 直接复用
    ├─ IModuleRepository → 扩展为 ICellRepository
    └─ ReferenceChange → 扩展为 DiscreteChange
```

---

## 10. 复用收益评估

| 维度 | 原有实现 | 复用后 | 节省 |
| :--- | :--- | :--- | :--- |
| **代码量** | 约5000行 | 约1500行新增 | 70% |
| **开发时间** | 约12周 | 约4周 | 67% |
| **测试覆盖** | 已有测试 | 复用测试 + 新增测试 | 50% |
| **维护成本** | 独立维护 | 统一维护 | 降低 |

---

## 11. 注意事项

1. **兼容性**：确保使用现有接口的最新版本
2. **扩展点**：通过继承而非修改现有代码
3. **事务一致性**：确保变化记录与实际操作在同一事务中
4. **性能优化**：快照操作可能影响性能，考虑异步快照

---

## 附录：关键映射表

| CELL概念 | 现有组件 | 映射关系 |
| :--- | :--- | :--- |
| Cell | Module | 一对一 |
| CellGateway | 无 | 新增 |
| ReferenceManagementLayer | ReferenceManager | 扩展 |
| DiscreteChange | ReferenceChange | 扩展 |
| CellOperation | 无 | 新增 |

---

*文档版本: 1.0*  
*最后更新: 2026-05-10*