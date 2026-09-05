
#nullable enable

using System.Reflection;
using ECS.Core;
using TConfig.ECS;

namespace GraphFramework.Ecs
{
    internal static class EcsReflectionHelper
    {
        public static void AddComponent(EntityManager em, Entity entity, object component)
        {
            var componentType = component.GetType();
            var method = typeof(EntityManager).GetMethod("AddComponent")!
                .MakeGenericMethod(componentType);
            method.Invoke(em, new object[] { entity, component });
        }

        public static void RemoveComponent(EntityManager em, Entity entity, Type componentType)
        {
            var method = typeof(EntityManager).GetMethod("RemoveComponent")!
                .MakeGenericMethod(componentType);
            method.Invoke(em, new object[] { entity });
        }

        public static bool TryGetComponent(EntityManager em, Entity entity, Type componentType, out object? component)
        {
            var method = typeof(EntityManager).GetMethod("TryGetComponent")!
                .MakeGenericMethod(componentType);
            var parameters = new object[] { entity, null! };
            var result = (bool)method.Invoke(em, parameters)!;
            component = result ? parameters[1] : null;
            return result;
        }

        public static void SetComponent(EntityManager em, Entity entity, object component)
        {
            var componentType = component.GetType();
            var method = typeof(EntityManager).GetMethod("SetComponent")!
                .MakeGenericMethod(componentType);
            method.Invoke(em, new object[] { entity, component });
        }

        public static List<(Type type, object component)> GetComponentTypes(EntityManager em, Entity entity)
        {
            var result = new List<(Type, object)>();
            var componentArraysField = typeof(EntityManager)
                .GetField("_componentArrays", BindingFlags.NonPublic | BindingFlags.Instance);
            if (componentArraysField?.GetValue(em) is Dictionary<Type, object> arrays)
            {
                foreach (var kvp in arrays)
                {
                    if (TryGetComponent(em, entity, kvp.Key, out var component) && component != null)
                    {
                        result.Add((kvp.Key, component));
                    }
                }
            }
            return result;
        }
    }

    public interface IEcsCommand
    {
        string Name { get; }

        void Execute(EcsWorld world);

        void Undo(EcsWorld world);
    }

    public class CreateEntityCommand : IEcsCommand
    {
        public string Name => "CreateEntity";

        private Entity _createdEntity;

        public Entity CreatedEntity => _createdEntity;

        public void Execute(EcsWorld world)
        {
            _createdEntity = world.EntityManager.CreateEntity();
        }

        public void Undo(EcsWorld world)
        {
            world.EntityManager.DestroyEntity(_createdEntity);
        }
    }

    public class DestroyEntityCommand : IEcsCommand
    {
        public string Name => "DestroyEntity";

        private readonly Entity _entity;
        private readonly List<(Type type, object component)> _savedComponents = new();
        private bool _executed;

        public DestroyEntityCommand(Entity entity)
        {
            _entity = entity;
        }

        public void Execute(EcsWorld world)
        {
            var components = EcsReflectionHelper.GetComponentTypes(world.EntityManager, _entity);
            _savedComponents.AddRange(components);

            world.EntityManager.DestroyEntity(_entity);
            _executed = true;
        }

