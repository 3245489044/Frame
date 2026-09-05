# TestInfrastructure — 通用测试框架

## 概述

TestInfrastructure 是一个**通用测试框架**，与具体领域业务逻辑无关。它提供了一套完整的测试基础设施，包括测试基类、断言辅助、Mock 工厂、UI 测试工具等，可被任何 .NET 测试项目复用。

## 核心设计原则

1. **领域无关性** — 所有代码与具体业务逻辑解耦，不依赖任何领域类型
2. **可扩展性** — 通过接口和基类提供扩展点，各领域可自定义实现
3. **减少重复** — 消除各测试项目中重复的测试基础设施代码
4. **统一规范** — 提供统一的测试编写规范和最佳实践

## 项目结构

```
TestInfrastructure/
├── Abstractions.cs          # 抽象层：IFixtureContext、IFixtureScope、TestResult、TestGroupAttribute
├── TestBase.cs              # 测试基类：TestBase、SkeletonTestBase、IntegrationTestBase、FileOutputTestBase、IsolatedTestBase
├── TestGroupBuilder.cs      # 测试用例构建器：TestGroupBuilder、TestGroupResult
├── AssertionHelper.cs       # 断言辅助：AssertionHelper、AssertionSkeleton
├── TestHelper.cs            # 通用辅助：TestHelper、TestPathResolver、TestLogger、TestReportGenerator
├── CleanupHelper.cs         # 清理辅助：CleanupHelper、TempDirectoryScope、TestDataIsolator
├── MockBase.cs              # Mock 基类：MockBase、MockFactory、CallbackRecorder、FuncMock、ValueProvider
├── RepoRootLocator.cs      # 仓库根目录定位器（惰性定位并缓存，供"解析产物+仓库目录"类测试复用）
├── ValidationTestHelper.cs  # 验证辅助：ValidationTestHelper、EnumTestHelper、LogCategoryTestHelper
├── TestCategories.cs        # 测试分层分类常量（Unit/Concurrency/Integration/Ui/EndToEnd）
├── AsyncTestHelper.cs       # 异步超时护栏（WithTimeoutAsync / WaitUntilOrThrowAsync）
├── UI/
│   ├── MessagePumpEngine.cs # 消息泵引擎（WinForms UI 测试）
│   ├── UiTestHelper.cs      # UI 测试辅助（控件查找、操作、验证）
│   └── SplitterTestHelper.cs# Splitter 测试辅助
└── TestInfrastructure.csproj
```

> 注：`Program.cs` / `ProgramRunner.cs` / `PathManager.cs`（`CentralConsole` 命名空间）不属于本测试框架，
> 已迁回其归属工程 `CentralTest/CSharpTest/CentralConsole.csproj`。

## 快速开始

### 1. 引用 TestInfrastructure

在测试项目的 `.csproj` 中添加项目引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\TestInfrastructure\TestInfrastructure.csproj" />
</ItemGroup>
```

### 2. 编写测试

```csharp
using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class MyTests : TestBase
{
    [TestMethod]
    public void TestSomething()
    {
        // 使用 TestGroupBuilder 组织测试
        var result = new TestGroupBuilder("MyGroup")
            .Add("Check1", () => true)
            .Add("Check2", () => { Assert.IsTrue(1 + 1 == 2); })
            .Run();

        result.AssertAllPassed(printSummary: true);
    }
}
```

### 3. 使用 Mock

```csharp
// 创建回调记录器
var recorder = MockFactory.CreateCallback(out Action action);
action();
Assert.AreEqual(1, recorder.CallCount);

