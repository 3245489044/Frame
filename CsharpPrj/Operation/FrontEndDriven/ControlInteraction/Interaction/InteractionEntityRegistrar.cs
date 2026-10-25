using ECS.Core;
using FrontEndDriven.ECS.Components;
using Share.EventBindingOrchestrator.Binding;
using TConfig;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlInteraction.Interaction;

/// <summary>
/// 交互实体注册器（A1）：把 page 域声明节点注册为 ECS 实体，使其能被 <c>EcsEventBus.Enqueue("page:x", ...)</c> 命中。
/// <para>实体 = <see cref="ControlInfoComponent"/>(Name=域限定键) + <see cref="ScopeComponent"/> +
/// <see cref="EventBindingsComponent"/> + <see cref="EventBindingStateComponent"/>(Bound) + <see cref="InteractionDomainComponent"/>。
/// 置 Bound 的理由：page 元素无物理控件，<see cref="EventBindingSystem"/> 只处理 Pending 实体，避免被误判 Failed。</para>
/// <para>幂等：同 scope + 同名实体复用并更新绑定，重复装配不重复建实体。</para>
/// </summary>
public sealed class InteractionEntityRegistrar
{
    private readonly EntityManager _entityManager;

    public InteractionEntityRegistrar(EntityManager entityManager)
        => _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));

    /// <summary>注册 page 域节点；返回新建/更新的实体数。</summary>
    public int Register(IReadOnlyList<InteractionNode> nodes, string scope)
    {
        if (nodes == null || nodes.Count == 0) return 0;

        var changed = 0;
        foreach (var node in nodes)
        {
            if (node.Domain != TConfig.InteractionDomains.PageName) continue;
            if (string.IsNullOrWhiteSpace(node.Name)) continue;
            if (!node.HasInteraction) continue;

            var key = TConfig.InteractionDomains.Qualify(TConfig.InteractionDomains.PageName, node.Name);
            var entity = FindByKey(key, scope);
            if (!entity.IsValid)
                entity = _entityManager.CreateEntity();

            Upsert(entity, new ControlInfoComponent
            {
                Name = key,
                Text = node.Name,
                Enabled = true,
                Visible = true
            });

            Upsert(entity, new ScopeComponent { Scope = scope ?? string.Empty });
            Upsert(entity, new InteractionDomainComponent { Domain = TConfig.InteractionDomains.PageName });
            Upsert(entity, new EventBindingStateComponent
            {
                State = EventBindingState.Bound,
                RetryCount = 0,
                ErrorMessage = string.Empty
            });

            Upsert(entity, new EventBindingsComponent
            {
                Bindings = BuildBindings(node)
            });

            changed++;
        }

        return changed;
    }

    private Entity FindByKey(string key, string scope)
    {
        foreach (var entity in _entityManager.GetEntities<ControlInfoComponent>())
        {
            if (!_entityManager.TryGetComponent<ControlInfoComponent>(entity, out var info))
                continue;
            if (!string.Equals(info.Name, key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_entityManager.TryGetComponent<ScopeComponent>(entity, out var sc))
                continue;
            if (!string.Equals(sc.Scope, scope ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                continue;
            return entity;
        }

        return Entity.Invalid;
    }

    private static List<EventBindingComponent> BuildBindings(InteractionNode node)
    {
        var result = new List<EventBindingComponent>();
        if (node.Events?.EventsDictionary is not { Count: > 0 } events)
            return result;

        var parameters = node.Bindings?.Parameters?.ToArray() ?? Array.Empty<ParameterBinding>();
        var output = node.Bindings?.Output ?? node.Bindings?.OutputTarget ?? string.Empty;
        var outputMode = node.Bindings?.OutputMode;
        var timeout = node.Bindings?.FunctionTimeoutMs;

        foreach (var kvp in events)
        {
            if (kvp.Value is not { Count: > 0 } functions) continue;
            foreach (var functionName in functions)
            {
                if (string.IsNullOrWhiteSpace(functionName)) continue;

                result.Add(new EventBindingComponent
                {
                    EventName = kvp.Key,
                    FunctionName = functionName,
                    Parameters = parameters,
                    OutputTarget = output,
                    OutputMode = outputMode,
                    FunctionTimeoutMs = timeout
                });
            }
        }

        return result;
    }

    private void Upsert<T>(Entity entity, T component) where T : struct, IComponent
    {
        if (_entityManager.HasComponent<T>(entity))
            _entityManager.AddOrSetComponent(entity, component);
        else
            _entityManager.AddComponent(entity, component);
    }
}
