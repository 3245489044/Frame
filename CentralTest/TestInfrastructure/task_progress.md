# 修复任务进度

## 高优先级（错误逻辑修复）
- [x] 修复 MessagePumpEngine.Shutdown() 中 shutdownSw 未使用问题
- [x] 修复 MessagePumpEngine.RunOnUiThreadAsync() 线程安全问题
- [x] 修复 MessagePumpEngine 全局 SynchronizationContext 未恢复问题
- [x] 修复 TestGroupBuilder.Run() 死锁风险
- [x] 修复 TestHelper.WaitUntil() 超时语义问题
- [x] 修复 TestDataIsolator 构造函数调用实例方法问题

## 中优先级（重复逻辑消除）
- [x] 提取 RunOnUi 公共方法到 UiTestHelper（SplitterTestHelper 委托调用）
- [x] 删除 UiTestHelper 中与 SplitterTestHelper 重复的 Splitter 方法
- [x] 合并 AssertAllPassed 与 AssertNoFailures

## 低优先级（冗余逻辑优化）
- [x] 统一 IntegrationTestBase 清理逻辑使用 CleanupHelper
- [x] 统一 FileOutputTestBase 路径策略使用 TestPathResolver（保留原路径，添加注释说明）

## 本次完善（2026-08-22）
- [x] **修复编译**：`Program.cs` / `ProgramRunner.cs` / `PathManager.cs`（`CentralConsole` 命名空间）误置于本目录导致 13 个缺失包编译错误——已迁回归属工程 `CentralTest/CSharpTest/CentralConsole.csproj`，本库净化为纯测试框架
- [x] **MSTest 版本对齐**：csproj 硬编码 3.8.3 → 引用版本化 props 的 `$(MSTestTestFrameworkVersion)`（4.x，与全库测试工程一致）；`OutputPath` 统一到 `CentralTest\BuildOutput`
- [x] **修复 23 个消费方引用**：`CentralTest\CSharpTest` 下原指向 `CSharpTest\TestInfrastructure\` 的断裂引用全部重指向根 `CentralTest\TestInfrastructure\`
- [x] **新增通用模块**：`TestCategories.cs`（分类常量）、`AsyncTestHelper.cs`（异步超时护栏）
- [x] **应用**：Agent.Core.Tests 引用本库，4 个并发/调度类标 `Concurrency` 分类 + 方法级 `[Timeout]`；Agent.Axioms.Tests 的 AgentRunCancellationTests 标 `Unit` + `[Timeout]`
  - 注：实测 MSTest 4.x 的 `[Timeout]` **仅方法级有效**（类级报 CS0592），故统一放到 `[TestMethod]` 上