// 创建函数 Mock
var funcMock = MockFactory.CreateFuncMock("result");
var result = funcMock.Execute();
Assert.AreEqual("result", result);
```

### 4. UI 测试

```csharp
[TestClass]
public class MyUiTests : TestBase
{
    [TestMethod]
    public void TestForm()
    {
        // 初始化消息泵
        MessagePumpEngine.Instance.Initialize();

        // 创建 Form
        var form = MessagePumpEngine.Instance.CreateForm();
        var button = new Button { Text = "Click Me", Name = "btnTest" };
        form.Controls.Add(button);

        // 查找并操作控件
        var found = UiTestHelper.FindControl<Button>(form, "btnTest");
        Assert.IsNotNull(found);
        UiTestHelper.ClickButton(found);
    }
}
```

## 核心组件说明

### 测试基类

| 基类 | 用途 |
|------|------|
| `TestBase` | 基础测试基类，提供计时、日志、生命周期管理 |
| `SkeletonTestBase` | Skeleton 流水线测试基类 |
| `IntegrationTestBase` | 集成测试基类，提供临时文件管理 |
| `FileOutputTestBase` | 文件输出测试基类 |
| `IsolatedTestBase` | 隔离测试基类，使用 IFixtureScope 隔离静态状态 |

### 抽象接口

| 接口 | 用途 |
|------|------|
| `IFixtureContext` | 夹具上下文接口，Arrange 和 Assert 之间的数据通道 |
| `IFixtureScope` | 夹具作用域接口，隔离依赖静态状态的测试 |
| `ISkeleton<TIn, TOut>` | Skeleton 接口，可执行的测试步骤 |

### 辅助工具

| 工具类 | 用途 |
|--------|------|
| `TestGroupBuilder` | 链式 API 组织测试用例组 |
| `AssertionHelper` | TestGroupResult 的断言扩展方法 |
| `TestHelper` | 通用测试辅助方法（等待、重试、计时） |
| `TestPathResolver` | 测试路径解析 |
| `TestLogger` | 结构化日志记录 |
| `TestReportGenerator` | 测试报告生成（文本/Markdown） |
| `CleanupHelper` | 安全清理操作 |
| `TempDirectoryScope` | 临时目录作用域（using 块自动清理） |
| `TestDataIsolator` | 测试数据隔离 |
| `ValidationTestHelper` | 参数验证测试（null/空字符串/不存在等） |
| `EnumTestHelper` | 枚举映射和谓词测试 |
| `LogCategoryTestHelper` | 字符串→枚举解析测试 |
| `TestCategories` | 测试分层分类常量（`Unit`/`Concurrency`/`Integration`/`Ui`/`EndToEnd`），供 `[TestCategory]` 标注，支撑按分类过滤 |
| `AsyncTestHelper` | 异步超时护栏（`WithTimeoutAsync`/`WaitUntilOrThrowAsync`），代码内 await 点超时兜底 |
| `MockFactory` | Mock 对象工厂 |
| `CallbackRecorder` | 回调记录器 |
| `FuncMock` | 函数 Mock |
| `ValueProvider` | 值提供者 |
| `RepoRootLocator` | 仓库根目录定位器（`RepoRootLocator.Root` 惰性计算并缓存） |

### UI 测试组件

| 组件 | 用途 |
|------|------|
| `MessagePumpEngine` | 消息泵引擎，提供 UI 线程封送 |
| `UiTestHelper` | UI 控件查找、操作、验证 |
| `ControlFinder` | 链式控件查找 API |
| `ControlTester` | 链式控件验证 API |
| `SplitterTestHelper` | SplitContainer 测试辅助 |

## 测试分类与超时护栏

按 `[TestCategory]` 分层，让"核心逻辑秒级信号"与"完整回归"分离；对并发/调度/异步测试加 `[Timeout]` 护栏，避免死循环/死锁无限挂死 testhost。

```csharp
using TestInfrastructure;

[TestClass]
[TestCategory(TestCategories.Concurrency)] // 类级分类（runner 可按 TestCategory 过滤）
public class ConcurrencyTests
{
    [TestMethod]
    [Timeout(15000)] // MSTest [Timeout] 仅支持方法级（实测 4.x 类级报 CS0592）
    public async Task SomeAsyncTest()
    {
        // 代码内 await 点也可叠加 AsyncTestHelper 精确护栏
        await AsyncTestHelper.WithTimeoutAsync(SomeTask(), 5000, "SomeTask");
    }
}
```

约定类别见 [TestCategories](TestCategories.cs)：`Unit`（纯逻辑秒级）、`Concurrency`（真实时序，需 `[Timeout]`）、`Integration`（外部依赖）、`Ui`、`EndToEnd`。

## 扩展指南

### 创建领域特定的 FixtureContext

```csharp
public class MyFixtureContext : FixtureContextBase
{
    public List<string> Items { get; } = new();

    public override void Reset()
    {
        Items.Clear();
        base.Reset();
    }
}
```

### 创建领域特定的 FixtureScope

```csharp
public class MyScope : FixtureScopeBase
{
    private string _savedState;

    public override void Enter()
    {
        _savedState = SomeStaticClass.State;
        base.Enter();
    }

    public override void Exit()
    {
        SomeStaticClass.State = _savedState;
    }
}
```

### 创建领域特定的 Skeleton

```csharp
public class MySkeleton : ISkeleton<MyContext, MyContext>
{
    public MyContext Execute(MyContext input, SkeletonContext context)
    {
        // 业务逻辑
        return input;
    }

    public async Task<MyContext> ExecuteAsync(MyContext input, SkeletonContext context)
    {
        return await Task.FromResult(Execute(input, context));
    }
}
```

## 迁移指南

从旧版（领域相关）迁移到新版（通用框架）：

1. 将测试类继承从 `WdUITestBase` 改为 `TestBase` 或 `IsolatedTestBase`
2. 将领域特定的 FixtureContext 改为实现 `IFixtureContext` 接口
3. 将领域特定的 FixtureScope 改为继承 `FixtureScopeBase`
4. 使用 `TestGroupBuilder` 替代手动测试用例组织
5. 使用 `ValidationTestHelper` 替代重复的参数验证测试
6. 使用 `MockFactory` 替代手动 Mock 创建
