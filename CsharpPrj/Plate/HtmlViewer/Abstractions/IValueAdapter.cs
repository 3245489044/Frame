#nullable enable

using TConfig.Controls;

namespace UiBackend.Abstractions;

/// <summary>
/// 值适配器——值语义（<see cref="ValueSemantic"/>）与节点读写的后端差异封装。
/// 消费方（事件桥参数绑定 / 脚本命令）按语义名取节点值，类型差异由实现（WinForms 委托
/// ValueContractRegistry、HTML 映射 DOM value/textContent）承载，不逐处散落控件类型判断。
/// </summary>
public interface IValueAdapter
{
    /// <summary>取控件类型契约的默认值语义（textbox→Text、listview→Selection...）。</summary>
    ValueSemantic GetDefaultSemantic(string controlType);

    /// <summary>按语义读节点值；节点自身未实现该语义时返回 null。</summary>
    object? GetValue(IUiElementNode node, ValueSemantic semantic);

    /// <summary>按语义写节点值。</summary>
    void SetValue(IUiElementNode node, ValueSemantic semantic, object? value);
}
