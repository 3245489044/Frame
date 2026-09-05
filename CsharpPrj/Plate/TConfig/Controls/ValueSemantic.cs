
namespace TConfig.Controls;

/// <summary>
/// 控件值语义（值语义契约第一维）：控件的"值"按语义命名，消费方（输入采集/参数绑定/命令 set/输出写值）
/// 按语义名编程，类型差异由契约（ControlValueContract）承载——不再按控件类型散落 if 分支。
/// </summary>
public enum ValueSemantic
{
    /// <summary>文本值（TextBox/Label/Button/DomainUpDown...）。</summary>
    Text,

    /// <summary>选中值（ListView/ComboBox/TreeView/DataGridView/ListBox 选中项）。</summary>
    Selection,

    /// <summary>内容集（ListView/ComboBox/ListBox 全部项的重填与枚举）。</summary>
    Content,

    /// <summary>布尔状态（CheckBox/RadioButton）。</summary>
    Checked,

    /// <summary>数值范围（NumericUpDown/TrackBar/ProgressBar）。</summary>
    Range,

    /// <summary>文件/图片路径（PictureBox.ImageLocation）。</summary>
    Path
}
