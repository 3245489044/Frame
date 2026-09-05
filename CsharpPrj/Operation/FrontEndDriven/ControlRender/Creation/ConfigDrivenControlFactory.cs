
using System;
using ECS.Core;
using ECS.Bridge;
using TConfig;
using TConfig.ECS.Components;
using ECS.Components;
using FrontEndDriven.ECS;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.Container;
using FrontEndDriven.Diagnostics;

namespace FrontEndDriven.ECS.Creation;

public class ConfigDrivenControlFactory
{
    private readonly EntityManager _entityManager;
    private readonly ControlFactory _controlFactory;

    private string _currentScope = "MainLayout";

    public ConfigDrivenControlFactory(
        EntityManager entityManager,
        ControlFactory controlFactory)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlFactory = controlFactory ?? throw new ArgumentNullException(nameof(controlFactory));
    }
    
    public Entity Convert(UIConfiguration config, string scope = "MainLayout")
    {
        _currentScope = scope;
        var rootEntity = CreateRootEntity(config);
        ConvertChildren(config, rootEntity);
        return rootEntity;
    }

    public Entity CreateRootEntity(UIConfiguration config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // root 实体为容器面板，非窗体：Type="panel" 保证 ControlTypeRegistry/ControlStrategyRegistry 可创建实际控件，
        // 避免 Type="form" 未被注册导致 root 控件创建失败、关系挂载错位触发 WinForms 循环控件引用
        // S2（作用域根命名隔离·根治 D1 同名 root 覆盖）：根名由固定 "root" 改为 "root:{scope}"（scope 唯一化），
        // 使 _controlPool 按实体名注册天然跨 scope 唯一，杜绝各 scope 同名 "root" 实体在 _nameToControl 中互相覆盖。
        var rootName = $"root:{_currentScope}";

        var rootEntity = _controlFactory.CreateFromConfig(new ControlConfig
        {
            Type = "panel",
            Name = rootName,
            Text = config.Form?.Title ?? "Root"
        });

        AddControlInfoComponent(rootEntity, rootName, config.Form?.Title ?? "Root", true);

        var width = config.Form?.Width ?? 1200;
        var height = config.Form?.Height ?? 800;
        _entityManager.AddComponent(rootEntity, new ContainerSizeComponent
        {
            Left = 0,
            Top = 0,
            Width = width > 0 ? width : 1200,
            Height = height > 0 ? height : 800
        });

        _entityManager.AddComponent(rootEntity, new ProportionalLayoutComponent
        {
            Left = 0, Top = 0, Width = 1, Height = 1
        });

        AddPendingComponents(rootEntity, includeCapability: false);
        // E3（单轨澄清）：root 实体**不建实体控件**——根容器控件由宿主注入路径（RegisterRootContainer / ContainerRegistry）
        // 注册，AddPendingComponents(includeCapability:false) 跳过 PendingCapabilityComponent，
        // 使 root 不被 EcsCapabilityApplier 重复创建（防双轨重复创建；原实现「加完再删」冗余往返已收敛，C8）。
        // 普通控件（view/control）则保留 PendingCapabilityComponent，统一经 EcsCapabilityApplier 建树并 Register(entity,name,control)。
        // root 的物理挂载与宿主寻址完全依赖 HostMountService.AttachRootToHost 按 "root:{scope}" 解析，不再经实体->控件映射。

        return rootEntity;
    }

    public void ConvertChildren(UIConfiguration config, Entity rootEntity)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (!rootEntity.IsValid)
            throw new ArgumentException("rootEntity 无效，请确保已通过 CreateRootEntity 创建。", nameof(rootEntity));

        if (config.Views is { Count: > 0 })
        {
            for (int i = 0; i < config.Views.Count; i++)
            {
                ConvertView(config.Views[i], rootEntity, i);
            }
        }
    }

    private void ConvertView(ViewConfig view, Entity parentEntity, int siblingOrder)
    {
        var controlConfig = new ControlConfig
        {
            Type = string.IsNullOrEmpty(view.Type) ? "panel" : view.Type,
            Name = view.Name,
            Text = view.Name,
            Controls = view.Controls,
            Layout = new LayoutPosition { Left = 0, Top = 0, Width = 1, Height = 1 }
        };

        ConvertControl(controlConfig, parentEntity, "view", siblingOrder);
    }

    private void ConvertControl(ControlConfig control, Entity parentEntity, string? parentType, int siblingOrder = 0)
    {
        // 结构校验（声明层早期拦截，避免挂载层静默跳过产生孤儿控件）：
        // 由统一校验器 ValidateStructure 判定容器嵌套约束（tabpage/splitterpanel 宿主、tabcontrol 子类型、容器叶子）。
        // 例：pageConversation 被误写成 tabcontrol 的同级兄弟时，此处当场报 Error 而非静默 orphaning。
        ValidateStructure(control, parentType);

        var entity = _controlFactory.CreateFromConfig(control);

        AddControlInfoComponent(entity, control.Name, control.Text, control.Enabled ?? true);

        // 兄弟顺序建模：记录控件在父容器声明数组中的 0 基下标，使挂载序 = 声明序（不依赖 ECS 枚举序）。
        _entityManager.AddOrSetComponent(entity, new SiblingOrderComponent
        {
            OrderIndex = siblingOrder
        });

        _entityManager.AddComponent(entity, new ParentEntityComponent
        {
            ParentEntity = parentEntity
        });

        AddPendingComponents(entity);
        ApplyHiddenSemantics(entity, control);

        // 容器语义组件注入（TabControl 一等容器）：
        //  tabcontrol → TabContainerComponent(SelectedIndex) 承载选中态（显式注入，不依赖 WinForms 隐式 auto-select）。
        //  TabPage 语义由物理类型在挂载端口与可见性检查器裁决，不设独立组件。
        if (string.Equals(control.Type, "tabcontrol", StringComparison.OrdinalIgnoreCase))
        {
            _entityManager.AddOrSetComponent(entity, new TabContainerComponent
            {
                SelectedIndex = control.SelectedIndex ?? 0
            });
        }

        if (string.Equals(control.Type, "splitcontainer", StringComparison.OrdinalIgnoreCase)
            && control.SplitterDistance.HasValue)
        {
            var ratio = new SplitterRatioComponent
            {
                Ratio = Math.Clamp(control.SplitterDistance.Value, 0.0, 1.0)
            };

            _entityManager.AddOrSetComponent(entity, ratio);
        }


        if (control.Controls is { Count: > 0 })
        {
            for (int i = 0; i < control.Controls.Count; i++)
            {
                ConvertControl(control.Controls[i], entity, control.Type, i);
            }
        }

        if (control.Panel1Controls is { Count: > 0 })
        {
            var panel1Entity = CreateSplitterPanelEntity(entity, $"{control.Name}_Panel1", "Panel1", control.Orientation);
            for (int i = 0; i < control.Panel1Controls.Count; i++)
            {
                ConvertControl(control.Panel1Controls[i], panel1Entity, "splitterpanel", i);
            }
        }

        if (control.Panel2Controls is { Count: > 0 })
        {
            var panel2Entity = CreateSplitterPanelEntity(entity, $"{control.Name}_Panel2", "Panel2", control.Orientation);
            for (int i = 0; i < control.Panel2Controls.Count; i++)
            {
                ConvertControl(control.Panel2Controls[i], panel2Entity, "splitterpanel", i);
            }
        }
    }

    private Entity CreateSplitterPanelEntity(Entity parentEntity, string name, string panelSide, string? orientation)
    {
        var panelEntity = _controlFactory.CreateFromConfig(new ControlConfig
        {
            Type = "splitterpanel",
            Name = name,
            Text = name,
            Layout = new LayoutPosition { Left = 0, Top = 0, Width = 1, Height = 1 }
        });

        AddControlInfoComponent(panelEntity, name, name, true);

        _entityManager.AddComponent(panelEntity, new ParentEntityComponent
        {
            ParentEntity = parentEntity
        });

        _entityManager.AddComponent(panelEntity, new SplitterPanelComponent
        {
            PanelSide = panelSide,
            Orientation = orientation ?? "Vertical"
        });

        AddPendingComponents(panelEntity);

        return panelEntity;
    }

    /// <summary>
    /// M4 配置结构前置校验（统一校验器）：在声明层早期拦截容器嵌套约束，避免挂载层静默跳过产生孤儿控件。
    /// 约束：tabpage/splitterpanel 必须嵌套于合法宿主；tabcontrol 直接子控件必须为 tabpage；
    /// 必须含子的容器（tabcontrol/splitcontainer）不得作为叶子。违规一律输出 Error（提示人工修配置）。
    /// </summary>
    private void ValidateStructure(ControlConfig control, string? parentType)
    {
        if (control == null)
            return;

        var type = control.Type;

        // M4 既有：tabpage 必须嵌套于 tabcontrol，否则无 TabControl 宿主 → 不显示。
        if (Is(type, "tabpage") && !Is(parentType, "tabcontrol"))
        {
            FrontEndDrivenLog.Log("ConfigDrivenControlFactory_tabpage_misnested",
                new { controlName = control.Name, parentType = parentType ?? "(none)" });
        }

        // M4 扩展1：splitterpanel 必须嵌套于 splitcontainer（配置显式声明 splitterpanel 类型时）。
        if (Is(type, "splitterpanel") && !Is(parentType, "splitcontainer"))
        {
            FrontEndDrivenLog.Log("ConfigDrivenControlFactory_splitterpanel_misnested",
                new { controlName = control.Name, parentType = parentType ?? "(none)" });
        }

        // M4 扩展2：tabcontrol 的直接子控件必须为 tabpage（tabpage 是 tabcontrol 的唯一合法子类型）。
        if (Is(parentType, "tabcontrol") && !Is(type, "tabpage"))
        {
            FrontEndDrivenLog.Log("ConfigDrivenControlFactory_tabcontrol_nonpage_child",
                new { controlName = control.Name, childType = type ?? "(none)", parentType });
        }

        // M4 扩展3：必须含子的容器（tabcontrol/splitcontainer）不得作为叶子（无任何子控件声明）。
        if (IsRequiredChildContainer(type) && !HasAnyChildren(control))
        {
            FrontEndDrivenLog.Log("ConfigDrivenControlFactory_container_leaf",
                new { controlName = control.Name, type = type ?? "(none)" });
        }
    }

    private static bool Is(string? a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>必须含子控件的容器类型（缺子即结构错误；panel/groupbox 可合法为空占位）。</summary>
    private static bool IsRequiredChildContainer(string? type)
        => Is(type, "tabcontrol") || Is(type, "splitcontainer");

    private static bool HasAnyChildren(ControlConfig control)
        => control.Controls is { Count: > 0 }
           || control.Panel1Controls is { Count: > 0 }
           || control.Panel2Controls is { Count: > 0 };

    private void AddControlInfoComponent(Entity entity, string name, string text, bool enabled)
    {
        _entityManager.AddComponent(entity, new ControlInfoComponent
        {
            Name = name ?? string.Empty,
            Text = text ?? string.Empty,
            Enabled = enabled,
            Visible = true
        });
    }

    private void AddPendingComponents(Entity entity, bool includeCapability = true)
    {
        // C8：includeCapability 参数化——root 实体不需要 PendingCapabilityComponent（不建物理控件），
        // 由 CreateRootEntity 传 false，消除「先 AddPendingComponents 再 RemoveComponent」的冗余往返。
        if (includeCapability && !_entityManager.HasComponent<PendingCapabilityComponent>(entity))
            _entityManager.AddComponent(entity, new PendingCapabilityComponent());

        if (!_entityManager.HasComponent<PendingLayoutComponent>(entity))
            _entityManager.AddComponent(entity, new PendingLayoutComponent());

        if (!_entityManager.HasComponent<PendingStyleComponent>(entity))
            _entityManager.AddComponent(entity, new PendingStyleComponent());

        if (!_entityManager.HasComponent<EventBindingStateComponent>(entity))
            _entityManager.AddComponent(entity, new EventBindingStateComponent
            {
                State = EventBindingState.Pending
            });

        if (!_entityManager.HasComponent<ScopeComponent>(entity))
            _entityManager.AddComponent(entity, new ScopeComponent
            {
                Scope = _currentScope
            });
    }

    /// <summary>
    /// 从配置声明 hiddenSemantics 填充可见性语义组件（Phase ②：配置驱动的扩展点）。
    /// 缺省（未声明/无效值）不写组件 → 可见性解析器按类型/运行时结构语义（tab/空容器）或 ExpectVisible 兜底。
    /// </summary>
    private void ApplyHiddenSemantics(Entity entity, ControlConfig control)
    {
        if (string.IsNullOrWhiteSpace(control.HiddenSemantics))
            return;

        if (!TryParseVisibilityIntent(control.HiddenSemantics, out var intent))
        {
            FrontEndDrivenLog.Log("ConfigDrivenControlFactory_invalid_hiddenSemantics",
                new { controlName = control.Name, value = control.HiddenSemantics });
            return;
        }

        _entityManager.AddComponent(entity, new VisibilitySemanticsComponent
        {
            Intent = intent,
            Reason = "config-declared",
            // 语义隐藏（如整页/条件区）通常应被子控件继承；显式隐藏(deferred)与应可见不继承。
            InheritToChildren = intent == VisibilityIntent.SemanticHidden
        });
    }

    /// <summary>
    /// hiddenSemantics 字符串 → VisibilityIntent 映射（大小写不敏感）。未知值返回 false 并置 ExpectVisible。
    /// 合法输入源 = FrontEndDrivenConstants.HiddenSemanticsAllowedValues（与 ContractBuilder schema 校验共用单一真源）——
    /// 新增语义值须同时登记常量并在本 switch 补映射，避免「校验通过但解析失败」的漂移。
    /// </summary>
    private static bool TryParseVisibilityIntent(string value, out VisibilityIntent intent)
    {
        var trimmed = value.Trim();
        if (!FrontEndDriven.FrontEndDrivenConstants.HiddenSemanticsAllowedValues.Contains(trimmed))
        {
            intent = VisibilityIntent.ExpectVisible;
            return false;
        }

        switch (trimmed.ToLowerInvariant())
        {
            case "expect":
            case "expect-visible":
                intent = VisibilityIntent.ExpectVisible;
                return true;
            case "semantic":
            case "semantic-hidden":
                intent = VisibilityIntent.SemanticHidden;
                return true;
            case "deferred":
            case "deferred-hidden":
                intent = VisibilityIntent.DeferredHidden;
                return true;
            default:
                intent = VisibilityIntent.ExpectVisible;
                return false;
        }
    }
}