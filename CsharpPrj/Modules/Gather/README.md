# Modules.Gather — 聚拢通用层

> 职责：声明驱动的聚拢管线内核（Need × Stage × Produce），零领域语义、零 UI 依赖、BCL-only。

## 1. 组成

| 件 | 职责 |
|----|------|
| `GatherSpec` | 声明数据模型：`GatherSpecSet` → `GatherViewSpec`（`Extends` + `Exclude*` 只承载组合意图） |
| `GatherSpecLoader` | 装载 + 组合折叠（`ResolveInheritance`）+ 合格性判定（V1–V15） |
| `GatherHub` / `GatherRegistry` | 唯一读写面 / 装配期闭合（R1–R8） |
| `GatherModule`（`IGatherModule` + `GatherModuleBuilder`） | 模块声明面；`FromView` 派生 Need/Function/Stage/Produce，`ProvideFor` 只登记视图需要的实现 |
| `DeclarationGather<T>` | **所有声明装载的唯一实现**：路径解析 → 读取 → 内容指纹(SHA256) → 解析 → 校验 → 失败姿态 → 可见化 |

## 2. 声明语法（gather-spec-v1）

```json
{
  "schema": "gather-spec-v1",
  "views": {
    "shell": { "stages": [...], "needs": [...], "produces": [...] },
    "task":  { "extends": "shell", "excludeProduces": ["board"],
               "needs": [{ "domain": "prompt", "scope": "task" }], "produces": [] }
  }
}
```

组合语义：

- 合并键：stage → `name`，need → `domain`，produce → `name`（子视图同键覆盖，新键追加）。
- 排除：`excludeStages` / `excludeNeeds` / `excludeProduces`；**排除不存在项 ⇒ 红**（声明漂移）。
- 组合折叠发生在合格性判定**之前**：raw 视图只有 `extends` 是合法的（V3 不会误判）。
- `sections` 必填（阶段渲染的壳段声明），缺失 ⇒ 装载期抛。

## 3. 装载纪律（W-4）

- `DeclarationLoadPolicy.FailClosed`：缺失/不合法 ⇒ 抛。
- `DeclarationLoadPolicy.VisibleFallback`：缺失/不合法 ⇒ 可见化事件 + `UsedFallback`（调用方须显式回退，禁静默）。
- 指纹取**内容** SHA256（非 mtime），供漂移与新鲜度对账。

## 4. 新增视图

1. 在 `Config\gather\*.json` 增/改视图声明（能 `extends` 就 `extends`，禁复制粘贴）。
2. 模块侧仅 `builder.FromView(view)` + `builder.ProvideFor(view, domain, producer)`。
3. 跑 `Check-GatherSpec.ps1 -Check`（S1–S12，含正负对照）。
