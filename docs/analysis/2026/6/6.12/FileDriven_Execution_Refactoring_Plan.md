# FileDriven.Execution 重构修复计划

## 修复清单

### 严重问题（P0）
- [ ] 1. 删除 NonEventCommandHandler.Handle() 中的 switch 分支，完全依赖 NonEventCommandRegistry
- [ ] 2. 修复 hasFatalError 导致资源泄漏的问题，改用细粒度 try/finally
- [ ] 3. 修复 GUI 模式 ExecutionTime 丢失问题
- [ ] 4. 统一 JSON 脚本转换逻辑，消除 ResolveCommandFile 和 ScriptLoadSkeleton 的功能重叠

### 重复逻辑（P1）
- [ ] 5. 提取 findBindingFunc 包装逻辑为公共方法
- [ ] 6. 提取命令执行统计逻辑为公共辅助方法
- [ ] 7. 消除 BatchId 生成逻辑重复

### 冗余逻辑（P2）
- [ ] 8. 删除 PerformNonEventCommandValidation 中无用的 catch/throw 块
- [ ] 9. 简化 ExecutionPipelineEntry.Dispose()
- [ ] 10. 评估 FileDrivenPipelineContext 中冗余便捷属性是否可移除
- [ ] 11. 修复 CommandFrameworkUsage.Reset() 为 Initialize()

### 错误逻辑（P1）
- [ ] 12. 修复 Demo() 方法的递归风险
- [ ] 13. 修复 StartGuiMode 中命令文件解析失败时的行为
- [ ] 14. 修复 HandleWait 与 HandleSleep 行为不一致
- [ ] 15. 修复 BindingResolver Obsolete 属性使用问题
