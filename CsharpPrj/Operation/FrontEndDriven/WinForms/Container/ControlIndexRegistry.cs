
#nullable enable

using System.Windows.Forms;
using TConfig.Controls;
using FrameworkAbstractions.Threading;
using FrontEndDriven.Finder;

namespace FrontEndDriven.Container;

/// <summary>
/// 普通控件索引（ControlIndexRegistry，原 ContainerRegistry[ControlIndex]）——「控件名 → Control」普通控件索引服务。
/// 容器聚拢点重构后，本类仅承载普通控件（非容器角色）索引，供 ControlRegistryAwareFinder 等既有调用方使用；
/// 容器角色控件统一由 <see cref="ContainerRegistry"/>（容器聚拢点）登记（SingleImplementation：容器事实源唯一）。
/// </summary>
public class ControlIndexRegistry : IControlRegistry
{
    private readonly SynchronizedAccessor<ControlIndex> _syncIndex;

    public ControlIndexRegistry()
    {
        _syncIndex = new SynchronizedAccessor<ControlIndex>(new ControlIndex());
    }


    public void Register(string name, object control)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("控件名称不能为空", nameof(name));

        if (control is not Control ctrl)
            throw new ArgumentException("注册的控件必须是 Control 类型", nameof(control));

        _syncIndex.Write(idx => idx.Register(ctrl));
    }

    public void Unregister(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        _syncIndex.Write(idx => idx.UnregisterByName(name));
    }

    public object? Find(string name) => FindByName(name);

    public bool TryGetValue(string name, out object? control)
    {
        control = FindByName(name);
        return control != null;
    }

    public void Clear() => _syncIndex.Write(idx => idx.Clear());


    public void Rebuild(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _syncIndex.Write(idx => idx.Rebuild(root));
    }

    public Control? FindByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return _syncIndex.Read(idx => idx.FindByName(name));
    }

    public T? FindByName<T>(string name) where T : Control
        => FindByName(name) as T;

    public bool Contains(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return _syncIndex.Read(idx => idx.Contains(name));
    }

    public IEnumerable<string> GetKeys()
        => _syncIndex.Read(idx => idx.GetKeys().ToList());

    public IEnumerable<Control> GetAll()
        => _syncIndex.Read(idx => idx.GetAll().ToList());

    public int Count => _syncIndex.Read(idx => idx.Count);
}
