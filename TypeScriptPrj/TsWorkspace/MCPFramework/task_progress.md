# MCPFramework 代码修复任务清单

## 严重错误修复
- [x] #10 writeModuleRule 正则替换破坏 .csproj 文件
- [ ] #11 readModuleRule 相对路径计算少一级
- [ ] #12 handleRemove 正则缩进假设过于严格
- [ ] #13 handleModify moduleLayer 类型转换问题
- [ ] #15 server.ts 错误处理可能输出 "[object Object]"
- [ ] #14 handleGenerate handler 命名冲突风险
- [ ] #16 getRegisteredTools 未处理空 handler 名

## 重复/冗余修复
- [ ] #1 候选路径遍历模式重复 → 提取 findFirstExisting 到 utils
- [ ] #5/#6 lazyLoadHandler loaders 数组冗余 + 双层 try-catch
- [ ] #2 7 个 handle 函数"查找项目根目录"代码重复
- [ ] #3 7 个 handle 函数统计输出模式重复
- [ ] #7/#8/#18 handleSync/handleInject 未实现功能 + 未使用变量
- [ ] #17 lazyLoadHandler .ts 扩展名加载总是失败
