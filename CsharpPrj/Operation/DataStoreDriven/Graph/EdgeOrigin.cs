namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 边来源受控词（区分「配置声明派生边」与「运行时归属边」）：
/// <c>declared</c> 由声明推导（删除必须走声明墓碑）；<c>runtime</c> 为运行时写边（硬删即持久，seed 不管理）。
/// </summary>
public static class EdgeOrigin
{
    /// <summary>声明派生边（唯一写入点 = 协调器内部声明路径）。</summary>
    public const string Declared = "declared";

    /// <summary>运行时边（task/history/session/节点写透等）。</summary>
    public const string Runtime = "runtime";
}
