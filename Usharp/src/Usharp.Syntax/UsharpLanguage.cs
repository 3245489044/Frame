namespace Usharp.Syntax;

/// <summary>
/// Usharp 语言面常量（单一真源）：五前缀由本包自持（见 <see cref="UsharpPrefixes.Scope"/>，
/// 已注册到 <c>Plate.DslMethod.Parsing.DslPrefixRegistry</c>，<b>不写入框架核心表</b>），
/// 本类只登记「字段名 / 空间种类」等语言层词汇，避免在语法/语义两处各写一份字面量。
/// 前缀注册表是唯一权威（新增前缀 = 在 <see cref="UsharpPrefixes.Scope"/> 增条目，文法不变）。
/// </summary>
public static class UsharpLanguage
{
    /// <summary>空间声明前缀（坐标空间）。</summary>
    public const string SpacePrefix = "[Space]";

    /// <summary>矩阵声明前缀（空间上的一次变换）。</summary>
    public const string MatrixPrefix = "[Matrix]";

    /// <summary>流声明前缀（链 / 工作流）。</summary>
    public const string FlowPrefix = "[Flow]";

    /// <summary>引用声明前缀（带空间归属的引用）。</summary>
    public const string RefPrefix = "[Ref]";

    /// <summary>观测声明前缀（第五语言单元：作用域 / 实体 / 事件；子类由 <c>kind=</c> 区分）。</summary>
    public const string ObsPrefix = "[Obs]";

    /// <summary>观测声明子类取值：<c>kind=file|scope|entity|event</c>。</summary>
    public const string KindFile = "file";
    public const string KindScope = "scope";
    public const string KindEntity = "entity";
    public const string KindEvent = "event";

    /// <summary>[Obs] 字段名：父作用域 / 所属作用域 / 实体类型 / 必填字段 / 度量键 / 枚举域 / 版本。</summary>
    public const string FieldParent = "parent";
    public const string FieldScope = "scope";
    public const string FieldEntity = "entity";
    public const string FieldRequired = "required";
    public const string FieldFields = "fields";
    public const string FieldMetrics = "metrics";
    public const string FieldEnum = "enum";
    public const string FieldVersion = "version";

    /// <summary>[Flow] 步骤行的发射绑定字段（<c>step &lt;key&gt; emit=k1,k2</c>）。</summary>
    public const string FieldEmit = "emit";

    /// <summary><c>enum=</c> 多字段分段符（<c>|</c> 与 <c>,</c> 已被字段/列表占用）。</summary>
    public const char EnumFieldSeparator = ';';

    /// <summary>空间种类字段名（<c>kind=keys|values</c>）。</summary>
    public const string FieldKind = "kind";

    /// <summary>矩阵/流绑定空间字段名（<c>over=&lt;SpaceId&gt;</c>）。</summary>
    public const string FieldOver = "over";

    /// <summary>
    /// 子空间划分字段（<c>contains=&lt;ParentSpaceId&gt;:&lt;EdgeType&gt;</c>；多父层以 <c>;</c> 分段）——
    /// 本空间是父空间的**坐标子集**（垂直轴的划分归属，见设计方案 §3.1）。
    /// 方向 = 子声明父（与内核 <c>HierarchyContainment</c> 的父子方向一致，避免反向推导双实现）。
    /// </summary>
    public const string FieldContains = "contains";

    /// <summary>
    /// 层处理器绑定字段（<c>process=&lt;ProcessId&gt;</c>；<c>[Matrix]</c>/<c>[Flow]</c> 共用）——
    /// 「小矩阵 = 该层的处理代数」（喂 <c>HierarchyLevelSpec.ProcessId</c>，见设计方案 §3.1）。
    /// 一层恰一个声明者（多者 → US1011）。
    /// </summary>
    public const string FieldProcess = "process";

    /// <summary>
    /// 处理编排字段（<c>order=</c> / <c>stop=</c> / <c>budget=</c>；<c>[Flow]</c> 专属）——
    /// 喂内核 <c>HierarchyProcessPlan</c>（方向 / 展开止层 / 预算）。
    /// </summary>
    public const string FieldOrder = "order";
    public const string FieldStop = "stop";
    public const string FieldBudget = "budget";

