# 修复任务进度

## 严重问题（3个）
- [x] 1. EventContext 重复定义 — 删除 Models/EventContext.cs
- [x] 2. ListView 写入逻辑完全重复 — WinFormsValueConverter 委托给 EventBindingHelper
- [x] 3. Task.Run 中 AsyncLocal 诊断上下文丢失 — 捕获并传递 DiagnosticContext

## 中等问题（7个）
- [x] 4. 反射辅助方法委托残留 — 删除 EventBinderTransformer 和 ParameterBuilderTransformer 中的委托方法
- [x] 5. ReentryGuard 重复定义 — 提取为独立文件
- [x] 6. GetBoundEventNames 与 EventTypeMapperService 重复遍历 — 合并
- [x] 7. EventBindingPipelineRunner.ProcessControls 手动快照管理 — 简化
- [x] 8. EventExecutionService 为 null 时的静默失败 — 绑定阶段检查
- [x] 9. EventBindingPipelineRunner 中 EventExecutionService 的静态解析 — 改为构造函数注入
- [ ] 10. ContainerBinderPipeline 中 RegisterControlsAsync 的重复调用 — 已确认是合理设计（InitCommands 后重新注册），无需修改
