# AgentTest 测试整理合并与通用逻辑提取分析

> 分析日期：2026-08-24
> 分析范围：`CentralTest\AgentTest` 下 10 个测试工程（约 89 个 `[TestClass]`）与 `CentralTest\TestInfrastructure` 现有通用测试框架
> 目标：回答两个问题
> 1. 哪些测试可以整理合并（去重、拆分、归并、分类）；
> 2. 哪些通用逻辑和框架可以提取到 `TestInfrastructure`。

---

## 一、现状总览

### 1.1 AgentTest 测试工程结构

```
CentralTest\AgentTest\
├── AgentTest.csproj            # Exe 启动入口（Program.cs，Agent 测试控制台）
├── Program.cs
├── Agent.Axioms.Tests\         # 12 文件：LLM 客户端、State/Round、Skill、Plugins(SemanticKernel)、Axiom 绑定
├── Agent.ClineUsage.Tests\     # 11 文件：ClineClient、Daemon IPC、Round 状态映射、Stateless 执行器
├── Agent.Core.Tests\           # 约 41 文件（最大工程）：DynamicWorkflow、Loop/调度、Conversation/会话、Routing、规则引擎、快照、TaskPool
├── Agent.Guardrails.Tests\     # 1 文件：GlobalLlmGate
├── Agent.Persistence.Tests\    # 14 文件：SQLite/Provider/Store/关系/快照持久化
├── Boundary.Tests\             # 5 文件：压力计算、触发框架、仲裁、决策映射
├── DslMethod.Tests\            # 1 文件：Axiom 绑定架构校验
├── ReferenceSchema.Tests\      # 1 文件：引用 Schema DSL 架构校验
├── StorageTemplate.Tests\      # 1 文件：存储模板 DSL 架构校验
└── HybridSchedulerCheck\       # 1 文件：Program.cs（可执行验证脚本，非 MSTest）
```

### 1.2 关键观察

| 观察 | 说明 |
|------|------|
| **TestInfrastructure 复用率极低** | 全 AgentTest 仅 `Agent.Core.Tests` 引用了 `TestInfrastructure`，且仅 4 个并发/调度类实际使用；其余 9 个工程完全未接入 |
| **入口 Exe 与测试子工程混置** | `AgentTest.csproj` 是 `OutputType=Exe` 的控制台入口，各子工程又 `ProjectReference` 该 Exe 并内建 `*Tests.csproj`，职责边界需厘清 |
| **存在骨架性/架构合规测试** | `DslMethod/ReferenceSchema/StorageTemplate` 三工程本质是"DSL/架构合规验证"，高度同构，非领域功能测试 |
| **排除性编译项** | `EndToEndIntegrationTests.cs`、`EndToEndConversationLocalTests.cs` 被 `Compile Remove` 排除（依赖 GUI 装配环境），仅供可执行复现 |

---

## 二、可整理合并的测试

### 2.1 单文件多 `[TestClass]` 拆分与归并

| 文件 | 现状 | 建议 |
|------|------|------|
| `Agent.Core.Tests\DynamicWorkflowCoreTests.cs` | 单文件内含 6 个 `[TestClass]`（见 L12/331/409/484/671/779，如 `DynamicWorkflowMergerTests`），覆盖 merge/parse/build/execute | 按被测单元拆分为独立文件，遵循"一文件一 TestClass"；同时将共用 Merger 构造脚手架提取为测试夹具 |
| `Agent.ClineUsage.Tests\ClineClientIntegrationTests.cs` | 单文件内含 2 个 `[TestClass]`（`ClineClientIntegrationTests` + `ClineExecutorInjectionTests`） | 拆分：集成冒烟与 Executor 注入属不同被测目标，应独立成文件 |
| `Boundary.Tests\BoundaryTriggerFrameworkTests.cs` | 反应式/主动式/仲裁等多场景集中一文件 | 可按"触发源（Reactive/Proactive/Arbiter）"拆分为多个测试类，或保持但统一数据钩子 |

### 2.2 三个 DSL 架构合规测试工程高度同构 → 归并

三文件均为同构的"解析→注册→校验（悬空/重复/dep）→JSON 产物→运行时注册表→fail-fast"链路：

- `DslMethod.Tests\DslAxiomBindingTests.cs`
- `ReferenceSchema.Tests\ReferenceSchemaDslTests.cs`
- `StorageTemplate.Tests\StorageTemplateDslTests.cs`

三者几乎复刻了同一套结构（`[TestCategory("DslMethod")]`、同样的 `FindRepoRoot`、同样的编译期校验断言模式）。**建议**：
1. 抽取共享的"DSL 合规校验骨架/断言基类"（见 §3.3）下沉 `TestInfrastructure`；
2. 三个工程各自仅保留领域特定的 `SampleDsl` 与预期结论，删减重复的脚手架；
3. 评估是否可将三个工程合并为一个 `DslCompliance.Tests`（按被测 DSL 命名空间划分测试类），以消除三份并行的 csproj/重复引导。

