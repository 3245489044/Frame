using System;
using System.Collections.Generic;
using System.Linq;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.KeySpace.Runtime;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 键面投影实现（GP-1 聚拢点）：**零逻辑**转发——归一/构造全部委托 <see cref="KeyAlgebra"/>，
/// 只读兼容形态全部取自 <see cref="KeySurfaceRules.Readable"/>（即声明面 <c>readOnly</c>）。
/// <para>
/// 存在理由：同一实体此前在存储/删除/探针三处各写「候选键列表」，形态由代码而非声明决定。
/// 本类把该事实收敛为「声明 → 形态」一处投影；调用方只传族 + 身份。
/// </para>
/// </summary>
public sealed class KeyProjection : IKeyProjection
{
    private static readonly object HostGate = new();
    private static KeySpaceDeclaration? _hostDeclaration;
    private static KeyProjection? _host;

    private readonly KeyAlgebra _algebra;
    private readonly IReadOnlyDictionary<string, KeyFamily> _families;

    /// <summary>以族声明构造（无域词；族由调用方注入）。</summary>
    public KeyProjection(IEnumerable<KeyFamily> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        var list = families.Where(static family => family is not null).ToArray();
        _families = list.ToDictionary(static family => family.Id, StringComparer.Ordinal);
        _algebra = new KeyAlgebra(list);
    }

    /// <summary>以键空间声明构造。</summary>
    public KeyProjection(KeySpaceDeclaration declaration)
        : this((declaration ?? throw new ArgumentNullException(nameof(declaration))).Families)
    {
    }

    /// <summary>
    /// 组合根已装配的投影（<see cref="KeySpaceHost"/>）；未装配即抛（禁回落第二套拼接路径）。
    /// </summary>
    public static KeyProjection Host
    {
        get
        {
            lock (HostGate)
            {
                var declaration = KeySpaceHost.Declaration
                    ?? throw new InvalidOperationException(
                        $"{KeyReasonCodes.ProjectionHostMissing}: 键空间声明未装配（须先在组合根 Configure）");
                if (!ReferenceEquals(declaration, _hostDeclaration))
                {
                    _hostDeclaration = declaration;
                    _host = new KeyProjection(declaration);
                }

                return _host!;
            }
        }
    }

    /// <inheritdoc />
    public SurfaceKey Project(string family, string raw, KeySurface surface)
    {
        if (TryProject(family, raw, surface, out var key))
        {
            return key;
        }

        throw new InvalidOperationException(
            $"{KeyReasonCodes.Unparsable}: 键 '{raw}' 无法投影到 {family}/{surface}");
    }

    /// <inheritdoc />
    public bool TryProject(string family, string raw, KeySurface surface, out SurfaceKey key)
    {
        key = SurfaceKey.Empty;
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(family)) return false;
        if (!_families.ContainsKey(family)) return false;
        if (!_algebra.TryNormalize(raw, family, out var normalized, out _)) return false;
        if (!_algebra.TryBuild(normalized.Point, surface, out var built, out _)) return false;
        key = built;
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Candidates(string family, string raw, KeySurface surface)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(family))
        {
            return Array.Empty<string>();
        }

        if (!_families.TryGetValue(family, out var declared)) return Array.Empty<string>();
        if (!_algebra.TryNormalize(raw, family, out var normalized, out _)) return Array.Empty<string>();

        var candidates = new List<string>();
        foreach (var (grammar, _) in KeySurfaceRules.Readable(declared, surface))
        {
            if (!grammar.TryBuild(normalized.Point.Values, out var rendered, out _)) continue;
            if (rendered.Length == 0) continue;
            if (!candidates.Contains(rendered, StringComparer.Ordinal)) candidates.Add(rendered);
        }

        if (candidates.Count == 0 && _algebra.TryBuild(normalized.Point, surface, out var canonical, out _))
        {
            candidates.Add(canonical.Value);
        }

        return candidates;
    }

    /// <inheritdoc />
    public bool SameEntity(string left, string right, string family)
    {
        if (string.Equals(left, right, StringComparison.Ordinal)) return true;
        if (!_algebra.TryNormalize(left, family, out var leftKey, out _)) return false;
        if (!_algebra.TryNormalize(right, family, out var rightKey, out _)) return false;
        if (!string.Equals(leftKey.Point.Family, rightKey.Point.Family, StringComparison.Ordinal)) return false;

        var leftValues = leftKey.Point.Values;
        var rightValues = rightKey.Point.Values;
        if (leftValues.Count != rightValues.Count) return false;
        foreach (var pair in leftValues)
        {
            if (!rightValues.TryGetValue(pair.Key, out var value)) return false;
            if (!string.Equals(value, pair.Value, StringComparison.Ordinal)) return false;
        }

        return true;
    }
}
