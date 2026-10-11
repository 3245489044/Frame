using System.Text.Json.Nodes;
using Modules.Node.Spec;

namespace Modules.Node.Stream;

/// <summary>
/// Stream **帧级选择**应用器（通用；域无关）：唯一权威 = 声明面的
/// <c>selections</c>（迷你表达式 <c>kind in (…)</c> / <c>true</c> / <c>false</c>），
/// 复用框架 <see cref="ExpressionSelectionPredicate"/> 解析（禁自造表达式文法）。
/// <para>
/// 差异参数 = 声明相对路径（仅可见化标签）+ 缺省选择名。
/// 语义：<c>all</c> 全量；未声明 selectionId ∨ 表达式不可识别 ⇒ **⊥**（<see cref="TryResolve"/> 返回 false + 原因，不静默放行）。
/// </para>
/// <para>
/// 纪律（P20）：本件**零语言依赖** —— 装载（<c>.ushar</c> 编译 / 配置解析链）由上层适配器完成，
/// 经 <see cref="FromSelections"/> / <see cref="FromDeclaration"/> 注入。
/// </para>
/// </summary>
public class StreamFrameSelector
{
    private readonly IReadOnlyDictionary<string, ExpressionSelectionPredicate> _predicates;
    private readonly string _declarationSubPath;
    private readonly string _defaultSelectionId;

    /// <summary>从已编译谓词表构造（上层装载器用）。</summary>
    /// <param name="predicates">选择名 → 谓词。</param>
    /// <param name="declarationSubPath">声明相对路径（原因可见化用）。</param>
    /// <param name="defaultSelectionId">缺省选择名。</param>
    protected StreamFrameSelector(
        IReadOnlyDictionary<string, ExpressionSelectionPredicate> predicates,
        string declarationSubPath,
        string defaultSelectionId)
    {
        _predicates = predicates;
        _declarationSubPath = declarationSubPath;
        _defaultSelectionId = defaultSelectionId;
    }

    /// <summary>已编译的帧级选择名集（对账/诊断用）。</summary>
    public IReadOnlyCollection<string> Ids => _predicates.Keys.ToArray();

    /// <summary>是否声明了某 selection 名。</summary>
    /// <param name="selectionId">选择名。</param>
    public bool IsKnown(string selectionId)
        => !string.IsNullOrWhiteSpace(selectionId) && _predicates.ContainsKey(selectionId);

    /// <summary>
    /// 解析 selectionId 为可求值谓词：未声明 ∨ 表达式不可识别 ⇒ <c>false</c> + 原因（⊥，不放行）。
    /// 空/未指定 ⇒ 缺省选择名。
    /// </summary>
    /// <param name="selectionId">选择名（可空）。</param>
    /// <param name="predicate">编译谓词（失败为 null）。</param>
    /// <param name="reason">不可用原因（成功为 null）。</param>
    public bool TryResolve(
        string? selectionId, out ExpressionSelectionPredicate? predicate, out string? reason)
    {
        var id = string.IsNullOrWhiteSpace(selectionId) ? _defaultSelectionId : selectionId!;
        if (!_predicates.TryGetValue(id, out var found))
        {
            predicate = null;
            reason = $"selection-undeclared：帧级选择 '{id}' 未在 {_declarationSubPath}.selections 声明（⊥，不放行）";
            return false;
        }

        if (!found.IsRecognized)
        {
            predicate = null;
            reason = $"selection-unparsed：帧级选择 '{id}' 表达式不可识别（⊥，不放行）";
            return false;
        }

        predicate = found;
        reason = null;
        return true;
    }

    /// <summary>由「选择名 → 表达式」表构造（缺缺省名时补全量谓词）；上层装载器（Usharp/JSON/配置）复用本入口。</summary>
    /// <param name="selections">选择名 → 表达式（可空表达式 = 未识别）。</param>
    /// <param name="declarationSubPath">声明相对路径（原因可见化用）。</param>
    /// <param name="defaultSelectionId">缺省选择名。</param>
    public static StreamFrameSelector FromSelections(
        IReadOnlyDictionary<string, string?> selections, string declarationSubPath, string defaultSelectionId)
    {
        var predicates = new Dictionary<string, ExpressionSelectionPredicate>(StringComparer.Ordinal);
        foreach (var pair in selections)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                predicates[pair.Key] = new ExpressionSelectionPredicate(pair.Key, pair.Value ?? string.Empty);
            }
        }

        predicates.TryAdd(defaultSelectionId, ExpressionSelectionPredicate.All());
        return new StreamFrameSelector(predicates, declarationSubPath, defaultSelectionId);
    }

    /// <summary>从声明 JSON 文本编译帧级选择（缺缺省名时补全量谓词；无声明 ⇒ 仅全量）。</summary>
    /// <param name="declarationJson">声明 JSON（可空）。</param>
    /// <param name="declarationSubPath">声明相对路径（原因可见化用）。</param>
    /// <param name="defaultSelectionId">缺省选择名。</param>
    public static StreamFrameSelector FromDeclaration(
        string? declarationJson, string declarationSubPath, string defaultSelectionId)
    {
        var predicates = new Dictionary<string, ExpressionSelectionPredicate>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(declarationJson))
        {
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(declarationJson!);
            }
            catch (System.Text.Json.JsonException)
            {
                parsed = null;
            }

            if (parsed is JsonObject root && root["selections"] is JsonObject selections)
            {
                foreach (var pair in selections)
                {
                    var expression = pair.Value is JsonValue value && value.TryGetValue<string>(out var text)
                        ? text
                        : null;
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        predicates[pair.Key] = new ExpressionSelectionPredicate(pair.Key, expression!);
                    }
                }
            }
        }

        predicates.TryAdd(defaultSelectionId, ExpressionSelectionPredicate.All());
        return new StreamFrameSelector(predicates, declarationSubPath, defaultSelectionId);
    }
}