### 2.3 Agent.Persistence.Tests 内的重叠与相邻测试

| 项目 | 现状 | 建议 |
|------|------|------|
| `SqliteNodeStoreTests.cs` vs `SqlitePersistenceTests.cs` | 两者都手动创建临时 Sqlite 库、`Setup` 建 `PersistenceOptions`/连接、`Teardown` 删目录；`SqliteNodeStoreTests` 专注 `ur_nodes` 幂等 upsert，`SqlitePersistenceTests` 专注事务/快照 | 二者存储层重叠度高，可保留但统一抽取 `SqliteTestFixture`（§3.2）消除各造各的库；同类的 `SnapshotPersistenceTests` 也归入同一 fixture |
| `DataStoreProviderFactoryTests.cs` / `PersistenceProviderFactoryTests.cs` | 两个 Factory 解析测试相邻，`DataStoreProviderFactoryTests` 内 `Options(PersistenceProvider)` 私有辅助（L116-124）为每个 provider 生成独立临时 db 路径 | 合并为一个 `DataStoreProviderFactoryTests` 或以同一 fixture 承载；临时 db 路径逻辑上移为 `SqliteTestFixture` |
| `SqliteNodeStoreTests.cs` L116-124 的 `Options()` | 在每个新测试处重复传真 `DatabasePath=TempPath+Guid` | 收敛为 `SqliteTestFixture.CreateOptions(provider)` |

### 2.4 排除性/复现性测试的归类

`Agent.Core.Tests\EndToEndIntegrationTests.cs`、`EndToEndConversationLocalTests.cs` 依赖 `UnifiedRegistryCenter`/GUI 环境，属"可执行复现验证"而非可回归单元测试，目前靠 `Compile Remove` 排除。**建议**：统一打 `[TestCategory(TestCategories.EndToEnd)]` 并按分类过滤运行，替代 `Compile Remove` 的心智负担；`HybridSchedulerCheck\Program.cs` 是运行验证脚本，可归入口工程复用 §3.5 的定位器/引导辅助。

### 2.5 分类标注统一

全库已有 34 处 `[TestCategory]`（28 文件），但用的是裸字符串（`"DslMethod"`、`"DynamicWorkflowMerger"`、`"Boundary*"`、`"ClineDaemon"` 等），与 `TestInfrastructure.TestCategories`（`Unit/Concurrency/Integration/Ui/EndToEnd`）是两套体系。**建议**：以 `TestCategories` 分层为主分类（决定回归范围/超时护栏），领域字符串降级为二级分类，保证"核心逻辑秒级信号"与"完整回归"可过滤分离。

---

## 三、可提取通用逻辑到 TestInfrastructure

> 原则：仅提取**领域无关**、可跨工程复用的基础设施。与 `TestInfrastructure` 现有能力重叠者优先复用，新增真正缺失者。

### 3.1 仓库根目录定位器 `RepoRootLocator`（高价值，已有多处复制）

`FindRepoRoot`（基于 `AppContext.BaseDirectory` 向上回溯查找同时含 `CsharpPrj` 与 `Projects\Agent` 的目录）在多个文件重复实现：

- `DslMethod.Tests\DslAxiomBindingTests.cs`（L36-47）
- `ReferenceSchema.Tests\ReferenceSchemaDslTests.cs`（L31-42）
- `StorageTemplate.Tests\StorageTemplateDslTests.cs`

**建议**：提取为 `TestInfrastructure.RepoRootLocator`（缓存一次定位，避免每次扫描上抛），供所有"解析产物 + 仓库目录"类测试复用。

### 3.2 SQLite 临时库夹具 `SqliteTestFixture`（高价值，16 处手动管理临时目录）

全 AgentTest 有 **16 个文件 / 20 处** `Path.GetTempPath()` 手动拼 Guid + 建目录 + Delete（`SqliteNodeStoreTests`、`SqlitePersistenceTests`、`SnapshotPersistenceTests`、`ConfigStoreTests`、`NodeOutputStoreTests`、`DataStoreProviderFactoryTests`、`PersistenceProviderFactoryTests`、`SkillCatalogTests` 等）。相互独立的实现都存在"跨测试文件句柄/文件锁冲突"的隐患（见 `DataStoreProviderFactoryTests.cs` L119 注释）。

**建议**：新增 `SqliteTestFixture`（创建独立临时库、返回 `PersistenceOptions`/连接、`IDisposable` 自动清理），或让 `IntegrationTestBase` + `TempDirectoryScope` 补一个针对 SQLite 的专用 scope。优先复用现有 `IntegrationTestBase` / `TempDirectoryScope` 的清理契约，只补"数据库 options 构造"这一薄层。

