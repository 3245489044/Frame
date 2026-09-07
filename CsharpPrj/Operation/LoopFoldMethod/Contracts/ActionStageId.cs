using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Contracts;

/// <summary>
/// 作用点（ActionPoint）固定枚举 —— 脊柱管道的八级固定阶段。顺序不可改（对齐分析文档《Agent 通用脊柱提取方案》§二）。
/// 固定逻辑（管道形状 / 阶段顺序 / 有界执行 / 收敛）由框架独占；路由逻辑（每级选哪个 handler）经
/// <see cref="Operation.LoopFoldMethod.Routing.IActionRouter{TState}"/> 分发，语义不进固定层。
/// </summary>
public enum ActionStageId
{
    /// <summary>1. 建租约 / 状态中枢 / 注入（路由可选：状态投影源）。</summary>
    Initialize = 0,

    /// <summary>2. 本轮执行形状 / 选 executor / 节点拓扑（路由：自由）。</summary>
    RouteRound = 1,

    /// <summary>3. 单隅有界执行 / 失败分类 / stall（固定层纯规定；节点 handler / 函数调用为路由）。</summary>
    ExecuteRound = 2,

    /// <summary>4. 规范 EVAL 文档 + 归一（路由：评估语义）。</summary>
    Evaluate = 3,

    /// <summary>5. 裁决壳 → <c>LoopDecision</c>（路由：决策词 / mapper）。</summary>
    Decide = 4,

    /// <summary>6. TS/TO/TM + 收敛终决（固定层纯规定）。</summary>
    Terminate = 5,

    /// <summary>7. Outbox / 串行写门 / 有界等待（固定层纯规定；存储后端为路由）。</summary>
    Persist = 6,

    /// <summary>8. 结果收敛（全部纯规定）。</summary>
    Converge = 7,
}

/// <summary>
/// 脊柱管道固定顺序 —— 八作用点的不可变推进次序。任一步骤存在「随策略变化的支路集合」即判自由逻辑漏入固定层，
/// 立即上提（可度量审计纪律）。
/// </summary>
public static class SpinalStageOrder
{
    /// <summary>固定推进顺序（1→8），任何实现不得重排或插入自定义阶段。</summary>
    public static readonly IReadOnlyList<ActionStageId> Fixed = new[]
    {
        ActionStageId.Initialize,
        ActionStageId.RouteRound,
        ActionStageId.ExecuteRound,
        ActionStageId.Evaluate,
        ActionStageId.Decide,
        ActionStageId.Terminate,
        ActionStageId.Persist,
        ActionStageId.Converge,
    };
}