        public void Undo(EcsWorld world)
        {
            if (!_executed) return;

            try
            {
                var undoMethod = typeof(EntityManager).GetMethod("UndoDestroyEntity",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (undoMethod != null)
                {
                    undoMethod.Invoke(world.EntityManager, new object[] { _entity });
                }
                else
                {
                    var entitiesField = typeof(EntityManager).GetField("_entities",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (entitiesField != null)
                    {
                        if (entitiesField.GetValue(world.EntityManager) is System.Collections.IList entityList)
                        {
                            entityList.Add(_entity);
                        }
                    }
                }

                foreach (var (type, component) in _savedComponents)
                {
                    EcsReflectionHelper.AddComponent(world.EntityManager, _entity, component);
                }
            }
            catch
            {
                var newEntity = world.EntityManager.CreateEntity();
                foreach (var (type, component) in _savedComponents)
                {
                    EcsReflectionHelper.AddComponent(world.EntityManager, newEntity, component);
                }
            }
        }
    }

    public class AddComponentCommand : IEcsCommand
    {
        public string Name => "AddComponent";

        private readonly Entity _entity;
        private readonly object _component;

        public AddComponentCommand(Entity entity, object component)
        {
            _entity = entity;
            _component = component;
        }

        public void Execute(EcsWorld world)
        {
            EcsReflectionHelper.AddComponent(world.EntityManager, _entity, _component);
        }

        public void Undo(EcsWorld world)
        {
            EcsReflectionHelper.RemoveComponent(world.EntityManager, _entity, _component.GetType());
        }
    }

    public class RemoveComponentCommand : IEcsCommand
    {
        public string Name => "RemoveComponent";

        private readonly Entity _entity;
        private readonly Type _componentType;
        private object? _savedComponent;

        public RemoveComponentCommand(Entity entity, Type componentType)
        {
            _entity = entity;
            _componentType = componentType;
        }

        public void Execute(EcsWorld world)
        {
            if (EcsReflectionHelper.TryGetComponent(world.EntityManager, _entity, _componentType, out var component))
            {
                _savedComponent = component;
                EcsReflectionHelper.RemoveComponent(world.EntityManager, _entity, _componentType);
            }
        }

        public void Undo(EcsWorld world)
        {
            if (_savedComponent != null)
            {
                EcsReflectionHelper.AddComponent(world.EntityManager, _entity, _savedComponent);
            }
        }
    }

    public class SetComponentCommand : IEcsCommand
    {
        public string Name => "SetComponent";

        private readonly Entity _entity;
        private readonly object _newComponent;
        private object? _oldComponent;

        public SetComponentCommand(Entity entity, object newComponent)
        {
            _entity = entity;
            _newComponent = newComponent;
        }

        public void Execute(EcsWorld world)
        {
            var componentType = _newComponent.GetType();
            if (EcsReflectionHelper.TryGetComponent(world.EntityManager, _entity, componentType, out var oldComponent))
            {
                _oldComponent = oldComponent;
            }
            EcsReflectionHelper.SetComponent(world.EntityManager, _entity, _newComponent);
        }

        public void Undo(EcsWorld world)
        {
            if (_oldComponent != null)
            {
                EcsReflectionHelper.SetComponent(world.EntityManager, _entity, _oldComponent);
            }
            else
            {
                EcsReflectionHelper.RemoveComponent(world.EntityManager, _entity, _newComponent.GetType());
            }
        }
    }

    public class EcsTransaction : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly List<IEcsCommand> _commands = new();
        private readonly List<IEcsCommand> _executedCommands = new();
        private readonly object _lock = new();
        private bool _committed;
        private bool _rolledBack;
        private bool _disposed;

        public string Name { get; }

        public int CommandCount => _commands.Count;

        public bool IsCommitted => _committed;

        public bool IsRolledBack => _rolledBack;

        public EcsTransaction(EcsWorld world, string name = "unnamed")
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            Name = name;
        }

        public void Enqueue(IEcsCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            lock (_lock)
            {
                if (_committed)
                    throw new InvalidOperationException("Cannot enqueue command after transaction has been committed.");

                if (_rolledBack)
                    throw new InvalidOperationException("Cannot enqueue command after transaction has been rolled back.");

                _commands.Add(command);
            }
        }

        public void EnqueueRange(IEnumerable<IEcsCommand> commands)
        {
            foreach (var cmd in commands)
            {
                Enqueue(cmd);
            }
        }

        public void Commit()
        {
            lock (_lock)
            {
                if (_committed)
                    throw new InvalidOperationException("Transaction has already been committed.");

                if (_rolledBack)
                    throw new InvalidOperationException("Transaction has already been rolled back.");

                _committed = true;

                foreach (var cmd in _commands)
                {
                    try
                    {
                        cmd.Execute(_world);
                        _executedCommands.Add(cmd);
                    }
                    catch (Exception ex)
                    {
                        RollbackInternal();
                        throw new InvalidOperationException(
                            $"Transaction '{Name}' failed at command '{cmd.Name}': {ex.Message}", ex);
                    }
                }

                _commands.Clear();
            }
        }

        public void Rollback()
        {
            lock (_lock)
            {
                if (_committed)
                    throw new InvalidOperationException("Cannot rollback after commit. Use RollbackInternal for error recovery.");

                if (_rolledBack)
                    return;

                _rolledBack = true;
                RollbackInternal();
            }
        }

        private void RollbackInternal()
        {
            for (int i = _executedCommands.Count - 1; i >= 0; i--)
            {
                try
                {
                    _executedCommands[i].Undo(_world);
                }
                catch
                {
                }
            }

            _executedCommands.Clear();
            _commands.Clear();
        }

        public EcsTransaction CreateNested(string name)
        {
            return new EcsTransaction(_world, $"{Name}.{name}");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (!_committed && !_rolledBack)
                {
                    Rollback();
                }

                _commands.Clear();
                _executedCommands.Clear();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}