    /// <summary>遍历方向取值（<c>order=</c>；书写面 = 内核 <c>HierarchyTraversalOrder</c> 的合法值域）。</summary>
    public const string OrderBottomUp = "bottom_up";
    public const string OrderTopDown = "top_down";

    /// <summary>
    /// 链间连接字段（<c>connects=&lt;上游流号&gt;:&lt;连接边类型&gt;</c>；多上游以 <c>;</c> 分段）——
    /// **方向即语义**（上游在前）；补齐「块间连接」既有能力的前端声明面（连接算法仍归 WorkFlowMethod）。
    /// </summary>
    public const string FieldConnects = "connects";

    /// <summary>引用字段：所属空间 / 坐标键 / 源 / 目标 / 意图 / 边类型。</summary>
    public const string FieldSpace = "space";
    public const string FieldKey = "key";
    public const string FieldSrc = "src";
    public const string FieldTgt = "tgt";
    public const string FieldIntent = "intent";
    public const string FieldEdge = "edge";

    /// <summary><c>[Edge]</c> 声明的 UR 词汇表显式映射字段（<c>ur=Dependency</c>）——语言面字段名单源。</summary>
    public const string FieldUr = "ur";

    /// <summary>[Flow] 成功准则字段（<c>success=decision|run_to_end</c>；缺省 decision，对齐既有骨架契约口径）。</summary>
    public const string FieldSuccess = "success";

    /// <summary>流步骤行的行首标记（<c>step &lt;key&gt;</c> / <c>step: &lt;key&gt;</c> / <c>step=&lt;key&gt;</c>）。</summary>
    public const string StepMarker = "step";

    /// <summary>
    /// 步骤行是否带 <c>step</c> 标记：标记后须跟分隔符（<c>:</c> / <c>=</c> / 空白），或整行即标记。
    /// 用于防止裸键名（<c>steps</c> / <c>step2</c> / <c>step_count</c>）被误剥前缀而静默改写成另一坐标。
    /// </summary>
    public static bool HasStepMarker(string text)
    {
        if (!text.StartsWith(StepMarker, StringComparison.Ordinal)) return false;
        if (text.Length == StepMarker.Length) return true;
        var next = text[StepMarker.Length];
        return next is ':' or '=' || char.IsWhiteSpace(next);
    }

    /// <summary>子流引用标记（<c>step cline_execute ~ session-cline</c>）——工作流连接链的唯一语法。</summary>
    public const char SubFlowMarker = '~';

    /// <summary>键型空间（坐标无值域，不可比较）。</summary>
    public const string KindKeys = "keys";

    /// <summary>值型空间（坐标有值域：flag / ordinal / numeric）。</summary>
    public const string KindValues = "values";

    /// <summary>值型空间的合法值类（Usharp 语言层词汇；数值语义由 BasicMethod.Declarative.Spec.ParameterKind 承担）。</summary>
    public const string KindFlag = "flag";
    public const string KindOrdinal = "ordinal";
    public const string KindNumeric = "numeric";

    /// <summary>合法值类集合（单源：矩阵侧映射表 <c>UsharpValueKindMap</c> 由其派生）。</summary>
    public static readonly string[] ValueKinds = { KindFlag, KindOrdinal, KindNumeric };

    /// <summary>
    /// 多段字段切分（`;` 分段、去空段、逐段去空白）—— `enum=` / `contains=` / `connects=` / `budget=`
    /// 共用同一分段口径（`|` 与 `,` 已被字段/列表占用）。
    /// </summary>
    public static string[] SplitSegments(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(EnumFieldSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>按**首个** `:` 切为两段（无 `:` → 第二段空串；两段均去空白）——「名 : 类型」形状的统一切口。</summary>
    public static (string Left, string Right) SplitPair(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var colon = segment.IndexOf(':');
        return colon < 0
            ? (segment.Trim(), string.Empty)
            : (segment[..colon].Trim(), segment[(colon + 1)..].Trim());
    }
}
