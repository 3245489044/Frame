using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 键一致性门（显式调用，非织入）：键实例 ↔ 身份值袋 ↔ 族前缀。
/// <para>
/// 判据载体 = 键形与值袋（结构事实），不含动机/收益等执行者叙述；
/// 调用点仅两处：唯一写门、读端口构造（R1：键求值仍调用点可见）。
/// </para>
/// </summary>
public static class KeyIntegrityGuard
{
    /// <summary>
    /// 校验「键实例」与调用方身份值袋一致：值袋每项都须在键的实际反解中出现且相等；
    /// 值袋键不是该族已声明轴 ⇒ 失败（禁静默忽略多写的轴）。
    /// </summary>
    public static bool TryCheck(
        KeyOps ops,
        KeyInstance key,
        IReadOnlyDictionary<string, string> values,
        out KeyDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentNullException.ThrowIfNull(values);
        diagnostic = null;

        if (!ops.TryRender(key, out var rendered, out diagnostic))
        {
            return false;
        }

        if (!ops.TryParse(key.Family, key.Surface, rendered, out var parsed, out diagnostic))
        {
            diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.IdentityValueMismatch, KeyResolution.Unparsable,
                KeyRef.Of(key.Family, rendered), "键自身无法反解（键形与声明不一致）");
            return false;
        }

        foreach (var pair in values)
        {
            var axis = KeyAxis.Of(pair.Key);
            var actual = ops.Value(parsed, axis);
            if (actual is null)
            {
                diagnostic = KeyDiagnostic.Error(
                    KeyReasonCodes.IdentityAxisUnknown, KeyResolution.NotDeclared,
                    KeyRef.Of(key.Family, rendered), $"值袋轴 '{pair.Key}' 不在键面声明中");
                return false;
            }

            if (!string.Equals(actual, pair.Value, StringComparison.Ordinal))
            {
                diagnostic = KeyDiagnostic.Error(
                    KeyReasonCodes.IdentityValueMismatch, KeyResolution.Mismatched,
                    KeyRef.Of(key.Family, rendered),
                    $"轴 '{pair.Key}' 值不一致（键={actual}; 值袋={pair.Value}）");
                return false;
            }
        }

        return true;
    }

    /// <summary>校验失败即抛（写门 fail-fast；不静默放行）。</summary>
    public static void Assert(
        KeyOps ops,
        KeyInstance key,
        IReadOnlyDictionary<string, string> values)
    {
        if (TryCheck(ops, key, values, out var diagnostic))
        {
            return;
        }

        throw new InvalidOperationException(
            diagnostic is null ? "键一致性校验失败" : $"{diagnostic.Code}: {diagnostic.Message}");
    }

    /// <summary>族前缀判据：键文本是否属指定族（只读判定，不做切分）。</summary>
    public static bool HasFamilyPrefix(KeyOps ops, string familyId, string? key, out string prefix)
    {
        ArgumentNullException.ThrowIfNull(ops);
        prefix = ops.Prefix(familyId);
        return key is { Length: > 0 } && key.StartsWith(prefix, StringComparison.Ordinal);
    }
}
