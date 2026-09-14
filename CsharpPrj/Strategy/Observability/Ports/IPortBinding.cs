namespace Strategy.Observability.Ports;

/// <summary>
/// 端口绑定（E10）：<b>可调用边界</b>的唯一实现面对外表达。
/// 生产实现与测试替身共用同一契约（<see cref="Boundary.BoundaryContract"/>），
/// 差异仅在绑定的 <see cref="IPortBinding"/> 实例（真实实现 / 标准替身 / 装饰器）。
/// </summary>
public interface IPortBinding
{
    /// <summary>调用端口；返回值即出证取值根（可为 null）。</summary>
    ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default);
}
