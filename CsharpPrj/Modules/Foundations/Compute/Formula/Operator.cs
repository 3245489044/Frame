namespace Modules.Compute.Formula;

/// <summary>场定义域：决定 tile 形状与并行自由度，而不是由调用方临时决定。</summary>
public enum FieldShape
{
    /// <summary>单点（测试/常量）。</summary>
    Scalar = 0,

    /// <summary>逐列（x,z）：高度、气候、水面。</summary>
    Column2D = 1,

    /// <summary>逐体素（x,y,z）。</summary>
    Voxel3D = 2,

    /// <summary>lattice 单元角点（可被三线性复用）：噪声角值。</summary>
    LatticeCell = 3,
}

/// <summary>tile 形状（缓存块粒度）：由场的定义域 + 访问模式决定。</summary>
public enum TilePolicy
{
    /// <summary>32×32 列（2D 场，64³ chunk ⇒ 1 tile）。</summary>
    Column32 = 0,

    /// <summary>8×8×8 体素（3D 场，落在 L1/L2）。</summary>
    Voxel8 = 1,

    /// <summary>整卷一次（小体量集合/常量）。</summary>
    Whole = 2,
}

/// <summary>算子成本类：决定「存还是算」。</summary>
public enum CostClass
{
    /// <summary>整数哈希：贵（≤ 常数次/单元），必须存。</summary>
    Hash = 0,

    /// <summary>纯浮点：便宜，可重算。</summary>
    Flop = 1,

    /// <summary>内存搬运/多通道读写：偏贵，优先存。</summary>
    Memory = 2,

    /// <summary>分支/查表：偏贵，可重算（需保持判定顺序）。</summary>
    Band = 3,
}

/// <summary>缓冲来源（决定分配器与生命周期）。</summary>
public enum BufferSource
{
    /// <summary>不物化（重算）。</summary>
    None = 0,

    /// <summary><c>Layout.Arena</c>：worker 私有、chunk 结束整条回退。</summary>
    Arena = 1,

    /// <summary><c>Layout.ScratchPool</c>：跨 chunk 复用（顶点/大缓冲）。</summary>
    Scratch = 2,

    /// <summary>调用方输出缓冲（最终交付）。</summary>
    Output = 3,
}

/// <summary>物化决策。</summary>
public enum Materialization
{
    Recompute = 0,
    Store = 1,
}

/// <summary>
/// 算子描述（编译期输入）：Kind + 输入张量 + 输出张量 + 定义域 + tile + 成本 + 参数签名。
/// <para><b>参数签名</b>是「同一 Kind 不同参数」的唯一区分；缺省 0 表示与现有默认参数一致。</para>
/// <para><b>可交换</b>标记决定 CSE 是否排序输入；不可交换算子（差值/减法链）必须保持输入顺序，
/// 否则会错误合并 <c>a-b</c> 与 <c>b-a</c>。</para>
/// </summary>
public sealed record OperatorSpec(
    string Kind,
    IReadOnlyList<TensorKey> Inputs,
    TensorKey Output,
    FieldShape Shape,
    TilePolicy Tile,
    CostClass Cost,
    long ParameterSignature = 0,
    bool Commutative = false)
{
    public static OperatorSpec Create(
        string kind,
        TensorKey output,
        FieldShape shape,
        TilePolicy tile,
        CostClass cost,
        long parameterSignature,
        params TensorKey[] inputs) =>
        new(kind, inputs, output, shape, tile, cost, parameterSignature);
}
