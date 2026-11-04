using System.Text;
using MathematicalAlgorithms.Core;

namespace MathematicalAlgorithms.Formula;

/// <summary>
/// 公式签名：复用 <see cref="SignatureHash"/> 的 FNV-1a 64 位链。
/// <para>只混结构身份（id/version/kind/shape/target/unit/inputs/coefficients/domain），
/// 不混 expression/fit/residual/status/generatedAt，使「系数或域变化 ⇒ 签名变化」可判定。</para>
/// </summary>
public static class FormulaSignature
{
    public static long Compute(FormulaDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        long hash = SignatureHash.Begin();
        hash = MixString(hash, definition.Id);
        hash = SignatureHash.Mix(hash, definition.Version);
        hash = MixString(hash, definition.Kind.ToString());
        hash = MixString(hash, definition.Shape.ToString());
        hash = MixString(hash, definition.Target);
        hash = MixString(hash, definition.Unit);

        foreach (FormulaInputSpec input in (definition.Inputs ?? Array.Empty<FormulaInputSpec>())
                     .OrderBy(static i => i.Name, StringComparer.Ordinal))
        {
            hash = MixString(hash, input.Name);
            hash = MixString(hash, input.Unit);
            hash = MixRange(hash, input.Domain, null);
        }

        foreach (FormulaCoefficient coefficient in (definition.Coefficients ?? Array.Empty<FormulaCoefficient>())
                     .OrderBy(static c => c.Name, StringComparer.Ordinal))
        {
            hash = MixString(hash, coefficient.Name);
            hash = MixDouble(hash, coefficient.Value);
        }

        FormulaSegment[] segments = (definition.Segments ?? Array.Empty<FormulaSegment>())
            .OrderBy(static s => s.MinX)
            .ThenBy(static s => s.MaxX)
            .ToArray();
        hash = SignatureHash.Mix(hash, (long)segments.Length);
        foreach (FormulaSegment segment in segments)
        {
            hash = MixDouble(hash, segment.MinX);
            hash = MixDouble(hash, segment.MaxX);
            hash = MixDouble(hash, segment.Slope);
            hash = MixDouble(hash, segment.Intercept);
        }

        FormulaDomain domain = definition.Domain ?? FormulaDomain.Empty;
        if (domain.Ranges is not null)
        {
            foreach (string key in domain.Ranges.Keys.OrderBy(static k => k, StringComparer.Ordinal))
                hash = MixRange(hash, domain.Ranges[key], key);
        }

        hash = MixString(hash, domain.Guard.ToString());
        return hash;
    }

    public static string Format(long signature) => $"fnv1a64:0x{signature:X16}";

    private static long MixString(long hash, string? value)
    {
        if (value is null)
            return SignatureHash.Mix(hash, -1L);

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash = SignatureHash.Mix(hash, (long)bytes.Length);
        foreach (byte valueByte in bytes)
            hash = SignatureHash.Mix(hash, (long)valueByte);

        return hash;
    }

    private static long MixDouble(long hash, double value)
        => SignatureHash.Mix(hash, BitConverter.DoubleToInt64Bits(value));

    private static long MixRange(long hash, FormulaRange? range, string? name)
    {
        if (name is not null)
            hash = MixString(hash, name);

        if (range is null)
            return SignatureHash.Mix(hash, -2L);

        hash = SignatureHash.Mix(hash, range.Min.HasValue ? 1 : 0);
        if (range.Min.HasValue)
            hash = MixDouble(hash, range.Min.Value);

        hash = SignatureHash.Mix(hash, range.Max.HasValue ? 1 : 0);
        if (range.Max.HasValue)
            hash = MixDouble(hash, range.Max.Value);

        if (range.Values is null)
        {
            hash = SignatureHash.Mix(hash, -1L);
        }
        else
        {
            hash = SignatureHash.Mix(hash, (long)range.Values.Count);
            foreach (double value in range.Values)
                hash = MixDouble(hash, value);
        }

        return hash;
    }
}
