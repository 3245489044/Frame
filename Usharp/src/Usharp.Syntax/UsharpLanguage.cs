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

    /// <summary>表结构声明前缀（数据层：让数据库表由声明派生；<c>[Table]</c>）。</summary>
    public const string TablePrefix = "[Table]";

    /// <summary>记录载荷声明前缀（数据层：行/文档的 USharp 化载体；<c>[Record]</c>）。</summary>
    public const string RecordPrefix = "[Record]";

    /// <summary>配置声明前缀（配置面：让配置项由声明派生；<c>[Config]</c>）。</summary>
    public const string ConfigPrefix = "[Config]";

    /// <summary>规则声明前缀（规则面：条件 → 动作；<c>[Rule]</c>）。</summary>
    public const string RulePrefix = "[Rule]";

    // ---- 配置/规则语言单元字段名（[Config]/[Rule]）----
    // 单源：禁在声明类/装载器各写一份字面量。`scope` 复用 <see cref="FieldScope"/>（不造别名）。

    /// <summary>配置叶值字段（<c>value=</c>）。</summary>
    public const string FieldValue = "value";

    /// <summary>配置节/规则集引用字段（<c>rules=</c>）。</summary>
    public const string FieldRules = "rules";

    /// <summary>规则名 / 具名键字段（<c>name=</c>）。</summary>
    public const string FieldName = "name";

    /// <summary>规则条件字段（<c>condition=</c>）。</summary>
    public const string FieldCondition = "condition";

    /// <summary>规则动作字段（<c>action=</c>）。</summary>
    public const string FieldAction = "action";

    /// <summary>规则理由字段（<c>reason=</c>）。</summary>
    public const string FieldReason = "reason";

    /// <summary>规则启用位字段（<c>enabled=</c>）。</summary>
    public const string FieldEnabled = "enabled";

    // ---- 数据层字段名（[Table]/[Record]）----

    /// <summary>表/记录所属容器（<c>container=</c>：nodes/tasks/resources/endpoints/events）。</summary>
    public const string FieldContainer = "container";

    /// <summary>表主键列名（<c>key=</c>；表声明头 / 记录声明头共用）。</summary>
    public const string FieldTable = "table";

    /// <summary>索引列集（<c>index=(a,b)</c>；表声明头）。</summary>
    public const string FieldIndex = "index";

    /// <summary>表列类型受控词（数据层值域）。</summary>
    public static readonly string[] ColumnTypes =
        { "string", "int", "decimal", "bool", "record", "list", "bytes" };

    /// <summary>表列标志受控词（数据层标声明）。</summary>
    public static readonly string[] ColumnFlags =
        { "auto", "unique", "source", "derived", "blob" };


    /// <summary>分支声明前缀（R3 声明式控制流：判定键 → then/else 激活/抑制）。</summary>
    public const string BranchPrefix = "[Branch]";

    /// <summary>多路分派声明前缀（R3：选择键取值 → 路由目标）。</summary>
    public const string SwitchPrefix = "[Switch]";

    /// <summary>调用声明前缀（R3：承载节点 → 子流调用）。</summary>
    public const string CallPrefix = "[Call]";

    /// <summary>区域声明前缀（R3+：父层单节点 + 子计划迭代 → 电路子电路/区域）。</summary>
    public const string RegionPrefix = "[Region]";

    /// <summary>受控边声明前缀（R3+：<c>judge</c> 判真后 <c>from → to</c> 边才激活）。</summary>
    public const string WhenPrefix = "[When]";

    /// <summary>受护作用域声明前缀（R3+：作用域内节点失败 → 吞并继续）。</summary>
    public const string GuardPrefix = "[Guard]";

    /// <summary>域定义声明前缀（节点域定义面：identity / placements / selections / retentions）。</summary>
    public const string DomainPrefix = "[Domain]";

    /// <summary>聚拢声明前缀（收集端口：族 → kind 集 + kind 塑形词表；U2）。</summary>
    public const string GatherPrefix = "[Gather]";

    /// <summary>聚拢声明续行首词（<c>[Gather]</c>）：判别字段 / 二级分组字段 / 族声明 / 塑形声明。</summary>
    public const string MarkerKindField = "kind-field";
    public const string MarkerGroupBy = "group-by";
    public const string MarkerFamily = "family";
    public const string MarkerShaping = "shaping";

    /// <summary>塑形字段名（<c>shaping</c> 行内 <c>k=v</c>；与 JSON 键名同形）。</summary>
    public const string FieldFamily = "family";
    public const string FieldTitleKey = "titleKey";
    public const string FieldSummary = "summary";
    public const string FieldBadge = "badge";
    public const string FieldTimeKey = "timeKey";
    public const string FieldShaped = "shaped";

    /// <summary>域定义面取用语义词表子块名（U3）：<c>marks</c>（名→序值）/ <c>origins</c>（受控词集）。</summary>
    public const string FieldMarks = "marks";
    public const string FieldOrigins = "origins";

    /// <summary>控制流声明字段名（R3，<c>[Branch]/[Switch]/[Call]</c> 共用）：所属流 / 承载节点 / 判定键 / 选择键 / 目标。</summary>
    public const string FieldFlow = "flow";
    public const string FieldNode = "node";
    public const string FieldJudge = "judge";
    public const string FieldSelector = "selector";
    public const string FieldThen = "then";
    public const string FieldElse = "else";

    /// <summary>控制流声明字段名（R3）：路由集 / 默认目标 / 被调 Kind / 子流号 / 入映射 / 出映射。</summary>
    public const string FieldRoutes = "routes";
    public const string FieldDefault = "default";
    public const string FieldCallee = "callee";
    public const string FieldChildFlow = "child";
    public const string FieldInBindings = "in";
    public const string FieldOutBindings = "out";

    /// <summary>区域声明字段名（R3+，<c>[Region]</c>）：子流 / 迭代方式 / 迭代上限（判定端口复用 <see cref="FieldJudge"/>）。</summary>
    public const string FieldIteration = "iter";
    public const string FieldMaxIterations = "max";

    /// <summary>受控边字段名（R3+，<c>[When]</c>）：前驱 / 后继 / 判定键（<c>from=</c>/<c>to=</c>/<c>judge=</c>）。</summary>
    public const string FieldFrom = "from";
    public const string FieldTo = "to";

    /// <summary>受护作用域字段名（R3+，<c>[Guard]</c>）：受护节点集（<c>nodes=a;b;c</c>）。</summary>
    public const string FieldNodes = "nodes";

    /// <summary>区域迭代方式取值：<c>iter=once|while_true|until_true</c>（对齐引擎 <c>RegionIterationPlan</c>）。</summary>
    public const string IterationOnce = "once";
    public const string IterationWhileTrue = "while_true";
    public const string IterationUntilTrue = "until_true";

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

    /// <summary>
    /// <c>[Domain]</c> 字段名（节点域定义面）：身份模板 / 上下文坐标 / 序数坐标 / 位置块 / 选择词表 / 保留词表——
    /// 语言面字段名单源（禁在解析/发射各写一份字面量）。
    /// </summary>
    public const string FieldIdentity = "identity";
    public const string FieldContext = "context";
    public const string FieldSequence = "sequence";
    public const string FieldPlacements = "placements";
    public const string FieldSelections = "selections";
    public const string FieldRetentions = "retentions";

    /// <summary>
    /// <c>placement</c> 项字段名（位置块内逐条声明）：id / level / carrier / key / form / retention / selection / writer / state。
    /// <c>key</c> 复用 <see cref="FieldKey"/>（同一键字段名，禁造别名）。
    /// </summary>
    public const string FieldId = "id";
    public const string FieldLevel = "level";
    public const string FieldCarrier = "carrier";
    public const string FieldForm = "form";
    public const string FieldRetention = "retention";
    public const string FieldSelection = "selection";
    public const string FieldWriter = "writer";
    public const string FieldState = "state";

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
    /// 第三空间种类：词元空间（<c>kind=terms</c>）—— 坐标带**词元集**（命名枚举，不可量化、不可比较），
    /// 与值型空间的「值域」互斥。书写面 = <c>&lt;轴&gt; | zone=&lt;象限&gt; | terms=&lt;t1&gt;,&lt;t2&gt;</c>；
    /// 装配产物 = 判据读法（<c>DecisionGraph</c>），非值域矩阵（见 E1 施工计划 §2/§4）。
    /// </summary>
    public const string KindTerms = "terms";

    /// <summary>
    /// 空间种类取值集合（**单源**）：<c>[Space] kind=</c> 的合法性判据（<c>US0006</c>）与书写模板由此派生，
    /// 禁在注册表/语法/发射各写一份取值域。
    /// </summary>
    public static readonly string[] SpaceKinds = { KindKeys, KindValues, KindTerms };

    /// <summary><c>[Space] kind=terms</c> 词元行字段名：象限归属 / 词元集。</summary>
    public const string FieldZone = "zone";
    public const string FieldTerms = "terms";

    /// <summary><c>[Matrix]</c> 判据读法的层 / 规则字段名（E1）。</summary>
    public const string FieldOutput = "output";
    public const string FieldAbsorb = "absorb";

    /// <summary><c>[Matrix]</c> 判据读法的规则字段名：面内优先级 / 必需词元集 / 产出标签（E1）。</summary>
    public const string FieldPriority = "priority";
    public const string FieldWhen = "when";
    public const string FieldLabel = "label";

    /// <summary><c>[Matrix]</c> 判据读法的续行首词（层 / 规则 / 复合特征）。</summary>
    public const string RowLayerMarker = "layer";
    public const string RowRuleMarker = "rule";
    public const string RowCrossMarker = "cross";

    /// <summary>词元引用 <c>&lt;轴&gt;.&lt;词元&gt;</c> 的分隔符（禁用 <c>-</c>，坐标名文法见 <see cref="UsharpIdentifier"/>）。</summary>
    public const char TermSeparator = '.';

    /// <summary>判据读法续行首词集合（<see cref="TryReadRowMarker"/> 的判据源；声明序）。</summary>
    public static readonly string[] RowMarkers = { RowLayerMarker, RowRuleMarker, RowCrossMarker };

    /// <summary>
    /// 续行首词判据（<c>layer</c>/<c>rule</c>/<c>cross</c>）：首词后须跟分隔符（空白 / <c>|</c>），或整行即首词
    /// —— 与 <see cref="HasStepMarker"/> 同款防裸键误剥（<c>layer_count</c> 不得被判为层行）。
    /// </summary>
    /// <param name="text">续行原文（调用方已 Trim）。</param>
    /// <param name="marker">out：命中的首词（未命中 = 空串，调用方据 false 判定）。</param>
    public static bool TryReadRowMarker(string text, out string marker)
    {
        marker = string.Empty;
        if (string.IsNullOrEmpty(text)) return false;

        foreach (var candidate in RowMarkers)
        {
            if (!text.StartsWith(candidate, StringComparison.Ordinal)) continue;
            if (text.Length == candidate.Length) { marker = candidate; return true; }
            var next = text[candidate.Length];
            if (next == '|' || char.IsWhiteSpace(next)) { marker = candidate; return true; }
        }

        return false;
    }

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

    /// <summary>空白即 null（否则去空白）—— 声明可空字段的统一切口（D8：消除各声明类逐字复制）。</summary>
    public static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
