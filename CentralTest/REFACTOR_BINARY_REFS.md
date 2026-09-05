# CentralTest 收敛到「直接引用已烘焙 dll」方案与分批实施清单

> 目标：CentralTest 的测试工程像 Agent/FileDriven 消费 CsharpPrj 那样，**引用产品已烘焙的权威 dll，
> 不在 CentralTest 内部把产品源码(ProjectReference)重新编译**。改测试只重编测试工程，改产品只重编产品、刷新权威目录。

## 1. 根因（为什么现在是"在内部编译"）

仓库根属性 `Directory.Build.props` 权威注释：
> `CsharpBuildOutputDir = CsharpPrj\BuildOutput\$(Configuration)\`（无 TFM）是 CsharpPrj 单一权威产物目录，
> 供产品用 `<Reference HintPath="$(CsharpBuildOutputDir)\X.dll" />` 引用其 dll，**避免直接从源码重复编译 CsharpPrj**。

仓库确立两种消费形态（都在 csproj + Directory.Build.props/targets 里）：
- **方案B（二进制，本仓库"直接使用原本 dll"的载体）**：Agent——`Projects/Agent/Directory.Build.props`
  集中一张 `CsharpPrjBinaryRefs` `<Reference HintPath>` 清单；根 `Directory.Build.targets` 的
  `EnsureCsharpBaked`（`RequireCsharpPrjBaked=true`）在权威目录缺 dll 时先烘焙 `MainProj.sln`，平时**不把 CsharpPrj 拉进自身构建图**。
- **FileDriven**：聚合 ProjectReference + `Private=false` + targets 剔除上游复制，运行期 `Resolving` 权威目录原地解析
  （"不持有、不重编副本"，最终仍落在权威 dll 上）。

CentralTest 的问题（已盘点到 csproj 级）：
1. `CentralTest/Directory.Build.props` 向**整个 CentralTest 树**注入 CsharpPrj 6 大聚合 ProjectReference
   （默认 Private=true）→ **AgentTest 子树的测试(测 Agent)也被误注入并连带编译 CsharpPrj 源码**。
2. `CentralTest/CSharpTest/Directory.Build.props` 同样注入聚合 PR，作为闭包来源。
3. 一批测试 csproj 仍散落对产品源码的 leaf ProjectReference：
   - AgentTest：`$(AgentProjectRoot)\src\Agent.*.csproj`（Agent.Core/Runtime/Axioms/…）
   - CSharpTest：`..\..\..\Operation\…、Share\…、BasicMethod\…`（这些相对路径在 CentralTest 深度会**悬空**，
     指向不存在的 `CentralTest\Operation\…`——工程陈旧/不能编的迹象）
   - FileDriven：`CSharpTest/FileDriven.Tests/FileDriven.UI.Testes` leaf-PR `FileDriven.Execution/UI/Pipeline`，
     其中 **`FileDriven.UI.csproj` 全仓不存在（悬空）**。
4. CentralTest **无 .sln、无统一构建入口**，产物散落于 `CentralTest\BuildOutput` 与子树 obj，另堆积大量
   `TestResults/Deploy_*` 历史目录。

## 2. 目标架构

```
CentralTest/
  Directory.Build.props        # 仅向上 Import 仓库根；预置 Configuration/OutputPath；不再注入聚合 PR
  Directory.Build.targets      # （可选）子树上收 target
  CSharpTest/Directory.Build.props
      Import CentralTest.props
      Import build/props/CsharpPrj.BinaryRefs.props   # 方案B：全部 CsharpPrj 权威 dll
      RequireCsharpPrjBaked=true                        # 缺 dll 时经根 EnsureCsharpBaked 先烘焙
      去掉原 6 聚合 ProjectReference 注入
  AgentTest/Directory.Build.props   # （新建）专门子树，不再吃 CentralTest 根里的 CsharpPrj 聚合
      Import build/props/CsharpPrj.BinaryRefs.props
      + AgentBinaryRefs：$(AgentProjectRoot)\BuildOutput\$(Configuration)\*.dll（Agent.Core/Contract/Runtime/Axioms/Orchestration/…）
      +（可选）EnsureAgentBaked 守护：缺 dll 时构建 Projects/Agent/Agent.sln
  各测试 csproj：
      移除对 产品(CsharpPrj/Agent/FileDriven) 源码的 ProjectReference   ← 由 props 集中 Reference 覆盖闭包
      保留对 CentralTest 内部共享工程(TestInfrastructure、AgentTest.csproj 宿主) 的 ProjectReference（仍编译，属测试基建）
  TestInfrastructure/TestInfrastructure.csproj
      Import CsharpPrj.BinaryRefs.props（它本身也消费 CsharpPrj）或维持其既有引用
