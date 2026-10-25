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
        IReadOnlyDictionary<KeySurface, SurfaceGrammar> Surfaces);

    private sealed record Match(KeyPoint Point, bool Compatible);

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

            _families[family.Id] = new FamilyEntry(family, identity, surfaces);
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

    private bool TryParseCore(
        string? raw, KeySurface surface, string? familyHint, bool report,
        out SurfaceKey key, out KeyDiagnostic? diagnostic)
    {
        key = SurfaceKey.Empty;
        diagnostic = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            diagnostic = KeyDiagnostic.Warn(
                KeyReasonCodes.Blank, KeyResolution.Empty, null, "空白键");
            if (report) _diagnostics.Report(diagnostic);
            return false;
        }

        var matches = new List<Match>();
        if (!string.IsNullOrWhiteSpace(familyHint))
        {
            if (_families.TryGetValue(familyHint!, out var entry))
            {
                Collect(entry, surface, raw!, matches);
            }
        }
        else
        {
            foreach (var entry in _families.Values)
            {
                if (!HasFamilyPrefix(entry, surface)) continue;
                Collect(entry, surface, raw!, matches);
            }
        }

        if (matches.Count == 0)
        {
            diagnostic = KeyDiagnostic.Warn(
                KeyReasonCodes.Unparsable, KeyResolution.Unparsable,
                string.IsNullOrWhiteSpace(familyHint) ? null : KeyRef.Of(familyHint!, surface.ToString()),
                $"键面 '{surface}' 反解失败");
            if (report) _diagnostics.Report(diagnostic);
            return false;
        }

        if (matches.Count > 1)
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.FamilyAmbiguous, KeyResolution.Ambiguous, null,
                $"键面 '{surface}' 命中多个族：禁择一");
            if (report) _diagnostics.Report(diagnostic);
            return false;
        }

        var match = matches[0];
        key = new SurfaceKey(match.Point.Family, surface, match.Point, raw!);
        if (match.Compatible)
        {
            diagnostic = KeyDiagnostic.Info(
                KeyReasonCodes.LegacyForm, KeyResolution.Resolved,
                KeyRef.Of(match.Point.Family, raw!), "命中只读兼容形态");
            if (report) _diagnostics.Report(diagnostic);
        }

        return true;
    }

    private static void Collect(FamilyEntry entry, KeySurface surface, string raw, List<Match> matches)
    {
        if (!entry.Surfaces.TryGetValue(surface, out var grammar)) return;

        foreach (var (candidate, compatible) in grammar.Readable)
        {
            if (!candidate.TryParseAny(raw, out var values, out _)) continue;

            var point = new KeyPoint(entry.Family.Id, values.Present());
            matches.Add(new Match(point, compatible));
            return;
        }
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
