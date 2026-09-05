# Markdown++ Rust 语法编译器设计文档

> **编译器前端设计规范** — 基于连续语义的富媒体实时渲染引擎的语法解析层规范

---

## 目录

1. [核心设计原则](#一核心设计原则)
2. [形式化语法定义（EBNF）](#二形式化语法定义ebnf)
3. [Rust 类型系统设计](#三rust-类型系统设计)
4. [解析管道架构](#四解析管道架构)
5. [错误处理策略](#五错误处理策略)
6. [扩展机制](#六扩展机制)

---

## 一、核心设计原则

### 1.1 设计哲学

Markdown++ 编译器遵循以下核心原则，确保语法解析的正确性、可扩展性和向后兼容性：

**向下兼容**：所有标准 Markdown 语法保持不变，现有 `.md` 文件直接可用
**渐进增强**：新语法是可选的，不影响纯文本可读性，一份 `.mdpp` 文件既是源码也是文档
**语义优先**：新增语法表达内容语义（何时播放、如何过渡、播放多久），而非视觉样式（字体、颜色、边距）
**统一描述**：同一份 Markdown 源码同时定义文档结构、幻灯片节奏和视频时间轴

### 1.2 语法扩展分类

Markdown++ 在标准 Markdown 基础上增加四类语法扩展，按解析优先级从高到低排列：

| 扩展类型 | 语法形式 | 解析阶段 | 语义含义 |
|----------|----------|----------|----------|
| Frontmatter | `--- ... ---` | 预处理阶段 | 文档元数据（标题、作者、主题） |
| 行为指令 | `@command args` | 块级解析 | 播放控制（暂停、跳转、循环） |
| 智能容器 | `::: ... :::` | 块级解析 | 布局定义（分栏、网格、堆叠） |
| 属性注入 | `{ #id .class key=value }` | 后处理阶段 | 元素属性（时长、动画、样式） |

### 1.3 解析顺序约束

为保证语法的明确性和可预测性，解析器必须遵循以下顺序约束：

1. **Frontmatter 优先**：文件头部的 YAML Frontmatter 必须首先解析，其配置影响后续所有解析行为
2. **指令优先于容器**：`@` 指令不能出现在 `:::` 容器内部，指令是全局行为控制
3. **容器嵌套有限制**：`:::` 容器可以嵌套，但嵌套深度不超过 3 层
4. **属性注入后处理**：`{ }` 属性块始终附着于前一个语法元素，不会独立存在

---

## 二、形式化语法定义（EBNF）

### 2.1 元语法约定

```ebnf
(* 字符类 *)
DIGIT       = "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" ;
LETTER      = "a" | "b" | ... | "z" | "A" | "B" | ... | "Z" ;
ALPHA_NUM   = LETTER | DIGIT | "_" ;
WHITESPACE  = " " | "\t" | "\r" ;
NEWLINE     = "\n" | "\r\n" ;

(* 基本规则 *)
IDENTIFIER  = LETTER, { ALPHA_NUM } ;
INTEGER     = { DIGIT } ;
FLOAT       = INTEGER, ".", INTEGER ;
```

### 2.2 Frontmatter 语法

```ebnf
document       = frontmatter?, markdown_content ;

frontmatter    = "---", NEWLINE, yaml_content, NEWLINE, "---", NEWLINE ;

yaml_content   = { yaml_line } ;
yaml_line      = (yaml_key, ":", WHITESPACE?, yaml_value) | yaml_comment | WHITESPACE* ;
yaml_key       = IDENTIFIER, { ALPHA_NUM | "-" } ;
yaml_value     = STRING | INTEGER | FLOAT | "true" | "false" | "null" ;
yaml_comment   = "#", { ANY_CHAR - NEWLINE } ;

STRING         = '"', { ANY_CHAR - '"' }, '"' | "'", { ANY_CHAR - "'" }, "'" ;
```

**Frontmatter 字段定义**：

| 字段名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `title` | string | `""` | 文档标题 |
| `author` | string | `""` | 作者信息 |
| `theme` | string | `"light"` | 主题名称 |
| `defaultDuration` | duration | `"4s"` | 默认停留时长 |
| `totalDuration` | duration | 自动计算 | 总播放时长 |

### 2.3 行为指令语法

```ebnf
directive          = DIRECTIVE_START, directive_command, directive_args?, NEWLINE ;

DIRECTIVE_START    = "@" ;

directive_command  = "pause" | "resume" | "goto" | "speed" | "loop" | "wait-click" | "stop" ;

directive_args     = WHITESPACE, directive_arg, { WHITESPACE, directive_arg } ;

directive_arg      = IDENTIFIER | STRING | speed_value ;

speed_value        = FLOAT, "x" | "normal" | "slow" | "fast" ;
```

**指令语义表**：

| 指令 | 参数 | 语义 |
|------|------|------|
| `@pause` | 无 | 暂停自动滚动，等待用户交互 |
| `@resume` | 无 | 恢复自动滚动 |
| `@goto` | `anchor_id` | 跳转到指定ID的锚点 |
| `@speed` | `speed_value` | 调整播放速度（0.5x, 1x, 1.5x, 2x） |
| `@loop` | 无 | 开启循环播放 |
| `@wait-click` | 无 | 等待用户点击后继续 |
| `@stop` | 无 | 停止播放并重置到起始位置 |

### 2.4 智能容器语法

```ebnf
container              = CONTAINER_START, container_attributes?, NEWLINE, 
                         container_content, CONTAINER_END, NEWLINE ;

CONTAINER_START        = ":::" ;
CONTAINER_END          = ":::" ;

container_attributes   = WHITESPACE, (container_class | container_attr_block) ;

container_class        = ".", IDENTIFIER, { ".", IDENTIFIER } ;

container_attr_block   = "{", container_attr_list?, "}" ;

container_attr_list    = container_attr, { ",", WHITESPACE?, container_attr } ;

container_attr         = attr_key, ("=" | ":"), attr_value ;

attr_key               = IDENTIFIER ;
attr_value             = STRING | duration_value | transition_value | layout_value | BOOLEAN ;

duration_value         = (FLOAT | INTEGER), "s" ;
transition_value       = "fade" | "slide" | "zoom" | "none" ;
layout_value           = "split" | "grid" | "stack" | "center" ;
BOOLEAN                = "true" | "false" ;

container_content      = { block_content } ;
block_content          = paragraph | heading | list | code_block | image | container | NEWLINE ;
```

**容器属性语义**：

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `duration` | duration | 继承父级 | 容器整体停留时间 |
| `transition` | transition | `"fade"` | 进入/退出动画效果 |
| `layout` | layout | `"stack"` | 布局模式 |
| `align` | string | `"left"` | 水平对齐方式 |

### 2.5 属性注入语法

```ebnf
attribute_block         = "{", attribute_list?, "}" ;

attribute_list          = attribute, { WHITESPACE, attribute } ;

attribute               = id_attribute | class_attribute | key_value_attribute ;

id_attribute            = "#", IDENTIFIER ;

class_attribute         = ".", IDENTIFIER ;

key_value_attribute     = attr_key, "=", attr_value ;
```

**属性注入适用位置**：

| 语法元素 | 属性附着方式 | 示例 |
|----------|-------------|------|
| 标题 | 标题文本后 | `# 标题 { #intro duration=5s }` |
| 图片 | 图片语法后 | `![alt](src) { animation=fade-in }` |
| 代码块 | 围栏后 | ```chart { type="bar" }``` |
| 容器 | 起始标记后 | `::: .split-view { transition=slide }` |

### 2.6 富媒体引用语法

```ebnf
media_reference      = "!", "[", alt_text, "]", "(", media_source, ")", attribute_block? ;

alt_text             = { ANY_CHAR - "]" } ;

media_source         = URL | FILE_PATH ;

URL                  = "http://", { ANY_CHAR - ")" } | "https://", { ANY_CHAR - ")" } ;
FILE_PATH            = { ANY_CHAR - ")" - " " } ;
```

**媒体类型自动检测**：

| 文件扩展名 | 媒体类型 | 文档模式 | 幻灯片模式 | 视频模式 |
|------------|----------|----------|------------|----------|
| `.png`, `.jpg`, `.svg` | 图片 | 内联显示 | 全屏显示 | 内联显示 |
| `.mp4`, `.webm` | 视频 | 静态预览 | 全屏播放 | 自动播放 |
| `.mp3`, `.wav`, `.ogg` | 音频 | 隐藏 | 隐藏 | 背景播放 |
| `.pdf` | PDF | 内联预览 | 指定页码 | 自动翻页 |

### 2.7 数据驱动图表语法

```ebnf
chart_block          = FENCED_CODE_START, "chart", chart_attributes?, NEWLINE, 
                       chart_data, NEWLINE, FENCED_CODE_END ;

FENCED_CODE_START    = "```" ;
FENCED_CODE_END      = "```" ;

chart_attributes     = WHITESPACE, "{", chart_attr_list?, "}" ;

chart_attr_list      = chart_attr, { ",", WHITESPACE?, chart_attr } ;

chart_attr           = attr_key, "=", attr_value ;

chart_data           = JSON_OBJECT ;
```

**图表属性语义**：

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `type` | string | `"line"` | 图表类型（line/bar/pie/scatter） |
| `animation` | string | `"none"` | 动画效果（draw/fade/grow） |
| `duration` | duration | `"4s"` | 动画持续时间 |

### 2.8 完整语法层次结构

```text
Document
├── Frontmatter (optional)
│   └── Metadata fields
└── Content Blocks
    ├── Heading (with attribute injection)
    ├── Paragraph
    ├── List (Ordered/Unordered)
    ├── Code Block (standard + chart extension)
    ├── Media Reference (image/video/audio/PDF)
    ├── Container (with nested blocks)
    └── Directive (@pause, @goto, etc.)
```

---

## 三、Rust 类型系统设计

### 3.1 位置跟踪类型

```rust
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Span {
    pub start_line: usize,
    pub start_col: usize,
    pub end_line: usize,
    pub end_col: usize,
}

impl Span {
    pub fn new(start_line: usize, start_col: usize, end_line: usize, end_col: usize) -> Self {
        Span {
            start_line,
            start_col,
            end_line,
            end_col,
        }
    }
    
    pub fn merge(&self, other: &Span) -> Span {
        Span {
            start_line: self.start_line.min(other.start_line),
            start_col: if self.start_line == other.start_line {
                self.start_col.min(other.start_col)
            } else {
                self.start_col
            },
            end_line: self.end_line.max(other.end_line),
            end_col: if self.end_line == other.end_line {
                self.end_col.max(other.end_col)
            } else {
                other.end_col
            },
        }
    }
}
```

### 3.2 核心类型层次

```rust
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Document {
    pub frontmatter: Option<Frontmatter>,
    pub blocks: Vec<Block>,
    pub ir: IntermediateRepresentation,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Frontmatter {
    #[serde(rename = "title", default)]
    pub title: String,
    #[serde(rename = "author", default)]
    pub author: String,
    #[serde(rename = "theme", default = "default_theme")]
    pub theme: Theme,
    #[serde(rename = "defaultDuration", default = "default_duration")]
    pub default_duration: Duration,
    #[serde(flatten)]
    pub custom_fields: HashMap<String, YamlValue>,
}

fn default_theme() -> Theme {
    Theme::Light
}

fn default_duration() -> Duration {
    Duration::from_seconds(4.0)
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Theme {
    Light,
    Dark,
    #[serde(untagged)]
    Custom(String),
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(untagged)]
pub enum YamlValue {
    String(String),
    Integer(i64),
    Float(f64),
    Boolean(bool),
    Null,
}
```

### 3.3 Block 枚举定义

```rust
#[derive(Debug, Clone, PartialEq)]
pub enum Block {
    Heading(Heading),
    Paragraph(Paragraph),
    List(List),
    CodeBlock(CodeBlock),
    Media(Media),
    Container(Container),
    Directive(Directive),
    HorizontalRule(Span),
    Blockquote(Blockquote),
    Table(Table),
    AttributeBlock(AttributeBlock),
}

#[derive(Debug, Clone, PartialEq)]
pub struct Heading {
    pub span: Span,
    pub level: u32,
    pub text: String,
    pub attributes: Attributes,
    pub anchor: Option<String>,
    pub duration: Option<Duration>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Paragraph {
    pub span: Span,
    pub content: String,
    pub attributes: Attributes,
    pub inlines: Vec<Inline>,
}

#[derive(Debug, Clone, PartialEq)]
pub enum Inline {
    Text { span: Span, content: String },
    Strong { span: Span, children: Vec<Inline> },
    Emphasis { span: Span, children: Vec<Inline> },
    Link { span: Span, href: String, text: String },
    Image { span: Span, src: String, alt: String, attributes: Attributes },
    Code { span: Span, content: String },
    AttributeBlock { span: Span, attributes: Attributes },
}

#[derive(Debug, Clone, PartialEq)]
pub struct List {
    pub span: Span,
    pub items: Vec<ListItem>,
    pub ordered: bool,
    pub attributes: Attributes,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ListItem {
    pub span: Span,
    pub content: String,
    pub checked: Option<bool>,
    pub inlines: Vec<Inline>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct CodeBlock {
    pub span: Span,
    pub language: Option<String>,
    pub code: String,
    pub attributes: Attributes,
    pub chart_data: Option<ChartData>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Media {
    pub span: Span,
    pub source: String,
    pub alt_text: String,
    pub media_type: MediaType,
    pub attributes: Attributes,
    pub autoplay: bool,
    pub loop: bool,
    pub volume: f64,
    pub pages: Option<PageRange>,
}

#[derive(Debug, Clone, PartialEq)]
pub enum MediaType {
    Image,
    Video,
    Audio,
    Pdf,
    Unknown,
}

#[derive(Debug, Clone, PartialEq)]
pub struct PageRange {
    pub start: usize,
    pub end: Option<usize>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Container {
    pub span: Span,
    pub classes: Vec<String>,
    pub attributes: Attributes,
    pub children: Vec<Block>,
    pub transition: Transition,
    pub layout: Layout,
    pub duration: Option<Duration>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Directive {
    pub span: Span,
    pub command: DirectiveCommand,
    pub args: Vec<String>,
}

#[derive(Debug, Clone, PartialEq)]
pub enum DirectiveCommand {
    Pause,
    Resume,
    Goto(String),
    Speed(f64),
    Loop,
    WaitClick,
    Stop,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Blockquote {
    pub span: Span,
    pub content: Vec<Block>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Table {
    pub span: Span,
    pub headers: Vec<String>,
    pub rows: Vec<Vec<String>>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct AttributeBlock {
    pub span: Span,
    pub attributes: Attributes,
}
```

### 3.4 属性系统类型

```rust
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct Attributes {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub id: Option<String>,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub classes: Vec<String>,
    #[serde(flatten)]
    pub properties: HashMap<String, AttributeValue>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(untagged)]
pub enum AttributeValue {
    String(String),
    Integer(i64),
    Float(f64),
    Boolean(bool),
    Duration(Duration),
    Transition(Transition),
    Layout(Layout),
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Duration {
    pub value: f64,
    #[serde(rename = "unit", default = "default_time_unit")]
    pub unit: TimeUnit,
}

impl Duration {
    pub fn from_seconds(seconds: f64) -> Self {
        Duration {
            value: seconds,
            unit: TimeUnit::Seconds,
        }
    }
    
    pub fn as_seconds(&self) -> f64 {
        match self.unit {
            TimeUnit::Seconds => self.value,
            TimeUnit::Milliseconds => self.value / 1000.0,
            TimeUnit::Minutes => self.value * 60.0,
        }
    }
}

fn default_time_unit() -> TimeUnit {
    TimeUnit::Seconds
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum TimeUnit {
    Seconds,
    Milliseconds,
    Minutes,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Transition {
    Fade,
    Slide,
    Zoom,
    None,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Layout {
    Split,
    Grid,
    Stack,
    Center,
}
```

### 3.5 图表数据类型

```rust
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ChartData {
    #[serde(rename = "type", default = "default_chart_type")]
    pub chart_type: ChartType,
    #[serde(rename = "animation", default = "default_animation")]
    pub animation: AnimationType,
    #[serde(rename = "duration", default = "default_duration")]
    pub duration: Duration,
    pub title: String,
    pub labels: Vec<String>,
    pub datasets: Vec<Dataset>,
}

fn default_chart_type() -> ChartType {
    ChartType::Line
}

fn default_animation() -> AnimationType {
    AnimationType::None
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ChartType {
    Line,
    Bar,
    Pie,
    Scatter,
    Area,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum AnimationType {
    None,
    Draw,
    Fade,
    Grow,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Dataset {
    pub label: String,
    pub values: Vec<f64>,
    pub colors: Vec<String>,
}
```

### 3.6 中间表示（IR）类型

**设计原则**：IR 层只携带逻辑顺序和时间语义，不包含像素级的空间布局信息（如 `bounds`、`y`、`height`）。精确的空间计算由渲染引擎在运行时根据字体度量和视口宽度完成。

```rust
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct IntermediateRepresentation {
    #[serde(rename = "$schema")]
    pub schema: String,
    pub version: String,
    pub metadata: Metadata,
    pub nodes: Vec<IRNode>,
    pub timeline: Timeline,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Metadata {
    pub title: String,
    pub author: String,
    pub theme: String,
    #[serde(rename = "defaultDuration")]
    pub default_duration: f64,
    #[serde(rename = "totalDuration")]
    pub total_duration: f64,
    #[serde(rename = "totalHeight", skip_serializing)]
    pub total_height: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "type")]
pub enum IRNode {
    #[serde(rename = "slide")]
    Slide(SlideNode),
    #[serde(rename = "section")]
    Section(SectionNode),
    #[serde(rename = "text")]
    Text(TextNode),
    #[serde(rename = "list")]
    List(ListNode),
    #[serde(rename = "image")]
    Image(ImageNode),
    #[serde(rename = "video")]
    Video(VideoNode),
    #[serde(rename = "audio")]
    Audio(AudioNode),
    #[serde(rename = "chart")]
    Chart(ChartNode),
    #[serde(rename = "container")]
    Container(ContainerNode),
    #[serde(rename = "command")]
    Command(CommandNode),
    #[serde(rename = "table")]
    Table(TableNode),
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SlideNode {
    pub id: String,
    pub tag: String,
    pub content: String,
    pub duration: f64,
    pub style: Style,
    pub animation: Animation,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SectionNode {
    pub id: String,
    pub tag: String,
    pub content: String,
    pub duration: f64,
    pub children: Vec<IRNode>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TextNode {
    pub id: String,
    pub content: String,
    pub style: Style,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ListNode {
    pub id: String,
    pub items: Vec<String>,
    pub ordered: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ImageNode {
    pub id: String,
    pub src: String,
    pub animation: Animation,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct VideoNode {
    pub id: String,
    pub src: String,
    pub autoplay: bool,
    pub loop: bool,
    pub duration: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AudioNode {
    pub id: String,
    pub src: String,
    pub background: bool,
    pub volume: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ChartNode {
    pub id: String,
    #[serde(rename = "chartType")]
    pub chart_type: String,
    pub data: ChartData,
    pub duration: f64,
    pub animation: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ContainerNode {
    pub id: String,
    pub layout: String,
    pub children: Vec<IRNode>,
    pub transition: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CommandNode {
    pub id: String,
    pub command: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TableNode {
    pub id: String,
    pub headers: Vec<String>,
    pub rows: Vec<Vec<String>>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Style {
    #[serde(rename = "fontSize", skip_serializing_if = "Option::is_none")]
    pub font_size: Option<String>,
    #[serde(rename = "textAlign", skip_serializing_if = "Option::is_none")]
    pub text_align: Option<String>,
    #[serde(rename = "verticalAlign", skip_serializing_if = "Option::is_none")]
    pub vertical_align: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub color: Option<String>,
    #[serde(rename = "backgroundColor", skip_serializing_if = "Option::is_none")]
    pub background_color: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Animation {
    pub enter: String,
    pub exit: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Timeline {
    pub mapping: Vec<TimeMapping>,
    #[serde(rename = "totalDuration")]
    pub total_duration: f64,
    #[serde(rename = "totalHeight", skip_serializing)]
    pub total_height: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TimeMapping {
    pub y: f64,
    pub t: f64,
}
```

**布局估计接口**：编译器提供可选的布局估计能力，供渲染引擎调用以获取初步布局信息：

```rust
pub trait LayoutEstimator {
    fn estimate_height(&self, node: &IRNode) -> f64;
    fn estimate_width(&self, node: &IRNode) -> f64;
    fn set_font_metrics(&mut self, metrics: FontMetrics);
}

pub struct FontMetrics {
    pub font_family: String,
    pub font_size: f64,
    pub line_height: f64,
    pub viewport_width: f64,
}

pub struct DefaultLayoutEstimator {
    metrics: FontMetrics,
}

impl DefaultLayoutEstimator {
    pub fn new(metrics: FontMetrics) -> Self {
        DefaultLayoutEstimator { metrics }
    }
}

impl LayoutEstimator for DefaultLayoutEstimator {
    fn estimate_height(&self, node: &IRNode) -> f64 {
        match node {
            IRNode::Slide(_) => 1080.0,
            IRNode::Section(_) => 1080.0,
            IRNode::Text(n) => {
                let chars = n.content.len() as f64;
                let chars_per_line = self.metrics.viewport_width / (self.metrics.font_size * 0.6);
                let lines = (chars / chars_per_line).ceil();
                lines * self.metrics.line_height
            }
            IRNode::Image(_) => 600.0,
            IRNode::Video(_) => 600.0,
            IRNode::Chart(_) => 800.0,
            _ => 200.0,
        }
    }
    
    fn estimate_width(&self, _node: &IRNode) -> f64 {
        self.metrics.viewport_width
    }
    
    fn set_font_metrics(&mut self, metrics: FontMetrics) {
        self.metrics = metrics;
    }
}
```

---

## 四、解析管道架构

### 4.1 整体架构

采用**策略A**：预扫描源文件识别扩展区域，mask 后委托 pulldown-cmark 解析标准 Markdown，最后合并扩展解析结果。

```text
┌─────────────────────────────────────────────────────────────────────────┐
│                         Markdown++ 解析管道                              │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌──────────────┐                                                       │
│  │  输入: .mdpp │                                                       │
│  │   源文件     │                                                       │
│  └──────┬───────┘                                                       │
│         │                                                               │
│         v                                                               │
│  ┌──────────────────────┐    ┌─────────────────────────────────────┐    │
│  │  阶段1: 扩展区域扫描  │ -> │  识别并记录所有扩展语法的位置范围      │    │
│  │  (ExtensionScanner)  │    │  - @指令行范围                       │    │
│  └──────────┬───────────┘    │  - :::容器范围                       │    │
│             │                │  - 属性块{ }范围                     │    │
│             v                │  - ```chart围栏范围                  │    │
│  ┌──────────────────────┐    └─────────────────────────────────────┘    │
│  │  阶段2: 扩展区域Mask  │                                               │
│  │  (ExtensionMasker)   │ -> 将扩展区域替换为占位符文本                  │
│  └──────────┬───────────┘    示例: "@pause" -> "__MDPP_DIRECTIVE_1__"    │
│             │                                                           │
│             v                                                           │
│  ┌──────────────────────┐                                               │
│  │  阶段3: 标准解析     │ -> pulldown-cmark 解析 mask 后的文本           │
│  │  (pulldown-cmark)    │    生成标准 Markdown Event 流                   │
│  └──────────┬───────────┘                                               │
│             │                                                           │
│             v                                                           │
│  ┌──────────────────────┐    ┌─────────────────────────────────────┐    │
│  │  阶段4: 事件流转换    │ -> │  将 Event 流转换为 Block 枚举列表     │    │
│  │  (EventToBlock)      │    │  保留位置信息和内联元素结构             │    │
│  └──────────┬───────────┘    └─────────────────────────────────────┘    │
│             │                                                           │
│             v                                                           │
│  ┌──────────────────────┐                                               │
│  │  阶段5: 扩展内容恢复  │ -> 用原始扩展区域内容替换占位符                │
│  │  (ExtensionRestore)  │    解析扩展语法生成对应的 Block                 │
│  └──────────┬───────────┘                                               │
│             │                                                           │
│             v                                                           │
│  ┌──────────────────────┐                                               │
│  │  阶段6: 属性注入     │ -> 将 { } 属性块附着到前一个语法元素            │
│  │  (AttributeInjector) │    支持块级和内联级属性注入                     │
│  └──────────┬───────────┘                                               │
│             │                                                           │
│             v                                                           │
│  ┌──────────────────────┐                                               │
│  │  阶段7: IR 生成器    │ -> 将 AST Block 列表转换为标准化 IR            │
│  │  (IRGenerator)       │    计算时间轴映射，生成 Metadata 和 Timeline    │
│  └──────────┬───────────┘                                               │
│             │                                                           │
│             v                                                           │
│  ┌──────────────┐                                                       │
│  │  输出: IR    │                                                       │
│  │  (JSON/结构) │                                                       │
│  └──────────────┘                                                       │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 4.2 阶段一：扩展区域扫描（ExtensionScanner）

**职责**：预扫描源文件，识别所有 Markdown++ 扩展语法的位置范围

**扫描算法**：

```rust
struct ExtensionScanner {
    source: String,
    extensions: Vec<ExtensionRegion>,
    line_number: usize,
    column: usize,
}

struct ExtensionRegion {
    region_type: ExtensionType,
    start: Span,
    end: Span,
    content: String,
}

enum ExtensionType {
    Directive,
    ContainerStart,
    ContainerEnd,
    AttributeBlock,
    ChartBlock,
    Frontmatter,
}

impl ExtensionScanner {
    fn scan(source: &str) -> Vec<ExtensionRegion> {
        let mut scanner = ExtensionScanner {
            source: source.to_string(),
            extensions: Vec::new(),
            line_number: 1,
            column: 1,
        };
        
        let chars: Vec<char> = source.chars().collect();
        let mut i = 0;
        
        while i < chars.len() {
            match chars[i] {
                '@' if i == 0 || chars[i-1] == '\n' => {
                    scanner.scan_directive(&chars, &mut i);
                }
                ':' if i + 2 < chars.len() && chars[i+1] == ':' && chars[i+2] == ':' => {
                    scanner.scan_container(&chars, &mut i);
                }
                '{' => {
                    scanner.scan_attribute_block(&chars, &mut i);
                }
                '`' if i + 2 < chars.len() && chars[i+1] == '`' && chars[i+2] == '`' => {
                    scanner.scan_fenced_code(&chars, &mut i);
                }
                '\n' => {
                    scanner.line_number += 1;
                    scanner.column = 1;
                    i += 1;
                }
                _ => {
                    scanner.column += 1;
                    i += 1;
                }
            }
        }
        
        scanner.extensions
    }
}
```

### 4.3 阶段二：扩展区域 Mask（ExtensionMasker）

**职责**：将扩展区域替换为占位符文本，使 pulldown-cmark 能够正确解析剩余内容

**Mask 策略**：

| 扩展类型 | Mask 格式 | 示例 |
|----------|-----------|------|
| 指令 | `__MDPP_DIRECTIVE_N__` | `@pause` → `__MDPP_DIRECTIVE_1__` |
| 容器开始 | `__MDPP_CONTAINER_START_N__` | `::: .split` → `__MDPP_CONTAINER_START_1__` |
| 容器结束 | `__MDPP_CONTAINER_END_N__` | `:::` → `__MDPP_CONTAINER_END_1__` |
| 属性块 | `__MDPP_ATTR_N__` | `{ duration=5s }` → `__MDPP_ATTR_1__` |
| 图表块 | `__MDPP_CHART_N__` | ```chart {...}``` → `__MDPP_CHART_1__` |

```rust
struct ExtensionMasker {
    regions: Vec<ExtensionRegion>,
    mask_counter: usize,
}

impl ExtensionMasker {
    fn mask(source: &str, regions: &[ExtensionRegion]) -> (String, Vec<MaskMapping>) {
        let mut masked = String::new();
        let mut mappings = Vec::new();
        let mut last_end = 0;
        
        for region in regions {
            masked.push_str(&source[last_end..region.start.start_col]);
            
            let mask = format!("__MDPP_{}_{}__", 
                region.region_type, 
                Self::mask_counter
            );
            masked.push_str(&mask);
            
            mappings.push(MaskMapping {
                mask,
                region: region.clone(),
            });
            
            last_end = region.end.end_col;
            Self::mask_counter += 1;
        }
        
        masked.push_str(&source[last_end..]);
        
        (masked, mappings)
    }
}

struct MaskMapping {
    mask: String,
    region: ExtensionRegion,
}
```

### 4.4 阶段三：标准 Markdown 解析（pulldown-cmark）

**技术选型**：基于 `pulldown-cmark` crate 进行标准 Markdown 解析

**处理流程**：

1. 将 mask 后的文本传递给 pulldown-cmark 解析器
2. 获取 `Event` 流（Start/End/Text/Code/Image 等）
3. 使用 `OffsetIter` 保留位置信息（字节偏移 → 行号/列号）

```rust
use pulldown_cmark::{Parser, Event, Options};
use pulldown_cmark::offset::OffsetIter;

fn parse_standard_markdown(masked_source: &str) -> Vec<(Event, Span)> {
    let mut options = Options::empty();
    options.insert(Options::ENABLE_STRIKETHROUGH);
    options.insert(Options::ENABLE_TABLES);
    options.insert(Options::ENABLE_FOOTNOTES);
    options.insert(Options::ENABLE_TASKLISTS);
    
    let parser = Parser::new_ext(masked_source, options);
    let offset_iter = OffsetIter::new(masked_source, parser);
    
    let mut events = Vec::new();
    
    for (event, range) in offset_iter {
        let span = Span::new(
            get_line_number(masked_source, range.start),
            get_column_number(masked_source, range.start),
            get_line_number(masked_source, range.end),
            get_column_number(masked_source, range.end),
        );
        events.push((event, span));
    }
    
    events
}
```

### 4.5 阶段四：事件流转换（EventToBlock）

**职责**：将 pulldown-cmark 的 Event 流转换为 Block 枚举列表，保留位置信息和内联元素结构

**转换规则**：

| Event 类型 | Block 类型 | 转换逻辑 |
|------------|-----------|----------|
| Start(Heading(n)) | Heading | 收集后续 Text 内容 |
| Start(Paragraph) | Paragraph | 收集内联元素 |
| Start(List(ordered)) | List | 收集 ListItem |
| Start(CodeBlock(lang)) | CodeBlock | 收集后续 Code 内容 |
| Start(Image) | Media | 提取 src 和 alt |
| Start(BlockQuote) | Blockquote | 递归处理子元素 |
| Start(Table) | Table | 收集 headers 和 rows |
| Text | - | 作为内联元素 |
| Code | - | 作为内联元素 |
| SoftBreak/HardBreak | - | 换行处理 |

### 4.6 阶段五：扩展内容恢复（ExtensionRestore）

**职责**：用原始扩展区域内容替换占位符，解析扩展语法生成对应的 Block

**恢复流程**：

```rust
fn restore_extensions(blocks: &mut Vec<Block>, mappings: &[MaskMapping]) {
    for mapping in mappings {
        match mapping.region.region_type {
            ExtensionType::Directive => {
                let directive = parse_directive(&mapping.region.content);
                insert_block_at_span(blocks, Block::Directive(directive));
            }
            ExtensionType::ContainerStart => {
                let container = parse_container_start(&mapping.region.content);
                insert_block_at_span(blocks, Block::Container(container));
            }
            ExtensionType::ContainerEnd => {
                close_container(blocks);
            }
            ExtensionType::AttributeBlock => {
                let attr_block = parse_attribute_block(&mapping.region.content);
                insert_block_at_span(blocks, Block::AttributeBlock(attr_block));
            }
            ExtensionType::ChartBlock => {
                let code_block = parse_chart_block(&mapping.region.content);
                replace_block_at_span(blocks, Block::CodeBlock(code_block));
            }
            _ => {}
        }
    }
}
```

### 4.7 阶段六：属性注入器（AttributeInjector）

**职责**：将 `{ }` 属性块附着到对应的语法元素，支持块级和内联级

**块级属性注入**：

```text
输入: [Heading, AttributeBlock, Paragraph, AttributeBlock]

处理步骤:
1. 遍历 Block 列表
2. 遇到 AttributeBlock 时，查找前一个非空 Block
3. 将属性注入到该 Block 的 attributes 字段
4. 从列表中移除 AttributeBlock
5. 返回处理后的 Block 列表
```

**内联级属性注入**：

```text
输入段落内联: [Text, Image, AttributeBlock, Text]

处理步骤:
1. 遍历段落的 inlines 列表
2. 遇到 AttributeBlock 时，查找前一个 Inline 元素
3. 如果是 Image，将属性注入到 Image 的 attributes 字段
4. 从 inlines 列表中移除 AttributeBlock
5. 返回处理后的 inlines 列表
```

**注入算法**：

```rust
struct AttributeInjector;

impl AttributeInjector {
    fn inject(blocks: Vec<Block>) -> Vec<Block> {
        let mut result = Vec::new();
        let mut pending_attr: Option<Attributes> = None;
        
        for mut block in blocks {
            match &mut block {
                Block::AttributeBlock(attr_block) => {
                    pending_attr = Some(attr_block.attributes.clone());
                    continue;
                }
                _ => {
                    if let Some(attrs) = pending_attr.take() {
                        Self::merge_attributes(&mut block, attrs);
                    }
                }
            }
            
            Self::inject_inline_attributes(&mut block);
            result.push(block);
        }
        
        result
    }
    
    fn inject_inline_attributes(block: &mut Block) {
        match block {
            Block::Paragraph(p) => {
                let mut inlines = Vec::new();
                let mut pending_attr: Option<Attributes> = None;
                
                for mut inline in p.inlines.drain(..) {
                    match &mut inline {
                        Inline::AttributeBlock(ab) => {
                            pending_attr = Some(ab.attributes.clone());
                            continue;
                        }
                        Inline::Image(img) => {
                            if let Some(attrs) = pending_attr.take() {
                                img.attributes.merge(attrs);
                            }
                        }
                        _ => {
                            pending_attr = None;
                        }
                    }
                    inlines.push(inline);
                }
                
                p.inlines = inlines;
            }
            _ => {}
        }
    }
    
    fn merge_attributes(block: &mut Block, attrs: Attributes) {
        match block {
            Block::Heading(h) => h.attributes.merge(attrs),
            Block::Paragraph(p) => p.attributes.merge(attrs),
            Block::List(l) => l.attributes.merge(attrs),
            Block::CodeBlock(c) => c.attributes.merge(attrs),
            Block::Media(m) => m.attributes.merge(attrs),
            Block::Container(c) => c.attributes.merge(attrs),
            _ => {}
        }
    }
}
```

### 4.8 阶段七：IR 生成器（IRGenerator）

**职责**：将 AST（Block 列表）转换为标准化的中间表示（IR）

**设计原则**：IR 层只携带逻辑顺序和时间语义，不包含像素级的空间布局信息。精确的空间计算由渲染引擎在运行时完成。

**生成流程**：

```text
IR 生成流程
├── 1. 遍历 AST Block 列表
├── 2. 提取时间语义 (duration)
│   ├── 从属性中提取 duration
│   ├── 继承父级 duration
│   ├── 使用 Frontmatter 默认值
├── 3. 计算时间轴映射 (Y → T)
│   ├── 初始化 current_time = 0
│   ├── 按逻辑顺序遍历节点
│   ├── 累加 duration 计算时间点
│   └── 生成 mapping 表 (逻辑位置 → 时间)
├── 4. 生成 IRNode 列表
│   ├── Block → IRNode 类型映射
│   ├── 填充 duration、animation
│   └── 处理嵌套结构
├── 5. 生成 Metadata
│   ├── 提取 Frontmatter 字段
│   ├── 计算 total_duration
│   └── 设置默认值
└── 6. 生成 Timeline
    ├── 汇总 mapping 表
    └── 记录播放速度 (逻辑高度 / 总时长)
```

**时间轴映射算法**：

```rust
fn generate_timeline(nodes: &Vec<IRNode>, default_duration: f64) -> Timeline {
    let mut mapping = Vec::new();
    let mut current_time = 0.0;
    let mut logical_position = 0;
    
    for node in nodes {
        mapping.push(TimeMapping {
            y: logical_position as f64,
            t: current_time,
        });
        
        let duration = get_node_duration(node, default_duration);
        current_time += duration;
        logical_position += 1;
    }
    
    Timeline {
        mapping,
        total_duration: current_time,
        total_height: logical_position as f64,
    }
}

fn get_node_duration(node: &IRNode, default_duration: f64) -> f64 {
    match node {
        IRNode::Slide(n) => n.duration,
        IRNode::Section(n) => n.duration,
        IRNode::Video(n) => n.duration,
        IRNode::Chart(n) => n.duration,
        _ => default_duration,
    }
}
```

---

## 五、错误处理策略

### 5.1 错误类型定义

```rust
#[derive(Debug, Clone, PartialEq)]
pub enum ParseError {
    // Frontmatter 错误
    FrontmatterNotClosed { line: usize },
    FrontmatterMalformed { line: usize, message: String },
    
    // 指令错误
    UnknownDirective { line: usize, command: String },
    DirectiveMissingArgs { line: usize, command: String },
    DirectiveInvalidArgs { line: usize, command: String, args: Vec<String> },
    
    // 容器错误
    ContainerNotClosed { line: usize },
    ContainerNestedTooDeep { line: usize, max_depth: usize },
    ContainerInvalidAttribute { line: usize, attr: String },
    
    // 属性错误
    AttributeMalformed { line: usize, block: String },
    AttributeUnknownKey { line: usize, key: String },
    AttributeInvalidValue { line: usize, key: String, value: String },
    AttributeMissingValue { line: usize, key: String },
    
    // 图表错误
    ChartDataInvalid { line: usize, message: String },
    ChartUnknownType { line: usize, chart_type: String },
    
    // 媒体错误
    MediaSourceInvalid { line: usize, source: String },
    MediaTypeUnknown { line: usize, source: String },
    
    // 通用错误
    UnexpectedEndOfFile { line: usize },
    InternalError { message: String },
}

impl std::error::Error for ParseError {}

impl std::fmt::Display for ParseError {
    fn fmt(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        match self {
            ParseError::FrontmatterNotClosed { line } => {
                write!(f, "Line {}: Frontmatter not closed", line)
            }
            ParseError::UnknownDirective { line, command } => {
                write!(f, "Line {}: Unknown directive '@{}'", line, command)
            }
            ParseError::ContainerNotClosed { line } => {
                write!(f, "Line {}: Container not closed", line)
            }
            _ => write!(f, "{:?}", self),
        }
    }
}
```

### 5.2 错误处理策略

**错误恢复机制**：

| 错误类型 | 恢复策略 | 示例 |
|----------|----------|------|
| 未闭合容器 | 在下一个容器开始或文档结束时强制闭合 | `::: ... (EOF)` → 自动添加 `:::` |
| 未知指令 | 忽略该指令，继续解析 | `@unknown` → 跳过 |
| 无效属性 | 忽略无效属性，保留有效属性 | `{ invalid=value }` → 空 Attributes |
| 图表数据错误 | 降级为普通代码块 | JSON 解析失败 → 显示代码 |
| 嵌套过深 | 拒绝解析，返回错误 | 第4层嵌套 → 报错终止 |

**错误报告格式**：

```text
ERROR [Line 42, Column 15]: Unknown directive '@invalid'
  Suggestion: Did you mean '@pause' or '@resume'?
  Context:    @invalid  <!-- 这里出错 -->
  
WARNING [Line 78]: Container nesting depth (4) exceeds maximum (3)
  Context:    ::: .inner { }
                ::: .deep { }
                  ::: .too-deep { }  <!-- 超出限制 -->
```

### 5.3 诊断信息结构

```rust
#[derive(Debug, Clone, PartialEq)]
pub struct Diagnostic {
    pub severity: Severity,
    pub line: usize,
    pub column: usize,
    pub message: String,
    pub suggestion: Option<String>,
    pub context: String,
}

#[derive(Debug, Clone, PartialEq)]
pub enum Severity {
    Error,
    Warning,
    Info,
}
```

---

## 六、扩展机制

### 6.1 插件架构设计

```rust
pub trait ParserExtension: Send + Sync {
    fn name(&self) -> &str;
    fn priority(&self) -> u32;
    fn can_parse(&self, token: &Token) -> bool;
    fn parse(&self, tokens: &mut TokenStream) -> Result<Block, ParseError>;
    fn inject_attributes(&self, block: &mut Block) -> Result<(), ParseError>;
}

pub struct Parser {
    extensions: Vec<Box<dyn ParserExtension>>,
    default_duration: Duration,
    container_max_depth: usize,
}

impl Parser {
    fn register_extension(&mut self, extension: Box<dyn ParserExtension>) {
        self.extensions.push(extension);
        self.extensions.sort_by(|a, b| a.priority().cmp(&b.priority()));
    }
    
    fn parse_extension(&mut self, tokens: &mut TokenStream) -> Option<Result<Block, ParseError>> {
        for ext in &self.extensions {
            if ext.can_parse(tokens.peek()) {
                return Some(ext.parse(tokens));
            }
        }
        None
    }
}
```

### 6.2 内置扩展

| 扩展名称 | 优先级 | 功能 |
|----------|--------|------|
| `FrontmatterExtension` | 100 | 解析 YAML Frontmatter |
| `DirectiveExtension` | 90 | 解析 `@` 指令 |
| `ContainerExtension` | 80 | 解析 `:::` 容器 |
| `ChartExtension` | 70 | 解析图表代码块 |
| `MediaExtension` | 60 | 检测媒体类型 |
| `AttributeExtension` | 50 | 注入属性块 |

### 6.3 自定义扩展示例

```rust
struct CustomExtension;

impl ParserExtension for CustomExtension {
    fn name(&self) -> &str {
        "custom-extension"
    }
    
    fn priority(&self) -> u32 {
        50
    }
    
    fn can_parse(&self, region: &ExtensionRegion) -> bool {
        region.region_type == ExtensionType::Custom && 
        region.content.starts_with("{{")
    }
    
    fn parse(&self, region: &ExtensionRegion) -> Result<Block, ParseError> {
        let content = region.content.trim_start_matches("{{").trim_end_matches("}}");
        Ok(Block::Paragraph(Paragraph {
            span: region.start,
            content: content.to_string(),
            attributes: Attributes::default(),
            inlines: Vec::new(),
        }))
    }
    
    fn inject_attributes(&self, _block: &mut Block) -> Result<(), ParseError> {
        Ok(())
    }
}
```

---

## 七、JSON 序列化验证

### 7.1 Cargo.toml 依赖配置

```toml
[dependencies]
serde = { version = "1.0", features = ["derive"] }
serde_json = "1.0"
pulldown-cmark = "0.9"
yaml-rust = "0.4"
```

### 7.2 IR JSON 序列化示例

**输入 Markdown++ 源码**：

```markdown
---
title: "技术文档示例"
author: "技术团队"
theme: dark
defaultDuration: 4s
---

# Markdown++ 引擎概述 { .slide duration=8s }

**实时渲染引擎技术演示**

@pause

## 核心架构 { duration=5s }

- 语法层：Markdown++ 增强解析
- 渲染层：统一视口控制
- IR层：标准化中间表示
```

**输出 IR JSON**：

```json
{
  "$schema": "https://mdpp.dev/schema/ir-v1.json",
  "version": "1.0",
  "metadata": {
    "title": "技术文档示例",
    "author": "技术团队",
    "theme": "dark",
    "defaultDuration": 4,
    "totalDuration": 13
  },
  "nodes": [
    {
      "type": "slide",
      "id": "slide-1",
      "tag": "h1",
      "content": "Markdown++ 引擎概述",
      "duration": 8,
      "style": {
        "fontSize": "64px",
        "textAlign": "center",
        "verticalAlign": "middle"
      },
      "animation": {
        "enter": "fade",
        "exit": "fade"
      }
    },
    {
      "type": "text",
      "id": "text-1",
      "content": "实时渲染引擎技术演示",
      "style": {}
    },
    {
      "type": "command",
      "id": "command-1",
      "command": "pause"
    },
    {
      "type": "section",
      "id": "section-1",
      "tag": "h2",
      "content": "核心架构",
      "duration": 5,
      "children": [
        {
          "type": "list",
          "id": "list-1",
          "items": [
            "语法层：Markdown++ 增强解析",
            "渲染层：统一视口控制",
            "IR层：标准化中间表示"
          ],
          "ordered": false
        }
      ]
    }
  ],
  "timeline": {
    "mapping": [
      {"y": 0, "t": 0},
      {"y": 1, "t": 8},
      {"y": 2, "t": 8},
      {"y": 3, "t": 13}
    ],
    "totalDuration": 13
  }
}
```

### 7.3 序列化/反序列化验证

```rust
use serde_json;

fn test_ir_serialization() {
    let ir = IntermediateRepresentation {
        schema: "https://mdpp.dev/schema/ir-v1.json".to_string(),
        version: "1.0".to_string(),
        metadata: Metadata {
            title: "Test Document".to_string(),
            author: "Test Author".to_string(),
            theme: "light".to_string(),
            default_duration: 4.0,
            total_duration: 8.0,
            total_height: 2.0,
        },
        nodes: vec![
            IRNode::Slide(SlideNode {
                id: "slide-1".to_string(),
                tag: "h1".to_string(),
                content: "Test Title".to_string(),
                duration: 8.0,
                style: Style::default(),
                animation: Animation {
                    enter: "fade".to_string(),
                    exit: "fade".to_string(),
                },
            }),
        ],
        timeline: Timeline {
            mapping: vec![
                TimeMapping { y: 0.0, t: 0.0 },
                TimeMapping { y: 1.0, t: 8.0 },
            ],
            total_duration: 8.0,
            total_height: 1.0,
        },
    };
    
    let json = serde_json::to_string_pretty(&ir).unwrap();
    println!("Serialized IR:\n{}", json);
    
    let deserialized: IntermediateRepresentation = serde_json::from_str(&json).unwrap();
    assert_eq!(ir, deserialized);
}
```

---

## 附录：语法兼容性矩阵

### A.1 标准 Markdown 兼容性

| 语法元素 | 支持状态 | 备注 |
|----------|----------|------|
| 标题 (#) | ✅ 完全支持 | 支持属性注入 |
| 粗体/斜体 | ✅ 完全支持 | 标准 Markdown |
| 列表 | ✅ 完全支持 | 有序/无序/任务列表 |
| 链接 | ✅ 完全支持 | 标准 Markdown |
| 图片 | ✅ 扩展支持 | 支持媒体类型检测和属性 |
| 代码块 | ✅ 扩展支持 | 支持图表代码块 |
| 引用 | ✅ 完全支持 | 标准 Markdown |
| 表格 | ✅ 完全支持 | 标准 Markdown |
| 水平线 | ✅ 完全支持 | 标准 Markdown |

### A.2 CommonMark 扩展

| 扩展 | 支持状态 | 计划 |
|------|----------|------|
| 脚注 | ❌ 不支持 | Phase 2 |
| 定义列表 | ❌ 不支持 | Phase 2 |
| 任务列表 | ✅ 支持 | 标准功能 |
| 自动链接 | ✅ 支持 | 标准功能 |
| 硬换行 | ✅ 支持 | 标准功能 |

### A.3 Markdown++ 独有扩展

| 扩展 | 状态 | 说明 |
|------|------|------|
| Frontmatter | ✅ 已实现 | YAML 格式 |
| @指令 | ✅ 已实现 | 7种指令 |
| :::容器 | ✅ 已实现 | 支持嵌套 |
| 属性注入 | ✅ 已实现 | { } 语法 |
| 图表代码块 | ✅ 已实现 | JSON 数据 |
| 富媒体嵌入 | ✅ 已实现 | 自动类型检测 |