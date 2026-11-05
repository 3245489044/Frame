# Strategy.Catalog — 通用分级查询框架（字典式目录）

> 定位：把「一大票平铺条目」按 **轴 × 分级** 重组为可逐级展开的目录面，供工具面 / 函数库 / 模板目录 /
> 产出链 / 注册表 / 节点域 / 资源发现等任意集合复用。
> 机制与语义分离：框架内零领域字符串（红线），领域词只经 `ICatalogSource` 由上层注入。
> 分级纪律：**索引浅、明细按需**——索引/目录只出摘要行，明细走 `describe` 按需展开。

## 目录

| 编号 | 职责 | 位置 | 关键类型 |
|---|---|---|---|
| C1 | 数据模型 | `Model/` | `CatalogItem` / `CatalogDetail` / `CatalogAxis` / `CatalogAxisValue` / `CatalogAxes`（轴值派生唯一口径） |
| C2 | 分级视图 | `Model/` | `CatalogTier`(Index/Group/Entry/Detail) / `CatalogIndex` / `CatalogGroupRow` / `CatalogList` / `CatalogSearchResult` / `CatalogStats` / `CatalogHint` |
| C3 | 扩展端口 | `Ports/` | `ICatalogSource`（唯一扩展点）/ `ICatalogSourceRegistry` |
| C4 | 分组器（单一实现） | `Core/` | `CatalogGrouper`：`Index` / `List` / `Group` / `Describe` / `HintFor` / `Search` / `SearchAll` / `Stats` / `Info` |
| C5 | 装配与查询面 | `Core/` | `CatalogRegistry` / `CatalogQueryService`（唯一查询面）/ `DelegateCatalogSource` / `ListCatalogSource`（一行接入语法糖） |
| C6 | JSON 门面 | `Json/` | `CatalogJson`（`CatalogQuery` 统一入参 → 稳定 JSON 输出；错误收敛为 `status:"error"` 信封 + 注册源清单） |
| C7 | 层次化（不定深度） | `Hierarchy/` | `HierarchyCatalogSource`（schema → 目录面）/ `HierarchyCatalogExpander`（引擎跑实例，产出各层实例数） |
| 异常 | 类型化错误 | `Diagnostics/` | `CatalogException` / `UnknownCatalogSourceException` / `DuplicateCatalogSourceException` / `UnknownCatalogAxisException` |

## 五件事（唯一查询契约）

| op | 语义 | 入参 | 出参（tier） |
|---|---|---|---|
| `sources` | 有哪些目录可查（最外层索引） | — | `sources[]` |
| `index` | 一级索引：常驻条目 + 轴目录行（值 + 计数） | `sourceId` | `CatalogIndex`（index） |
| `list` | 二级目录：某轴取值下的条目摘要 | `sourceId`,`axisId`,`value` | `CatalogList`（group） |
| `describe` | 明细：单条完整信息（字段/契约/执行体轨） | `sourceId`,`key` | `CatalogDetail`（detail）+ 未命中 `hint` |
| `search` / `searchAll` | 检索：键/显示名/摘要/标签/轴取值命中（有上限，截断可见） | `sourceId?`,`query`,`limit?` | `CatalogSearchResult`（entry） |
| `stats` | 统计：规模 + 各轴分布 + 最大分组 | `sourceId` | `CatalogStats`（index） |

对照「新华字典」：`sources`=字典柜 → `index`=部首/拼音表（含字数）→ `list`=部首页字表 →
`describe`=字条（读音/义项/例句）→ `search`=按字/拼音查字 → `stats`=凡例（收字规模）。

## 接入三档（按成本递增）

```csharp
// ① 一行接入（平铺清单最常用形态；前缀轴自动派生）
registry.Register(ListCatalogSource.ByPrefix("functions", "函数库",
    names.Select(n => (n, (string?)null))));

// ② 两段委托（要自定义摘要/执行体轨/常驻标记）
registry.Register(new DelegateCatalogSource("tools", "工具面",
    new[] { CatalogAxis.Of("prefix", "域") },
    enumerate: () => items,
    describe: key => BuildDetail(key)));

// ③ 自定义实现（明细来自别的存储/契约表时）
public sealed class MySource : ICatalogSource { /* Axes / Enumerate / Describe */ }
```

层次化（层数由数据决定 / 不定深度）直接把 schema 投影成目录面：

```csharp
registry.Register(HierarchyCatalogSource.For(schema, "nodes", "节点域"));
// 实例数（每层多少条）：复用引擎遍历 + 环守卫 + 预算
var expansion = await new HierarchyCatalogExpander(schema, reader).ExpandAsync(LevelRef.Of(root, scope));
```

## 复用的 Frame 设施

- `Modules\Data\DeclarativeDataflow`：`HierarchySchema` / `SchemaTopology` / `RelationalHierarchyTopology` /
  `HierarchyProcessEngine` / `ILevelProcessor` / `ProcessBudget`。
  **遍历 / 环守卫 / 深度预算 / 节点预算一律复用，禁再造层遍历逻辑**（先例：`Agent.Modules.Response.Discovery`）。
- `BasicMethod.Initialization`：`PortGuard`（P1 空值护栏收敛，全局 using）。
- JSON：`System.Text.Json`（内置；`UnsafeRelaxedJsonEscaping` 保中文可读）。

## 纪律（红线）

1. 框架内**不得**出现 tool/skill/llm/agent/节点 等具体领域字符串；领域词只经 `ICatalogSource` 注入。
2. 分组 / 检索 / 统计 / 分级**只有一份实现**（`CatalogGrouper`）；禁各域再造 `Xxx_GetDomains` / `Xxx_GetLibraries` / `Xxx_ListChains` 变体。
3. 轴与显示名是**数据**：轴定义、取值显示名由 source 提供，框架内禁硬编码映射表（对比旧 `ToolCatalogGrouper.DisplayNames`）。
4. 索引**不得**携带明细；检索有上限且截断可见（不静默丢结果、不静默返空——未命中必给 `hint`）。
5. 未注册源 / 未声明轴 / 重复 Id 一律**调用边界抛**（`CatalogException` 族），不静默兜底；替换走 `Replace`。

## 验收

```powershell
cd D:\Project\Main\Frame\CsharpPrj
dotnet build Strategy\Catalog\Catalog.csproj      # 本工程独立编译
dotnet build Strategy\Strategy.csproj             # 聚合链（须在其他构建空闲时跑，见 BuildGuard）
```

## 迁移现状

六套既有目录面（工具面 / 数学域 / 函数库 / 注册表 / 模板目录 / 产出链）的收敛映射、迁移阶段与对拍判据，
见同目录《分级查询通用框架-六套目录收敛方案.md》；**逐文件、可执行的改造步骤与验收判据**见
《分级查询通用框架-重构施工单.md》（含 §0 烘焙前置接线、W1–W7 工作项、对拍脚本与回滚路径）。
