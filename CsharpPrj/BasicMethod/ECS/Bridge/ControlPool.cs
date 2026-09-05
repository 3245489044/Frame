
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Forms;
using ECS.Core;
using ECS.Core.Lifecycle;
using Modules.InternalStorage.Extensions;
using TConfig.ContextFrame.Abstractions;
using StorageLayer = TConfig.StorageLayer;

namespace ECS.Bridge
{
    public class ControlPool : IDisposable, IEntityLifecycleSubscriber
    {
        public const string EntityToControlMap = "entity_to_control";
        public const string ControlToEntityMap = "control_to_entity";
        public const string NameToEntityIndex = "name_to_entity";

        private const string ReversePrefix = "bidir_reverse:";
        private const string IndexMetaPrefix = "index_meta:";
        private const string IndexDataPrefix = "index_data:";

        private readonly IStorageCell _cell;
        private readonly IStorageRegistry _registry;
        private readonly EntityManager? _entityManager;
        private readonly EntityLifetimeBridge? _lifetimeBridge;
        private int _count;
        private bool _disposed;


        private readonly ConcurrentDictionary<string, Control> _nameToControl = new(StringComparer.OrdinalIgnoreCase);

        // 实体→控件直接映射（挂载按实体权威解析）：消除「跨作用域同名控件（如 MainView/root 未 scope 化的普通控件）
        // 在 _nameToControl 中被后注册者覆盖」导致的挂错宿主（历史 D1「同名 root 覆盖」的泛化）。
        // GetControl(Entity) 严格按本字典返回实体专属控件，不再经 name 中转（D2：消除 _entityToName 冗余地址）。
        // 实体↔名关系由持久双向映射（IStorageCell.MapForward）唯一承载（GetControlName/GetEntity 权威源），
        // 此处不再维护第三套内存 name 索引。
        private readonly ConcurrentDictionary<Entity, Control> _entityToControl = new();

        public ControlPool(IStorageCell cell, IStorageRegistry registry)
        {
            _cell = cell ?? throw new ArgumentNullException(nameof(cell));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            var metaKey = $"{IndexMetaPrefix}{NameToEntityIndex}";
            _cell.Set(metaKey, new IndexMetaData
            {
                Name = NameToEntityIndex,
                EntityType = typeof(Entity).FullName!,
                IsUnique = true
            }, StorageLayer.Session);
        }

        public ControlPool(EntityManager entityManager, IStorageCell cell, IStorageRegistry registry)
            : this(entityManager, cell, registry, null)
        {
        }

        /// <summary>
        /// 带 EntityLifetimeBridge 的构造（阶段3：按名解析当前代设施）。bridge 由 PipelineCompositionFactory 装配，
        /// Register 联动登记名称→当前代绑定，GetEntity 返回前经 bridge/IsEntityValid 校验。
        /// </summary>
        public ControlPool(
            EntityManager entityManager,
            IStorageCell cell,
            IStorageRegistry registry,
            EntityLifetimeBridge? lifetimeBridge)
            : this(cell, registry)
        {
            _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
            _lifetimeBridge = lifetimeBridge;

            _entityManager.EntityDestroying += OnEntityDestroying;
        }

        public void Register(Entity entity, string controlName, Control control)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(controlName))
                return;

            MapForward(entity, controlName);
            var dataKey = $"{IndexDataPrefix}{NameToEntityIndex}:{controlName}";
            _cell.Set(dataKey, entity.ToString(), StorageLayer.Session);

            _nameToControl[controlName] = control;
            _entityToControl[entity] = control;   // 实体→控件直接映射（挂载按实体权威，防跨 scope 同名覆盖）

            // 阶段3：联动登记名称→当前代绑定（EntityLifetimeBridge 按名解析设施）
            _lifetimeBridge?.RegisterBinding(controlName, entity);

