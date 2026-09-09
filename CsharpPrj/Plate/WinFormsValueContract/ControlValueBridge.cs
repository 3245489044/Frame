using TConfig.Controls;

namespace WinFormsValueContract;

/// <summary>
/// 值桥：<see cref="IControlService"/> 的默认实现（值语义读写 + 契约注册 + 变更信号查询）。
/// 真源方向由消费方约定：读 = ECS 真源优先 + 物理契约兜底；写 = ECS→物理单向（本桥仅物理投影）；
/// 物理→ECS 回灌经 <see cref="GetValueChangedEvent"/> 订阅"值变更信号"后单向执行。
/// </summary>
public sealed class ControlValueBridge : IControlService
{
    private readonly IControlValueAccessor? _physicalAccessor;

    /// <summary>
    /// 值桥：<see cref="IControlService"/> 的默认实现（值语义读写 + 契约注册 + 变更信号查询）。
    /// <paramref name="physicalAccessor"/> = 物理控件读写内核（IControlValueAccessor，如 WinFormsControlValueWriter），
    /// 用于「契约未覆盖类型」的默认语义写值兜底——把原 EcsStateAccessor 的
    /// 「契约写 / IControlValueAccessor 兜底」双轨收敛为单一服务入口（D5 收敛）。
    /// 读路径保持契约优先（无契约返回 null，保留 EcsStateAccessor 的 ControlInfo.Text 兜底语义）。
    /// </summary>
    public ControlValueBridge(IControlValueAccessor? physicalAccessor = null)
    {
        _physicalAccessor = physicalAccessor;
    }

    /// <summary>按契约默认语义读值（无契约返回 null）。</summary>
    public object? GetValue(object control)
    {
        var contract = ResolveContract(control);
        return contract?.GetAccessor(contract.DefaultSemantic)?.Reader(control);
    }

    /// <summary>按指定语义读值（语义未声明返回 null）。</summary>
    public object? GetValue(object control, ValueSemantic semantic)
        => ResolveAccessor(control, semantic)?.Reader(control);

    /// <summary>
    /// 按默认语义写值，返回写后规范值（null = 无规范值或写失败）。
    /// D5 收敛：契约未覆盖类型 → 物理内核（IControlValueAccessor）兜底写值（无规范值返回 null）。
    /// </summary>
    public string? SetValue(object control, object? value)
    {
        var contract = ResolveContract(control);
        if (contract != null)
            return SetValue(control, value, contract.DefaultSemantic);

        // 契约未覆盖类型：物理内核兜底（合并原 EcsStateAccessor 的 IControlValueAccessor 回退，物理写语义一致）
        if (_physicalAccessor != null && _physicalAccessor.IsSupported(control))
        {
            _physicalAccessor.SetValue(control, value);
            return null;
        }

        return null;
    }

    /// <summary>按指定语义写值，返回写后规范值（null = 无规范值或写失败）。</summary>
    public string? SetValue(object control, object? value, ValueSemantic semantic)
        => ResolveAccessor(control, semantic)?.Writer(control, value);

    /// <summary>控件类型是否有契约（默认语义可用）。</summary>
    public bool IsSupported(object control) => ResolveContract(control) != null;

    /// <summary>控件类型是否支持指定语义。</summary>
    public bool IsSupported(object control, ValueSemantic semantic)
        => ResolveAccessor(control, semantic) != null;

    /// <summary>注册契约（类型差异扩展点）。</summary>
    public void RegisterContract(string controlType, ControlValueContract contract)
        => ValueContractRegistry.Register(contract ?? throw new ArgumentNullException(nameof(contract)));

    /// <summary>解析契约（含基类回退）。</summary>
    public ControlValueContract? GetContract(Type controlType)
        => ValueContractRegistry.GetForType(controlType);

    /// <summary>查询语义的"值变更信号"事件名（物理采集订阅用）；无则 null。</summary>
    public string? GetValueChangedEvent(object control, ValueSemantic semantic)
    {
        var contract = ResolveContract(control);
        return contract?.ValueChangedEvents.TryGetValue(semantic, out var evt) == true ? evt : null;
    }

    /// <summary>清空全部契约（测试隔离用）。</summary>
    public void ClearContracts() => ValueContractRegistry.Clear();

    private static ControlValueContract? ResolveContract(object control)
        => control == null ? null : ValueContractRegistry.GetForType(control.GetType());

    private static ValueAccessor? ResolveAccessor(object control, ValueSemantic semantic)
        => ResolveContract(control)?.GetAccessor(semantic);
}
