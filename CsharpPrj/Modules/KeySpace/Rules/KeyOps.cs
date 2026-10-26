using System.Globalization;
using System.Text;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.KeySpace.Runtime;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 键固有函数闭集（8 算子）：键的一切变换只经本类，调用方禁自建拼接/切片。
/// <para>
/// 设计（T2/T3）：族/面/轴/模板全部来自声明，算子只做装配与投影——
/// <c>Root</c>（族根）→ <c>With</c>（挂轴）→ <c>At</c>（选面）→ <c>Render</c>（投影）；
/// 反解 <c>TryParse</c> → <c>Value</c>（取值）；派生 <c>Derive</c>；过滤 <c>FilterPrefix</c>/<c>Filter</c>。
/// </para>
/// <para>
/// 纪律：本类不内置任何域词；族未声明 / 轴不在面内 / 缺轴 一律显式失败（不产半成品键）。
/// </para>
/// </summary>
public sealed class KeyOps
{
    private static readonly object HostGate = new();
    private static KeySpaceDeclaration? _hostDeclaration;
    private static KeyOps? _hostOps;

    private readonly KeyAlgebra _algebra;
    private readonly IReadOnlyDictionary<string, KeyFamily> _families;

    private KeyOps(IEnumerable<KeyFamily> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        var list = families.Where(static f => f is not null).ToArray();
        _families = list.ToDictionary(static f => f.Id, StringComparer.Ordinal);
        _algebra = new KeyAlgebra(list, NullKeyDiagnosticsSink.Instance);
    }

    /// <summary>以族声明构造算子实例（产品侧装配；框架层不持有域词）。</summary>
    public static KeyOps FromFamilies(IEnumerable<KeyFamily> families) => new(families);

    /// <summary>以键空间声明构造算子实例。</summary>
    public static KeyOps FromDeclaration(KeySpaceDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return new KeyOps(declaration.Families);
    }

    /// <summary>
    /// 组合根已装配的键空间（<see cref="KeySpaceHost"/>）；未装配即抛（禁回落到第二套拼接路径）。
    /// </summary>
    public static KeyOps Host
    {
        get
        {
            lock (HostGate)
            {
                var declaration = KeySpaceHost.Declaration
                    ?? throw new InvalidOperationException(
                        $"{KeyReasonCodes.OpsHostMissing}: 键空间声明未装配（须先在组合根 Configure）");
                if (!ReferenceEquals(declaration, _hostDeclaration))
                {
                    _hostDeclaration = declaration;
                    _hostOps = FromDeclaration(declaration);
                }

                return _hostOps!;
            }
        }
    }

    /// <summary>族根（默认存储面）。</summary>
    public KeyInstance Root(string familyId, KeySurface surface = KeySurface.Storage)
    {
        RequireFamily(familyId);
        return KeyInstance.Root(familyId, surface);
    }

    /// <summary>挂轴值（空值 = 移除；禁空轴名）。</summary>
    public KeyInstance With(KeyInstance key, KeyAxis axis, string? value)
    {
        RequireFamily(key.Family);
        if (axis.IsEmpty)
        {
            throw new ArgumentException($"{KeyReasonCodes.AxisNotInFamily}: 轴名为空", nameof(axis));
        }

        return key.With(axis, value);
    }

