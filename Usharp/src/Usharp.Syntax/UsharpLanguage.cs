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

    /// <summary>引用字段：所属空间 / 坐标键 / 源 / 目标 / 意图 / 边类型。</summary>
    public const string FieldSpace = "space";
    public const string FieldKey = "key";
    public const string FieldSrc = "src";
    public const string FieldTgt = "tgt";
    public const string FieldIntent = "intent";
    public const string FieldEdge = "edge";

    /// <summary>[Flow] 成功准则字段（<c>success=decision|run_to_end</c>；缺省 decision，对齐既有骨架契约口径）。</summary>
    public const string FieldSuccess = "success";

    /// <summary>流步骤行的行首标记（<c>step &lt;key&gt;</c> / <c>step: &lt;key&gt;</c> / <c>step=&lt;key&gt;</c>）。</summary>
    public const string StepMarker = "step";

    /// <summary>子流引用标记（<c>step cline_execute ~ session-cline</c>）——工作流连接链的唯一语法。</summary>
    public const char SubFlowMarker = '~';

    /// <summary>键型空间（坐标无值域，不可比较）。</summary>
    public const string KindKeys = "keys";

    /// <summary>值型空间（坐标有值域：flag / ordinal / numeric）。</summary>
    public const string KindValues = "values";

    /// <summary>值型空间的合法值类（Usharp 语言层词汇；数值语义由 BasicMethod.Declarative.Spec.ParameterKind 承担）。</summary>
    public static readonly string[] ValueKinds = { "flag", "ordinal", "numeric" };
}
