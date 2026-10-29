#nullable enable

using System;
using System.Threading.Tasks;

namespace UiBackend.Abstractions;

/// <summary>
/// 产物归一（G3 通用 helper，P-B 去整页兜底）——领域入口「类型化产物或显式 Skip」的单一实现：
/// <c>null</c>（异常/未装配）→ <see cref="RenderOutput.Skip"/>（不写），**不再回退整页叙述**。
/// <para>删除第二写落地路径（整页 body reconcile）：空产物 = 不写，由渲染层显式产出
/// <see cref="RenderOutput.Skip"/> 表达；首挂载唯一一次导航由写协调器 <c>_navigatedHosts</c> 承担，不在此处。</para>
/// </summary>
public static class RenderOutputPassthrough
{
    /// <summary>await 产物；非 null 原样返回；异常/未装配 → <see cref="RenderOutput.Skip"/>（不写，且异常可见化）。</summary>
    public static async Task<RenderOutput> OrSkip(Func<Task<RenderOutput?>> artifact, Action<Exception>? onError = null)
    {
        try
        {
            var output = await artifact().ConfigureAwait(false);
            return output ?? RenderOutput.Skip;
        }
        catch (Exception ex)
        {
            // P0-3：捕获不得静默 —— 至少打 Warn（宿主可注入 onError 转正式日志通道）。
            onError?.Invoke(ex);
            System.Diagnostics.Trace.TraceWarning(
                $"[RenderOutputPassthrough] 产物异常按 Skip 处理：{ex.GetType().Name}: {ex.Message}");
            return RenderOutput.Skip;
        }
    }
}
