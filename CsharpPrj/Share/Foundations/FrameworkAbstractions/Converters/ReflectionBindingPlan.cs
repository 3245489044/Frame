using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace FrameworkAbstractions.Converters;

/// <summary>
/// CLR 类型 ⟷ 值树 的**成员绑定计划**：每类型编译一次（getter/setter 预编译为委托），运行时零逐次反射调用。
/// <para>
/// 只负责「按公开成员名读写 + 构造实例」的通用反射语义，不含任何领域值类型；
/// 「哪些成员进入读写面」由调用方以 <c>include</c> 谓词传入（如忽略标记、派生字段等语义留在调用方）。
/// </para>
/// <para>口径：无参公共构造优先，否则取参数最多的公共构造（位置 record 主构造）；成员名大小写无关；索引器不参与。</para>
/// </summary>
public sealed class ReflectionBindingPlan
{
    /// <summary>单个成员的绑定：元数据 + 预编译访问器（无读/写访问器时为 null）。</summary>
    public sealed class Member
    {
        public string Name = string.Empty;
        public Type Type = typeof(object);
        public PropertyInfo? Property;
        public Func<object, object?>? Get;
        public Action<object, object?>? Set;
    }

    /// <summary>构造入口：无参构造优先；否则参数最多的公共构造（其参数表见 <see cref="CtorParams"/>）。</summary>
    public ConstructorInfo? Ctor;

    /// <summary>非无参构造的参数表（用于按参数名绑定）；无参构造时为 null 或空。</summary>
    public ParameterInfo[]? CtorParams;

    /// <summary>进入读写面的成员（按声明序）。</summary>
    public Member[] Members = Array.Empty<Member>();

    /// <summary>按名检索（大小写无关）。</summary>
    public readonly Dictionary<string, Member> ByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>构建类型计划（<paramref name="include"/> 为 null 时收全部公开实例属性；索引器恒排除）。</summary>
    public static ReflectionBindingPlan Build(Type type, Func<PropertyInfo, bool>? include = null)
    {
        var plan = new ReflectionBindingPlan
        {
            Ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null),
        };

        if (plan.Ctor is null)
        {
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault(c => c.GetParameters().Length > 0);
            if (ctor is not null) { plan.Ctor = ctor; plan.CtorParams = ctor.GetParameters(); }
        }

        var members = new List<Member>();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length != 0) continue;
            if (include is not null && !include(prop)) continue;

            var member = new Member { Name = prop.Name, Type = prop.PropertyType, Property = prop };

            if (prop.GetGetMethod(nonPublic: false) is not null)
            {
                var o = Expression.Parameter(typeof(object), "o");
                var body = Expression.Convert(Expression.Property(Expression.Convert(o, type), prop), typeof(object));
                member.Get = Expression.Lambda<Func<object, object?>>(body, o).Compile();
            }

            if (prop.GetSetMethod(nonPublic: false) is not null)
            {
                var o = Expression.Parameter(typeof(object), "o");
                var v = Expression.Parameter(typeof(object), "v");
                var assign = Expression.Assign(
                    Expression.Property(Expression.Convert(o, type), prop),
                    Expression.Convert(v, prop.PropertyType));
                member.Set = Expression.Lambda<Action<object, object?>>(assign, o, v).Compile();
            }

            members.Add(member);
            plan.ByName[prop.Name] = member;
        }

        plan.Members = members.ToArray();
        return plan;
    }

    /// <summary>按名检索成员（大小写无关）；未命中返回 null。</summary>
    public Member? Find(string name) => ByName.TryGetValue(name, out var member) ? member : null;
}

/// <summary>
/// 带缓存的计划工厂：缓存键 = 类型（成员过滤谓词固定在构造时，避免谓词参与键导致缓存无界）。
/// <para>调用方应持有**唯一**实例（如 static readonly），即「每类型只编译一次」。</para>
/// </summary>
public sealed class ReflectionPlanCache
{
    private readonly ConcurrentDictionary<Type, ReflectionBindingPlan> _plans = new();
    private readonly Func<PropertyInfo, bool>? _include;

    public ReflectionPlanCache(Func<PropertyInfo, bool>? include = null) => _include = include;

    /// <summary>取（或首次构建并缓存）类型计划。</summary>
    public ReflectionBindingPlan Get(Type type) => _plans.GetOrAdd(type, t => ReflectionBindingPlan.Build(t, _include));
}
