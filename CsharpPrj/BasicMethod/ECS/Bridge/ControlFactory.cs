
using ECS.Core;
using TConfig;
using TConfig.ECS.Components;

namespace ECS.Bridge;

public class ControlFactory
{
    private readonly EntityManager _entityManager;

    public ControlFactory(EntityManager entityManager)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    }

    public Entity CreateFromConfig(ControlConfig config)
    {
        var entity = _entityManager.CreateEntity();

        _entityManager.AddComponent(entity, new ControlTypeComponent
        {
            Type = config.Type,
            Name = config.Name ?? string.Empty,
            Text = config.Text ?? string.Empty,
            Enabled = config.Enabled ?? true
        });

        AddLayoutComponents(entity, config);

        AddStyleComponents(entity, config);

        AddBehaviorComponents(entity, config);

        AddStaticDataComponents(entity, config);

        AddEventComponents(entity, config);

        _entityManager.AddComponent(entity, new ControlStateComponent
        {
            IsCreated = false,
            IsLayoutApplied = false,
            IsStyleApplied = false,
            IsEventsBound = false,
            LastUpdated = DateTime.UtcNow
        });

        return entity;
    }

    private void AddLayoutComponents(Entity entity, ControlConfig config)
    {
        bool layoutIntentResolved = false;

        if (config.Layout is { } layout)
        {
            // M2·布局意图化：意图分流（缺省 proportional 存量兼容；dock/anchor 由 RenderPort 原生接管）
            var intent = string.IsNullOrWhiteSpace(layout.Intent)
                ? "proportional"
                : layout.Intent!.Trim().ToLowerInvariant();

            switch (intent)
            {
                case "dock":
                    _entityManager.AddComponent(entity, new LayoutIntentComponent
                    {
                        Intent = "dock",
                        Dock = string.IsNullOrWhiteSpace(layout.Dock) ? "Fill" : layout.Dock!
                    });
                    layoutIntentResolved = true;
                    break;

                case "anchor":
                    _entityManager.AddComponent(entity, new LayoutIntentComponent
                    {
                        Intent = "anchor",
                        Anchor = layout.Anchor ?? string.Empty
                    });
                    layoutIntentResolved = true;
                    break;

                default: // "proportional" / "fixed"（fixed 由下方 PixelLayoutComponent 承载像素直定位）
                    if (intent != "fixed")
                    {
                        var layoutLeft = layout.Left;
                        var layoutTop = layout.Top;

                        // bottom/right 锚定：Top/Left 为 -1 未设置哨兵时，由 Bottom/Right 换算为正向比例 top/left
                        // （ECS LayoutSystem 按正向比例结算：Top = prop.Top * parentHeight）。
                        // 修复：此前直接拷贝 layout.Top（bottom 锚定控件为 -1 哨兵）→ 像素 Top = -parentHeight
                        // （status-bar 模板 {left,bottom,width,height} 实测 -815），Bottom/Right 锚定被整体忽略。
                        if (layout.Bottom >= 0 && layoutTop < 0 && layout.Height > 0)
                            layoutTop = 1.0 - layout.Bottom - layout.Height;
                        if (layout.Right >= 0 && layoutLeft < 0 && layout.Width > 0)
                            layoutLeft = 1.0 - layout.Right - layout.Width;

                        _entityManager.AddComponent(entity, new ProportionalLayoutComponent
                        {
                            Left = (float)layoutLeft,
                            Top = (float)layoutTop,
                            Width = (float)layout.Width,
                            Height = (float)layout.Height
                        });
                        layoutIntentResolved = true;
                    }
                    break;
            }
        }

        // B·布局意图单一语法：节点级 dock 字段（非 layout.intent 语法）统一投影为 LayoutIntentComponent，
        // 消除「同一意图两种表达、仅 layout.intent 接线」的双语法分裂（SingleImplementation）。
        // 此前 sideContainer 的节点级 "dock": "Fill" 在 ECS 真源中整体丢失，仅靠 EcsConfigContainer
        // 构造函数物理兜底——物理与 ECS 各说各话（布局意图丢失根因）。
        if (!layoutIntentResolved
            && !string.IsNullOrWhiteSpace(config.Dock)
            && !string.Equals(config.Dock, "None", StringComparison.OrdinalIgnoreCase))
        {
            _entityManager.AddComponent(entity, new LayoutIntentComponent
            {
                Intent = "dock",
                Dock = config.Dock!
            });
            layoutIntentResolved = true;
        }

        // A·容器默认布局语义：type=container 且未解析出任何布局意图 → 默认 Fill 父容器
        // （与 WinForms 侧 EcsConfigContainer.Dock=Fill 语义对齐）。保证空载容器（autoLoad=false）在
        // ECS 真源中即有尺寸，不再静默退化为 0x0——布局完整性：解析不出 → 明确默认语义而非无真相。
        if (!layoutIntentResolved
            && string.Equals(config.Type, "container", StringComparison.OrdinalIgnoreCase))
        {
            _entityManager.AddComponent(entity, new LayoutIntentComponent
            {
                Intent = "dock",
                Dock = "Fill"
            });
        }

        _entityManager.AddComponent(entity, new PixelLayoutComponent
        {
            Left = config.X,
            Top = config.Y,
            Width = config.Width,
            Height = config.Height
        });
    }

    private void AddStyleComponents(Entity entity, ControlConfig config)
    {
        if (config.Style is { } style)
        {
            _entityManager.AddComponent(entity, new ColorStyleComponent
            {
                BackColor = style.BackColor ?? string.Empty,
                ForeColor = style.ForeColor ?? string.Empty,
                BorderColor = style.BorderColor ?? string.Empty,
                BorderWidth = style.BorderWidth
            });

            _entityManager.AddComponent(entity, new FontStyleComponent
            {
                FontFamily = style.FontFamily ?? "Microsoft Sans Serif",
                FontSize = style.FontSize,
                Bold = style.FontStyle?.Contains("Bold") ?? false,
                Italic = style.FontStyle?.Contains("Italic") ?? false
            });
        }
    }

    private void AddBehaviorComponents(Entity entity, ControlConfig config)
    {
        // 行为属性（multiline/readOnly/scrollBars）为静态配置事实，写入 ECS 组件供创建期应用物理控件。
        bool hasBehavior = false;
        var behavior = new ControlBehaviorComponent { ScrollBars = string.Empty };

        if (config.Multiline.HasValue)
        {
            behavior.Multiline = config.Multiline.Value;
            hasBehavior = true;
        }

        if (config.ReadOnly.HasValue)
        {
            behavior.ReadOnly = config.ReadOnly.Value;
            hasBehavior = true;
        }

        if (!string.IsNullOrWhiteSpace(config.ScrollBars))
        {
            behavior.ScrollBars = config.ScrollBars!;
            hasBehavior = true;
        }

        if (hasBehavior)
        {
            _entityManager.AddComponent(entity, behavior);
        }
    }

    /// <summary>
    /// 静态数据捕获：把 ControlConfig 中被旧管线整体丢弃的静态属性（items/selectedIndex/checkedItems/columns/view/checked/dropDownStyle）
    /// 写入 ControlStaticDataComponent（单一权威），由 WinFormsOutputPort.CreateControl 经 ControlStaticDataApplier 创建期应用物理控件。
    /// 与 AddBehaviorComponents 的 hasBehavior 惰性写入一致——仅当存在任一静态字段才写组件，避免空组件污染。
    /// </summary>
    private void AddStaticDataComponents(Entity entity, ControlConfig config)
    {
        // tabcontrol 的 SelectedIndex 由 ConvertControl 注入 TabContainerComponent（ApplyTabSelection 消费），
        // 不属本子系统范围，提前排除，避免冗余组件与「无应用器」告警。
        if (string.Equals(config.Type, "tabcontrol", StringComparison.OrdinalIgnoreCase))
            return;

        bool hasStatic = config.Items is { Count: > 0 }
            || config.SelectedIndex.HasValue
            || config.CheckedItems is { Count: > 0 }
            || config.Columns is { Count: > 0 }
            || config.ListViewColumns is { Count: > 0 }
            || !string.IsNullOrWhiteSpace(config.View)
            || config.FullRowSelect == true
            || config.GridLines == true
            || !string.IsNullOrWhiteSpace(config.DropDownStyle)
            || config.Checked.HasValue;

        if (!hasStatic)
            return;

        List<ListViewColumnConfig>? columns = null;
        if (config.ListViewColumns is { Count: > 0 })
        {
            columns = config.ListViewColumns;
        }
        else if (config.Columns is { Count: > 0 })
        {
            columns = new List<ListViewColumnConfig>(config.Columns.Count);
            foreach (var col in config.Columns)
            {
                columns.Add(new ListViewColumnConfig
                {
                    Name = col.Name,
                    Text = col.Text,
                    Width = col.Width
                });
            }
        }

        _entityManager.AddComponent(entity, new ControlStaticDataComponent
        {
            Items = config.Items,
            SelectedIndex = config.SelectedIndex,
            CheckedItems = config.CheckedItems,
            Columns = columns,
            View = string.IsNullOrWhiteSpace(config.View) ? null : config.View,
            FullRowSelect = config.FullRowSelect ?? false,
            GridLines = config.GridLines ?? false,
            DropDownStyle = string.IsNullOrWhiteSpace(config.DropDownStyle) ? null : config.DropDownStyle,
            Checked = config.Checked
        });
    }

    private void AddEventComponents(Entity entity, ControlConfig config)
    {
        var bindings = new List<EventBindingComponent>();

        if (config.Events is { } events)
        {
            var hasDictionaryBindings = false;

            foreach (var kvp in events.EventsDictionary)
            {
                var eventName = kvp.Key;
                foreach (var funcName in kvp.Value)
                {
                    if (string.IsNullOrWhiteSpace(funcName))
                        continue;
                    hasDictionaryBindings = true;
                    bindings.Add(new EventBindingComponent
                    {
                        EventName = eventName,
                        FunctionName = funcName,
                        Parameters = Array.Empty<TConfig.ParameterBinding>(),
                        OutputTarget = string.Empty
                    });
                }
            }

            if (events.Bindings is { } binding)
            {
                if (hasDictionaryBindings)
                {
                    bool hasOutputOrParams = (binding.Parameters != null && binding.Parameters.Count > 0)
                        || !string.IsNullOrWhiteSpace(binding.OutputTarget ?? binding.Output);

                    // 无 FunctionName：参数/输出目标为配置级，应用到所有字典事件
                    if (string.IsNullOrEmpty(binding.FunctionName))
                    {
                        if (hasOutputOrParams)
                        {
                            for (int i = 0; i < bindings.Count; i++)
                            {
                                var b = bindings[i];
                                var updated = b;
                                // D6 收敛：参数 DTO 直通（TConfig.ParameterBinding），不再复制构造 ECS 层同构副本
                                updated.Parameters = binding.Parameters?.ToArray()
                                    ?? Array.Empty<TConfig.ParameterBinding>();
                                updated.OutputTarget = binding.OutputTarget ?? binding.Output ?? string.Empty;
                                updated.OutputMode = binding.OutputMode;
                                bindings[i] = updated;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < bindings.Count; i++)
                        {
                            var b = bindings[i];
                            if (string.Equals(b.FunctionName, binding.FunctionName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (hasOutputOrParams)
                                {
                                    var updated = b;
                                    // D6 收敛：参数 DTO 直通
                                    updated.Parameters = binding.Parameters?.ToArray()
                                        ?? Array.Empty<TConfig.ParameterBinding>();
                                    updated.OutputTarget = binding.OutputTarget ?? binding.Output ?? string.Empty;
                                    updated.OutputMode = binding.OutputMode;
                                    bindings[i] = updated;
                                }
                                break;
                            }
                        }
                    }
                }
                else if (!hasDictionaryBindings && !string.IsNullOrEmpty(binding.FunctionName))
                {
                    bindings.Add(new EventBindingComponent
                    {
                        EventName = "click",
                        FunctionName = binding.FunctionName,
                        // D6 收敛：参数 DTO 直通
                        Parameters = binding.Parameters?.ToArray()
                            ?? Array.Empty<TConfig.ParameterBinding>(),
                        OutputTarget = binding.OutputTarget ?? binding.Output ?? string.Empty,
                        OutputMode = binding.OutputMode
                    });
                }
            }
        }

        if (bindings.Count > 0)
        {
            _entityManager.AddComponent(entity, new EventBindingsComponent
            {
                Bindings = bindings
            });
        }
    }
}
