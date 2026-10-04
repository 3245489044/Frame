
using System.Windows.Forms;
using MetaCommand;
using WinFormsValueContract;

namespace Modules.Common.WinForms
{
    /// <summary>
    /// WinForms 控件服务（P4 单轨化）：控件值读写委托值语义契约层（IControlService / ValueContractRegistry），
    /// 不再维护本类型适配器表（原 RegisterDefaultAdapters 的 16+ 适配器已由契约层内置吸收，
    /// 见 WinFormsValueContract/BuiltInValueContracts）。
    /// 本类仅保留宿主能力：查找（FindByName）、UI 线程调度（InvokeIfRequired/RunOnUiThread/ExecuteOnUiThread）、
    /// 事件名映射（BindEvent 用 RegisterEventMapping）。
    /// 阶段B（消费者抽象化）：依赖 <see cref="TConfig.Controls.IControlService"/> 抽象；无注入时回退默认后端
    /// （<see cref="ControlValueBridge"/> = 当前值语义后端），使具体实现可归后端所有。
    /// </summary>
    public class WinFormsControlService : ControlServiceBase
    {
        private readonly TConfig.Controls.IControlService _valueService;

        public WinFormsControlService()
            : this(new ControlValueBridge())
        {
        }

        public WinFormsControlService(TConfig.Controls.IControlService valueService)
        {
            // 必填端口（T）：控件值服务由装配点显式提供；无参构造是组件自装配的唯一入口。
            _valueService = valueService ?? throw new ArgumentNullException(nameof(valueService));

            // 事件名映射（BindEvent 用）保留原语义；控件值适配器不再在此注册（契约层统一）
            RegisterEventMapping("click", "Click");
            RegisterEventMapping("doubleclick", "DoubleClick");
            RegisterEventMapping("textchanged", "TextChanged");
            RegisterEventMapping("checkedchanged", "CheckedChanged");
            RegisterEventMapping("selectedindexchanged", "SelectedIndexChanged");
            RegisterEventMapping("valuechanged", "ValueChanged");
            RegisterEventMapping("selectedvaluechanged", "SelectedValueChanged");
            RegisterEventMapping("enter", "Enter");
            RegisterEventMapping("leave", "Leave");
            RegisterEventMapping("keypress", "KeyPress");
            RegisterEventMapping("keydown", "KeyDown");
            RegisterEventMapping("keyup", "KeyUp");
            RegisterEventMapping("mouseclick", "MouseClick");
            RegisterEventMapping("mousedoubleclick", "MouseDoubleClick");
        }

        protected override TConfig.Controls.IControlService ValueService => _valueService;


        public override void InvokeIfRequired(object control, Action action)
        {
            if (control is Control c)
            {
                if (c.InvokeRequired) c.Invoke(action);
                else action();
            }
            else action();
        }

        public override Task RunOnUiThread(object control, Func<Task> asyncAction)
        {
            if (control is Control c && c.InvokeRequired)
                return c.Invoke(asyncAction) as Task ?? Task.CompletedTask;
            return asyncAction();
        }

        public override void ExecuteOnUiThread(object control, Action action)
        {
            if (control is Control c)
            {
                if (c.InvokeRequired) c.Invoke(action);
                else action();
            }
            else action();
        }


        public override object? FindByName(string name)
        {
            foreach (Form form in Application.OpenForms)
            {
                var found = FindByName(form, name);
                if (found != null) return found;
            }
            return null;
        }

        public object? FindInForm(string formName, string controlName)
        {
            foreach (Form form in Application.OpenForms)
            {
                if (string.Equals(form.Name, formName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(form.Text, formName, StringComparison.OrdinalIgnoreCase))
                {
                    return FindByName(form, controlName);
                }
            }
            return null;
        }

        public List<Form> GetOpenForms() => Application.OpenForms.Cast<Form>().ToList();


        public void SetItems(object control, IEnumerable<string> items)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (items == null) throw new ArgumentNullException(nameof(items));

            InvokeIfRequired(control, () =>
            {
                switch (control)
                {
                    case ListView listView:
                        listView.BeginUpdate();
                        try
                        {
                            listView.Items.Clear();
                            foreach (var item in items)
                                listView.Items.Add(new ListViewItem(item));
                        }
                        finally
                        {
                            listView.EndUpdate();
                        }
                        break;

                    case ComboBox comboBox:
                        comboBox.Items.Clear();
                        foreach (var item in items)
                            comboBox.Items.Add(item);
                        break;

                    case ListBox listBox:
                        listBox.Items.Clear();
                        foreach (var item in items)
                            listBox.Items.Add(item);
                        break;

                    default:
                        SetValue(control, string.Join(",", items));
                        break;
                }
            });
        }
    }
}
