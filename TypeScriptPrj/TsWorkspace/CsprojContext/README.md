# CsprojContext

MCP 服务器，用于读取 .csproj 中嵌入的 ModuleRule 属性，实现项目架构动态导航。

## .csproj 嵌入格式约定

每个 `.csproj` 使用自定义 `PropertyGroup`（Label="ModuleRule"）嵌入项目规则：

```xml
<PropertyGroup Label="ModuleRule">
  <ModuleRule_Title>模块显示名称</ModuleRule_Title>
  <ModuleRule_Layer>层编号（0-7）</ModuleRule_Layer>
  <ModuleRule_Description>模块功能描述</ModuleRule_Description>
  <ModuleRule_Keywords>关键词1,关键词2,关键词3</ModuleRule_Keywords>
</PropertyGroup>

<ItemGroup Label="ModuleRule_Rules">
  <ModuleRule_Rule Include="模块级规则1" />
  <ModuleRule_Rule Include="模块级规则2" />
</ItemGroup>

<ItemGroup Label="ModuleRule_Prompts">
  <ModuleRule_Prompt Include="当需要xxx时，优先查看此模块" />
  <ModuleRule_Prompt Include="此模块的注意事项：xxx" />
</ItemGroup>
```

## 设计原则

1. **基础规则在 .clinerules，项目规则在 .csproj** — 职责分离
2. **按需读取，不进对话** — 使用 MCP 沙箱处理，只有摘要进入上下文
3. **.csproj 是单一数据源** — 模块描述、依赖、规则都在 .csproj 中，修改一处即可
4. **渐进式采用** — 先给核心模块加模块规则属性，再逐步推广到所有模块
5. **不影响构建** — 模块规则属性对 MSBuild 透明，不改变构建行为

## 开发

```bash
npm run build    # 编译
npm run generate # 重新生成工具定义
npm run dev      # 开发模式
npm run cli      # CLI 模式
```
