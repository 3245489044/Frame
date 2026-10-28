using System;
using System.Collections.Generic;
using Modules.KeySpace.Model;

namespace Modules.Node.Identity;

/// <summary>
/// 图端点键族**单源目录**（history / node / session）：模板、面、前缀只在此声明一次。
/// <para>消费方一律经 <see cref="Modules.KeySpace.Rules.KeyOps"/> 装配/投影，禁第二套字面量。</para>
/// </summary>
public static class HierarchyKeyFamilies
{
    public const string HistoryId = "hierarchy.history";
    public const string NodeId = "hierarchy.node";
    public const string SessionId = "hierarchy.session";

    public static KeyFamily History { get; } = new(
        HistoryId,
        "history:{historyId}",
        "history.",
        KeyRelation.Container,
        KeyCardinality.Many,
        Array.Empty<string>(),
        IdentityKeys: new[] { "historyId" },
        Surfaces: new Dictionary<KeySurface, KeySurfaceSpec>
        {
            // 只读兼容形态 <c>{historyId}</c>：迁移期旧行裸键（只反解、不构造）。
            [KeySurface.Storage] = new KeySurfaceSpec(
                KeySurface.Storage, new[] { "history:{historyId}" }, "history:",
                ReadOnlyTemplates: new[] { "{historyId}" }),
            [KeySurface.Endpoint] = new KeySurfaceSpec(
                KeySurface.Endpoint, new[] { "history.{historyId}" }, "history."),
        });

    public static KeyFamily Node { get; } = new(
        NodeId,
        "{workflowId}.{nodeKey}[@{instanceKey}]",
        string.Empty,
        KeyRelation.Member,
        KeyCardinality.Many,
        Array.Empty<string>(),
        IdentityKeys: new[] { "nodeKey" },
        Surfaces: new Dictionary<KeySurface, KeySurfaceSpec>
        {
            [KeySurface.Storage] = new KeySurfaceSpec(
                KeySurface.Storage, new[] { "{nodeKey}[@{instanceKey}]" }),
            [KeySurface.Endpoint] = new KeySurfaceSpec(
                KeySurface.Endpoint,
                new[] { "{workflowId}.{nodeKey}[@{instanceKey}]", "{workflowId}.{nodeKey}" }),
        });

    public static KeyFamily Session { get; } = new(
        SessionId,
        "session.{sessionName}",
        "session.",
        KeyRelation.Container,
        KeyCardinality.Many,
        Array.Empty<string>(),
        IdentityKeys: new[] { "sessionName" },
        Surfaces: new Dictionary<KeySurface, KeySurfaceSpec>
        {
            [KeySurface.Storage] = new KeySurfaceSpec(
                KeySurface.Storage, new[] { "session:{sessionName}" }, "session:"),
            [KeySurface.Endpoint] = new KeySurfaceSpec(
                KeySurface.Endpoint, new[] { "session.{sessionName}" }, "session."),
        });

    public static IReadOnlyList<KeyFamily> All { get; } = new[] { History, Node, Session };
}
