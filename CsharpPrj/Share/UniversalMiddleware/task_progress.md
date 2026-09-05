# UniversalMiddleware 重构任务进度

## 阶段 1：清理已删除/废弃的代码
- [x] 删除 `Serialization/Converters/StyleReferenceConverter.cs`
- [x] 删除 `Configuration/UIConfiguration.cs`
- [x] 标记/清理 `ServiceCollectionExtensions` 中的空方法
- [x] 删除 `JsonConverterRegistrar.RegisterToSafe`（与 RegisterToCompact 重复）

## 阶段 2：消除重复逻辑
- [x] 合并 `UnifiedJsonSerializer.ValidateModel<T>()` 和 `JsonValidator.ValidateModel<T>()`
- [x] 统一 `JsonPrettyPrinter.Minify` 委托至 CommonLib（保留本地实现，CommonLib 无 Minify）

## 阶段 3：删除冗余的静态包装类
- [x] 删除 `ProportionalMath`、`StackMath`、`ClampMath`、`OverlapMathWrapper` 静态类
- [x] 修复 `OverlapColorMapper.GetLevelDescription` 引用已删除类的问题

## 阶段 4：修复逻辑错误
- [x] 修复 `LayoutValidator.ValidateDefinition` 中引用已删除类的问题
- [x] 修复 `Container.cs` 中引用已删除 `UIConfiguration` 的问题
- [x] 修复 `OverlapColorMapper.GetLevelDescription` 引用已删除 `OverlapMathWrapper` 的问题
- [x] 确认 `JsonPipelineBuilder.Build()` 深拷贝已实现
- [x] 确认 `SmartLayoutEngine` 构造函数参数兼容性

## 阶段 5：代码组织优化（可选）
- [ ] 拆分 `Infrastructure.cs` → `Messaging/`、`Layout/`、`Diagnostics/` 子目录
- [ ] 拆分 `Serialization.cs` → 完善子目录结构
- [ ] 拆分 `Core.cs` → `Pipeline/`、`Middleware/`、`Transforms/` 子目录
