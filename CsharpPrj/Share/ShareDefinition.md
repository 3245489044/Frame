**Share 层不能反向引用 Operation 层、Strategy 层、以及 Frame 之外的产品工程（Projects\*）**

**Share 层只允许引用 BasicMethod 层、Modules 层、Plate 层（TConfig/JsonMethod/XmlMethod/WinFormsValueContract 等）、Protocol 层**

**唯一已登记例外：CommandFramework -> Projects\Route\Route.csproj**（命令配置资源路径经 Route.InformationPaths 解析，2026-09-06 路径单一真源收敛 决策 A）；登记处 CsharpPrj\modules.json 的 exceptions.references，待收敛为 Frame 侧抽象 + 组合根注入后移除。

## 强制方式

| 层 | 手段 | 位置 |
|----|------|------|
| 构建期（硬规则） | Share 出口规则守卫 | CsharpPrj\LayerBoundary.targets（LayerBarrier_Enforce=error|warn|off） |
| 离线全量（四类检查） | 悬空 / 未属性化 / 层序 / 循环依赖 + 反向引用报告 | PowerShellScript\Share\Solution\Test-ProjectReferences.ps1 -Check |

## 引用书写规范

ProjectReference 一律使用模块路径属性，禁止 ..\ 相对路径字面量：

    <ProjectReference Include="$(Proj_StateManagement)" />

模块路径单一真源：CsharpPrj\modules.json -> Generate-ModulePaths.ps1 -> generated\Directory.Build.props.modules.generated.props。
目录调整只需改 modules.json 一处并重新生成，全部 csproj 零改动。

## 目录分组

| 分组 | 模块 |
|------|------|
| Share\Foundations\ | FrameworkAbstractions、StateManagement、ConfigurationModels、ScopeFramework、TreeTraceFramework |
| Share\Frameworks\ | UniversalWorkflowFramework、GraphFramework、LayoutAnalysis、Diagnostics、Conversation |
| Share\Composition\ | CommandFramework、UniversalMiddleware、PlaceholderFramework、ControlLookup |
| Share\Orchestration\ | EventBindingOrchestrator、TaskStatusPool、Adapters、HostAssembly |