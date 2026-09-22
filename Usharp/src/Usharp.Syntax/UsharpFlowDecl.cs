using Plate.DslMethod;
using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>流的一步：节点键 + 可选子链引用（<c>~ subflow</c> ⇒ 工作流连接链的唯一语法）+ 可选发射绑定（<c>emit=k1,k2</c>）。</summary>
public sealed record UsharpFlowStep(string Key, string? SubFlow)
{
    /// <summary>本步发射的事件分类（<c>emit=</c> 缺省 → 空表 = 不发射）。</summary>
    public IReadOnlyList<string> Emits { get; init; } = Array.Empty<string>();

    public static UsharpFlowStep? Parse(string line)
    {
        var text = line.Trim();
        if (text.Length == 0) return null;
        if (UsharpLanguage.HasStepMarker(text))
            text = text[UsharpLanguage.StepMarker.Length..].TrimStart(':', '=', ' ', '\t');

        // 发射绑定须先于 `~` 剥离：否则 `step k emit=a,b ~ Sub` 的 emit 段会被吞进子流名。
        var emits = ExtractEmits(ref text);

        string? sub = null;
        var tilde = text.IndexOf(UsharpLanguage.SubFlowMarker);
        if (tilde >= 0)
        {
            sub = text[(tilde + 1)..].Trim();
            text = text[..tilde].Trim();
            if (sub.Length == 0) sub = null;
        }
        return text.Length == 0 ? null : new UsharpFlowStep(text, sub) { Emits = emits };
    }

