# OrchGraph.cs 拆分重构 - 下一步执行计划

## 当前状态

OrchGraph.cs 共 3218 行，包含 6 个命名空间：

| # | 命名空间 | 行数 | 状态 |
|---|---------|------|------|
| 1 | `OrchestrationConnect.Updates` | ~540行 | ✅ 已迁移到 GraphFramework/Directives + Services |
| 2 | `OrchestrationConnect.GraphBridge` | ~1140行 | ✅ 已迁移到 GraphFramework/Pipeline |
| 3 | `OrchestrationConnect.Deweight` | ~830行 | ✅ 已迁移到 GraphFramework/Deduplication |
| 4 | `WorkflowConnectorFramework.Core` | ~180行 | ✅ 已迁移到 GraphFramework/Connector |
| 5 | `OrchestrationConnect.Updates.Placeholders` | ~80行 | ✅ 已迁移到 GraphFramework/Placeholders |
| 6 | `OrchestrationConnect.Key` | ~450行 | ✅ 已迁移到 GraphFramework/KeyGeneration |

---

## 已完成步骤（Steps 4-6）

### ✅ Step 4: 迁移 WorkflowConnectorFramework.Core → GraphFramework/Connector

**已创建文件：** `GraphFramework/Connector/` 目录（5个文件）
- `IConnectableNode.cs` - 可连接节点接口
- `ConnectorInterfaces.cs` - 连接器框架核心接口（IChain, IConnectionDirective, INodeIdGenerator, INodeCloner, INodeReferenceRewriter, INodeProcessor, IConnectionContext, IDirectiveHandler, IConnectionRebuilder, IConnectorStep）
- `PipelineContext.cs` - 管线上下文（含 ReferenceEqualityComparer）
- `NodeResolver.cs` - 节点解析器
- `Directives.cs` - 连接指令实现（SequentialDirective, InsertDirective）

### ✅ Step 5: 迁移 Placeholders → GraphFramework/Placeholders

**已创建文件：** `GraphFramework/Placeholders/` 目录（1个文件）
- `PlaceholderEngine.cs` - 占位符引擎 + PlaceholderResolutionContext

### ✅ Step 6: 迁移 Key → GraphFramework/KeyGeneration

**已创建文件：** `GraphFramework/KeyGeneration/` 目录（2个文件）
- `KeyManager.cs` - 键管理器（EnsureChainSuffix, GenerateCallkey, GenerateReference, ExtractChainIndex）
- `KeyProcessor.cs` - 键处理器（ProcessConnectionKeyMapping, ProcessNodeKeys, 层次化键处理）

---

## 下一步：Step 7 - 删除 OrchGraph.cs 中的已迁移代码

**操作：** 删除 OrchGraph.cs 中已迁移的 3 个命名空间（~2500行）

**具体步骤：**
1. 删除 `OrchestrationConnect.Updates` 命名空间（第38-540行）
2. 删除 `OrchestrationConnect.GraphBridge` 命名空间（第543-1680行）
3. 删除 `OrchestrationConnect.Deweight` 命名空间（第1683-2508行）
4. 保留 `WorkflowConnectorFramework.Core`（第2514-2688行）
5. 保留 `OrchestrationConnect.Updates.Placeholders`（第2692-2765行）
6. 保留 `OrchestrationConnect.Key`（第2769-3218行）

**注意：** 删除前需确保：
- 所有外部引用已更新为新命名空间
- 编译通过

---

## 执行顺序建议

```
Step 4: Connector 迁移  →  编译验证
Step 5: Placeholders 迁移  →  编译验证
Step 6: KeyGeneration 迁移  →  编译验证
Step 7: 删除旧代码  →  最终编译验证
```

## 风险点

1. **命名空间冲突：** `GraphFramework/` 下已有 Pipeline、Deduplication、Directives、Services、Adapters、Snapshots、Transformers 等子目录，需确保新目录名不冲突
2. **循环引用：** Key 命名空间引用了 Updates 命名空间（`using OrchestrationConnect.Updates`），迁移时需注意
3. **外部依赖：** `WorkflowConnectorFramework.Core` 中的类型可能被外部项目引用
