using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 通用取值端口实现：领域模型**直接以内核 IR 为值域**时零适配接入
/// （构造器传两个选择器：<c>rootSelector</c> 取 IR 根、<c>spanResolver</c> 取路径定位）。
/// <para>路径解析唯一实现 = <see cref="RulePath"/>（本类不复制 dot-path 解析逻辑）。</para>
/// </summary>
/// <typeparam name="TModel">领域模型（如 NMD 文档）。</typeparam>
public sealed class ConversionObjectRuleModel<TModel> : IRuleObjectModel<TModel>
{
    private static readonly IReadOnlyList<ConversionNode> NoNodes = Array.Empty<ConversionNode>();

    private readonly Func<TModel, ConversionNode?> _rootSelector;
    private readonly Func<TModel, string, SourceSpan>? _spanResolver;

    /// <summary>构筑取值端口。</summary>
    /// <param name="rootSelector">IR 根选择器（不得为 null）。</param>
    /// <param name="spanResolver">路径定位选择器（可空 = 无定位，返回 <see cref="SourceSpan.None"/>）。</param>
    public ConversionObjectRuleModel(
        Func<TModel, ConversionNode?> rootSelector,
        Func<TModel, string, SourceSpan>? spanResolver = null)
    {
        _rootSelector = rootSelector ?? throw new ArgumentNullException(nameof(rootSelector));
        _spanResolver = spanResolver;
    }

    /// <summary>IR 根（选择器返回 null 时 = 空模型）。</summary>
    /// <param name="model">领域模型。</param>
    public ConversionNode? Root(TModel model) => _rootSelector(model);

    /// <summary>节点相对路径取值（路径文法非法 ⇒ 取值缺失；文法在构筑期已校验）。</summary>
    /// <param name="node">当前节点。</param>
    /// <param name="relPath">相对路径。</param>
    /// <param name="value">out：取值。</param>
    public bool TryGet(ConversionNode? node, string relPath, out ConversionNode? value)
    {
        value = null;
        return RulePath.TryParse(relPath, out var path, out _) && path.TryResolve(node, out value);
    }

    /// <summary>
    /// 集合枚举：路径解析后**展开一层数组**——故 <c>modules</c>（指向数组）与
    /// <c>modules[*]</c>（通配展开）两种写法得到同一元素序列，避免调用方记忆两套语义。
    /// </summary>
    /// <param name="node">当前节点。</param>
    /// <param name="collectionPath">集合路径。</param>
    public IReadOnlyList<ConversionNode> Enumerate(ConversionNode? node, string collectionPath)
    {
        if (!RulePath.TryParse(collectionPath, out var path, out _)) return NoNodes;

        var resolved = path.ResolveAll(node);
        var elements = new List<ConversionNode>();
        for (var i = 0; i < resolved.Count; i++)
        {
            if (resolved[i] is ConversionArray array) elements.AddRange(array.Items);
            else elements.Add(resolved[i]);
        }

        return elements;
    }

    /// <summary>路径定位（无选择器 ⇒ <see cref="SourceSpan.None"/>）。</summary>
    /// <param name="model">领域模型。</param>
    /// <param name="path">路径。</param>
    public SourceSpan SpanOf(TModel model, string path)
        => _spanResolver is null ? SourceSpan.None : _spanResolver(model, path);
}
