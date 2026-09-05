# RuleCheck 统一规则校验框架设计文档

> **通用规则校验引擎** — 将"输入 → 解析 → 规则匹配 → 输出诊断"抽象为统一框架，适用于任意格式的源码/配置校验

---

## 目录

1. [核心设计理念](#一核心设计理念)
2. [架构总览](#二架构总览)
3. [Core Layer — 框架核心](#三core-layer--框架核心)
4. [Frontend Layer — 前端层](#四frontend-layer--前端层)
5. [Output Layer — 输出层](#五output-layer--输出层)
6. [规则定义格式](#六规则定义格式)
7. [CLI 设计](#七cli-设计)
8. [项目结构](#八项目结构)
9. [实施路线](#九实施路线)
10. [设计决策记录](#十设计决策记录)

---

## 一、核心设计理念

### 1.1 统一本质

许多工具本质上遵循相同的模式：

```
输入 → 解析 → 规则匹配 → 输出诊断
```

无论是编译器、linter、配置校验器还是静态分析工具，其核心流程都是将某种格式的输入解析为结构化表示，然后应用一组规则进行检查，最后输出诊断结果。

RuleCheck 框架将这个模式抽象为通用框架，使不同领域的校验工具共享同一套基础设施。

### 1.2 设计原则

- **统一核心**：Source、IR、Rule、Diagnostic 等核心抽象与具体前端无关
- **前端可插拔**：不同格式（JSON、YAML、Markdown、自定义 DSL 等）作为插件式前端
- **规则可配置**：规则用 YAML 定义，不硬编码，支持自定义扩展
- **输出多样化**：终端彩色输出、JSON、HTML、SARIF（VS Code 集成）
- **零运行时依赖**：Rust 编译为单 exe，可集成到任意构建管线

### 1.3 适用场景

| 场景 | 输入 | 解析方式 | 规则示例 |
|------|------|---------|---------|
| 配置文件校验 | JSON / YAML / TOML | 反序列化 | 字段必填、类型检查、交叉引用 |
| 源码语法检查 | 自定义 DSL | 词法/语法分析 | 语法正确性、语义约束 |
| 文档规范检查 | Markdown / HTML | 解析器 | 结构完整性、链接有效性 |
| 数据格式校验 | CSV / XML | 流式解析 | 字段格式、值范围 |
| 协议合规检查 | 二进制 / 文本协议 | 协议解析器 | 字段边界、校验和 |

---

## 二、架构总览

```
┌─────────────────────────────────────────────────────────────┐
│                    RuleCheck Framework                       │
│                    (通用规则校验引擎)                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                   Core Layer                         │   │
│  │                                                      │   │
│  │  Source        IR        Rule trait    RuleEngine    │   │
│  │  (输入源)    (中间表示)   (规则接口)    (执行引擎)     │   │
│  │                                                      │   │
│  │  Diagnostic   Report     RuleContext   AutoFix       │   │
│  │  (诊断信息)  (报告)      (上下文)      (自动修复)     │   │
│  │                                                      │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │           Diagnostic Pipeline                  │  │   │
│  │  │  Rule A → Rule B → Rule C → ... → Report      │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │               Frontend Layer                         │   │
│  │                                                      │   │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐          │   │
│  │  │ Frontend │  │ Frontend │  │ Frontend │  ...      │   │
│  │  │   A      │  │   B      │  │   C      │          │   │
│  │  │          │  │          │  │          │          │   │
│  │  │ Parser   │  │ Parser   │  │ Parser   │          │   │
│  │  │ → IR     │  │ → IR     │  │ → IR     │          │   │
│  │  │ Rules:   │  │ Rules:   │  │ Rules:   │          │   │
│  │  │  - ...   │  │  - ...   │  │  - ...   │          │   │
│  │  └──────────┘  └──────────┘  └──────────┘          │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │               Output Layer                           │   │
│  │                                                      │   │
│  │  TerminalFormatter  │  JsonFormatter  │  HtmlFormatter│   │
│  │  (终端彩色)          │  (JSON 格式)     │  (HTML 报告)  │   │
│  │                                                      │   │
│  │  SarifFormatter     │  GitHubActionsFormatter        │   │
│  │  (VS Code 集成)     │  (CI/CD 注解)                   │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 三、Core Layer — 框架核心

### 3.1 Source — 统一输入源

```rust
/// 任何可校验的源文件
pub enum Source {
    /// 文件系统中的文件
    File(PathBuf),
    /// 内存中的字符串（用于测试或管道输入）
    String { name: String, content: String },
    /// 流式输入
    Stream(Box<dyn Read>),
}

impl Source {
    /// 获取源名称（文件名或自定义名称）
    pub fn name(&self) -> &str;

    /// 获取源内容
    pub fn content(&self) -> Result<String>;

    /// 将字节偏移量转换为行号/列号
    pub fn offset_to_line_col(&self, offset: usize) -> (usize, usize);

    /// 获取文件扩展名（用于自动检测前端类型）
    pub fn extension(&self) -> Option<&str>;
}

/// 源位置信息
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SourceLocation {
    pub file: PathBuf,
    pub line: usize,
    pub column: usize,
    pub offset: Option<usize>,
}

/// 源范围
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SourceSpan {
    pub start: SourceLocation,
    pub end: SourceLocation,
}
```

### 3.2 IR — 统一中间表示

```rust
/// 解析后的统一中间表示
pub enum IR {
    /// 通用树形结构（适用于大多数场景）
    Tree(IRNode),
    /// 自定义 AST（前端可保留自己的 AST 类型）
    Custom(Box<dyn Any>),
}

/// 通用树节点
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IRNode {
    pub kind: String,                           // 节点类型标识
    pub name: Option<String>,                   // 节点名称
    pub properties: HashMap<String, Value>,     // 键值对属性
    pub children: Vec<IRNode>,                  // 子节点
    pub span: Option<SourceSpan>,               // 源码位置
}

impl IRNode {
    /// 按路径查询属性（支持嵌套路径，如 "a.b.c"）
    pub fn get_property(&self, path: &str) -> Option<&Value>;

    /// 查找第一个匹配名称的子节点
    pub fn find_child(&self, name: &str) -> Option<&IRNode>;

    /// 查找所有匹配类型的子节点
    pub fn find_children_by_kind(&self, kind: &str) -> Vec<&IRNode>;

    /// 收集所有节点的名称列表
    pub fn collect_names(&self) -> HashSet<String>;

    /// 遍历所有节点（前序）
    pub fn walk(&self) -> IRNodeWalker;
}
```

### 3.3 Rule — 规则接口

```rust
/// 一条校验规则
pub trait Rule: Send + Sync {
    /// 规则唯一标识
    fn id(&self) -> &str;

    /// 规则显示名称
    fn name(&self) -> &str;

    /// 默认严重级别
    fn severity(&self) -> Severity;

    /// 判断此规则是否适用于指定源
    fn applies_to(&self, source: &Source) -> bool;

    /// 执行校验，返回诊断信息列表
    fn check(&self, ir: &IR, context: &RuleContext) -> Vec<Diagnostic>;
}

/// 规则上下文（交叉引用所需）
pub struct RuleContext<'a> {
    /// 所有待校验的源文件
    pub all_sources: &'a [Source],
    /// 所有解析后的 IR
    pub all_irs: &'a [IR],
    /// 前端提供的额外上下文（如注册表、模板等）
    pub extra: HashMap<String, Box<dyn Any>>,
    /// 全局配置
    pub config: &'a CheckConfig,
}

/// 严重级别
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum Severity {
    Error,   // 必须修复
    Warning, // 建议修复
    Info,    // 仅供参考
    Hint,    // 代码风格提示
}
```

### 3.4 Diagnostic — 诊断信息

```rust
/// 诊断信息
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Diagnostic {
    /// 严重级别
    pub severity: Severity,
    /// 错误码（如 "E001"、"W002"）
    pub code: String,
    /// 触发的规则 ID
    pub rule_id: String,
    /// 人类可读的错误信息
    pub message: String,
    /// 源码位置
    pub location: Option<SourceLocation>,
    /// 修复建议
    pub suggestion: Option<String>,
    /// 上下文代码片段
    pub context: Option<String>,
    /// 自动修复方案
    pub fix: Option<AutoFix>,
}

/// 自动修复方案
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AutoFix {
    pub description: String,
    pub edits: Vec<TextEdit>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TextEdit {
    pub offset: usize,
    pub length: usize,
    pub new_text: String,
}
```

### 3.5 Report — 校验报告

```rust
/// 校验报告
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Report {
    /// 报告元数据
    pub metadata: ReportMetadata,
    /// 所有诊断信息（平铺）
    pub diagnostics: Vec<Diagnostic>,
    /// 按文件分组的诊断信息
    pub files: Vec<FileReport>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ReportMetadata {
    pub timestamp: String,
    pub duration_ms: u64,
    pub total_files: usize,
    pub total_rules: usize,
    pub summary: ReportSummary,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ReportSummary {
    pub errors: usize,
    pub warnings: usize,
    pub infos: usize,
    pub hints: usize,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct FileReport {
    pub file: PathBuf,
    pub frontend: String,
    pub diagnostics: Vec<Diagnostic>,
    pub summary: ReportSummary,
}
```

### 3.6 RuleEngine — 规则执行引擎

```rust
/// 规则执行引擎
pub struct RuleEngine {
    rules: Vec<Box<dyn Rule>>,
    frontends: HashMap<String, Box<dyn Frontend>>,
}

impl RuleEngine {
    pub fn new() -> Self;

    /// 注册规则
    pub fn register_rule(&mut self, rule: Box<dyn Rule>);

    /// 注册前端
    pub fn register_frontend(&mut self, name: &str, frontend: Box<dyn Frontend>);

    /// 从 YAML 文件加载规则
    pub fn load_rules_from_yaml(&mut self, path: &Path) -> Result<()>;

    /// 执行校验
    pub fn check(&self, sources: &[Source]) -> Result<Report>;

    /// 自动检测前端类型（根据文件扩展名）
    pub fn detect_frontend(source: &Source) -> Option<&str>;
}
```

### 3.7 Frontend — 前端接口

```rust
/// 前端：将 Source 解析为 IR
pub trait Frontend: Send + Sync {
    /// 前端名称
    fn name(&self) -> &str;

    /// 支持的文件扩展名列表
    fn supported_extensions(&self) -> Vec<&str>;

    /// 将源文件解析为统一 IR
    fn parse(&self, source: &Source) -> Result<IR>;

    /// 可选：提供额外上下文（如注册表、模板等）
    fn extra_context(&self) -> HashMap<String, Box<dyn Any>> {
        HashMap::new()
    }
}
```

---

## 四、Frontend Layer — 前端层

### 4.1 前端开发指南

开发一个新的前端只需实现 `Frontend` trait：

```rust
struct MyFrontend;

impl Frontend for MyFrontend {
    fn name(&self) -> &str { "my-format" }
    fn supported_extensions(&self) -> Vec<&str> { vec!["my", "myfmt"] }

    fn parse(&self, source: &Source) -> Result<IR> {
        let content = source.content()?;

        // 1. 解析输入为结构化数据
        let parsed = my_parser::parse(&content)?;

        // 2. 转换为统一 IR
        let root = IRNode {
            kind: "document".into(),
            name: None,
            properties: HashMap::new(),
            children: vec![
                IRNode {
                    kind: "section".into(),
                    name: Some("header".into()),
                    properties: HashMap::new(),
                    children: vec![],
                    span: None,
                },
            ],
            span: None,
        };

        Ok(IR::Tree(root))
    }
}
```

### 4.2 前端注册

```rust
let mut engine = RuleEngine::new();
engine.register_frontend("my-format", Box::new(MyFrontend));
```

### 4.3 规则与前端的关系

- 规则通过 `applies_to()` 方法判断是否适用于某个源文件
- 规则通过 `RuleContext` 获取所有源文件和 IR，实现交叉引用校验
- 规则可以访问前端提供的 `extra` 上下文（如注册表、模板等）

---

## 五、Output Layer — 输出层

### 5.1 报告格式化接口

```rust
/// 报告格式化器
pub trait ReportFormatter {
    fn format(&self, report: &Report) -> String;
    fn format_file(&self, file_report: &FileReport) -> String;
    fn format_diagnostic(&self, diagnostic: &Diagnostic) -> String;
}
```

### 5.2 终端输出（TerminalFormatter）

```text
RuleCheck v0.1.0 — 校验报告
═══════════════════════════════════════════════════════════
文件: example.config.json (auto-detected)
───────────────────────────────────────────────────────────
  ERROR [E001] 字段必填校验 (rule-001)
  → 必填字段 'name' 缺失
  → 位置: 第 12 行, 第 5 列
  → 建议: 添加 "name": "..." 字段

  WARNING [W001] 命名规范 (rule-002)
  → 字段名 'UserName' 应使用 camelCase 'userName'
  → 位置: 第 8 行, 第 3 列
  → 建议: 将 "UserName" 改为 "userName"

───────────────────────────────────────────────────────────
摘要: 1 错误, 1 警告, 0 信息
已用时间: 8ms
```

### 5.3 JSON 输出（JsonFormatter）

```json
{
  "metadata": {
    "timestamp": "2026-07-12T19:30:00Z",
    "duration_ms": 8,
    "total_files": 1,
    "total_rules": 10,
    "summary": { "errors": 1, "warnings": 1, "infos": 0, "hints": 0 }
  },
  "files": [
    {
      "file": "example.config.json",
      "frontend": "auto-detected",
      "summary": { "errors": 1, "warnings": 1, "infos": 0, "hints": 0 },
      "diagnostics": [
        {
          "severity": "error",
          "code": "E001",
          "rule_id": "rule-001",
          "message": "必填字段 'name' 缺失",
          "location": { "file": "example.config.json", "line": 12, "column": 5 },
          "suggestion": "添加 \"name\": \"...\" 字段"
        }
      ]
    }
  ]
}
```

### 5.4 SARIF 输出（SarifFormatter）

SARIF（Static Analysis Results Interchange Format）是微软定义的静态分析结果交换格式，VS Code 原生支持：

```json
{
  "$schema": "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
  "version": "2.1.0",
  "runs": [{
    "tool": { "driver": { "name": "RuleCheck", "version": "0.1.0" } },
    "results": [{
      "ruleId": "rule-001",
      "level": "error",
      "message": { "text": "必填字段 'name' 缺失" },
      "locations": [{
        "physicalLocation": {
          "artifactLocation": { "uri": "example.config.json" },
          "region": { "startLine": 12, "startColumn": 5 }
        }
      }]
    }]
  }]
}
```

---

## 六、规则定义格式

### 6.1 YAML 规则定义

```yaml
# 规则集元数据
name: "示例规则集"
version: "1.0"
frontend: my-format
description: "示例校验规则"

# 规则列表
rules:
  - id: rule-001
    name: 字段必填校验
    severity: error
    description: 检查必填字段是否存在
    tags: [correctness, required]
    check:
      type: function
      name: check_required_fields
    params:
      required_fields: ["name", "version", "type"]

  - id: rule-002
    name: 命名规范
    severity: warning
    description: 字段名应使用 camelCase
    tags: [style, naming]
    check:
      type: function
      name: check_naming_convention
    params:
      pattern: "^[a-z][a-zA-Z0-9]*$"

  - id: rule-003
    name: 值范围校验
    severity: error
    description: 数值字段应在指定范围内
    tags: [correctness, bounds]
    check:
      type: function
      name: check_value_range
    params:
      fields:
        - name: "count"
          min: 0
          max: 100
        - name: "ratio"
          min: 0.0
          max: 1.0
```

### 6.2 内联规则（用于简单校验）

```yaml
rules:
  - id: rule-004
    name: 模板引用存在性
    severity: error
    check:
      type: inline
      expression: |
        ir.nodes()
            .filter(|n| n.properties.contains_key("template"))
            .all(|n| templates.contains_key(n.properties["template"]))
```

### 6.3 规则分组

```yaml
groups:
  - name: correctness
    description: 正确性校验（必须修复）
    rules: [rule-001, rule-003]

  - name: style
    description: 代码风格校验
    rules: [rule-002]
```

---

## 七、CLI 设计

### 7.1 命令结构

```text
rulecheck — 通用规则校验工具

用法:
  rulecheck check [路径...]         校验文件或目录
  rulecheck init [--frontend <类型>]  初始化规则模板
  rulecheck explain <错误码>         解释错误码含义
  rulecheck list [--frontend <类型>]  列出可用规则
  rulecheck version                  显示版本信息

选项:
  --frontend, -f <类型>    指定前端类型（auto / 已注册的前端名称）
  --rules, -r <路径>       规则定义文件或目录
  --format, -o <格式>      输出格式（terminal / json / html / sarif）
  --fix                    尝试自动修复
  --verbose, -v            详细输出
  --config, -c <路径>      配置文件路径
```

### 7.2 使用示例

```bash
# 校验单个文件（自动检测前端类型）
rulecheck check ./example.config.json

# 校验整个目录
rulecheck check ./configs/

# 指定前端类型
rulecheck check ./docs/*.md --frontend markdown

# 输出 JSON 格式报告
rulecheck check ./configs --format json > report.json

# 输出 SARIF 格式（VS Code 集成）
rulecheck check ./configs --format sarif > results.sarif

# 自动修复
rulecheck check ./example.config.json --fix

# 初始化规则模板
rulecheck init --frontend my-format

# 解释错误码
rulecheck explain E001

# 列出所有可用规则
rulecheck list --frontend my-format
```

### 7.3 配置文件

```yaml
# .rulecheck.yaml
frontend: auto
rules:
  - ./rules/*.yaml
output:
  format: terminal
  color: true
fix: false
verbose: false
ignore:
  - "**/node_modules/**"
  - "**/dist/**"
  - "**/build/**"
```

---

## 八、项目结构

```
rulecheck/
├── Cargo.toml                          # workspace 根
├── .rulecheck.yaml                     # 默认配置文件
│
├── crates/
│   ├── rulecheck-core/                 # 框架核心
│   │   ├── Cargo.toml
│   │   └── src/
│   │       ├── lib.rs
│   │       ├── source.rs               # Source 定义
│   │       ├── ir.rs                   # IR 定义
│   │       ├── rule.rs                 # Rule trait
│   │       ├── diagnostic.rs           # Diagnostic / Report
│   │       ├── engine.rs               # RuleEngine
│   │       ├── context.rs              # RuleContext
│   │       ├── frontend.rs             # Frontend trait
│   │       └── error.rs                # 错误类型
│   │
│   ├── rulecheck-reporting/            # 报告输出
│   │   ├── Cargo.toml
│   │   └── src/
│   │       ├── lib.rs
│   │       ├── terminal.rs             # 终端彩色输出
│   │       ├── json.rs                 # JSON 格式
│   │       ├── html.rs                 # HTML 报告
│   │       └── sarif.rs                # SARIF 格式
│   │
│   ├── rulecheck-fe-json/              # JSON 前端（示例）
│   │   ├── Cargo.toml
│   │   └── src/
│   │       ├── lib.rs
│   │       ├── parser.rs               # JSON 解析
│   │       ├── ir_adapter.rs           # JSON → IR 转换
│   │       └── rules/                  # JSON 规则集
│   │           ├── mod.rs
│   │           ├── required_fields.rs
│   │           ├── naming_convention.rs
│   │           └── value_range.rs
│   │
│   ├── rulecheck-fe-yaml/              # YAML 前端（示例）
│   │   ├── Cargo.toml
│   │   └── src/
│   │       ├── lib.rs
│   │       ├── parser.rs
│   │       ├── ir_adapter.rs
│   │       └── rules/
│   │
│   └── rulecheck-cli/                  # 统一 CLI
│       ├── Cargo.toml
│       └── src/
│           ├── main.rs
│           ├── commands/
│           │   ├── check.rs            # check 子命令
│           │   ├── init.rs             # init 子命令
│           │   ├── explain.rs          # explain 子命令
│           │   └── list.rs             # list 子命令
│           └── config.rs               # 配置加载
│
├── rules/                              # 规则定义（YAML）
│   ├── correctness.yaml
│   ├── style.yaml
│   └── custom-rules.yaml
│
└── tests/                              # 测试
    ├── fixtures/
    │   ├── valid_example.json
    │   ├── invalid_example.json
    │   └── valid_example.yaml
    ├── integration_test.rs
    └── rule_engine_test.rs
```

---

## 九、实施路线

### Phase 0 — 基础设施（1-2 天）

**目标**：搭建 workspace，实现 Core Layer 核心抽象

- [ ] 创建 Cargo workspace
- [ ] 实现 `rulecheck-core` crate：
  - `Source` 枚举 + 文件加载
  - `IR` 枚举 + `IRNode` 通用树
  - `Rule` trait + `RuleEngine`
  - `Diagnostic` / `Report` / `Severity`
  - `Frontend` trait
- [ ] 实现 `rulecheck-reporting` crate：
  - `TerminalFormatter`（终端彩色输出）
  - `JsonFormatter`（JSON 格式）
- [ ] 实现 `rulecheck-cli` crate：
  - `check` 子命令基本框架
  - 配置文件加载

### Phase 1 — 首个前端实现（2-3 天）

**目标**：实现一个完整的前端，验证框架可用性

- [ ] 选择一个具体格式（如 JSON）实现 `rulecheck-fe-json`
- [ ] 实现 Parser + IR Adapter
- [ ] 实现 3-5 条核心规则
- [ ] 编写 YAML 规则定义
- [ ] 编写测试用例
- [ ] 集成到目标项目的构建管线

### Phase 2 — 框架完善（2-3 天）

- [ ] `--fix` 自动修复功能
- [ ] `SarifFormatter`（VS Code 集成）
- [ ] `HtmlFormatter`（HTML 报告）
- [ ] 规则分组 + 按组执行
- [ ] 性能优化（增量校验、并行校验）

### Phase 3 — 生态建设（按需）

- [ ] 更多前端实现（YAML、TOML、Markdown 等）
- [ ] VS Code 插件（LSP 协议）
- [ ] CI/CD 集成（GitHub Actions）
- [ ] 规则市场（社区共享规则）

---

## 十、设计决策记录

### ADR-001：为什么用 Rust？

| 因素 | Rust | TypeScript | Python |
|------|------|-----------|--------|
| 性能 | ⚡ 原生速度 | 需要 Node.js | 需要 Python 运行时 |
| 单文件分发 | ✅ 单个 exe | ❌ 需要 node_modules | ❌ 需要 venv |
| 构建集成 | ✅ 直接调用 exe | ❌ 需要运行时环境 | ❌ 需要运行时环境 |
| 并发校验 | ✅ 零成本抽象 | ❌ 单线程 | ❌ GIL |
| 类型安全 | ✅ 编译期保证 | ⚠️ 运行时 | ⚠️ 运行时 |

**结论**：Rust 编译为单个 `.exe`，零运行时依赖，最适合作为构建工具链的一环。

### ADR-002：为什么用 IR 统一中间表示？

直接对原始格式进行校验会导致：
- 每个前端需要独立的规则引擎
- 交叉引用校验（如 A 格式引用 B 格式的内容）无法实现
- 规则定义无法跨前端复用

统一 IR 层使得：
- 规则引擎只需理解 `IRNode` 树结构
- 交叉引用校验在 IR 层面统一处理
- 新增前端只需实现 `Frontend` trait + IR Adapter

### ADR-003：为什么规则用 YAML 定义？

- **可配置性**：用户可以在不修改 Rust 代码的情况下调整规则严重级别
- **可扩展性**：用户可以通过 YAML 定义自定义规则
- **可读性**：YAML 比 Rust 代码更容易被非 Rust 开发者理解
- **版本管理**：规则定义可以独立于代码版本管理

### ADR-004：为什么用 `IR::Custom` 支持自定义 AST？

某些前端（如编程语言解析器）已有成熟的 AST 定义，强制转换为 `IRNode` 树可能导致信息丢失或性能开销。`IR::Custom` 允许前端保留自己的 AST 类型，规则通过 `downcast_ref` 访问具体类型。

---

> **文档版本**：v0.1.0
> **最后更新**：2026-07-12
