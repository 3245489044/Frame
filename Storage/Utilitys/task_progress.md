# CodeRLManagerIntegrationTests 改进任务

## 任务目标
改进 CodeRLManagerIntegrationTests 集成测试，使其直接接入当前代码库，运行并产出代码。

## 问题分析
1. CodeRLManager.BuildPipeline() 创建了 factory 但未使用，而是 new CodeRLPipeline(_config) 导致组件脱节
2. CodeRLPipeline(CodeRLConfig) 构造函数创建空的 CodeRLPipelineFactory，没有传入组件
3. 测试类未继承 TestBase，无法复用统一组件
4. 测试使用 Directory.GetCurrentDirectory() 作为 codebasePath，但测试目录没有真实代码

## 修改完成
- [x] 分析现有代码结构和问题
- [x] 修复 CodeRLPipeline.cs - 添加支持传入 factory 的 CodeRLConfig 构造函数重载
- [x] 修复 CodeRLManager.cs - BuildPipeline() 使用已创建的 factory 构建管道
- [x] 重写 CodeRLManagerIntegrationTests.cs - 继承 TestBase，使用真实代码库路径
- [x] 运行测试验证修改是否正确（无编译错误）
