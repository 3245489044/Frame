using System;
using System.Collections.Generic;
using System.Text;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 规则执行体：把一条 <see cref="RuleSpec"/> 适配为内核 <see cref="IGateRule{TModel}"/>。
/// <para>
/// 执行 = 作用域定位（<c>root</c> 或段序；集合段逐元素实例化）→ 逐实例求值判据 → 违规按规则严重度上报。
/// <b>不抛异常</b>（对齐闸A「全量失败可见」）；无作用域实例时不产生诊断（无实例 = 无证据，不假装通过/不假装失败）。
/// </para>
/// </summary>
/// <typeparam name="TModel">领域模型。</typeparam>
internal sealed class RuleSpecGateRule<TModel> : IGateRule<TModel>
{
    private static readonly IReadOnlyList<string> NoTexts = Array.Empty<string>();

    private readonly RuleSpec _spec;
    private readonly IRuleObjectModel<TModel> _objectModel;
    private readonly PredicateEvaluator<TModel> _evaluator;

    /// <summary>构筑规则执行体。</summary>
    /// <param name="spec">规则数据（已通过构筑期校验）。</param>
    /// <param name="objectModel">取值端口。</param>
    /// <param name="evaluator">求值内核。</param>
    public RuleSpecGateRule(
        RuleSpec spec,
        IRuleObjectModel<TModel> objectModel,
        PredicateEvaluator<TModel> evaluator)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _objectModel = objectModel ?? throw new ArgumentNullException(nameof(objectModel));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    /// <summary>规则名（诊断归因与去重键）。</summary>
    public string Name => _spec.Name;

    /// <summary>运行规则：逐作用域实例求值，违规上报。</summary>
    /// <param name="model">领域模型。</param>
    /// <param name="sink">诊断汇集器。</param>
    public void Check(TModel model, IConversionDiagnosticSink sink)
    {
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        var scopeRoot = _objectModel.Root(model);
        var instances = _spec.Scope.IsRoot
            ? new List<ScopeInstance> { new ScopeInstance(scopeRoot, "root") }
            : Traverse(model, scopeRoot, 0, new StringBuilder());

        for (var i = 0; i < instances.Count; i++)
        {
            if (_evaluator.Evaluate(_spec.Require, model, instances[i].Node)) continue;

            sink.Report(BuildDiagnostic(model, instances[i]));
        }
    }

    /// <summary>作用域遍历：按段序展开（集合段逐元素，单实例段取成员）。</summary>
    private List<ScopeInstance> Traverse(TModel model, ConversionNode? node, int index, StringBuilder path)
    {
        var result = new List<ScopeInstance>();
        if (index >= _spec.Scope.Segments.Count)
        {
            result.Add(new ScopeInstance(node, path.Length == 0 ? "root" : path.ToString()));
            return result;
        }

        if (node is null) return result;

        var segment = _spec.Scope.Segments[index];
        if (segment.IsCollection)
        {
            var elements = _objectModel.Enumerate(node, segment.Name);
            for (var i = 0; i < elements.Count; i++)
            {
                var child = Push(path, segment.Name + "[" + i + "]");
                result.AddRange(Traverse(model, elements[i], index + 1, child));
            }

            return result;
        }

        if (!RulePath.TryParse(segment.Name, out var member, out _)) return result;
        if (!member.TryResolve(node, out var value) || value is null) return result;

        return Traverse(model, value, index + 1, Push(path, segment.Name));
    }

    private static StringBuilder Push(StringBuilder path, string segment)
    {
        var next = new StringBuilder(path.Length + segment.Length + 1);
        if (path.Length > 0) next.Append(path).Append('.');
        next.Append(segment);
        return next;
    }

    /// <summary>构造诊断（消息模板占位 <c>{path}</c>/<c>{id}</c>；定位优先 <c>SpanHint</c>）。</summary>
    private ConversionDiagnostic BuildDiagnostic(TModel model, ScopeInstance instance)
    {
        var message = RenderMessage(model, instance);
        var path = string.IsNullOrWhiteSpace(_spec.SpanHint) ? instance.Path : _spec.SpanHint!;
        var span = _objectModel.SpanOf(model, path);

        return _spec.Severity == ConversionSeverity.Error
            ? ConversionDiagnostic.Error(_spec.Code, message, span)
            : _spec.Severity == ConversionSeverity.Warning
                ? ConversionDiagnostic.Warning(_spec.Code, message, span)
                : ConversionDiagnostic.Info(_spec.Code, message, span);
    }

    private string RenderMessage(TModel model, ScopeInstance instance)
    {
        var template = _spec.Message ?? string.Empty;
        if (template.IndexOf('{') < 0) return template;

        var rendered = template.Replace("{path}", instance.Path);

        if (rendered.IndexOf("{id}", StringComparison.Ordinal) >= 0)
        {
            var id = ResolveIdText(instance.Node);
            rendered = rendered.Replace("{id}", id);
        }

        return rendered;
    }

    /// <summary>取实例的 <c>id</c> 文本（无则空串；供 <c>{id}</c> 占位）。</summary>
    private static string ResolveIdText(ConversionNode? node)
    {
        if (node is not ConversionObject obj) return string.Empty;

        return obj.GetString("id") ?? string.Empty;
    }

    /// <summary>作用域实例（节点 + 路径文本）。</summary>
    private readonly struct ScopeInstance
    {
        public ScopeInstance(ConversionNode? node, string path)
        {
            Node = node;
            Path = path;
        }

        /// <summary>作用域节点。</summary>
        public ConversionNode? Node { get; }

        /// <summary>作用域路径文本（诊断可读性）。</summary>
        public string Path { get; }
    }
}
