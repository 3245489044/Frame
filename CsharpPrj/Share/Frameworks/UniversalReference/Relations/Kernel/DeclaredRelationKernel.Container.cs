namespace Operation.DataStoreDriven.Relations;

/// <summary>容器归属校验段（U4）：声明面 <c>container=</c> 的写前判据。</summary>
public sealed partial class DeclaredRelationKernel
{
    /// <summary>
    /// 校验目标端点容器是否等于期望容器（期望 = source 键）。
    /// <para>不一致：<c>on_mismatch=reject</c> ⇒ 返回拒绝原因；<c>report</c> ⇒ 记诊断后继续。
    /// 未装配容器解析端口 ⇒ 记 <c>container-unchecked</c>（可见化，禁静默放行）。</para>
    /// </summary>
    private async Task<string?> CheckContainerAsync(
        RelationOperation plan, string sourceKey, string targetKey, List<string> diagnostics, CancellationToken ct)
    {
        if (!plan.ChecksContainer) return null;

        if (_containers is null)
        {
            const string uncheckedReason = "container-unchecked：声明 container= 但未装配容器解析端口";
            diagnostics.Add(uncheckedReason);
            _diagnostics?.RelationWriteSkipped(plan.Id, plan.Edge, "container-uncheckable", plan.Container, targetKey);
            return null;
        }

        var actual = await _containers.GetContainerAsync(plan.Target, targetKey, ct).ConfigureAwait(false);
        if (string.Equals(actual, sourceKey, StringComparison.Ordinal)) return null;

        var reason = $"container-mismatch:{sourceKey}|{actual ?? "(none)"}";
        if (plan.ContainerRejects)
        {
            _diagnostics?.RelationRejected(plan.Id, plan.Edge, reason);
            return reason;
        }

        diagnostics.Add(reason);
        return null;
    }
}
