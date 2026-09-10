#nullable enable

using System.Windows.Forms;
using UniversalMiddleware.Abstractions;

namespace Share.ControlLookup;

/// <summary>
/// 实体树查找源（EntityTreeLookupSource）——经 <see cref="IEntityTreeService"/> 在「IEntityTreeRoot 宿主窗体」的
/// 实体树中按名查找控件。对应既有多级查找链中的「EntityTree」一层。作为中间优先级源
/// （ControlPool 之后、普通索引之前或之后由宿主决定；此处默认 <see cref="ControlPoolSource"/> 之后）。
/// </summary>
public sealed class EntityTreeLookupSource : IControlLookupSource
{
    private readonly IEntityTreeService _entityTreeService;
    private readonly Func<IEnumerable<Form>>? _formProvider;

    /// <param name="entityTreeService">实体树服务（.FindControlByName(rootEntity, name)）。</param>
    /// <param name="formProvider">IEntityTreeRoot 宿主窗体提供器；缺省用 Application.OpenForms。</param>
    public EntityTreeLookupSource(
        IEntityTreeService entityTreeService,
        int priority = 300,
        Func<IEnumerable<Form>>? formProvider = null)
    {
        _entityTreeService = entityTreeService ?? throw new ArgumentNullException(nameof(entityTreeService));
        Priority = priority;
        _formProvider = formProvider;
    }

    public string Name => "entity-tree";

    public int Priority { get; }

    public bool IsReady => _entityTreeService != null;

    public Control? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var forms = _formProvider?.Invoke() ?? Application.OpenForms.Cast<Form>();
        foreach (var form in forms)
        {
            if (form == null || form.IsDisposed || form is not IEntityTreeRoot root)
                continue;

            var control = _entityTreeService.FindControlByName(root.GetRootEntity(), name);
            if (control != null)
                return control;
        }

        return null;
    }

    public Control? FindByName(Control root, string name)
    {
        if (root is IEntityTreeRoot entityRoot
            && !string.IsNullOrWhiteSpace(name))
        {
            return _entityTreeService.FindControlByName(entityRoot.GetRootEntity(), name);
        }

        // root 非 IEntityTreeRoot 时退化为全局遍历（与 ControlRegistryAwareFinder 原跨 Form 语义对齐）。
        return FindByName(name);
    }
}
