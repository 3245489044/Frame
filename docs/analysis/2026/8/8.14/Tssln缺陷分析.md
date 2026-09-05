## 结论：是，这暴露了 TsSln 的一个设计局限（依赖图盲区），但编译失败的直接触发点是「构建产物缺失」

### 拆解：两个层面的问题

**① 直接触发点（非 TsSln 缺陷）**
`csg-protocol` 从未构建，`Protocol/CSG/build/index.d.ts` 不存在，而 `package.json` 的 `types` 字段指向它 → TS2307。这个只要 `pnpm --filter csg-protocol build` 就能消除。

**② 结构性缺陷（TsSln 的设计盲区）——这是你真正问的**
即便你运行完整的 `tsm build`，**TsSln 也永远不会构建 `csg-protocol`**。证据链：

1. **依赖解析只认 `.csproj` 的 `<ProjectReference>`**
   - `csprojParser.ts` 第 31 行只扫描 `<ProjectReference\s+Include=...>`（见第 31 行）
   - ClineApi.csproj 中没有 ProjectReference（依赖在 `package.json` 里 `"csg-protocol": "workspace:*"`）
   - 因此 TsSln 认为 **ClineApi 无依赖**，拓扑排序中它排在最前，不会等待任何前置

2. **项目集合只来自 `.sln` 文件**
   - `slnParser.ts` 只解析 `TypeScript.sln`，其中只有 5 个桥接项目（BasicMethod/MCPFramework/MCPBridge/CsprojContext/ClineApi）
   - `Protocol/CSG` 不在 sln 中，TsSln 根本不知道它的存在

3. **两套依赖图不一致（根因）**
   | 层 | 依赖图来源 | 包含 CSG？ | ClineApi→csg-protocol？ |
   |----|-----------|-----------|------------------------|
   | **模块解析层（pnpm workspace）** | `TsWorkspace/package.json` 的 `workspaces` + 各包 `dependencies` | ✅ 是 | ✅ 是 |
   | **构建调度层（TsSln）** | `TypeScript.sln` + `.csproj` `<ProjectReference>` | ❌ 否 | ❌ 否 |

   两层的项目和依赖**刻意分离**（跨包依赖正确地用 workspace 声明，避免手工重复建 csproj 引用），但 TsSln 作为「按依赖顺序构建」的编排器只认第二层 → 它对第一层的依赖完全无感知。**新增一个包 + 用它作为依赖，对 TsSln 是透明的**，这正是本次踩坑的机制。

### 判定

| 项 | 是否设计局限 |
|----|------------|
| TS2307 本身 | 否，是未构建的正常结果 |
| TsSln 不感知 package.json/pnpm workspace 依赖 | **是**，依赖解析范围过窄，与代码里实际使用的依赖声明机制脱节 |
| Protocol/CSG 未纳入 TypeScript.sln | **是**，项目集合定义遗漏（这也是"错误归属到 BasicMethod.csproj"的原因之一：sln/桥接层不完整） |
| 全局统一构建时依赖缺失的防护 | **是**，无「依赖未构建」的前置检查，依赖 TsSln → 失败，但错误信息只能靠 TS 编译器事后报 |

### 可选加固方向（供后续讨论，非本次必须）

1. **让 TsSln 的依赖解析覆盖 package.json**：`csprojParser` 之外增加 `packageJsonParser`，把 `dependencies["workspace:*"]` 并入项目依赖图，并让 `Protocol/CSG` 进入项目源（可在 `tsm.config.json` 增加第二个 source 指向它，或把 CSG 加入 TypeScript.sln）。
2. **约定层防护**：为 `csg-protocol` 这类被跨包依赖的库在 sln 中登记一个桥接项目，构建顺序自然前置（最小改动，但治标——每次新增库都要记得登记）。
3. **构建前校验**：TsSln 构建前检查每个项目依赖的 workspace 包是否已有 `build/index.d.ts`/`build/index.js`，缺失即报「先构建依赖 X」。

需要说明：本次的**最小修复**仍然是先 `pnpm --filter csg-protocol build` 重建产物（这不是设计问题，是构建顺序问题）；是否要按上述方向加固 TsSln，取决于你希望它长期承担「统一编排」的职责。

如果你想让我实施修复或加固，请切换到 **Act 模式**。