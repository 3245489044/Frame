
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrontEndDriven.ECS.Services;
using TConfig.Controls;
using FrontEndDriven.Finder;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Diagnostics;
using UniversalMiddleware.Abstractions;

namespace FrontEndDriven.Implementation;

public class ControlRegistryAwareFinder : IControlFinder
{
    private readonly IControlRegistry _registry;
    private readonly Share.ControlLookup.ControlLookupRegistry _lookup;
    private IEntityTreeService? _entityTreeService;
    private bool _entityTreeReady;

    public ControlRegistryAwareFinder(IControlRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        // 多级查找统一收敛到通用查找内核（Share.ControlLookup）：构造先注册 Registry 层（最低可信），
        // ControlPool / EntityTree 层在 SetEntityTreeService（ControlPool 就绪后）惰性注册。
        _lookup = new Share.ControlLookup.ControlLookupRegistry();
        _lookup.AddSource(new Share.ControlLookup.ControlIndexSource(_registry));
        // TreeWalkSource 末位兜底源（Priority=10000）：替换原 ControlFinderAdapter 纯深搜语义——未注入实体树/
        // 各级源未命中时回退控件树深搜，不再抛异常（E4 容错收敛）。
        _lookup.AddSource(new Share.ControlLookup.TreeWalkSource());
    }

    /// <summary>惰性注入 ECS 控件池（ControlPool 唯一事实源）与实体树服务，注册到查找内核。</summary>
    public void SetEntityTreeService(EntityManager entityManager, ControlPool controlPool)
    {
        _entityTreeService = new EntityTreeService(
            entityManager ?? throw new ArgumentNullException(nameof(entityManager)),
            controlPool ?? throw new ArgumentNullException(nameof(controlPool)));
        _entityTreeReady = true;

        // 注册高可信源（Priority 升序：ControlPool(100) → EntityTree(300) → Registry(1000，构造已注册)）
        _lookup.AddSource(new Share.ControlLookup.ControlPoolSource(controlPool));
        _lookup.AddSource(new Share.ControlLookup.EntityTreeLookupSource(_entityTreeService));
    }

    private IEntityTreeService GetEntityTreeService()
    {
        if (!_entityTreeReady || _entityTreeService == null)
            throw new InvalidOperationException(
                "EntityTreeService 尚未注入。请确保 EcsRenderPipeline.InitializeProtocolLayer() 已执行。" +
                "调用链：ControlServiceCollection → EcsRenderPipeline → SetEntityTreeService()");
        return _entityTreeService;
    }

    public object? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            FrontEndDrivenLog.Log("ControlRegistryAwareFinder_FindByName_null_or_empty");
            return null;
        }

        if (!_entityTreeReady)
        {
            FrontEndDrivenLog.Log("ControlRegistryAwareFinder_EntityTreeNotReady_fallback_treewalk",
                new { name });
        }

        // 多级查找收敛到查找内核（ControlPool → EntityTree → Registry → TreeWalk 深搜兜底，Priority 升序）
        var result = _lookup.FindByName(name);
        if (result.IsHit)
        {
            FrontEndDrivenLog.Log("ControlRegistryAwareFinder_Found_in_lookup", new { name, source = result.Source?.Name });
            return result.Control;
        }

        FrontEndDrivenLog.Log("ControlRegistryAwareFinder_Not_found", new { name, openFormsCount = Application.OpenForms.Count });
        return null;
    }

    public object? FindByName(object root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name))
            return null;

        if (root is not IEntityTreeRoot entityRoot)
            return null;

        var entityTree = GetEntityTreeService();
        var control = entityTree.FindControlByName(entityRoot.GetRootEntity(), name);
        if (control != null) return control;

        FrontEndDrivenLog.Log("ControlRegistryAwareFinder_FindByName_not_found_in_entity",
            new { name, rootType = root.GetType().Name });
        return null;
    }

    public T? FindByType<T>(object root) where T : class
    {
        if (root == null)
            return null;

        if (root is Control control)
        {
            return ControlFinder.FindAll(control, c => c is T).FirstOrDefault() as T;
        }

        return null;
    }

    public List<T> FindAllByType<T>(object root) where T : class
    {
        if (root == null)
            return new List<T>();

        if (root is Control control)
        {
            return ControlFinder.FindAll(control, c => c is T).Cast<T>().ToList();
        }

        return new List<T>();
    }

    public object? Find(object root, Func<object, bool> predicate)
    {
        if (root == null || predicate == null)
            return null;

        if (root is Control control)
        {
            return ControlFinder.Find(control, c => predicate(c));
        }

        return null;
    }

    public List<object> FindAll(object root, Func<object, bool> predicate)
    {
        if (root == null || predicate == null)
            return new List<object>();

        if (root is Control control)
        {
            return ControlFinder.FindAll(control, c => predicate(c)).Cast<object>().ToList();
        }

        return new List<object>();
    }

    public object? FindInForm(string formName, string controlName)
    {
        if (string.IsNullOrWhiteSpace(formName) || string.IsNullOrWhiteSpace(controlName))
            return null;

        var entityTree = GetEntityTreeService();

        var result = ControlFinder.FindAcrossForms(
            formMatcher: form =>
                string.Equals(form.Name, formName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(form.Text, formName, StringComparison.OrdinalIgnoreCase),
            controlLookup: form =>
            {
                if (form is IEntityTreeRoot root)
                {
                    var control = entityTree.FindControlByName(root.GetRootEntity(), controlName);
                    if (control != null)
                        return control;

                    FrontEndDrivenLog.Log("ControlRegistryAwareFinder_FindInForm_not_found_in_entity_tree",
                        new { formName, controlName });
                }
                return null;
            });

        return result;
    }


    public object? FindForm(object control)
    {
        if (control is Control c)
        {
            return c.FindForm();
        }

        return null;
    }

    public bool IsDescendantOf(object control, object ancestor)
    {
        if (control is Control c && ancestor is Control a)
            return ControlFinder.IsDescendantOf(c, a);

        return false;
    }

    public int GetDepth(object control)
    {
        if (control is Control c)
            return ControlFinder.GetDepth(c);

        return -1;
    }


    public TControl? FindByName<TControl>(string controlName) where TControl : Control
        => FindByName(controlName) as TControl;

    public Control? FindAcrossForms(
        Func<Form, bool> formMatcher,
        Func<Form, Control?> controlLookup)
        => ControlFinder.FindAcrossForms(formMatcher, controlLookup);

    public Control? FindRecursive(Control parent, string name)
        => ControlFinder.FindRecursive(parent, name);
}


