using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Usharp.Semantics;

namespace Usharp.Adapters.Agent;

/// <summary>
/// Agent 单加载点（反转后）：一次编译的**全部产物**（facets + 投影器产物 + 同源指纹）。
/// <para>
/// 运行期各框架只读本清单，**零自解析**（取代 <c>SkillCatalog.ParseFrontMatter</c> /
/// <c>PromptProfileCatalog.TryParse</c> / <c>ToolFaceAssembler.TrackFactories</c> 手写链路）。
/// </para>
/// <para>
/// 唯一引点（D）：本文件 = Agent 子域**唯一引点**；领域件引 Usharp 一律经本子域适配件，
/// 禁跨子域直引（与 <c>Observability</c> 的「唯一引观测处」同模板）。
/// </para>
/// </summary>
public sealed record AgentManifest(
    IReadOnlyList<UsharpSkillDecl> Skills,
    IReadOnlyList<UsharpProfileDecl> Profiles,
    IReadOnlyList<AgentToolFaceEdge> ToolFaces,
    AgentBehaviorTable? Behavior,
    IReadOnlyList<AgentPortNode> Ports,
    string SourceFingerprint,
    IReadOnlyList<UsharpLlmDecl>? LlmDecls = null,
    IReadOnlyList<UsharpSegmentDecl>? Segments = null)
{
    /// <summary>由一次编译装配清单（缺失的 facet → 空表/ null，不伪造）。</summary>
    public static AgentManifest Build(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var skills = compilation.TryGetFacet<IReadOnlyList<UsharpSkillDecl>>(out var s) ? s : Array.Empty<UsharpSkillDecl>();
        var profiles = compilation.TryGetFacet<IReadOnlyList<UsharpProfileDecl>>(out var p) ? p : Array.Empty<UsharpProfileDecl>();
        var faces = compilation.TryGetFacet<IReadOnlyList<AgentToolFaceEdge>>(out var f) ? f : Array.Empty<AgentToolFaceEdge>();
        var behavior = compilation.TryGetFacet<AgentBehaviorTable>(out var b) ? b : null;
        var ports = compilation.TryGetFacet<IReadOnlyList<AgentPortNode>>(out var n) ? n : Array.Empty<AgentPortNode>();
        var llmDecls = compilation.TryGetFacet<IReadOnlyList<UsharpLlmDecl>>(out var l) ? l : Array.Empty<UsharpLlmDecl>();
        var segments = compilation.TryGetFacet<IReadOnlyList<UsharpSegmentDecl>>(out var g) ? g : Array.Empty<UsharpSegmentDecl>();

        return new AgentManifest(skills, profiles, faces, behavior, ports, Fingerprint(skills, profiles, faces), llmDecls, segments);
    }

    /// <summary>同源指纹（I5）：代码契约与提示词产物共享一次编译的声明指纹。</summary>
    public static string Fingerprint(
        IReadOnlyList<UsharpSkillDecl> skills,
        IReadOnlyList<UsharpProfileDecl> profiles,
        IReadOnlyList<AgentToolFaceEdge> faces)
    {
        var sb = new StringBuilder();
        foreach (var s in skills) sb.Append("S:").Append(s.Name).Append('|').Append(s.Dsl).Append('|').Append(s.Body).Append(';');
        foreach (var p in profiles) sb.Append("P:").Append(p.Key).Append('|').Append(string.Join(",", p.Bands)).Append('|').Append(p.Base ? "1" : "0").Append(';');
        foreach (var f in faces) sb.Append("F:").Append(f.Face).Append('|').Append(f.Edge).Append('|').Append(f.Target).Append(';');

        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}

/// <summary>Agent 领域反转包集合（消费方一次取全；默认语言包 + <c>[Skill]/[Profile]</c> + 工具面）。</summary>
public static class AgentPacks
{
    /// <summary>
    /// 构造 Agent 编译用的**完整** pack 列表 = 默认语言包（<see cref="UsharpDomainPack.Instance"/>，提供
    /// spaces/matrices/flows/refs 等基础 facet）+ Agent 反转包。注册表参数由消费方注入（工具名 / 轨 id / band 白名单）。
    /// </summary>
    public static IReadOnlyList<IUsharpDomainPack> Create(
        IReadOnlyCollection<string>? knownTools = null,
        IReadOnlyCollection<string>? knownTracks = null,
        IReadOnlyCollection<string>? knownBands = null,
        IReadOnlyCollection<string>? knownFunctions = null,
        IReadOnlyCollection<string>? knownActions = null)
        => new IUsharpDomainPack[]
        {
            UsharpDomainPack.Instance,
            new AgentBehaviorPack(knownActions),
            new AgentPortPack(knownFunctions),
            new AgentSkillPack(knownTools, knownBands),
            new AgentSegmentPack(),
            new AgentToolPack(knownTracks),
            new AgentPromptPack(),
            new AgentLlmPack(),
        };
}
