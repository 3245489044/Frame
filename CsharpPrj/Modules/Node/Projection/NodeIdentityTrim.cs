using System.Globalization;
using Modules.Node.Identity;

namespace Modules.Node.Projection;

/// <summary>
/// 读侧身份裁剪（N7）：按声明面的位置键模板反解，比对 <c>container</c> / <c>round</c> 期望值。
/// <para>
/// 只裁剪读侧（写侧不裁剪）；<c>reason</c> 区分「不合形」（反解失败，键不属于本域键族）与
/// 「不属于本容器/本轮」，供调用方分类可见化（不再共用一个裸 <c>false</c>）。
/// </para>
/// </summary>
public static class NodeIdentityTrim
{
    /// <summary>判定读投影项是否属于期望的容器 / 轮次。</summary>
    /// <param name="framework">域框架（位置键模板来源）。</param>
    /// <param name="item">读投影项。</param>
    /// <param name="container">期望容器（<c>null</c> = 不裁剪）。</param>
    /// <param name="round">期望轮次（<c>null</c> = 不裁剪）。</param>
    /// <param name="reason">裁剪原因（命中为 <c>null</c>）。</param>
    public static bool TryMatch(
        NodeFramework framework,
        NodeProjectionItem item,
        string? container,
        int? round,
        out string reason)
    {
        reason = string.Empty;
        if (container is null && round is null)
        {
            return true;
        }

        if (!framework.Placements.TryGet(item.PlacementId, out var placement))
        {
            reason = "placement-unknown";
            return false;
        }

        if (!KeyTemplateEvaluator.TryParseValues(placement.KeyTemplate, item.Key, out var values))
        {
            reason = "key-unparsable";
            return false;
        }

        if (container is not null &&
            (!values.TryGetValue("container", out var actualContainer) ||
             !string.Equals(actualContainer, container, StringComparison.Ordinal)))
        {
            reason = "container-mismatch";
            return false;
        }

        if (round is { } expectedRound)
        {
            var expected = expectedRound.ToString(CultureInfo.InvariantCulture);
            if (!values.TryGetValue("round", out var actualRound) ||
                !string.Equals(actualRound, expected, StringComparison.Ordinal))
            {
                reason = "round-mismatch";
                return false;
            }
        }

        return true;
    }
}
