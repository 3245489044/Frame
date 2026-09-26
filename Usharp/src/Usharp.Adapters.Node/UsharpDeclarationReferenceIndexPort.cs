using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Node.Spec;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Node;

/// <summary>
/// 引用字段索引端口的**语言装配**（W1⑤）：字段面与反向引用均由声明面 <c>[Ref]</c> 行产出（零域内硬编码清单）。
/// <para>
/// 域分离（勿与「节点载荷引用字段面」混同 —— 两族字段不同源，禁互替）：
/// ① 本端口索引**声明面引用边**（<c>Config\nodes\hierarchy-refs.ushar</c> 的 <c>[Ref]</c>：
/// <c>space</c>/<c>key</c>/<c>src</c>/<c>tgt</c>/<c>intent</c>/<c>edge</c>/<c>scope</c>）；
/// ② 节点定义 JSON 的**载荷**引用字段（<c>NodeReferenceFields</c>：AddConfigAddKey / ParameterKeys / …）
/// 属节点域**属性面**，其上移依赖 U18 裁决（W6）—— 本端口不覆盖、不替代，亦不构成第二实现。
/// </para>
/// <para>
/// 派生口径：字段面 = **本编译实际用到**的已注册 `[Ref]` 字段（声明未用 ⇒ 不出现）；反向引用 = 该键出现在
/// 任一声明的 <c>key</c>/<c>src</c>/<c>tgt</c> 上的归因位置（<c>文件:行</c>）。
/// </para>
/// </summary>
public sealed class UsharpDeclarationReferenceIndexPort : IReferenceIndexPort
{
    /// <summary>语言注册的 `[Ref]` 字段词表（唯一权威 = <see cref="UsharpLanguage"/>；此处不新造清单）。</summary>
    private static readonly string[] RefFieldVocabulary =
    {
        UsharpLanguage.FieldSpace,
        UsharpLanguage.FieldKey,
        UsharpLanguage.FieldSrc,
        UsharpLanguage.FieldTgt,
        UsharpLanguage.FieldIntent,
        UsharpLanguage.FieldEdge,
        UsharpLanguage.FieldScope,
    };

    private readonly Dictionary<string, List<string>> _reverse = new(StringComparer.Ordinal);

    /// <param name="compilation">声明面编译产物（<c>hierarchy-refs.ushar</c> 等）。</param>
    public UsharpDeclarationReferenceIndexPort(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var refs = compilation.Refs;
        ReferenceFields = DeriveFields(refs);

        foreach (var reference in refs)
        {
            var location = $"{reference.SourceFile}:{reference.LineNumber}";
            Index(reference.Key, location);
            Index(reference.Source, location);
            Index(reference.Target, location);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ReferenceFields { get; }

    /// <summary>已建立反向索引的键（诊断用；字典序）。</summary>
    public IReadOnlyList<string> IndexedKeys => _reverse.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<string> ReverseReferences(string key) =>
        _reverse.TryGetValue(key, out var list) ? list.ToArray() : Array.Empty<string>();

    /// <summary>字段面派生：本编译实际 populate 的已注册字段（序 = <see cref="RefFieldVocabulary"/>）。</summary>
    private static IReadOnlyList<string> DeriveFields(IReadOnlyList<UsharpRefDecl> refs)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in refs)
        {
            if (!string.IsNullOrWhiteSpace(reference.SpaceId)) used.Add(UsharpLanguage.FieldSpace);
            if (!string.IsNullOrWhiteSpace(reference.Key)) used.Add(UsharpLanguage.FieldKey);
            if (!string.IsNullOrWhiteSpace(reference.Source)) used.Add(UsharpLanguage.FieldSrc);
            if (!string.IsNullOrWhiteSpace(reference.Target)) used.Add(UsharpLanguage.FieldTgt);
            if (!string.IsNullOrWhiteSpace(reference.Intent)) used.Add(UsharpLanguage.FieldIntent);
            if (!string.IsNullOrWhiteSpace(reference.Edge)) used.Add(UsharpLanguage.FieldEdge);
            if (reference.Scope is not null) used.Add(UsharpLanguage.FieldScope);
        }

        return RefFieldVocabulary.Where(used.Contains).ToArray();
    }

    private void Index(string? key, string location)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!_reverse.TryGetValue(key, out var list))
        {
            _reverse[key] = list = new List<string>();
        }

        if (!list.Contains(location, StringComparer.Ordinal))
        {
            list.Add(location);
        }
    }
}