    /// <summary>从步骤行剥离 <c>emit=&lt;k1,k2&gt;</c>（值止于空白或 <c>~</c>），返回事件分类表。</summary>
    private static IReadOnlyList<string> ExtractEmits(ref string text)
    {
        var marker = UsharpLanguage.FieldEmit + "=";
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return Array.Empty<string>();

        var after = text[(at + marker.Length)..];
        var end = after.Length;
        for (var i = 0; i < after.Length; i++)
            if (char.IsWhiteSpace(after[i]) || after[i] == UsharpLanguage.SubFlowMarker) { end = i; break; }

        var value = after[..end];
        text = (text[..at] + after[end..]).Trim();

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>
/// 链间连接声明的一条：<c>connects=&lt;UpstreamFlowId&gt;:&lt;EdgeType&gt;</c>（**方向即语义**：上游流号在前）。
/// <para>与 <see cref="UsharpContainmentDecl"/> 语义分离：此处的第一分量是**上游流号**（不是空间号），
/// 二者共用书写形状但不共用类型，避免一词双义。</para>
/// </summary>
public sealed record UsharpConnectDecl(string UpstreamFlowId, string EdgeType)
{
    /// <summary>解析 <c>&lt;上游流号&gt;:&lt;边类型&gt;</c> 列表（<c>;</c> 分段；形状校验留给闸A）。</summary>
    public static IReadOnlyList<UsharpConnectDecl> ParseList(string? field)
    {
        var items = new List<UsharpConnectDecl>();
        foreach (var segment in UsharpLanguage.SplitSegments(field))
        {
            var (upstream, edgeType) = UsharpLanguage.SplitPair(segment);
            items.Add(new UsharpConnectDecl(upstream, edgeType));
        }
        return items;
    }
}

/// <summary>[Flow] Name: over=&lt;SpaceId&gt; 的语法视图（链 / 工作流 = 以链为节点的图）。</summary>
public sealed record UsharpFlowDecl(
    string FlowId,
    string? OverSpaceId,
    string? SuccessCriterion,
    IReadOnlyList<UsharpFlowStep> Steps,
    string SourceFile,
    int LineNumber)
{
    /// <summary>
    /// 层处理器绑定（<c>process=&lt;ProcessId&gt;</c>；空 = 未绑定）——本流即该层的处理代数。
    /// **加法式 init 属性**（不改位置参数）；与 <c>[Matrix] process=</c> 共域，一层恰一个声明者（多者 → US1011）。
    /// </summary>
    public string? ProcessId { get; init; }

    /// <summary>
    /// 处理编排方向原文（<c>order=</c>；缺省 null = <c>bottom_up</c>）。
    /// **加法式 init 属性**；取值合法性/回退由 <c>UsharpPlanFields</c>（单一实现）裁决，闸A 对未知值报 US1018。
    /// </summary>
    public string? Order { get; init; }

    /// <summary>展开止层（<c>stop=&lt;LevelId&gt;</c>；null = 按 schema 展开到底）。</summary>
    public string? StopSpaceId { get; init; }

    /// <summary>
    /// <c>budget=</c> 字段原文（缺省 = null；**在场但非法** = 非空串）。
    /// 解析与合法性判定**唯一实现** = <c>UsharpPlanFields.TryBudget</c>（Syntax 层不再另存解析结果，
    /// 避免「null 的两种含义」被反推）。
    /// </summary>
    public string? BudgetRaw { get; init; }

    /// <summary>
    /// 链间连接（<c>connects=&lt;上游流号&gt;:&lt;边类型&gt;</c>；**方向即语义**：上游在前）——
    /// 补齐「块间连接」既有能力的前端声明面（连接算法仍归 WorkFlowMethod，不新增第二执行路径）。
    /// </summary>
    public IReadOnlyList<UsharpConnectDecl> Connects { get; init; } = Array.Empty<UsharpConnectDecl>();

    public static UsharpFlowDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var steps = new List<UsharpFlowStep>();
        foreach (var raw in d.Continuation)
        {
            var step = UsharpFlowStep.Parse(raw);
            if (step is not null) steps.Add(step);
        }
        var over = d.Field(UsharpLanguage.FieldOver);
        var success = d.Field(UsharpLanguage.FieldSuccess);
        var process = d.HasField(UsharpLanguage.FieldProcess)
            ? d.Field(UsharpLanguage.FieldProcess).Trim()
            : null;
        var order = d.HasField(UsharpLanguage.FieldOrder)
            ? d.Field(UsharpLanguage.FieldOrder).Trim()
            : null;
        var stop = d.HasField(UsharpLanguage.FieldStop)
            ? d.Field(UsharpLanguage.FieldStop).Trim()
            : null;
        var budgetRaw = d.HasField(UsharpLanguage.FieldBudget)
            ? d.Field(UsharpLanguage.FieldBudget).Trim()
            : null;

        return new UsharpFlowDecl(d.Name,
            string.IsNullOrWhiteSpace(over) ? null : over.Trim(),
            string.IsNullOrWhiteSpace(success) ? null : success.Trim(),
            steps, d.SourceFile, d.LineNumber)
        {
            ProcessId = process,
            Order = string.IsNullOrWhiteSpace(order) ? null : order,
            StopSpaceId = string.IsNullOrWhiteSpace(stop) ? null : stop,
            BudgetRaw = string.IsNullOrWhiteSpace(budgetRaw) ? null : budgetRaw,
            Connects = UsharpConnectDecl.ParseList(d.Field(UsharpLanguage.FieldConnects)),
        };
    }

    /// <summary>预算：<c>&lt;maxNodes&gt;;&lt;maxDepth&gt;</c> 两段正整数（任一段非正整数或段数不为 2 → null）。</summary>
    private static (int MaxNodes, int MaxDepth)? ParseBudget(string? raw)
    {
        var segments = UsharpLanguage.SplitSegments(raw);
        if (segments.Length != 2) return null;
        if (!int.TryParse(segments[0], out var maxNodes) || maxNodes <= 0) return null;
        if (!int.TryParse(segments[1], out var maxDepth) || maxDepth <= 0) return null;
        return (maxNodes, maxDepth);
    }
}

/// <summary>[Ref] Name: &lt;SpaceId&gt;:&lt;Key&gt; 的语法视图（带空间归属的引用 = 引用图的一条边）。</summary>
public sealed record UsharpRefDecl(
    string Name,
    string SpaceId,
    string Key,
    string? Source,
    string? Target,
    string? Intent,
    string? Edge,
    string SourceFile,
    int LineNumber)
{
    /// <summary>
    /// 作用域归属（<c>scope=&lt;ScopeKey&gt;</c>；**加法式 init 属性**）——白名单 = <c>[Obs] kind=scope</c>
    /// 的既有声明面（零新语法）；未声明值 → US1014。<c>HasField</c> 区分「缺省」与「在场」。
    /// </summary>
    public string? Scope { get; init; }

    public static UsharpRefDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var space = d.Field(UsharpLanguage.FieldSpace);
        var key = d.Field(UsharpLanguage.FieldKey);

        // 简写形态：`[Ref] Name: <SpaceId>:<Key>`（body 内含冒号）
        if (string.IsNullOrWhiteSpace(space) || string.IsNullOrWhiteSpace(key))
        {
            var body = d.Body?.Trim() ?? "";
            var colon = body.IndexOf(':');
            if (colon > 0)
            {
                if (string.IsNullOrWhiteSpace(space)) space = body[..colon];
                if (string.IsNullOrWhiteSpace(key)) key = body[(colon + 1)..];
            }
        }

        return new UsharpRefDecl(
            d.Name, (space ?? "").Trim(), (key ?? "").Trim(),
            NullIfEmpty(d.Field(UsharpLanguage.FieldSrc)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldTgt)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldIntent)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldEdge)),
            d.SourceFile, d.LineNumber)
        {
            Scope = d.HasField(UsharpLanguage.FieldScope) ? NullIfEmpty(d.Field(UsharpLanguage.FieldScope)) : null,
        };
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>把 DslDocument 投影为 Usharp 四类语法声明（按 <see cref="DslSymbolKind"/> 分派）。</summary>
public static class UsharpSyntaxProjector
{
    public static IReadOnlyList<UsharpSpaceDecl> Spaces(DslDocument doc)
        => doc.Declarations.Where(d => d.Kind == DslSymbolKind.Space)
            .Select(UsharpSpaceDecl.From).Where(s => s is not null).Select(s => s!).ToList();

    public static IReadOnlyList<UsharpMatrixDecl> Matrices(DslDocument doc)
        => doc.Declarations.Where(d => d.Kind == DslSymbolKind.Matrix)
            .Select(UsharpMatrixDecl.From).Where(s => s is not null).Select(s => s!).ToList();

    public static IReadOnlyList<UsharpFlowDecl> Flows(DslDocument doc)
        => doc.Declarations.Where(d => d.Kind == DslSymbolKind.Flow)
            .Select(UsharpFlowDecl.From).Where(s => s is not null).Select(s => s!).ToList();

    public static IReadOnlyList<UsharpRefDecl> Refs(DslDocument doc)
        => doc.Declarations.Where(d => d.Kind == DslSymbolKind.Ref)
            .Select(UsharpRefDecl.From).Where(s => s is not null).Select(s => s!).ToList();

    /// <summary><c>[Obs]</c> 观测声明（作用域 / 实体 / 事件；第五语言单元）。</summary>
    public static IReadOnlyList<UsharpObsDecl> Obs(DslDocument doc)
        => doc.Declarations.Where(d => d.Kind == DslSymbolKind.Obs)
            .Select(UsharpObsDecl.From).Where(s => s is not null).Select(s => s!).ToList();
}
