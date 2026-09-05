# FunctionLibraryLoader 反射装载失败 —— 根因分析

> 范围：FileDriven 运行时 6 个函数库（AgentUiLibrary、RuleMatchFunction、PromptFrameworkFunction、GuardFunction、McpToolFunction、EngineLibrary）经 `Type.GetType` 全部解析失败。
> 现状：已用 `AssemblyLoadContext.Default.Resolving` 兜底让 6 个库恢复加载；本文分析兜底并没有消除的**根本原因**，并给出成型方案方向（非补丁）。

---

## 1. 表象

运行时日志（`FunctionLibraryLoader.LoadFromConfig`）：

```
[ERROR] 函数库注册失败: detail=函数库 'AgentUiLibrary' 注册失败, reason=无法加载类型 'Agent.Launcher.AgentUiLibrary, Agent.Launcher'
[ERROR] 函数库注册失败: detail=函数库 'EngineLibrary' 注册失败, reason=无法加载类型 'GameEditor.Host.EngineLibrary, GameEditor.Host'
...（共 6 个）
```

本质一句话：`Type.GetType("Namespace.Class, AssemblyName", throwOnError:false)` 返回 `null`，即**按名解析失败**。DLL 物理文件明明存在于运行目录，宿主却"看不见"它们。

---

## 2. 直接原因

这 6 个程序集**不在宿主 FileDriven 的 `.deps.json` 中**。

.NET 默认 `AssemblyLoadContext` 只认可两类程序集来源：

1. 启动项目（或静态引用链上的项目）编译时记录进 deps.json 的程序集；
2. 运行时已被显式 `LoadFrom*` 加载过的程序集。

当前的 `Type.GetType` 调用对 Agent/Engine 相关 DLL 两条都不满足，于是解析失败。

> 关键事实：`.deps.json` 是**编译期生成的清单**，它决定"宿主认为哪些程序集可加载"。任何"编译后手动拷进目录"的 DLL 都不在清单里。

---

## 3. 中间原因：为什么"文件在，却不可加载"

为了守住红线「**FileDriven 不得引用 Projects\***」，采用了两条并行的部署/隔离手段：

- **PrjBridge 搬运**：`PrjBridge.csproj` 的自定义 `DeployBridgeToFileDrivenOutput` Target，用 MSBuild `Copy` 任务把 Agent/Engine 的权威产出物理拷进 FileDriven 运行目录；
- **`ReferenceOutputAssembly="false"`**：Engine 侧的 5 个 ProjectReference 只搬运字节，**不进入编译符号与依赖图**。

效果是：

```
磁盘层面：成功（dll 文件确实在）        —— ✅ Copy 任务完成了
装载层面：失败（deps.json 无登记）      —— ❌ 没有向装载器登记"可加载"
```

**拷贝只复制了产出物，并没有复制"该程序集可被加载"这个事实。**

---

## 4. 根本原因

> **编译期依赖图 与 运行期程序集发现 被硬切成了两套互不相通的机制，中间缺一个显式契约把它们接起来。**

拆开来看，根源有两层：

### 4.1 红线只约束了"不许编译期引用"，却没规定"运行时靠什么发现它们"

红线堵住了让 .NET **自然**加载这些程序集的唯一通道（静态引用 → deps.json 自动生成 → 装载天然成立）。

而替代它的"物理拷贝"机制，**只负责文件搬运，不负责装载登记**。

相当于：砍掉了系统的左腿（静态引用），却没有设计右腿（显式发现契约）。于是"这些程序集到底靠什么被加载"长期停留在**未定义**状态，直到首次运行暴雷。

### 4.2 反射装载依赖的是宿主 ALC 的隐式行为，而非显式设计

`Type.GetType` 的语义是"**在已登记的装载上下文里按名查找**"。

任何"手动拷进目录、又不被引用"的程序集，天然不在登记表里——这是 **.NET 框架的行为事实**，不是 bug。所以：

