# UniversalReference 引用 Schema DSL（单一事实源）

> **⚠ 已废弃（DEPRECATED，2026-10-12）**：词表真源已上移至 `Projects\Plug\Agent\Config\nodes\reference.ushar`
> （`[Node]/[Edge]/[Constraint]` 声明同语言、同真源；消费方 = `Usharp.Adapters.UniversalReference.UsharpReferenceSchemaEmitter`
> → `DeclaredBundle.ReferenceSchemaJson`）。本文件保留仅为兼容既有 `ReferenceSchemaCodeGenerator` 测试路径，
> 待 D2 批改写 `ReferenceSchemaDslTests` 读 `.ushar` 后删除。**禁新增内容**，变更一律作用于 `reference.ushar`。

> **层级**：数据定义层 | **用途**：集中声明 `Operation.DataStoreDriven` 经 UniversalReference 使用的**资源节点类型**（`[Node]`）、**引用类型词汇表**（`[Edge]`）与**配对约束**（`[Constraint]`）。运行时消费方：`Operation.DataStoreDriven.ReferenceSchema.ReferenceTypeRegistry`（Agent 无关层自持 DSL/产物，不反向依赖 Agent）。
> **编译验证**：`Plate.DslMethod.Schema.ReferenceSchemaCodeGenerator` + `ReferenceSchemaRegistry`（校验悬空边端节点 / 悬空约束 / 重复 / dep 目标）。
> **产物**：本文件为唯一事实源；`reference-schema.generated.json` / `schema-report.txt` 为【纯编译产物】，禁止手改——变更须作用于本文件后重生成。
> **扩展**：新增 `[Node]`/`[Edge]`/`[Constraint]` 行 → 重生成，文法不变。
> **升级**：类型 `dep:replaced-by(Y)` 标记废弃 → 生成器标记 deprecated，校验器拒绝新增引用。

## Node（资源节点类型）

> 映射 `ResourceIdentifier.ResourceType` / UniversalReference `NodeType`。

[Node] workflow: 工作流节点 | display=工作流 | color=#4CAF50 | version=1
[Node] module: 模块节点 | display=模块 | color=#2196F3 | version=1
[Node] resource: 资源节点 | display=资源 | color=#9E9E9E | version=1
[Node] checkpoint: 检查点节点 | display=检查点 | color=#FF9800 | version=1
[Node] node: 步骤节点 | display=节点 | color=#795548 | version=1
[Node] task: 任务/会话节点 | display=任务 | color=#3F51B5 | version=1
[Node] keymapping: 键映射节点 | display=键映射 | color=#009688 | version=1
[Node] session: 会话节点 | display=会话 | color=#00BCD4 | version=1
[Node] history: 历史容器节点 | display=历史 | color=#673AB7 | version=1

## Edge（引用类型词汇表）

> 映射 UniversalReference `ReferenceType` 词汇表 / `EdgeType`。`src`/`tgt` 为 `any` 表示不约束端点节点类型。

[Edge] Dependency: 依赖 | src=any | tgt=any | strength=1.0 | display=依赖 | version=1
[Edge] Includes: 包含 | src=any | tgt=any | strength=1.0 | display=包含 | version=1
[Edge] Uses: 使用 | src=any | tgt=any | strength=1.0 | display=使用 | version=1
[Edge] Extends: 扩展 | src=any | tgt=any | strength=1.0 | display=扩展 | version=1
[Edge] Requires: 要求 | src=any | tgt=any | strength=1.0 | display=要求 | version=1
[Edge] Other: 其他 | src=any | tgt=any | strength=1.0 | display=其他 | version=1
[Edge] node_to_node: 节点数据流 | src=any | tgt=any | strength=1.0 | display=节点数据流 | version=1
[Edge] keymapping_to_node: 键映射归属 | src=any | tgt=any | strength=1.0 | display=键映射归属 | version=1
[Edge] Calls: 控制流调用 | src=any | tgt=any | strength=1.0 | display=控制流调用 | version=1
[Edge] Owns: 工作流包含 | src=any | tgt=any | strength=1.0 | display=工作流包含 | version=1
[Edge] cross_db_link: 任务节点归属 | src=task | tgt=node | strength=1.0 | display=任务节点归属 | version=1
[Edge] session_owns: 会话图挂载 | src=session | tgt=node | strength=1.0 | display=会话图挂载 | version=1
[Edge] task_owns_task: 任务树归属 | src=task | tgt=task | strength=1.0 | display=任务树归属 | version=1
[Edge] history_owns_task: 历史归拢任务 | src=history | tgt=task | strength=1.0 | display=历史归拢任务 | version=1
[Edge] history_owns_node: 历史挂载节点 | src=history | tgt=node | strength=1.0 | display=历史挂载节点 | version=1 | dep=replaced-by(cross_db_link)
[Edge] Exports: 跨工作流导出 | src=workflow | tgt=node | strength=1.0 | display=跨工作流导出 | version=1
[Edge] Inherits: 工作流模板继承 | src=workflow | tgt=workflow | strength=1.0 | display=工作流模板继承 | version=1
[Edge] Implements: 端口契约实现 | src=workflow | tgt=resource | strength=1.0 | display=端口契约实现 | version=1
[Edge] DependsOn: 编译期依赖 | src=workflow | tgt=resource | strength=1.0 | display=编译期依赖 | version=1

## Constraint（配对约束）

> 映射 UniversalReference `ValidationRule`/`FilterRule`。`max` 为 `unbounded` 表示不设引用上限。

[Constraint] Dependency: 依赖约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] Includes: 包含约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] Uses: 使用约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] Extends: 扩展约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] Requires: 要求约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] Other: 其他约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] node_to_node: 节点数据流约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] keymapping_to_node: 键映射归属约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] Calls: 控制流调用约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] Owns: 工作流包含约束 | srcType=any | tgtTypes=any | max=unbounded
[Constraint] cross_db_link: 任务节点归属约束 | srcType=task | tgtTypes=node | max=unbounded
[Constraint] session_owns: 会话图挂载约束 | srcType=session | tgtTypes=node | max=unbounded
[Constraint] task_owns_task: 任务树归属约束 | srcType=task | tgtTypes=task | max=unbounded
[Constraint] history_owns_task: 历史归拢任务约束 | srcType=history | tgtTypes=task | max=unbounded
[Constraint] history_owns_node: 历史挂载节点约束 | srcType=history | tgtTypes=node | max=unbounded
[Constraint] Exports: 跨工作流导出约束 | srcType=workflow | tgtTypes=node | max=unbounded
[Constraint] Inherits: 工作流模板继承约束 | srcType=workflow | tgtTypes=workflow | max=unbounded
[Constraint] Implements: 端口契约实现约束 | srcType=workflow | tgtTypes=resource | max=unbounded
[Constraint] DependsOn: 编译期依赖约束 | srcType=workflow | tgtTypes=resource | max=unbounded
