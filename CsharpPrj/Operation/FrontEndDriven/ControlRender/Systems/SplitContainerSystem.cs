
using System;
using System.Collections.Generic;
using ECS.Core;
using ECS.Bridge;
using TConfig.ECS.Components;
using FrontEndDriven.ECS.Components;
using Ports;

namespace FrontEndDriven.ECS.Systems;

public class SplitContainerSystem
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly IEventSubscriptionOutput _subscriptionOutput;
    private readonly HashSet<Entity> _subscribedSplitters = new();

    public SplitContainerSystem(EntityManager entityManager, ControlPool controlPool, IEventSubscriptionOutput subscriptionOutput)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _subscriptionOutput = subscriptionOutput ?? throw new ArgumentNullException(nameof(subscriptionOutput));
    }


    public void Update()
    {
        var entities = _entityManager.GetEntities<ControlTypeComponent>();

        foreach (var entity in entities)
        {
            if (_subscribedSplitters.Contains(entity))
                continue;

            var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);
            if (!string.Equals(typeComp.Type, "splitcontainer", StringComparison.OrdinalIgnoreCase))
                continue;

            var control = _controlPool.GetControl(entity);
            if (control == null)
                continue;

            _subscriptionOutput.SubscribeSplitterMoved(control, entity);
            _subscribedSplitters.Add(entity);
        }
    }

    public void RemoveSubscription(Entity entity)
    {
        _subscribedSplitters.Remove(entity);
    }

}
