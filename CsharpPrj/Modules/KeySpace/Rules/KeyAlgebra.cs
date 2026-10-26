using System;
using System.Collections.Generic;
using System.Linq;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 键空间代数实现：面声明 → 文法 → 构造/解析/归一/转换。
/// <para>本类不持有域词；具体族由调用方以声明注入。词法一律委托 Modules.Parsing.Keys。</para>
/// </summary>
public sealed class KeyAlgebra : IKeyAlgebra
{
    private sealed record SurfaceGrammar(
        KeySurfaceSpec Spec,
        KeyGrammar Canonical,
        IReadOnlyList<(KeyGrammar Grammar, bool Compatible)> Readable);

    private sealed record FamilyEntry(
        KeyFamily Family,
        IReadOnlyList<string> IdentityKeys,
        IReadOnlyDictionary<string, KeyAxisSpec> Axes,
        IReadOnlyDictionary<KeySurface, SurfaceGrammar> Surfaces);

    private sealed record Match(
        string Family,
        KeyPoint Point,
        bool Compatible,
        string Template,
        int Score);

    private static readonly KeySurface[] NormalizeOrder =
    {
        KeySurface.Endpoint,
        KeySurface.Storage,
        KeySurface.Identity,
        KeySurface.Display,
        KeySurface.Level,
    };

    private readonly Dictionary<string, FamilyEntry> _families;
    private readonly IKeyDiagnosticsSink _diagnostics;

    /// <summary>以族声明构建代数；重复族 / 空族 id / 无面 ⇒ 构造期抛出。</summary>
    public KeyAlgebra(IEnumerable<KeyFamily> families, IKeyDiagnosticsSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(families);
        _diagnostics = diagnostics ?? NullKeyDiagnosticsSink.Instance;
        _families = new Dictionary<string, FamilyEntry>(StringComparer.Ordinal);

        foreach (var family in families)
        {
            ArgumentNullException.ThrowIfNull(family);
            if (string.IsNullOrWhiteSpace(family.Id))
            {
                throw new ArgumentException("键族声明缺少 id", nameof(families));
            }

            if (_families.ContainsKey(family.Id))
            {
                throw new InvalidOperationException($"键族重复: {family.Id}");
            }

            var surfaces = new Dictionary<KeySurface, SurfaceGrammar>();
            foreach (var spec in KeySurfaceRules.EffectiveSurfaces(family).Values)
            {
                var canonical = KeySurfaceRules.Canonical(family, spec.Surface);
                surfaces[spec.Surface] = new SurfaceGrammar(
                    spec, canonical, KeySurfaceRules.Readable(family, spec.Surface));
            }

            if (surfaces.Count == 0)
            {
                throw new InvalidOperationException($"键族 '{family.Id}' 未声明任何键面");
            }

            var identity = family.IdentityKeys;
            if (identity is null || identity.Count == 0)
            {
                identity = surfaces.Values.First().Canonical.ValueKeys;
            }

            _families[family.Id] = new FamilyEntry(
                family, identity, BuildAxes(family, surfaces.Values), surfaces);
        }
    }

    /// <inheritdoc />
    public SurfaceKey Build(KeyPoint point, KeySurface surface)
    {
        if (TryBuild(point, surface, out var key, out var diagnostic))
        {
            return key;
        }

        throw new InvalidOperationException(diagnostic is null ? "键构造失败" : diagnostic.Message);
    }

