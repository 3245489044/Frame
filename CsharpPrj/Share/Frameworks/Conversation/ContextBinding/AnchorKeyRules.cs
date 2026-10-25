using System;
using System.Collections.Generic;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;
using Modules.Node.Identity;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 锚点键解析规则（R5 唯一实现）—— 锚点族与图端点族**显式分离**：
/// <c>history.{ordinal}</c> 属会话锚点族（序数，非历史身份），不参与 <c>historyId</c> 端点解析；
/// 节点锚点复用 <see cref="NodeEndpointIds"/> 的端点文法，禁止本地首分隔切分。
/// </summary>
internal static class AnchorKeyRules
{
    /// <summary>会话历史锚点族（<c>history.{ordinal:digits}</c>；数字序数，不是 history 端点 Id）。</summary>
    private static readonly KeyFamily HistoryAnchorFamily = new(
        "conversation.history-anchor",
        "history.{ordinal:digits}",
        "history.",
        KeyRelation.Peer,
        KeyCardinality.Many,
        Array.Empty<string>(),
        IdentityKeys: new[] { "ordinal" },
        Surfaces: new Dictionary<KeySurface, KeySurfaceSpec>
        {
            [KeySurface.Endpoint] = new KeySurfaceSpec(
                KeySurface.Endpoint,
                new[] { "history.{ordinal:digits}" },
                "history."),
        });

    private static readonly KeyAlgebra Anchors = new(new[] { HistoryAnchorFamily });

    private static readonly string[] ReservedPrefixes =
    {
        "session.", "session:", "history:", "task:", "node:",
    };

    /// <summary>锚点可解析判定：非空且命中已知锚点/端点族；未命中 ⇒ false + 可见原因。</summary>
    public static bool TryResolve(string key, out string detail)
    {
        if (string.IsNullOrEmpty(key))
        {
            detail = "AnchorKey 为空（悬空）";
            return false;
        }

        if (Anchors.TryParse(key, KeySurface.Endpoint, HistoryAnchorFamily.Id, out _, out _))
        {
            detail = string.Empty;
            return true;
        }

        // history. 前缀已由锚点族认领：解析失败即非法（不得落入节点裸键兜底）。
        if (key.StartsWith(HistoryAnchorFamily.FamilyPrefix, StringComparison.Ordinal))
        {
            detail = $"history 锚点后缀非序数: '{key[HistoryAnchorFamily.FamilyPrefix!.Length..]}'";
            return false;
        }

        foreach (var prefix in ReservedPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                detail = $"锚点命中保留族前缀 '{prefix}'（非节点锚点）";
                return false;
            }
        }

        if (key.StartsWith("prompt.", StringComparison.Ordinal)
            || key.StartsWith("skill.", StringComparison.Ordinal)
            || key.StartsWith("tool.", StringComparison.Ordinal))
        {
            detail = string.Empty;
            return true;
        }

        // 节点锚点：带命名空间的端点按端点文法反解；裸键仅接受无分隔符形态。
        if (NodeEndpointIds.TryParseEndpoint(key, out _, out var nodeKey, out _) && nodeKey.Length > 0)
        {
            detail = string.Empty;
            return true;
        }

        if (!key.Contains('.') && !key.Contains('@'))
        {
            detail = string.Empty;
            return true;
        }

        detail = "节点键无法解析（非端点文法且含分隔符）";
        return false;
    }
}
