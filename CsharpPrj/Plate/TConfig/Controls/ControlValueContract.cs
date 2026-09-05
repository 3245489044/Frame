
namespace TConfig.Controls;

/// <summary>
/// 控件值契约：声明某控件类型支持的值语义（Accessors）+ 各语义的"值变更信号"事件名（ValueChangedEvents）。
/// 类型差异的唯一声明点——同类型可声明多语义（如 listview = Selection + Content），
/// 消费方经语义名取用，不再逐处手写控件类型判断。
/// </summary>
public sealed class ControlValueContract
{
    public ControlValueContract(
        string controlType,
        IReadOnlyDictionary<ValueSemantic, ValueAccessor> accessors,
        IReadOnlyDictionary<ValueSemantic, string>? valueChangedEvents = null,
        ValueSemantic defaultSemantic = ValueSemantic.Text)
    {
        ControlType = controlType ?? throw new ArgumentNullException(nameof(controlType));
        Accessors = accessors ?? throw new ArgumentNullException(nameof(accessors));
        ValueChangedEvents = valueChangedEvents ?? new Dictionary<ValueSemantic, string>();
        DefaultSemantic = defaultSemantic;
    }

    /// <summary>控件类型名（与 ControlTypeRegistry/ControlStrategyRegistry 同键）。</summary>
    public string ControlType { get; }

    /// <summary>该类型支持的值语义 → 访问器。</summary>
    public IReadOnlyDictionary<ValueSemantic, ValueAccessor> Accessors { get; }

    /// <summary>值语义 → 物理控件"值变更信号"事件名（InputCapturePort 订阅用）。</summary>
    public IReadOnlyDictionary<ValueSemantic, string> ValueChangedEvents { get; }

    /// <summary>无显式语义声明时的默认语义（listview → Selection；textbox → Text）。</summary>
    public ValueSemantic DefaultSemantic { get; }

    /// <summary>取指定语义的访问器；该语义未声明时返回 null。</summary>
    public ValueAccessor? GetAccessor(ValueSemantic semantic)
        => Accessors.TryGetValue(semantic, out var accessor) ? accessor : null;
}
