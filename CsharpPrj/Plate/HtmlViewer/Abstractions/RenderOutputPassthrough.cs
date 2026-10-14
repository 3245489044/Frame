#nullable enable

using System;
using System.Threading.Tasks;

namespace UiBackend.Abstractions;

/// <summary>
/// 产物透传（G3 通用 helper）——领域入口「有类型化产物则返回，否则整页叙述」的单一实现，
/// 消除消息桥各分支重复的 <c>if (x is not null) return x; return 整页()</c>。
/// <para>语义判据 = <c>artifact() is not null</c>：<see cref="RenderOutput.Skip"/> 亦为有效产物（不回退整页）。</para>
/// </summary>
public static class RenderOutputPassthrough
{
    /// <summary>await 产物；非 null → 返回，null → 返回兜底（整页叙述）。</summary>
    public static async Task<RenderOutput> FirstArtifact(
        Func<Task<RenderOutput?>> artifact,
        Func<Task<RenderOutput>> fallback)
    {
        var output = await artifact().ConfigureAwait(false);
        return output ?? await fallback().ConfigureAwait(false);
    }
}
