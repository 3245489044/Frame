#nullable enable

using System.Windows.Forms;

namespace Share.ControlLookup;

/// <summary>
/// WinForms 控件树递归深搜源（TreeWalkSource）——基于 <see cref="ControlTraverser"/> 的统一遍历原语。
/// 语义 = 「按名递归深搜」，内部统一采用 <see cref="Control.ControlCollection.Find"/>（searchAllChildren=true），
/// 与官方单一面 <c>FrameworkAbstractions.WinFormsAbstractions.IControlFinder</c> 的既定语义对齐。
/// 作为多级查找链中最低优先级的兜底源（控件树深搜最通用、最慢）。
/// </summary>
public sealed class TreeWalkSource : IControlLookupSource
{
    private readonly Func<IEnumerable<Form>>? _formProvider;

    /// <param name="priority">默认用大数（最后尝试）。</param>
    /// <param name="formProvider">全局扫描时的 Form 提供器；缺省用 <c>Application.OpenForms</c>。</param>
    public TreeWalkSource(int priority = 10_000, Func<IEnumerable<Form>>? formProvider = null)
    {
        Priority = priority;
        _formProvider = formProvider;
    }

    public string Name => "tree-walk";

    public int Priority { get; }

    public bool IsReady => true;

    /// <summary>在当前可见/全部 Form 的控件树中按名深搜（兼容既有全局 FindAcrossForms 语义）。</summary>
    public Control? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var forms = _formProvider?.Invoke() ?? Application.OpenForms.Cast<Form>();
        foreach (var form in forms)
        {
            if (form == null || form.IsDisposed)
                continue;

            var found = ControlTraverser.FindByName(form, name);
            if (found != null)
                return found;
        }

        return null;
    }

    public Control? FindByName(Control root, string name)
        => string.IsNullOrWhiteSpace(name) ? null : ControlTraverser.FindByName(root, name);
}