    /// <summary>挂轴值（整数）。</summary>
    public KeyInstance With(KeyInstance key, KeyAxis axis, int value)
        => With(key, axis, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>挂轴值（长整数）。</summary>
    public KeyInstance With(KeyInstance key, KeyAxis axis, long value)
        => With(key, axis, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>换面（坐标不变）。</summary>
    public KeyInstance At(KeyInstance key, KeySurface surface)
    {
        RequireFamily(key.Family);
        return key.At(surface);
    }

    /// <summary>投影为字符串（失败即抛；不产半成品键）。</summary>
    public string Render(KeyInstance key)
    {
        if (TryRender(key, out var rendered, out var diagnostic))
        {
            return rendered;
        }

        throw new InvalidOperationException(
            diagnostic is null ? "键构造失败" : $"{diagnostic.Code}: {diagnostic.Message}");
    }

    /// <summary>投影为字符串（缺轴/面未声明 ⇒ false + 诊断）。</summary>
    public bool TryRender(KeyInstance key, out string rendered, out KeyDiagnostic? diagnostic)
    {
        rendered = string.Empty;
        if (key.IsEmpty)
        {
            diagnostic = KeyDiagnostic.Error(KeyReasonCodes.Blank, KeyResolution.Empty, null, "空键实例");
            return false;
        }

        if (!_families.TryGetValue(key.Family, out var family))
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.FamilyUnresolved, KeyResolution.NotDeclared,
                KeyRef.Of(key.Family, string.Empty), $"键族未声明: {key.Family}");
            return false;
        }

        if (!KeySurfaceRules.EffectiveSurfaces(family).ContainsKey(key.Surface))
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.SurfaceMismatch, KeyResolution.NotDeclared,
                KeyRef.Of(key.Family, string.Empty), $"族 '{key.Family}' 未声明键面 '{key.Surface}'");
            return false;
        }

        if (!_algebra.TryBuild(key.Point, key.Surface, out var built, out diagnostic))
        {
            return false;
        }

        rendered = built.Value;
        return true;
    }

    /// <summary>反解：族 + 面 给定 ⇒ 不按分隔符猜（兼容模板只读命中）。</summary>
    public bool TryParse(
        string familyId, KeySurface surface, string? raw, out KeyInstance parsed, out KeyDiagnostic? diagnostic)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            diagnostic = KeyDiagnostic.Warn(KeyReasonCodes.Blank, KeyResolution.Empty, null, "空键文本");
            return false;
        }

        if (!_families.ContainsKey(familyId))
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.FamilyUnresolved, KeyResolution.NotDeclared,
                KeyRef.Of(familyId, raw!), $"键族未声明: {familyId}");
            return false;
        }

        if (!_algebra.TryParse(raw, surface, familyId, out var surfaceKey, out diagnostic))
        {
            return false;
        }

        parsed = new KeyInstance(surfaceKey.Family, surfaceKey.Surface, surfaceKey.Point);
        return true;
    }

    /// <summary>取轴值（缺席 ⇒ null）。</summary>
    public string? Value(KeyInstance key, KeyAxis axis) => key.Value(axis);

    /// <summary>族前缀（读侧族判据/前缀过滤唯一来源）。</summary>
    public string Prefix(string familyId, KeySurface surface = KeySurface.Storage)
    {
        var family = RequireFamily(familyId);
        var surfaces = KeySurfaceRules.EffectiveSurfaces(family);
        if (surfaces.TryGetValue(surface, out var spec) && !string.IsNullOrEmpty(spec.FamilyPrefix))
        {
            return spec.FamilyPrefix!;
        }

        if (!string.IsNullOrEmpty(family.FamilyPrefix))
        {
            return family.FamilyPrefix!;
        }

        var template = surfaces.TryGetValue(surface, out var known) ? known.CanonicalTemplate : family.Template;
        var builder = new StringBuilder();
        foreach (var segment in KeyTemplates.Segments(template))
        {
            if (segment.IsPlaceholder) break;
            builder.Append(segment.Text);
        }

        if (builder.Length == 0)
        {
            throw new InvalidOperationException(
                $"{KeyReasonCodes.PrefixSourceInvalid}: 族 '{familyId}' 面 '{surface}' 无前缀且模板无字面量起点");
        }

        return builder.ToString();
    }

    /// <summary>无约束过滤（族 × 面 × 作用域）。</summary>
    public KeyFilter Filter(KeyInstance scope, KeySurface surface = KeySurface.Storage)
    {
        RequireFamily(scope.Family);
        return KeyFilter.For(scope.Family, scope.Point, surface);
    }

    /// <summary>带轴约束的过滤（轴名由调用方给出；框架不内置域词）。</summary>
    public KeyFilter Filter(
        KeyInstance scope,
        KeyAxis? kindAxis,
        IReadOnlyList<string>? kinds,
        KeyAxis? roundAxis = null,
        IReadOnlyList<int>? rounds = null,
        KeySurface surface = KeySurface.Storage)
    {
        RequireFamily(scope.Family);
        return new KeyFilter(
            scope.Family,
            surface,
            scope.Point,
            kindAxis,
            kinds ?? Array.Empty<string>(),
            roundAxis,
            rounds ?? Array.Empty<int>());
    }

    /// <summary>族前缀直接产出过滤（读侧按前缀取集合；语义 = 同族同面）。</summary>
    public KeyFilter FilterPrefix(string familyId, KeySurface surface = KeySurface.Storage)
        => KeyFilter.For(familyId, KeyPoint.Empty, surface);

    /// <summary>族是否已声明（结构事实；不猜）。</summary>
    public bool IsDeclared(string familyId)
        => !string.IsNullOrWhiteSpace(familyId) && _families.ContainsKey(familyId);

    private KeyFamily RequireFamily(string familyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        if (!_families.TryGetValue(familyId, out var family))
        {
            throw new KeyNotFoundException($"{KeyReasonCodes.FamilyUnresolved}: 键族未声明: {familyId}");
        }

        return family;
    }
}
