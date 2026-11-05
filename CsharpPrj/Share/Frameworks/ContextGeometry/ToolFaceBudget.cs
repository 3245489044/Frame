namespace Share.ContextGeometry;

/// <summary>工具面结构判据结果（exposes=false ∧ toolCount&gt;0 ⇒ 违约）。</summary>
public readonly record struct ToolFaceVerdict(bool Ok, string Agent, int ToolCount, string Code);

/// <summary>
/// 工具面预算（结构断言，零语义）：编排角色不得在模型提示面持有工具 schema。
/// <para>载体 = 结构事实（声明 + 计数），执行者叙述不参与（对应 SEP.JudgmentCarrierStructural）。</para>
/// </summary>
public static class ToolFaceBudget
{
    /// <summary>判定一次请求的工具面是否合规。</summary>
    public static ToolFaceVerdict Evaluate(string? agent, int toolCount, bool exposes)
    {
        var name = agent ?? string.Empty;
        if (toolCount < 0)
            return new ToolFaceVerdict(false, name, 0, GeometryDiagnostics.ToolFaceCountInvalid);
        if (exposes) return new ToolFaceVerdict(true, name, toolCount, "ok");
        if (toolCount == 0) return new ToolFaceVerdict(true, name, 0, "ok");
        return new ToolFaceVerdict(false, name, toolCount, GeometryDiagnostics.ToolFaceViolation);
    }
}
