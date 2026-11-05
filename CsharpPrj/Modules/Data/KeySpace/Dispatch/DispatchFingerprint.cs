using System.Security.Cryptography;
using System.Text;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Dispatch;

/// <summary>目标集指纹（sha256(sorted targets + reason)；空转判据的确定性载体）。</summary>
public readonly record struct PlanFingerprint(string Value)
{
    /// <summary>空指纹（无目标；与「目标集为空」区分于 Reason 参与哈希）。</summary>
    public static PlanFingerprint Empty { get; } = new(string.Empty);
}

/// <summary>指纹计算唯一实现（纯函数；目标集/理由任一变化即变化）。</summary>
public static class DispatchFingerprint
{
    /// <summary>计算指纹：目标值取业务键（已归一化），按 Ordinal 排序后与理由拼接。</summary>
    public static PlanFingerprint Of(IEnumerable<KeyRef> targets, string reason)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var values = targets
            .Select(static target => target.Value)
            .Where(static value => !string.IsNullOrEmpty(value))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        var payload = string.Join('\n', values) + "|" + (reason ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return new PlanFingerprint(Convert.ToHexString(hash).ToLowerInvariant());
    }
}
