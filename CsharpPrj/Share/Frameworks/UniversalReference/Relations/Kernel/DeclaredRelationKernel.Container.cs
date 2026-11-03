namespace UniversalWorkflowFramework.Relations.Kernel;

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
        if (!plan.ChecksContainer()) return null;

        if (_containers is null)
        {
            const string uncheckedReason = "container-unchecked：声明 container= 但未装配容器解析端口";
            diagnostics.Add(uncheckedReason);
            _diagnostics?.RelationWriteUnavailable(plan.Id, plan.Edge, "container-uncheckable", plan.Container, targetKey);
            return null;
        }

        var resolution = await _containers.ResolveAsync(plan.Target, targetKey, ct).ConfigureAwait(false);
        switch (resolution.Status)
        {
            case RelationContainerStatus.NotApplicable:
                // 判据不适用（合法跳过）：写入照常落库，禁表述为「未落库」（ERR-4）。
                diagnostics.Add($"container-not-applicable:{resolution.Reason}");
                _diagnostics?.RelationWriteNotApplicable(
                    plan.Id, plan.Edge, resolution.Reason ?? "container-not-applicable", plan.Container, targetKey);
                return null;

            case RelationContainerStatus.Unresolvable:
                // 不可判定 ≠ 违规：解析失败只可见化，不据此拒绝（否则存量非域键被 on_mismatch=reject 误伤）。
                diagnostics.Add($"container-unresolved:{resolution.Reason}");
                _diagnostics?.RelationWriteUnavailable(
                    plan.Id, plan.Edge, "container-unresolved", plan.Container, targetKey);
                return null;
        }

        var actual = resolution.Container;
        if (string.Equals(actual, sourceKey, StringComparison.Ordinal)) return null;

        var reason = $"container-mismatch:{sourceKey}|{actual ?? "(none)"}";
        if (plan.ContainerRejects())
        {
            _diagnostics?.RelationRejected(plan.Id, plan.Edge, reason);
            return reason;
        }

        diagnostics.Add(reason);
        return null;
    }
}
