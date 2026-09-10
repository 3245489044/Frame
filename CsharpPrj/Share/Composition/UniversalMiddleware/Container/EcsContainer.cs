
#nullable enable
using System;
using System.ComponentModel;
using System.Windows.Forms;
using UniversalMiddleware.Abstractions;
using UniversalMiddleware.Conversion;
using UniversalMiddleware.Conversion.Components;
using UniversalMiddleware.Container.Events;
using UniversalMiddleware.Container.Lifecycle;
using Framework.Tree;
using TConfig.ECS;

namespace UniversalMiddleware.Container;

public class EcsContainer : Panel, IContainer
{

    private readonly ContainerNode _containerNode;
    private readonly ContainerLifecycle _lifecycle = new();
    private readonly JsonToEntityConverter _converter;


    public string ContainerName => Name;

    public JsonToEntityConverter Converter => _converter;

    public ContainerComponent? CurrentComponent { get; private set; }

    public bool IsLoaded => CurrentComponent != null;

    public ContainerNode ContainerNode => _containerNode;

    public ContainerLifecycle Lifecycle => _lifecycle;

    public ContainerEventManager EventManager { get; }


    ContainerConfig? IConfigContainer.ContainerConfig
    {
        get => null;
        set { }
    }

    async Task IConfigContainer.LoadConfigAsync(TConfig.UIConfiguration config)
    {
        await Task.CompletedTask;
    }

    IConfigContainer? IConfigContainer.FindSubContainer(string name)
        => FindSubContainer(name);

    IConfigContainer IConfigContainer.CreateSubContainer(string name, ContainerConfig? config)
        => CreateSubContainer(name, config);

    IEnumerable<IConfigContainer> IConfigContainer.GetSubContainers()
        => GetSubContainers();

    object? IConfigContainer.FindControlByName(string name)
        => FindControlByName(name);

    T? IConfigContainer.FindControl<T>(string name) where T : class
        => FindControl<T>(name);

    void IConfigContainer.ClearContent()
        => ClearContent();

    event EventHandler<ContainerConfigLoadedEventArgs>? IConfigContainer.ConfigLoaded
    {
        add => _configLoaded += value;
        remove => _configLoaded -= value;
    }
    private EventHandler<ContainerConfigLoadedEventArgs>? _configLoaded;

    event EventHandler? IConfigContainer.ContentChanged
    {
        add => ContentChanged += value;
        remove => ContentChanged -= value;
    }


    public event EventHandler? Loaded;

    public event EventHandler? ContentChanged;

    public event EventHandler? Unloaded;


    public EcsContainer(string? name = null, JsonToEntityConverter? converter = null)
    {
        if (!string.IsNullOrEmpty(name))
            Name = name;

        _converter = converter ?? new JsonToEntityConverter();
        _containerNode = new ContainerNode(Name, this);
        EventManager = new ContainerEventManager(this);
    }


    public void LoadFromJson(string json)
    {
        try
        {
            CurrentComponent = _converter.ParseContainer(json);
            OnLoaded();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载容器 '{Name}' 配置失败: {ex.Message}", ex);
        }
    }

    public void LoadFromComponent(ContainerComponent component)
    {
        try
        {
            CurrentComponent = component;
            OnLoaded();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载容器 '{Name}' 组件失败: {ex.Message}", ex);
        }
    }

    public void Unload()
    {
        if (!IsLoaded) return;

        CurrentComponent = null;
        Unloaded?.Invoke(this, EventArgs.Empty);
    }


    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IEntityTreeService? TreeService { get; set; }

    public virtual Entity GetRootEntity() => Entity.Invalid;


    public IConfigContainer FindSubContainer(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name), "容器名称不能为空");

        if (TreeService == null)
            throw new InvalidOperationException(
                $"EntityTreeService 未注入，无法查找容器 '{name}'。请确保在 InitializeRenderPipeline 中创建了 EntityTreeService。");

        var rootEntity = GetRootEntity();
        if (!rootEntity.IsValid)
            throw new InvalidOperationException(
                $"根 Entity 不可用，无法查找容器 '{name}'。请确保渲染管线已初始化。");

        var container = TreeService.FindContainerByName(rootEntity, name);
        if (container == null)
        {
            var rootInfo = rootEntity.ToString();
            throw new InvalidOperationException(
                $"容器 '{name}' 未找到。RootEntity={rootInfo}。请检查配置中是否包含名为 '{name}' 的容器。");
        }

        return container;
    }

    public virtual IConfigContainer CreateSubContainer(string name, ContainerConfig? config = null)
    {
        var container = new EcsContainer(name);
        Controls.Add(container);
        return container;
    }

    public IEnumerable<IConfigContainer> GetSubContainers()
    {
        if (TreeService == null)
            throw new InvalidOperationException(
                "EntityTreeService 未注入，无法获取子容器列表。请确保在 InitializeRenderPipeline 中创建了 EntityTreeService。");

        var rootEntity = GetRootEntity();
        if (!rootEntity.IsValid)
            throw new InvalidOperationException(
                "根 Entity 不可用，无法获取子容器列表。请确保渲染管线已初始化。");

        return TreeService.FindAllContainers(rootEntity);
    }


    public Control? FindControlByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name), "控件名称不能为空");

        if (TreeService == null)
            throw new InvalidOperationException(
                $"EntityTreeService 未注入，无法查找控件 '{name}'。请确保在 InitializeRenderPipeline 中创建了 EntityTreeService。");

        var rootEntity = GetRootEntity();
        if (!rootEntity.IsValid)
            throw new InvalidOperationException(
                $"根 Entity 不可用，无法查找控件 '{name}'。请确保渲染管线已初始化。");

        return TreeService.FindControlByName(rootEntity, name);
    }

    public T? FindControl<T>(string name) where T : class
        => FindControlByName(name) as T;


    public void ClearContent()
    {
        SuspendLayout();
        try
        {
            Controls.Clear();
            CurrentComponent = null;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            ResumeLayout(true);
        }
    }


    ContainerNode IContainer.Node => ContainerNode;

    ContainerLifecycle IContainer.Lifecycle => Lifecycle;

    ContainerEventManager IContainer.EventManager => EventManager;


    protected virtual void OnLoaded()
    {
        Loaded?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnContentChanged()
    {
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifecycle.TryTransitionTo(ContainerLifecycleState.Disposing);
            _lifecycle.TryTransitionTo(ContainerLifecycleState.Disposed);
        }
        base.Dispose(disposing);
    }
}
