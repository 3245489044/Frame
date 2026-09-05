
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using ECS.Core;
using ECS.Bridge;
using FrameworkAbstractions.ECS;

namespace FrontEndDriven.ComponentDataIntegration;

public sealed class EntityManagerEventSourceAdapter : IComponentEventSource
{
    private readonly EntityManager _entityManager;

    public EntityManagerEventSourceAdapter(EntityManager entityManager)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    }

    public event Action<Entity, Type>? ComponentAdded
    {
        add => _entityManager.ComponentAdded += value;
        remove => _entityManager.ComponentAdded -= value;
    }

    public event Action<Entity, Type>? ComponentRemoved
    {
        add => _entityManager.ComponentRemoved += value;
        remove => _entityManager.ComponentRemoved -= value;
    }

    public event Action<Entity, Type, object?, object>? ComponentUpdated
    {
        add => _entityManager.ComponentUpdated += value;
        remove => _entityManager.ComponentUpdated -= value;
    }

    private readonly Dictionary<Type, Func<object, Entity, object>> _getComponentCache = new();

    private Func<object, Entity, object> CreateGetComponentFunc(Type componentType)
    {
        var method = typeof(EntityManager).GetMethod("GetComponent")
            ?? throw new InvalidOperationException("EntityManager.GetComponent 方法未找到");
        var genericMethod = method.MakeGenericMethod(componentType);

        var instanceParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "instance");
        var entityParam = System.Linq.Expressions.Expression.Parameter(typeof(Entity), "entity");
        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Convert(instanceParam, typeof(EntityManager)),
            genericMethod,
            entityParam);
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<object, Entity, object>>(
            System.Linq.Expressions.Expression.Convert(call, typeof(object)),
            instanceParam, entityParam);
        return lambda.Compile();
    }

    public T GetComponent<T>(Entity entity) where T : struct
    {
        if (!_getComponentCache.TryGetValue(typeof(T), out var func))
        {
            func = CreateGetComponentFunc(typeof(T));
            _getComponentCache[typeof(T)] = func;
        }
        return (T)func(_entityManager, entity);
    }

    public bool IsEntityValid(Entity entity)
    {
        return _entityManager.IsEntityValid(entity);
    }
}

