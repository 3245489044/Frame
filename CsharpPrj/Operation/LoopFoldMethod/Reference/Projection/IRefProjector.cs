namespace Operation.LoopFoldMethod.Reference.Projection;

/// <summary>
/// 引用投影端口（Reference 空间维度唯一外部投影面）—— 把「累积历史 → 当前引用视图」投影成此刻的判定视图。
/// 思想对齐折叠头 <c>IFoldHeadProjector&lt;TState&gt;.Head(trace)</c>（把累积历史收敛成当前判定点），
/// 但作用于引用图（History→Task→Node 累积）而非状态带。
/// LLM 只是本端口的一种实现（提供丰富分层提示词视图）；Reference 本层只定**中立投影**，LLM 分层适配留 Agent/Planner。
/// </summary>
/// <typeparam name="TContext">累积引用关系输入（如 <see cref="RefHeadContext"/>）。</typeparam>
public interface IRefProjector<in TContext>
{
    /// <summary>把累积引用关系投影成当前执行上下文视图（供下轮 provide_prompt / cline_execute 消费）。</summary>
    IRefView Project(TContext ctx);
}
