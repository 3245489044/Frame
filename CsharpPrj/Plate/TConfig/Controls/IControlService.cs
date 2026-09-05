
namespace TConfig.Controls;

/// <summary>
/// 控件服务统一接口（值语义契约服务）。
/// 迁移终点：MetaCommand.IControlService 的 [Obsolete] 提示（"Use TConfig.Controls.IControlService"）指向本接口。
/// 消费方（输入采集 InputCapturePort / 参数绑定 EcsEventBus / 命令 set / 输出写值）按语义名编程，
/// 不再出现控件类型判断；类型差异由 ControlValueContract 承载（ValueContractRegistry 注册）。
/// </summary>
public interface IControlService
{
    /// <summary>按控件类型契约的默认语义读值。</summary>
    object? GetValue(object control);

    /// <summary>按指定语义读值。</summary>
    object? GetValue(object control, ValueSemantic semantic);

    /// <summary>
    /// 按默认语义写值，返回"写后规范值"（如 ListView 匹配选中后的 Tag 完整路径）——
    /// 调用方（ECS 真源）据此回写，保证"写入 ECS 的值 = 物理控件读回的值"一致；null = 无规范值（Content 语义或写失败）。
    /// </summary>
    string? SetValue(object control, object? value);

    /// <summary>按指定语义写值，返回"写后规范值"（同上）。</summary>
    string? SetValue(object control, object? value, ValueSemantic semantic);

    /// <summary>控件类型是否有契约（默认语义可用）。</summary>
    bool IsSupported(object control);

    /// <summary>控件类型是否支持指定语义。</summary>
    bool IsSupported(object control, ValueSemantic semantic);

    /// <summary>注册控件类型契约（类型差异的扩展点；新增控件类型 = 注册一条契约）。</summary>
    void RegisterContract(string controlType, ControlValueContract contract);

    /// <summary>解析控件类型契约（含基类回退）。</summary>
    ControlValueContract? GetContract(Type controlType);

    /// <summary>查询某语义的"值变更信号"事件名（物理采集订阅用）；无则 null。</summary>
    string? GetValueChangedEvent(object control, ValueSemantic semantic);

    /// <summary>清空全部契约。</summary>
    void ClearContracts();
}
