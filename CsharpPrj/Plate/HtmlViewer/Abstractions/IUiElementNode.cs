#nullable enable

namespace UiBackend.Abstractions;

/// <summary>
/// UI 元素节点统一访问器——把「布局投影 + 值读写 + 事件触发 + 子节点」归一，供
/// 内核（布局投影器 / 脚本命令执行器 / 事件分派）消费，与具体后端（WinForms/HTML/无头）解耦。
/// </summary>
public interface IUiElementNode
{
    /// <summary>控件名（脚本 target、参数 source 用）。</summary>
    string Name { get; }

    /// <summary>声明类型（button/textbox/panel/...）。</summary>
    string TypeName { get; }

    /// <summary>值语义（get / set / 参数 source=control / 输出写回）。</summary>
    object? GetValue();

    /// <summary>写值（set / 输出写回）。</summary>
    void SetValue(object? value);

    /// <summary>事件（click / 脚本命令触发）。</summary>
    void Invoke(string eventName, object? payload);

    /// <summary>布局投影（像素 rect → 物理定位）。</summary>
    void SetBounds(int x, int y, int width, int height);

    /// <summary>当前物理位置（像素）。</summary>
    (int X, int Y, int Width, int Height) GetBounds();

    /// <summary>子节点。</summary>
    IEnumerable<IUiElementNode> Children { get; }
}