    /// <inheritdoc />
    public bool TryBuild(
        KeyPoint point, KeySurface surface, out SurfaceKey key, out KeyDiagnostic? diagnostic)
    {
        key = SurfaceKey.Empty;
        diagnostic = null;

        if (point is null || string.IsNullOrWhiteSpace(point.Family))
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.NotDeclared, KeyResolution.NotDeclared, null, "键坐标缺少族");
            return false;
        }

        if (!_families.TryGetValue(point.Family, out var entry))
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.FamilyUnresolved, KeyResolution.NotDeclared,
                KeyRef.Of(point.Family, surface.ToString()), $"未声明族 '{point.Family}'");
            return false;
        }

        if (!entry.Surfaces.TryGetValue(surface, out var grammar))
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.SurfaceMismatch, KeyResolution.NotDeclared,
                KeyRef.Of(point.Family, surface.ToString()), $"族 '{point.Family}' 未声明面 '{surface}'");
            return false;
        }

        foreach (var name in entry.IdentityKeys)
        {
            if (string.IsNullOrWhiteSpace(point.Text(name)))
            {
                diagnostic = KeyDiagnostic.Error(
                    KeyReasonCodes.EmptyIdentity, KeyResolution.Empty,
                    KeyRef.Of(point.Family, surface.ToString()), $"身份占位 '{name}' 为空");
                return false;
            }
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in grammar.Canonical.ValueKeys)
        {
            var text = point.Text(name);
            if (!string.IsNullOrEmpty(text))
            {
                values[name] = text;
            }
        }

        try
        {
            var value = grammar.Canonical.Build(values);
            key = new SurfaceKey(point.Family, surface, point, value);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.Unparsable, KeyResolution.Unparsable,
                KeyRef.Of(point.Family, surface.ToString()), ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public string BuildLenient(KeyPoint point, KeySurface surface)
    {
        if (point is null || !_families.TryGetValue(point.Family, out var entry) ||
            !entry.Surfaces.TryGetValue(surface, out var grammar))
        {
            throw new InvalidOperationException(
                $"宽松构造失败：族 '{point?.Family}' 面 '{surface}' 未声明");
        }

        var ordered = grammar.Canonical.ValueKeys
            .Select(name => point.Text(name))
            .Select(text => string.IsNullOrEmpty(text) ? null : text)
            .ToArray();
        return grammar.Canonical.BuildLenient(ordered);
    }

    /// <inheritdoc />
    public bool TryParse(
        string? raw, KeySurface surface, string? familyHint,
        out SurfaceKey key, out KeyDiagnostic? diagnostic)
        => TryParseCore(raw, surface, familyHint, report: true, out key, out diagnostic);

    /// <summary>解析并返回命中证据（模板 / 只读兼容标记 / 特异性得分）。</summary>
    public bool TryParseDetailed(
        string? raw, KeySurface surface, string? familyHint,
        out SurfaceKey key, out KeyParseMatch? match, out KeyDiagnostic? diagnostic)
        => TryParseCore(raw, surface, familyHint, null, out key, out match, out diagnostic, report: true);

    /// <summary>
    /// 作用域解析：已知部分轴（如 <c>workflowId</c>）时按结构事实过滤候选；
    /// 反解值与已知轴不一致 ⇒ <c>key.scope-mismatch</c>（禁前缀切片猜测）。
    /// </summary>
    public bool TryParseScoped(
        string? raw, KeySurface surface, string? familyHint,
        IReadOnlyDictionary<string, string> knownAxes,
        out SurfaceKey key, out KeyParseMatch? match, out KeyDiagnostic? diagnostic)
        => TryParseCore(raw, surface, familyHint, knownAxes, out key, out match, out diagnostic, report: true);

    private bool TryParseCore(
        string? raw, KeySurface surface, string? familyHint, bool report,
        out SurfaceKey key, out KeyDiagnostic? diagnostic)
        => TryParseCore(raw, surface, familyHint, null, out key, out _, out diagnostic, report);

    private bool TryParseCore(
        string? raw, KeySurface surface, string? familyHint,
        IReadOnlyDictionary<string, string>? knownAxes,
        out SurfaceKey key, out KeyParseMatch? match, out KeyDiagnostic? diagnostic, bool report)
    {
        key = SurfaceKey.Empty;
        match = null;
        diagnostic = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            diagnostic = KeyDiagnostic.Warn(
                KeyReasonCodes.Blank, KeyResolution.Empty, null, "空白键");
            if (report) _diagnostics.Report(diagnostic);
            return false;
        }

        var matches = new List<Match>();
        KeyDiagnostic? axisFailure = null;
        var axisFailureScore = 0;
        if (!string.IsNullOrWhiteSpace(familyHint))
        {
            if (_families.TryGetValue(familyHint!, out var entry))
            {
                Collect(entry, surface, raw!, matches, ref axisFailure, ref axisFailureScore);
            }
        }
        else
        {
            foreach (var entry in _families.Values)
            {
                if (!HasFamilyPrefix(entry, surface)) continue;
                Collect(entry, surface, raw!, matches, ref axisFailure, ref axisFailureScore);
            }
        }

        if (matches.Count == 0)
        {
            // 形状/词表命中但轴约束不合法 ⇒ 报轴诊断（比「无模板匹配」更具可复算性）。
            diagnostic = axisFailure ?? KeyDiagnostic.Warn(
                KeyReasonCodes.ParseUnmatched, KeyResolution.Unparsable,
                string.IsNullOrWhiteSpace(familyHint) ? null : KeyRef.Of(familyHint!, surface.ToString()),
                $"键面 '{surface}' 无模板匹配（key.parse-unmatched）");
            if (report) _diagnostics.Report(diagnostic);
            return false;
        }

        if (knownAxes is { Count: > 0 })
        {
            var scoped = matches
                .Where(candidate => knownAxes.All(pair =>
                    string.Equals(candidate.Point.Text(pair.Key), pair.Value, StringComparison.Ordinal)))
                .ToList();
            if (scoped.Count == 0)
            {
                diagnostic = KeyDiagnostic.Error(
                    KeyReasonCodes.ScopeMismatch, KeyResolution.Ambiguous,
                    string.IsNullOrWhiteSpace(familyHint) ? null : KeyRef.Of(familyHint!, raw!),
                    $"键面 '{surface}' 已知轴与反解值不一致：{DescribeKnownAxes(knownAxes)}");
                if (report) _diagnostics.Report(diagnostic);
                return false;
            }

            matches = scoped;
        }

        var families = matches
            .Select(static candidate => candidate.Family)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (families.Count > 1)
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.FamilyAmbiguous, KeyResolution.Ambiguous, null,
                $"键面 '{surface}' 命中多个族：禁择一（{string.Join(", ", families)}）");
            if (report) _diagnostics.Report(diagnostic);
            return false;
        }

        var best = matches.Max(static candidate => candidate.Score);
        // 更具体的模板因轴约束不合格而落选时，禁由更宽的模板吞掉（否则非法键被静默降级为宽容器）。
        if (axisFailure is not null && axisFailureScore > best)
        {
            diagnostic = axisFailure;
            if (report) _diagnostics.Report(diagnostic);
            return false;
        }

        var top = matches.Where(candidate => candidate.Score == best).ToList();
        if (top.Count > 1)
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.ParseAmbiguous, KeyResolution.Ambiguous,
                KeyRef.Of(families[0], raw!),
                $"键面 '{surface}' 多个模板同分（歧义失败）：{string.Join(" / ", top.Select(static candidate => candidate.Template))}");
            if (report) _diagnostics.Report(diagnostic);
            return false;
        }

        var selected = top[0];
        key = new SurfaceKey(selected.Family, surface, selected.Point, raw!);
        match = new KeyParseMatch(selected.Template, selected.Compatible, selected.Score);
        if (selected.Compatible)
        {
            diagnostic = KeyDiagnostic.Info(
                KeyReasonCodes.LegacyForm, KeyResolution.Resolved,
                KeyRef.Of(selected.Family, raw!), $"命中只读兼容形态：{selected.Template}");
            if (report) _diagnostics.Report(diagnostic);
        }

        return true;
    }

    private static string DescribeKnownAxes(IReadOnlyDictionary<string, string> knownAxes)
        => string.Join(", ", knownAxes.Select(static pair => $"{pair.Key}={pair.Value}"));

    /// <summary>
    /// 候选枚举：**全部**模板参与（不预排序、不提前返回）；轴约束不合格的候选记入诊断但不中断。
    /// 胜负由 <see cref="Score(string, IReadOnlyDictionary{string, KeyAxisSpec})"/> 决定。
    /// </summary>
    private static void Collect(
        FamilyEntry entry,
        KeySurface surface,
        string raw,
        List<Match> matches,
        ref KeyDiagnostic? axisFailure,
        ref int axisFailureScore)
    {
        if (!entry.Surfaces.TryGetValue(surface, out var grammar)) return;

        foreach (var (candidate, compatible) in grammar.Readable)
        {
            if (!candidate.TryParse(raw, out var values)) continue;
            var score = Score(candidate.Template, entry.Axes);
            if (!TryCheckAxes(entry, candidate, values, raw, surface, out var failure))
            {
                if (failure is not null && score > axisFailureScore)
                {
                    axisFailure = failure;
                    axisFailureScore = score;
                }

                continue;
            }

            var point = new KeyPoint(entry.Family.Id, values.Present());
            matches.Add(new Match(entry.Family.Id, point, compatible, candidate.Template, score));
        }
    }

    private static bool TryCheckAxes(
        FamilyEntry entry,
        KeyGrammar grammar,
        KeyValues values,
        string raw,
        KeySurface surface,
        out KeyDiagnostic? diagnostic)
    {
        diagnostic = null;
        foreach (var spec in entry.Axes.Values)
        {
            if (!values.TryGetText(spec.Name, out var value) || value.Length == 0)
            {
                if (spec.Required)
                {
                    diagnostic = KeyDiagnostic.Error(
                        KeyReasonCodes.AxisMissing, KeyResolution.Unparsable,
                        KeyRef.Of(entry.Family.Id, raw),
                        $"键面 '{surface}' 必填轴缺失：{spec.Name}（模板 {grammar.Template}）");
                    return false;
                }

                continue;
            }

            if (!spec.Allows(value))
            {
                diagnostic = KeyDiagnostic.Error(
                    KeyReasonCodes.AxisInvalid, KeyResolution.Unparsable,
                    KeyRef.Of(entry.Family.Id, raw),
                    $"键面 '{surface}' 轴 '{spec.Name}' 值不在词表：{value}（模板 {grammar.Template}）");
                return false;
            }
        }

        var mismatched = grammar.ShapeMismatches(values.ToDictionary());
        if (mismatched.Count > 0)
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.AxisInvalid, KeyResolution.Unparsable,
                KeyRef.Of(entry.Family.Id, raw),
                $"键面 '{surface}' 轴形状不合：{string.Join(",", mismatched)}（模板 {grammar.Template}）");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 特异性打分（确定性算法）：字面量越具体、形状/词表约束越多、可选面越窄 ⇒ 分越高。
    /// 并列最高分不由顺序裁决，一律判歧义（<c>key.parse-ambiguous</c>）。
    /// </summary>
    private static int Score(string template, IReadOnlyDictionary<string, KeyAxisSpec> axes)
    {
        var literalChars = 0;
        var literalSegments = 0;
        var shaped = 0;
        var enumerated = 0;
        var placeholders = 0;
        var regions = new HashSet<int>();

        foreach (var segment in KeyTemplates.Segments(template))
        {
            if (!segment.IsPlaceholder)
            {
                literalChars += segment.Text.Length;
                if (segment.Text.Length > 0) literalSegments++;
                continue;
            }

            placeholders++;
            if (segment.Shape != KeyValueShape.Text) shaped++;
            if (axes.TryGetValue(segment.Text, out var spec) && spec.HasAllowedValues) enumerated++;
            if (segment.Region > 0) regions.Add(segment.Region);
        }

        return (literalChars * 100) + (literalSegments * 10) + (shaped * 5) + (enumerated * 8)
               - (regions.Count * 3) - placeholders;
    }

    /// <summary>
    /// 轴声明装配校验：轴名唯一 / 形状与模板段一致（单源 = 模板）/ 每个轴至少出现在一个模板。
    /// </summary>
    private static IReadOnlyDictionary<string, KeyAxisSpec> BuildAxes(
        KeyFamily family,
        IEnumerable<SurfaceGrammar> surfaces)
    {
        var declared = family.Axes ?? Array.Empty<KeyAxisSpec>();
        var axes = new Dictionary<string, KeyAxisSpec>(StringComparer.Ordinal);
        foreach (var spec in declared)
        {
            if (string.IsNullOrWhiteSpace(spec.Name))
            {
                throw new InvalidOperationException($"键族 '{family.Id}' 轴名为空");
            }

            if (!axes.TryAdd(spec.Name, spec))
            {
                throw new InvalidOperationException($"键族 '{family.Id}' 轴重复声明: {spec.Name}");
            }
        }

        if (axes.Count == 0)
        {
            return axes;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var surface in surfaces)
        {
            foreach (var (grammar, _) in surface.Readable)
            {
                foreach (var segment in grammar.Segments)
                {
                    if (!segment.IsPlaceholder) continue;
                    seen.Add(segment.Text);
                    if (!axes.TryGetValue(segment.Text, out var spec)) continue;

                    if (spec.Shape != KeyValueShape.Text && segment.Shape != spec.Shape)
                    {
                        throw new InvalidOperationException(
                            $"键族 '{family.Id}' 轴 '{spec.Name}' 形状冲突：声明 {spec.Shape} ≠ 模板 '{grammar.Template}' 的 {segment.Shape}");
                    }

                    if (spec.HasAllowedValues && segment.Shape is KeyValueShape.Number or KeyValueShape.Digits)
                    {
                        throw new InvalidOperationException(
                            $"键族 '{family.Id}' 轴 '{spec.Name}' 词表约束与数值形状冲突（模板 '{grammar.Template}'）");
                    }
                }
            }
        }

        foreach (var name in axes.Keys)
        {
            if (!seen.Contains(name))
            {
                throw new InvalidOperationException($"键族 '{family.Id}' 轴 '{name}' 未出现在任何模板中");
            }
        }

        return axes;
    }

    private static bool HasFamilyPrefix(FamilyEntry entry, KeySurface surface)
    {
        if (!entry.Surfaces.TryGetValue(surface, out var grammar)) return false;
        var prefix = grammar.Spec.FamilyPrefix ?? entry.Family.FamilyPrefix ?? string.Empty;
        return prefix.Length > 0;
    }

    /// <inheritdoc />
    public SurfaceKey Normalize(string? raw, string? familyHint)
    {
        if (TryNormalize(raw, familyHint, out var key, out var diagnostic))
        {
            return key;
        }

        throw new InvalidOperationException(
            diagnostic is null ? "键归一失败" : $"{diagnostic.Code}: {diagnostic.Message}");
    }

    /// <inheritdoc />
    public bool TryNormalize(
        string? raw, string? familyHint, out SurfaceKey key, out KeyDiagnostic? diagnostic)
    {
        key = SurfaceKey.Empty;
        diagnostic = null;

        foreach (var surface in NormalizeOrder)
        {
            if (!TryParseCore(raw, surface, familyHint, report: false, out var parsed, out _)) continue;
            if (TryBuild(parsed.Point, surface, out var canonical, out var buildDiagnostic))
            {
                key = canonical;
                return true;
            }

            diagnostic ??= buildDiagnostic;
        }

        if (diagnostic is null)
        {
            diagnostic = KeyDiagnostic.Warn(
                KeyReasonCodes.Unparsable, KeyResolution.Unparsable,
                string.IsNullOrWhiteSpace(familyHint) ? null : KeyRef.Of(familyHint!, "normalize"),
                "键无法归一到任何已声明面");
        }

        _diagnostics.Report(diagnostic);
        return false;
    }

    /// <inheritdoc />
    public SurfaceKey Convert(SurfaceKey key, KeySurface target)
    {
        if (key.IsEmpty) return key;
        if (key.Surface == target) return key;
        return Build(key.Point, target);
    }
}