### 3.3 架构/模式合规断言辅助 `ArchitectureAssert`（支撑 DSL 骨架）

DSL 三工程的断言模式（编译期校验悬空、重复、dep 未声明等）高度同构。**建议**：提取领域无关的"反射/契约校验断言"辅助，如：
- `AssertTypeImplements<TInterface>(Type)`；
- `AssertBindingNonDangling(...)` 这类"被引用的标识符必须能被登记表解析"的通用断言；
- 统一的 DSL 编译期错误集合核对器。

这样三工程各保留 `SampleDsl` + 期望失败集合，删除重复遍历逻辑。

### 3.4 内存 HTTP 存根 `MockHttpServer`（中价值，已有一份完整实现）

`Agent.Axioms.Tests\OpenAiCompatibleLlmClientTests.cs`（L24-114）内置一份完整的 `HttpListener` 存根：`FindFreePort` + 前缀绑定 + 自增 `RequestCount` + 最近请求体捕获 + `Dispose/DisposeAsync` 收尾。这是典型的**跨工程通用工具**（LLM 测试、LocalLlm 集成、网关测试等都需要本地回环 HTTP）。

**建议**：将其通用化为 `TestInfrastructure.MockHttpServer`（返回可配置响应体/状态码/捕获请求体），`OpenAiCompatibleLlmClientTests` 改为使用之，并补 `MockHttpServer` 的自身单测。注意与 `LocalLlmHttpIntegrationTests` 的 `LocalLlmHttpHost` 区分：前者是纯 stub，后者携带协议语义，不强行合并。

### 3.5 测试引导/路径/报告小工具补齐

| 现有能力 | 缺口 / 建议 |
|---------|------------|
| `TestPathResolver` | 已含 `GetTestOutputDirectory`；可将 `RepoRootLocator`（§3.1）并入或并列提供 |
| `TestBase / IntegrationTestBase` | 建议新增 `SqliteTestFixture` 后，测试类只需 `: SqliteTestBase` |
| `AsyncTestHelper.WithTimeoutAsync` | 已覆盖并发/调度超时，继续在并发类沿用 `[Timeout]` 方法论级护栏 |
| `MockFactory/CallbackRecorder` | 已有；`ClineClient*`、`RoundStateMapper*` 等类的自定义 mock 回调可逐步切换复用 |

---

## 四、优先级与实施建议

### 高优先级（收益最大、改动面受控）
1. 新增 `TestInfrastructure.SqliteTestFixture`（§3.2），`Agent.Persistence.Tests` 全工程切换，消除 16 处手动临时库管理；
2. 提取 `RepoRootLocator`（§3.1），DSL 三工程 + Persistence 相关文件复用；
3. `DynamicWorkflowCoreTests.cs` / `ClineClientIntegrationTests.cs` 按单文件单 TestClass 拆分（§2.1）。

### 中优先级
4. 提取 `ArchitectureAssert`（§3.3），归并 DslMethod/ReferenceSchema/StorageTemplate 三工程为共享骨架（§2.2）；
5. 通用化 `MockHttpServer`（§3.4），替换 `OpenAiCompatibleLlmClientTests` 内嵌 stub；
6. 推广 `TestInfrastructure` 引用到其余 9 个测试工程（当前仅 Agent.Core.Tests 接入）。

### 低优先级 / 分类治理
7. 统一 `TestCategories` 分层 + 领域二级分类，替换裸字符串（§2.5）；
8. 排除性测试改为 `[TestCategory(EndToEnd)]` 按分类运行，替代 `Compile Remove`（§2.4）。

---

## 五、风险与注意事项

1. **Exe 入口混置**：`AgentTest.csproj` 为 Exe 且被各子工程引用，合并测试工程前需先厘清"控制台入口"与"测试工程"的关系，避免破坏现有 `dotnet test` 与 `AgentTest run <query>` 两条路径。
2. **SQLite 文件锁**：SQLite/本地后端"构造即持有句柄"，提取 fixture 时**必须**每测试独立路径（沿承 `DataStoreProviderFactoryTests.cs` 现注释约定），否则引入新的跨测试锁冲突。
3. **复现性测试不可强行自动化**：`EndToEnd*` 依赖 GUI 装配，只能归类隔离，不能改为无依赖的单元测试。
4. **DSL 三工程合并是结构性改动**：涉及 csproj 引用与 `[TestCategory("DslMethod")]` 语义，建议先落地 §3.1/§3.3 的共享骨架，再考虑合并工程，分步降低风险。
5. **复用优先于新增**：凡 `IntegrationTestBase`、`TempDirectoryScope`、`AsyncTestHelper`、`MockFactory` 已覆盖的能力，先切换复用，避免在 `TestInfrastructure` 内再造平行实现。