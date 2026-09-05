namespace Plate.DslMethod.Model;

/// <summary>
/// [CR] 交叉引用（[CR: lib:styp.name.sub@intent]）。
/// 文法对齐 dsl.md §4.1：lib 唯一，type 为 STYP，name 为符号主键，sub 为点链子谓词。
/// </summary>
public sealed class DslReference
{
    /// <summary>库标识（注册库枚举，见 dsl.md §4.4）。</summary>
    public required string Lib { get; init; }

    /// <summary>符号类型（STYP），如 AX / Def / SM / Gate / Bind；旧形态（file.md:anchor）可为空。</summary>
    public string? Type { get; init; }

    /// <summary>符号名（定义点唯一主键）。</summary>
    public required string Name { get; init; }

    /// <summary>子谓词/子公式定位（点链，可选）。</summary>
    public string? Sub { get; init; }

    /// <summary>意图（def/deriv/inst/consume/align/see，可选）。</summary>
    public string? Intent { get; init; }

    /// <summary>原始引用文本。</summary>
    public required string Raw { get; init; }

    /// <summary>所在行号（1-based）。</summary>
    public int LineNumber { get; init; }

    public override string ToString() => Raw;
}

/// <summary>一条 DSL 声明（[AX]/[Def]/[Type]/[Gate]/[SM]/[Bind]/…）。</summary>
public sealed class DslDeclaration
{
    /// <summary>行前缀（如 "[AX]" / "[Bind]" / "%"）。</summary>
    public required string Prefix { get; init; }

    /// <summary>符号类型。</summary>
    public required DslSymbolKind Kind { get; init; }

    /// <summary>STYP 缩写（索引键用，对齐 dsl.md §4.7 锚点 ID）。</summary>
    public required string Stub { get; init; }

    /// <summary>符号名（定义点唯一主键）。</summary>
    public required string Name { get; init; }

    /// <summary>声明值（首个 `|` 之前；[Bind] 无独立 body 时为空）。</summary>
    public string Body { get; set; } = "";

    /// <summary>来源文件路径。</summary>
    public required string SourceFile { get; init; }

    /// <summary>所在行号（1-based）。</summary>
    public required int LineNumber { get; init; }

    /// <summary>原始行文本。</summary>
    public required string RawLine { get; init; }

    /// <summary>结构化字段（k=v，如 [Bind] 的 kind/impl/module/layer/status）。</summary>
    public List<KeyValuePair<string, string>> Fields { get; } = new();

    /// <summary>本声明包含的全部 [CR] 引用。</summary>
    public List<DslReference> References { get; } = new();

    /// <summary>缩进续行原文（[Gate] 分支 / [SM] 状态机步骤 / [Schema] 块等；协议 DSL 吸收通道）。</summary>
    public List<string> Continuation { get; } = new();

    /// <summary>fenced 模板块内容（[SM]/[Template] 头后 ``` 块；协议 DSL 吸收通道）。</summary>
    public string? Template { get; set; }

    /// <summary>fenced 块后 `| slots: a, b` 槽位（协议 DSL 吸收通道）。</summary>
    public List<string> Slots { get; } = new();

    /// <summary>[Gate] 后缩进条件行（field=…, op=…, value=[…]，协议 DSL replan 条件）。</summary>
    public List<Condition> Conditions { get; } = new();

    /// <summary>[Schema] 头后缩进块拼接文本（吸收 FormatSpecParser 规则；FormatProjector 据此还原字段/必填/枚举）。</summary>
    public string? SchemaBlock { get; set; }

    /// <summary>读取字段值（大小写不敏感）。</summary>
    public string Field(string key)
        => Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? "";

    /// <summary>是否含某字段。</summary>
    public bool HasField(string key)
        => Fields.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => $"{Prefix} {Name}";
}

/// <summary>[Gate] replan 条件（协议 DSL：field=…, op=…, value=[…]）。</summary>
public sealed class Condition
{
    public string? Field { get; set; }
    public string Op { get; set; } = "contains_any";
    public List<string> Value { get; } = new();
}

/// <summary>DSL 章节标题（## / ###）。</summary>
public sealed class DslSection
{
    public string Title { get; set; } = "";
    public int LineNumber { get; set; }
}

/// <summary>一个 DSL 文档（文件 → 声明 + 章节）。</summary>
public sealed class DslDocument
{
    /// <summary>文件路径。</summary>
    public string FilePath { get; set; } = "";

    /// <summary>库标识（lib，索引键用）。</summary>
    public string Lib { get; set; } = "";

    /// <summary>章节（导航用）。</summary>
    public List<DslSection> Sections { get; set; } = new();

    /// <summary>全部声明（扁平）。</summary>
    public List<DslDeclaration> Declarations { get; set; } = new();

    /// <summary>模板/状态机类节点视图（[SM]/[Template]，含 fenced Template/Slots）。</summary>
    public List<DslDeclaration> Templates { get; } = new();

    /// <summary>配置参数节点视图（%）。</summary>
    public List<DslDeclaration> Params { get; } = new();

    /// <summary>Schema 节点视图（[Schema]，含 SchemaBlock）。</summary>
    public List<DslDeclaration> Schemas { get; } = new();
}

/// <summary>[Bind] 契约绑定（实现↔公理对应）的强类型视图。</summary>
public sealed class BindingDeclaration
{
    /// <summary>绑定符号名。</summary>
    public required string Name { get; init; }

    /// <summary>关系种类：implement=代码实现 / derive=L0 契约渲染 / guard=校验器守卫。</summary>
    public string Kind { get; init; } = "implement";

    /// <summary>实现符号（代码类.方法 或 L0 契约文件:符号）。</summary>
    public string Impl { get; init; } = "";

    /// <summary>目标公理（[CR] 引用）。</summary>
    public DslReference? Target { get; init; }

    /// <summary>模块簇（module 字段，见 ContractDimensions.Modules）。</summary>
    public string Module { get; init; } = "";

    /// <summary>公理轴（axis 字段，见 ContractDimensions.Axes）。</summary>
    public string Axis { get; init; } = "";

    /// <summary>层级（layer 字段）。</summary>
    public string Layer { get; init; } = "";

    /// <summary>状态：aligned=已对齐 / gap=缺公理 / drift=漂移。</summary>
    public string Status { get; init; } = "aligned";

    /// <summary>来源文件路径。</summary>
    public string SourceFile { get; init; } = "";

    /// <summary>所在行号（1-based）。</summary>
    public int LineNumber { get; init; }
}
