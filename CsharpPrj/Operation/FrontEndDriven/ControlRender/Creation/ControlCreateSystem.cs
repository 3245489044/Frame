
using System;
using ECS.Core;
using ECS.Bridge;
using ECS.Components;
using TConfig.ECS.Components;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS.Components;
using Ports;

namespace FrontEndDriven.ECS.Systems;

public class EcsCapabilityApplier
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly IControlCreationOutput _creationOutput;

    public EcsCapabilityApplier(
        EntityManager entityManager,
        ControlPool controlPool,
        IControlCreationOutput creationOutput)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _creationOutput = creationOutput ?? throw new ArgumentNullException(nameof(creationOutput));
    }

    public void Update()
    {
        var entities = _entityManager.GetEntities<PendingCapabilityComponent>();

        foreach (var entity in entities)
        {
            if (_entityManager.HasComponent<SplitterPanelComponent>(entity))
            {
                _entityManager.RemoveComponent<PendingCapabilityComponent>(entity);
                continue;
            }

            if (!_entityManager.TryGetComponent<ControlTypeComponent>(entity, out var typeComp))
            {
                _entityManager.RemoveComponent<PendingCapabilityComponent>(entity);
                continue;
            }

            _creationOutput.CreateControl(entity, typeComp.Type);

            // 挂载唯一权威收敛：创建阶段只建控件入池，不挂载父链——物理挂载统一由 ApplyRelations
            // 按 (ParentId, OrderIndex) 稳定排序执行（见 WinFormApplier）。原创建期 AddToParent 依
            // PendingCapability 枚举序（与声明无关、边遍历边移出而打乱）先行挂载，且 TabPages.Add 幂等，
            // 使后到的权威挂载无法纠序 → tab 页签乱序根因。此处移除双轨，从源头消灭「谁先挂谁定序」竞态。
            _entityManager.RemoveComponent<PendingCapabilityComponent>(entity);
        }
    }
}
