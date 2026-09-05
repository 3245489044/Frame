
# DataManage 模块使用指南

## 概述

本文档详细介绍 DataManage 模块中核心逻辑的具体使用方法，包括图存储、基础存储、缓存机制和事务处理等功能。

---

## 目录

1. [图存储 (Graph Storage)](#1-图存储-graph-storage)
   - 1.1 IGraphStorage 接口使用
   - 1.2 CachedGraphStorage 使用
   - 1.3 GraphTransaction 使用

2. [基础存储 (KeyValue Store)](#2-基础存储-keyvalue-store)
   - 2.1 IKeyValueStore 接口使用
   - 2.2 FileKeyValueStore 使用
   - 2.3 SQLiteKeyValueStore 使用
   - 2.4 LiteDBKeyValueStore 使用

3. [缓存机制 (Cache)](#3-缓存机制-cache)
   - 3.1 IGraphStorageCache 使用
   - 3.2 CacheStatistics 使用

4. [索引管理 (Index Manager)](#4-索引管理-index-manager)
   - 4.1 IGraphIndexManager 使用

5. [事务处理 (Transaction)](#5-事务处理-transaction)
   - 5.1 ITransactionalKeyValueStore 使用
   - 5.2 IGraphTransaction 使用

6. [数据存储 (Data Storage)](#6-数据存储-data-storage)
   - 6.1 IDataStorage 使用
   - 6.2 WorkflowStorage 使用

7. [服务层 (Services)](#7-服务层-services)
   - 7.1 StorageFactory 使用
   - 7.2 UnifiedStorageManager 使用

---

## 1. 图存储 (Graph Storage)

### 1.1 IGraphStorage 接口使用

**创建实例**：
```csharp
// 通过 StorageFactory 创建图存储实例
var storageFactory = new StorageFactory();
var graphStorage = storageFactory.CreateGraphStorage(StorageEngine.SQLite, "path/to/storage");
await graphStorage.InitializeAsync();
```

**存储资源**：
```csharp
var resourceId = new ResourceIdentifier("user", "user-123");
var metadata = new Dictionary<string, object>
{
    { "Name", "张三" },
    { "Email", "zhangsan@example.com" },
    { "Age", 30 }
};

await graphStorage.StoreResourceAsync(resourceId, metadata, "symbol-001");
```

**获取资源**：
```csharp
var resource = await graphStorage.GetResourceAsync(resourceId);
if (resource != null)
{
    Console.WriteLine($"Name: {resource.Metadata["Name"]}");
}
```

**删除资源**：
```csharp
await graphStorage.DeleteResourceAsync(resourceId);
```

**按类型查询资源**：
```csharp
var users = await graphStorage.GetResourcesByTypeAsync("user");
foreach (var user in users)
{
    Console.WriteLine($"User: {user.Identifier.Id}");
}
```

**高级查询**：
```csharp
var filters = new Dictionary<string, object>
{
    { "Age", 30 }
};
var results = await graphStorage.QueryResourcesAsync(
    resourceType: "user",
    filters: filters,
    limit: 10,
    offset: 0);
```

**全文搜索**：
```csharp
var searchResults = await graphStorage.SearchResourcesAsync(
    keyword: "张三",
    searchFields: new List<string> { "Name", "Email" },
    maxResults: 20);
```

**批量操作**：
```csharp
var resources = new List<(ResourceIdentifier, Dictionary<string, object>, string?)>
{
    (new ResourceIdentifier("user", "u1"), new Dictionary<string, object> { { "Name", "李四" } }, null),
    (new ResourceIdentifier("user", "u2"), new Dictionary<string, object> { { "Name", "王五" } }, null)
};

await graphStorage.BulkStoreResourcesAsync(resources);
```

---

### 1.2 CachedGraphStorage 使用

**创建带缓存的图存储**：
```csharp
var innerStorage = storageFactory.CreateGraphStorage(StorageEngine.SQLite, "path/to/storage");
var cache = new GraphStorageCache();
var cachedStorage = new CachedGraphStorage(innerStorage, cache, TimeSpan.FromMinutes(10));

await cachedStorage.InitializeAsync();
```

**缓存工作原理**：
- 首次获取资源时，从底层存储读取并缓存
- 后续获取相同资源时，直接从缓存返回
- 存储/更新/删除操作会自动失效相关缓存

**获取缓存统计**：
```csharp
var stats = cachedStorage.GetCacheStatistics();
Console.WriteLine($"Hit Rate: {stats.HitRate:P2}");
Console.WriteLine($"Total Hits: {stats.TotalHits}");
Console.WriteLine($"Total Misses: {stats.TotalMisses}");
```

---

### 1.3 GraphTransaction 使用

**创建事务**：
```csharp
var transactionManager = new GraphTransactionManager(graphStorage);
using var transaction = await transactionManager.BeginTransactionAsync();

try
{
    // 在事务中执行操作
    await transaction.StoreResourceAsync(resourceId, metadata);
    await transaction.UpdateResourceAsync(resourceId2, updatedMetadata);
    
    // 提交事务
    await transaction.CommitAsync();
}
catch
{
    // 回滚事务
    await transaction.RollbackAsync();
    throw;
}
```

---

## 2. 基础存储 (KeyValue Store)

### 2.1 IKeyValueStore 接口使用

**通用操作**：
```csharp
var kvStore = new FileKeyValueStore("my-store");
kvStore.SetBaseDirectory("path/to/data");
await kvStore.InitializeAsync();

// 保存数据
var data = new Dictionary<string, object>
{
    { "Id", "record-001" },
    { "Value", "Hello World" },
    { "Timestamp", DateTime.UtcNow }
};
await kvStore.SaveAsync("my-container", data);

// 加载所有数据
var allRecords = await kvStore.LoadAllAsync("my-container");

// 删除数据
var deletedCount = await kvStore.DeleteAsync("my-container", "Id", "record-001");

// 检查存在性
var exists = await kvStore.ExistsAsync("my-container", "Id", "record-001");
```

---

### 2.2 FileKeyValueStore 使用

**创建文件存储**：
```csharp
var fileStore = new FileKeyValueStore("file-store");
fileStore.SetBaseDirectory(Path.Combine(PathManager.Instance.GetStorageDirectory(), "files"));
await fileStore.InitializeAsync();
```

**特点**：
- 使用 JSONL 格式存储
- 支持并发访问（AsyncLock）
- 适合小规模数据存储

---

### 2.3 SQLiteKeyValueStore 使用

**创建 SQLite 存储**：
```csharp
var sqliteStore = new SQLiteKeyValueStore("sqlite-store");
sqliteStore.SetDatabasePath(PathManager.Instance.GetDatabasePath("data.db"));
await sqliteStore.InitializeAsync();
```

**特点**：
- 使用 SQLite 数据库存储
- 支持事务操作
- 适合中规模数据存储

---

### 2.4 LiteDBKeyValueStore 使用

**创建 LiteDB 存储**：
```csharp
var liteDbStore = new LiteDBKeyValueStore("litedb-store");
liteDbStore.SetDatabasePath(PathManager.Instance.GetDatabasePath("data.db"));
await liteDbStore.InitializeAsync();
```

**特点**：
- 使用 LiteDB 文档数据库
- 支持复杂查询
- 适合灵活的数据模型

---

## 3. 缓存机制 (Cache)

### 3.1 IGraphStorageCache 使用

**创建缓存实例**：
```csharp
// 创建带过期策略的缓存
var cache = new GraphStorageCache(
    defaultExpiration: TimeSpan.FromMinutes(5),
    evictionPolicy: new LruEvictionPolicy(maxSize: 1000));
```

**缓存操作**：
```csharp
// 设置缓存
await cache.SetAsync(resourceId, resource);

// 获取缓存
var cachedResource = await cache.GetAsync(resourceId);

// 失效单个缓存
await cache.InvalidateAsync(resourceId);

// 按类型失效缓存
await cache.InvalidateByTypeAsync("user");

// 失效所有缓存
await cache.InvalidateAllAsync();

// 检查缓存存在
var exists = await cache.ExistsAsync(resourceId);
```

### 3.2 CacheStatistics 使用

**获取缓存统计**：
```csharp
var stats = cache.GetStatistics();

Console.WriteLine($"缓存命中率: {stats.HitRate:P2}");
Console.WriteLine($"总命中次数: {stats.TotalHits}");
Console.WriteLine($"总未命中次数: {stats.TotalMisses}");
Console.WriteLine($"当前缓存大小: {stats.CurrentSize}");
Console.WriteLine($"总驱逐次数: {stats.TotalEvictions}");
Console.WriteLine($"上次重置时间: {stats.LastReset}");
```

---

## 4. 索引管理 (Index Manager)

### 4.1 IGraphIndexManager 使用

**创建索引管理器**：
```csharp
var indexManager = new GraphIndexManager();
```

**索引操作**：
```csharp
// 创建索引
await indexManager.CreateIndexAsync("user", "Email");
await indexManager.CreateIndexAsync("user", "Name");

// 检查索引存在
var hasIndex = await indexManager.HasIndexAsync("user", "Email");

// 获取已索引的属性
var indexedProperties = await indexManager.GetIndexedPropertiesAsync("user");

// 通过索引查找
var resourceIds = await indexManager.LookupByIndexAsync("Email", "zhangsan@example.com");

// 删除索引
await indexManager.DropIndexAsync("user", "Email");

// 重建索引
await indexManager.RebuildIndexAsync("user");

// 获取索引统计
var stats = await indexManager.GetIndexStatisticsAsync();
```

---

## 5. 事务处理 (Transaction)

### 5.1 ITransactionalKeyValueStore 使用

**执行事务**：
```csharp
var transactionalStore = new SQLiteKeyValueStore("transactional-store");
await transactionalStore.InitializeAsync();

await transactionalStore.ExecuteInTransactionAsync(async (context) =>
{
    // 在事务上下文中执行操作
    await context.SaveAsync("container1", data1);
    await context.SaveAsync("container2", data2);
    await context.DeleteAsync("container3", "Id", "old-record");
});
```

### 5.2 IGraphTransaction 使用

**图存储事务**：
```csharp
var transaction = await graphStorage.BeginTransactionAsync();

try
{
    await transaction.StoreResourceAsync(resourceId1, metadata1);
    await transaction.StoreResourceAsync(resourceId2, metadata2);
    
    // 提交事务
    await transaction.CommitAsync();
}
catch
{
    // 回滚事务
    await transaction.RollbackAsync();
    throw;
}
```

---

## 6. 数据存储 (Data Storage)

### 6.1 IDataStorage 使用

**通用数据存储操作**：
```csharp
var dataStorage = new DataStorage(kvStore);
await dataStorage.InitializeAsync();

// 保存数据
await dataStorage.SaveAsync("config-key", new { Setting1 = "Value1", Setting2 = 123 });

// 加载数据
var config = await dataStorage.LoadAsync<MyConfig>("config-key");

// 删除数据
var deleted = await dataStorage.DeleteAsync("config-key");

// 检查存在性
var exists = await dataStorage.ExistsAsync("config-key");

// 获取所有键
var keys = await dataStorage.GetKeysAsync();

// 清空存储
await dataStorage.ClearAsync();
```

### 6.2 WorkflowStorage 使用

**工作流存储操作**：
```csharp
var workflowStorage = new WorkflowStorage<string>(kvStore);
await workflowStorage.InitializeAsync();

// 存储工作流
await workflowStorage.StoreAsync("workflow-001", workflowJson);

// 检索工作流
var workflow = await workflowStorage.RetrieveAsync("workflow-001");

// 删除工作流
await workflowStorage.DeleteAsync("workflow-001");

// 检查存在性
var exists = await workflowStorage.ExistsAsync("workflow-001");

// 获取所有工作流ID
var workflowIds = await workflowStorage.GetAllWorkflowIdsAsync();

// 清空所有工作流
await workflowStorage.ClearAllWorkflowsAsync();
```

---

## 7. 服务层 (Services)

### 7.1 StorageFactory 使用

**创建存储实例**：
```csharp
var factory = new StorageFactory();

// 创建 KV 存储
var kvStore = factory.CreateKeyValueStore(StorageEngine.File, "path/to/data");

// 创建图存储
var graphStorage = factory.CreateGraphStorage(StorageEngine.SQLite, "path/to/storage");

// 创建统一存储管理器
var unifiedManager = factory.CreateUnifiedStorageManager(StorageEngine.SQLite, "path/to/storage");
```

### 7.2 UnifiedStorageManager 使用

**统一管理多个存储**：
```csharp
var unifiedManager = new UnifiedStorageManager(dataStorage, workflowStorage, graphStorage);

await unifiedManager.InitializeAllAsync();

// 访问各存储
var dataStore = unifiedManager.DataStorage;
var workflowStore = unifiedManager.WorkflowStorage;
var graphStore = unifiedManager.GraphStorage;
```

---

## 8. 完整示例

### 示例1：完整的图存储工作流

```csharp
// 初始化
var factory = new StorageFactory();
var graphStorage = factory.CreateGraphStorage(StorageEngine.SQLite, "app-data");
await graphStorage.InitializeAsync();

// 创建缓存层
var cache = new GraphStorageCache();
var cachedStorage = new CachedGraphStorage(graphStorage, cache);

// 存储资源
var resourceId = new ResourceIdentifier("document", "doc-001");
var metadata = new Dictionary<string, object>
{
    { "Title", "技术文档" },
    { "Content", "这是一篇技术文档..." },
    { "CreatedAt", DateTime.UtcNow },
    { "Author", "张三" }
};
await cachedStorage.StoreResourceAsync(resourceId, metadata);

// 查询资源
var resource = await cachedStorage.GetResourceAsync(resourceId);
Console.WriteLine($"文档标题: {resource?.Metadata["Title"]}");

// 搜索资源
var results = await cachedStorage.SearchResourcesAsync("技术", new List<string> { "Title", "Content" });
Console.WriteLine($"找到 {results.Count} 个匹配结果");

// 事务操作
using var transaction = await graphStorage.BeginTransactionAsync();
try
{
    await transaction.StoreResourceAsync(new ResourceIdentifier("tag", "tag-001"), 
        new Dictionary<string, object> { { "Name", "技术" } });
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### 示例2：配置管理

```csharp
// 使用 KV 存储管理配置
var kvStore = new FileKeyValueStore("config-store");
kvStore.SetBaseDirectory("config");
await kvStore.InitializeAsync();

// 保存配置
var appConfig = new Dictionary<string, object>
{
    { "Version", "1.0.0" },
    { "DebugMode", false },
    { "MaxConnections", 100 }
};
await kvStore.SaveAsync("app-config", appConfig);

// 加载配置
var configs = await kvStore.LoadAllAsync("app-config");
foreach (var config in configs)
{
    Console.WriteLine($"Version: {config["Version"]}");
}
```

---

## 9. 最佳实践

### 9.1 存储引擎选择

| 场景 | 推荐引擎 | 原因 |
|------|---------|------|
| 小规模数据 | `FileKeyValueStore` | 简单轻量，无需额外依赖 |
| 中规模数据 | `SQLiteKeyValueStore` | 支持事务，性能较好 |
| 大规模/复杂查询 | `LiteDBKeyValueStore` | 文档数据库，灵活查询 |

### 9.2 缓存策略

- **读多写少场景**：使用较长缓存过期时间（如 10-30 分钟）
- **写多读少场景**：使用较短缓存过期时间（如 1-5 分钟）
- **关键数据**：考虑禁用缓存或设置较短过期时间

### 9.3 事务使用

- 多个相关操作应放在同一事务中
- 事务应尽量短小，避免长时间占用锁
- 捕获异常并正确回滚

---

## 10. 常见问题

### Q1: 如何选择合适的存储引擎？

**A**: 根据数据规模和查询需求选择：
- 开发/测试环境：`FileKeyValueStore`
- 生产环境（中小规模）：`SQLiteKeyValueStore`
- 生产环境（大规模/复杂查询）：`LiteDBKeyValueStore`

### Q2: 缓存数据不一致怎么办？

**A**: 缓存会在存储/更新/删除操作时自动失效，确保数据一致性。如果需要手动控制，可以调用 `InvalidateAsync` 方法。

### Q3: 事务失败如何处理？

**A**: 使用 try-catch 块捕获异常，在 catch 块中调用 `RollbackAsync()` 方法回滚事务。

### Q4: 如何提高查询性能？

**A**: 
1. 使用 `CachedGraphStorage` 缓存频繁访问的数据
2. 为常用查询字段创建索引
3. 使用批量操作减少数据库访问次数

---

## 附录：命名空间引用

```csharp
// 接口层
using DataManage.Abstractions;
using DataManage.Abstractions.Models;

// 基础存储层
using DataManage.Storage;
using DataManage.Storage.Helpers;

// 高级存储层
using DataManage.Data;
using DataManage.Data.Graph;
using DataManage.Data.Workflow;

// 服务层
using DataManage.Services;

// 工具层
using DataManage.Utilities;

// 异常定义
using DataManage.Exceptions;
```