```

- 共享清单文件已就位：`CentralTest/build/props/CsharpPrj.BinaryRefs.props`
  （39 个 dll，已核对 100% 存在于 `CsharpPrj\BuildOutput\Debug`）。
- 关键差异说明：产品"薄启动不复制"靠运行期 Resolving；**测试不行**（vstest 从本地加载），故测试一律
  `Private=true` 复制本地——目标只是"不把产品源码拉进 CentralTest 编译"，而非"测试输出不持有 dll"。
- NuGet 闭包：二进制 `<Reference>` **不传递 PackageReference**。个别测试源码直接用到的第三方类型
  （如 System.Data.SQLite、DeepCloner 等）若原先经聚合链传递获得，改二进制后需在这些测试 csproj 显式补
  `PackageReference`（按构建报错逐条补，数量应有限）。

## 3. 分批实施（每批以真实 `dotnet build` 验证后提交）

> 依赖顺序从"叶子/无产品 PR"到"产品 PR 多"推进；每批：改 props + 删对应 csproj 产品 PR → `dotnet build -c Debug` 该批 ≥1 工程 → 补漏包。

- 批 0（前置）：确认权威 dll 齐全；`git status` 记录基线。✅ 已确认（CsharpPrj 92 个 dll 在 `CsharpPrj\BuildOutput\Debug`）。
- 批 1：`TestInfrastructure` 收敛为二进制引用（它被最多测试共享，先做）。
- 批 2：`CSharpTest` 无产品-PR、纯靠聚合的一批（Threading/CSG.Unified/…）→ 靠 props 二进制闭包即可编译。
- 批 3：`CSharpTest` 带悬空 leaf-PR 的（FunctionStorage/EventTrigger/WdUI/CommandFramework/GraphFramework/Placeholder/Registry 等）
  → 删悬空 PR，靠 props；同时甄别其中**陈旧不可编**者，决定修复 vs 迁移 `_deprecated/`。
- 批 4：`AgentTest` 新建 props + 删 `$(AgentProjectRoot)\src\*.csproj` 产品 PR；处理 `HybridSchedulerCheck`（非测试宿主，单独看）。
- 批 5：`FileDriven.Tests/FileDriven.UI.Testes` —— 先判定 FileDriven.UI 是否已废弃；若死工程则整目录走
  `_deprecated/`，不再修。
- 批 6：建 `CentralTest.sln`（可选，按真需运行的测试子集聚合）；统一 `OutputPath=CentralTest\BuildOutput\$(Config)\`。

## 4. 清理指引（破坏性，须你确认后再跑，勿自动执行）

- `CentralTest/**/TestResults/Deploy_*`、`BuildOutput/obj` 等为构建/运行历史产物。是否 git 跟踪：
  先 `git ls-files 'CentralTest/**/TestResults/**' | Measure-Object`，未跟踪才可安全删除（否则 `git rm` 走提交）。
  建议先建 `CentralTest/.gitignore` 兜底（bin/obj/TestResults/BuildOutput），再把未跟踪产物清一遍。
- 迁移陈旧工程到 `_deprecated/`（而非删除源码），遵循 `clinerules/formula §七 FileDeprecationFlow`。
- 明确"废弃即删"前，用根 `references` 校验器跑反向引用，确认无 live 引用。

## 6. 实施状态（已执行）

已完成（2026-09，act 全量实施，"不考虑破坏测试"授权下）：
1. `CentralTest/build/props/CsharpPrj.BinaryRefs.props`：CsharpPrj 权威目录**全产品 dll 扫描引用**
   （排除 Microsoft.*/System.*/Newtonsoft.Json；`Reference Include=完整路径` 以避免 %() 元数据不展开）。
2. `CentralTest/Directory.Build.props`：去掉 6 大聚合 ProjectReference；点入 CsharpPrj 二进制闭包 +
   `RequireCsharpPrjBaked=true` + 统一 import CsharpPrj 生成的版本 props（消除 NU1015）。
3. 新增 `CentralTest/AgentTest/Directory.Build.props`：向上继承 CentralTest 根 + 追加 Agent 产品
   `$(AgentProjectRoot)\BuildOutput\$(Configuration)\Agent.*.dll` 二进制引用。
4. 批量移除 AgentTest+CSharpTest 各 csproj 中全部产品源码/悬空 `ProjectReference`（62 处），
   仅保留 CentralTest 内部共享（TestInfrastructure/宿主 AgentTest.csproj/CentralConsole/兄弟测试工程）。
5. 死工程 `FileDriven.Tests`（引用不存在的 FileDriven.UI）迁移到 `_deprecated\`。

验证（`dotnet build` 实测通过 0 错误）：`Threading.Tests`、`CSG.Unified.Tests`、`Agent.Guardrails.Tests`，
AgentTest 全量 10/15 通过；已确认不再把任何产品源码拉进 CentralTest 编译。

剩余失败均为【二进制消费的预期成本】，非机制缺陷，需按测试工程收尾：
- **产品权威烘焙未含最新模块/未重编**（CS0234 类型缺失，如 MetaCommand.Patterns/Storage、WdUI、EventTrigger）：
  属这些模块权威 dll 陈旧或不在当前聚合烘焙集 → 需重建对应产品（`CsharpPrj` 等）刷新权威目录后再编测试。
- **测试代码直接 using 第三方命名空间**（如 Agent.Core.Tests 的 `System.Reactive`、SemanticKernel 等）：
  二进制 `<Reference>` 不传递 NuGet → 在这些测试 csproj 显式补 `PackageReference`（镜像产品 csproj 所用版本）。
- AgentTest 中引用 `Agent.Operation` 的工程：Agent 权威输出无 Agent.Operation.dll（该产品模块未编），需先建产品。