            _count++;
        }

        public void Unregister(Entity entity)
        {
            ThrowIfDisposed();

            var controlName = GetForward(entity);
            if (controlName != null)
            {
                var dataKey = $"{IndexDataPrefix}{NameToEntityIndex}:{controlName}";
                _cell.Remove(dataKey, StorageLayer.Session);
                // [SC] 仅当名字槽仍指向本实体控件时才移除，防误删跨 scope 同名字面控件的映射（CtrlPool 同名覆盖修复）
                if (_entityToControl.TryGetValue(entity, out var ownControl)
                    && _nameToControl.TryGetValue(controlName, out var cur)
                    && ReferenceEquals(cur, ownControl))
                {
                    _nameToControl.TryRemove(controlName, out _);
                }
            }

            _entityToControl.TryRemove(entity, out _);

            Unmap(entity);

            _count--;
        }


        public string? GetControlName(Entity entity)
        {
            ThrowIfDisposed();
            return GetForward(entity);
        }

        public Control? GetControl(string controlName)
        {
            ThrowIfDisposed();
            return _nameToControl.TryGetValue(controlName, out var control) ? control : null;
        }

        public Control? GetControl(Entity entity)
        {
            ThrowIfDisposed();
            // D2：严格按实体返回专属控件，不再经 name 中转（_entityToName fallback 已移除）——
            // 该中转在跨 scope 同名场景会命中「最后注册者」控件（E1 隐患源），收敛为单一口径。
            // 若需按控件名查询，调用方显式用 GetControl(string)/GetControlName(entity)。
            return _entityToControl.TryGetValue(entity, out var control) ? control : null;
        }

        public Entity GetEntity(string controlName)
        {
            ThrowIfDisposed();

            var dataKey = $"{IndexDataPrefix}{NameToEntityIndex}:{controlName}";
            var entityStr = _cell.Get<string>(dataKey, StorageLayer.Session);
            if (entityStr != null && TryParseEntity(entityStr, out var entity))
                return EnsureCurrent(entity);

            var backward = GetBackward(controlName);
            return EnsureCurrent(backward);
        }

        /// <summary>
        /// 阶段3：名称→实体解析返回前校验当前代——已销毁/旧代实体一律返回 Entity.Invalid，
        /// 杜绝旧代 Entity 值泄漏给调用方（调用方访问组件即抛 Invalid entity 中断管线）。
        /// 无 EntityManager（独立构造）时跳过校验，保持原语义。
        /// </summary>
        private Entity EnsureCurrent(Entity entity)
        {
            if (!entity.IsValid || _entityManager == null)
                return entity;
            return _entityManager.IsEntityValid(entity) ? entity : Entity.Invalid;
        }

        public bool ContainsEntity(Entity entity)
        {
            ThrowIfDisposed();
            return ContainsForward(entity);
        }

        public bool IsRegistered(Entity entity)
        {
            ThrowIfDisposed();
            return ContainsForward(entity);
        }

        public bool ContainsControl(string controlName)
        {
            ThrowIfDisposed();
            return ContainsBackward(controlName);
        }

        public IEnumerable<Entity> GetAllEntities()
        {
            ThrowIfDisposed();
            var prefix = $"{EntityToControlMap}:";
            foreach (var key in _cell.GetKeys(StorageLayer.Session))
            {
                if (key.StartsWith(prefix) && !key.StartsWith(ReversePrefix))
                {
                    var entityStr = key[prefix.Length..];
                    if (TryParseEntity(entityStr, out var entity))
                        yield return entity;
                }
            }
        }

        public IEnumerable<string> GetAllControlNames()
        {
            ThrowIfDisposed();
            var reversePrefix = $"{ReversePrefix}{EntityToControlMap}:";
            foreach (var key in _cell.GetKeys(StorageLayer.Session))
            {
                if (key.StartsWith(reversePrefix))
                {
                    yield return key[reversePrefix.Length..];
                }
            }
        }

        public int Count
        {
            get
            {
                ThrowIfDisposed();
                return _count;
            }
        }

        public void Clear()
        {
            ThrowIfDisposed();
            var entities = GetAllEntities().ToList();
            foreach (var entity in entities)
            {
                Unregister(entity);
            }
            _count = 0;
        }


        // S1：双向映射实现上移复用 StorageBidirectionalExtensions（Modules.InternalStorage），
        // 消除与旧 StorageBidirectionalMap 的重复实现——key/value 序列化语义保持一致。
        private void MapForward(Entity key, string value)
            => _cell.MapForward<Entity, string>(EntityToControlMap, key, value);

        private string? GetForward(Entity key)
            => _cell.GetForward<Entity, string>(EntityToControlMap, key);

        private Entity GetBackward(string value)
            => _cell.GetBackward<Entity, string>(EntityToControlMap, value);

        private void Unmap(Entity key)
            => _cell.Unmap<Entity, string>(EntityToControlMap, key);

        private bool ContainsForward(Entity key)
            => _cell.ContainsForward<Entity, string>(EntityToControlMap, key);

        private bool ContainsBackward(string value)
            => _cell.ContainsBackward<Entity, string>(EntityToControlMap, value);


        private static bool TryParseEntity(string str, out Entity entity)
        {
            if (str.StartsWith("Entity(") && str.EndsWith(")"))
            {
                var inner = str[7..^1];
                var parts = inner.Split(':');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var id) &&
                    int.TryParse(parts[1], out var version))
                {
                    entity = new Entity(id, version);
                    return true;
                }
            }
            entity = Entity.Invalid;
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ControlPool));
        }

        private void OnEntityDestroying(Entity entity)
        {
            Unregister(entity);
        }

        // ── IEntityLifecycleSubscriber ──

        /// <summary>
        /// 单实体失效：委托 OnEntityDestroying（ControlPool 直接订阅 EntityManager.EntityDestroying 的既有路径），
        /// 保持幂等（Unregister 对已注销实体安全，双路径无副作用）。
        /// </summary>
        public void OnEntityInvalidated(Entity entity)
        {
            OnEntityDestroying(entity);
        }

        /// <summary>
        /// scope 整体失效（EntityLifecycleBus 广播）：物理移除 + Dispose 本 scope 控件（解除事件委托，
        /// 杜绝旧控件/旧引用残留——框架级引用脱节根因）+ 批量 Unregister（清理实体→控件、名称→控件、
        /// 存储双向映射与 name_to_entity 索引）。广播发生在实体销毁前，控件仍可从实体解析。
        /// </summary>
        public void OnScopeInvalidated(string scopeName, IReadOnlyList<Entity> scopeEntities)
        {
            if (scopeEntities == null || scopeEntities.Count == 0)
                return;

            foreach (var entity in scopeEntities)
            {
                if (GetControl(entity) is Control control)
                {
                    control.Parent?.Controls.Remove(control);
                    control.Dispose();
                }
                Unregister(entity);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_entityManager != null)
                _entityManager.EntityDestroying -= OnEntityDestroying;

            Clear();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private class IndexMetaData
        {
            public string Name { get; set; } = string.Empty;
            public string EntityType { get; set; } = string.Empty;
            public bool IsUnique { get; set; }
        }
    }
}
