# JsonUIBeautifier - JSON 配置文件美化框架

## 概述

JsonUIBeautifier 是一个专门用于处理 WdUI 风格 JSON 配置文件的智能美化框架。它从 `FileDriven\DrivenConfig\JsonConfig` 的 JSON 排布模式和 `WdUI` 的智能排布逻辑中汲取灵感，提供**分析→美化→重构**三阶段管线。

## 核心架构

```
┌─────────────────────────────────────────────────────────┐
│                    JsonUIBeautifier                      │
├─────────────────────────────────────────────────────────┤
│  Phase 1: 分析 (Analyzer)                                │
│  ├── StructureAnalyzer  - 树形结构分析、深度、类型分布    │
│  ├── StyleAnalyzer     - 颜色/字体分析、主题候选提取      │
│  └── LayoutAnalyzer    - 布局质量、间距一致性、模式识别   │
├─────────────────────────────────────────────────────────┤
│  Phase 2: 美化 (Beautifier)                              │
│  ├── LayoutOptimizer   - 网格对齐、布局优化              │
│  ├── ColorHarmonizer   - 颜色协调、相近色合并            │
│  ├── SpacingNormalizer - 间距标准化                      │
│  └── StyleUnifier      - 同类控件样式统一                │
├─────────────────────────────────────────────────────────┤
│  Phase 3: 重构 (Refactor)                                │
│  ├── ThemeExtractor    - 主题提取、注入 theme 节         │
│  ├── StyleDeduplicator - 样式去重、提取共享样式          │
│  └── SchemaUpgrader    - Schema 升级、布局策略推断       │
├─────────────────────────────────────────────────────────┤
│  工具 (Tools)                                            │
│  ├── JsonMinifier      - JSON 压缩/格式化                │
│  ├── ConfigValidator   - 配置完整性验证                  │
│  └── BatchProcessor    - 批量处理                        │
└─────────────────────────────────────────────────────────┘
```

## 设计理念

### 1. 从 JsonConfig 中汲取的排布智慧
- **结构化视图**：`views` → `controls` 的树形结构
- **布局描述**：`layout` 节使用 `left/top/width/height` 相对坐标
- **样式分离**：`backColor/foreColor/font` 等样式属性独立定义

### 2. 从 WdUI 中汲取的智能排布逻辑
- **布局模式识别**：自动识别 FormRow、Toolbar、VerticalStack 等模式
- **间距推断**：从现有布局中推断标准间距并统一
- **颜色协调**：自动合并视觉相近的颜色，提取主题色板

## 快速开始

```csharp
using JsonUIBeautifier;

// 基本使用
var beautifier = new JsonUIBeautifier();
var result = beautifier.ProcessFile("config.json", "config.beautified.json");
Console.WriteLine($"评分: {result.Report.Score.Total}/10");

// 自定义配置
var config = new BeautifierConfig 
{
    GridAlignment = 0.05,
    PrimaryColor = "#2563EB",
    EnableThemeExtraction = true
};
var beautifier = new JsonUIBeautifier(config);
var result = beautifier.ProcessFile("config.json");

// 仅分析
var report = beautifier.AnalyzeFile("config.json");
Console.WriteLine($"布局质量: {report.Score.LayoutQuality}");
Console.WriteLine($"颜色一致性: {report.Score.ColorConsistency}");
```

## 评分系统

美化报告提供 0-10 分的多维评分：

| 维度 | 说明 |
|------|------|
| LayoutQuality | 布局质量（重叠检测、边界检查） |
| ColorConsistency | 颜色一致性（颜色种类、相近色） |
| SpacingConsistency | 间距一致性（标准差评估） |
| StructureClarity | 结构清晰度（深度、容器比例） |
| **Total** | **综合评分** |

## 布局模式识别

框架能自动识别以下布局模式：

- **FormRow**: 标签+输入框交替排列
- **Toolbar**: 水平排列的按钮组
- **VerticalStack**: 垂直堆叠布局
- **HorizontalRow**: 水平排列布局
- **Card/Panel/Sidebar/Grid**: 更多模式可扩展

## 项目结构

```
JsonProcess/JsonUIBeautifier/
├── JsonUIBeautifier.csproj
├── README.md
└── src/
    ├── JsonUIBeautifier.cs          # 主入口类
    ├── Models/
    │   └── BeautifyModels.cs        # 数据模型
    ├── Utils/
    │   ├── JsonNavigator.cs         # JSON 导航工具
    │   ├── ColorUtils.cs            # 颜色处理工具
    │   └── LayoutMath.cs            # 布局数学工具
    ├── Analyzer/
    │   ├── StructureAnalyzer.cs     # 结构分析器
    │   ├── StyleAnalyzer.cs         # 样式分析器
    │   └── LayoutAnalyzer.cs        # 布局分析器
    ├── Beautifier/
    │   ├── LayoutOptimizer.cs       # 布局优化器
    │   ├── ColorHarmonizer.cs       # 颜色协调器
    │   ├── SpacingNormalizer.cs     # 间距标准化器
    │   └── StyleUnifier.cs          # 样式统一器
    ├── Refactor/
    │   ├── ThemeExtractor.cs        # 主题提取器
    │   ├── StyleDeduplicator.cs     # 样式去重器
    │   └── SchemaUpgrader.cs        # Schema 升级器
    ├── Pipeline/
    │   └── JsonBeautifierPipeline.cs # 美化管线
    └── Tools/
        ├── JsonMinifier.cs          # JSON 压缩工具
        ├── ConfigValidator.cs       # 配置验证器
        └── BatchProcessor.cs        # 批量处理器
```