> 「把 DLL 拷进目录，就一定能 `Type.GetType` 到」——这个部署假设本身从一开始就是错的。

现在的 `Resolving` 兜底，本质是"运行时到处救火"：把**本应由设计保证的不变量**（部署即登记）临时补在装载一侧。

---

## 5. 为什么这是"一类"问题，而非一次性问题

关键证据：**连 PrjBridge 自己也是被反射装载的**（FileDriven 同样不引用它）。

也就是说，"孤儿程序集 / 拷了文件但没登记"这个坑，绝对不是 Agent/Engine 专属：

```
凡是走「FileDriven 不引用 + 手动拷贝」路径的任何 DLL，将来都会复发。
```

- 今天补 Agent 的传递依赖；
- 明天补 PrjBridge 内部某个更深层的传递依赖；
- 后天补下一个产品（Produce / Engine）相同模式的 DLL。

打补丁是**随程序集数量线性增长**的维护成本；而根因是**系统性**的。每一项新增的反射装载程序集，都要重复踩一遍"文件在、登记没有"的坑，靠 `Resolving` 逐个救火。

---

## 6. 成型方案方向（非补丁）

目标是：把"被反射装载的扩展程序集"变成一等公民，实现「**部署即注册**」，从根本上杜绝"文件在、登记没有"的漂移。

### 6.1 scoped ALC + 构建期清单（替代隐式 `Resolving` 全局兜底）

- 宿主启动阶段，创建一个**专属扩展目录集**的 `AssemblyLoadContext`；
- `FunctionLibraryLoader` 只从该 ALC **显式发现类型**，不依赖 `Type.GetType` 撞运气；
- 逻辑从"谁丢了文件我替谁补" → "我主动到我受管目录里去找"。

### 6.2 让"搬运"与"登记"出自同一数据源

PrjBridge 既然负责搬运，就由它**顺带生成一份装配清单**（被搬运出去的完整集合 + 传递依赖），交给宿主启动时喂给 scoped ALC。

```
单一数据源 → 拷贝清单 == 装载清单 → 结构上不可能漂移
```

### 6.3 重新审视红线的成本 / 收益（更冷峻的一条）

若 Agent/Engine、或 PrjBridge 确实是 FileDriven 的**运行时依赖**，那么更诚实的做法是让 FileDriven **正常引用 PrjBridge**（桥本身就是用来解决"唯一引用 Projects\*"的）：

- 依赖图如实反映依赖 → deps.json 自然生成 → 装载天然成立；
- `ReferenceOutputAssembly=false` + 手工 `Copy` + `Resolving` **三个补丁式机制可整体退役**。

这是一条架构决策，需要权衡"红线隔离"与"装载可靠性"的优先级后由业务侧拍板。

---

## 7. 一句话结论

> **根本原因是：部署机制（拷文件）与装载机制（登记 deps.json）被隔离红线拆开后，从未用一个显式契约把它们接回；而 `Resolving` 只是在装载一侧多加了一道隐式补丁，两套机制依旧漂移。**

| 层次 | 现状 | 成因 |
| --- | --- | --- |
| 表象 | `Type.GetType` 返回 null | 6 个程序集不在 deps.json |
| 直接原因 | 磁盘有文件、装载无登记 | 拷贝 ≠ 登记 |
| 中间原因 | 红线 + `ReferenceOutputAssembly=false` | 只搬字节、不进依赖图 |
| 根本原因 | 编译依赖图 与 运行发现机制 断裂 | 无显式契约（部署即注册）把二者接回 |
| 方案 | scoped ALC + 构建期清单 | 单一数据源，部署即注册，杜绝漂移 |

---

*mtime: 2026-09-02*  
*关联文件：`CsharpPrj\Modules\Registry\FunctionLibraryLoader.cs`、`Projects\PrjBridge\PrjBridge.csproj`*