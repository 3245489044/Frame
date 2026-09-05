
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TConfig.ContextFrame.Abstractions;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Shared;

namespace FrontEndDriven.ECS;

public static class ControlStrategyRegistry
{
    private static readonly ConcurrentDictionary<string, IControlStrategy> _strategyMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>工厂委托到 <see cref="IControlStrategy"/> 的适配：收敛 factory 双表为单表（D6/D5）。</summary>
    private sealed class FuncStrategy : IControlStrategy
    {
        private readonly Func<object> _factory;

        public string ControlType { get; }

        public FuncStrategy(string controlType, Func<object> factory)
        {
            ControlType = controlType;
            _factory = factory;
        }

        public object Create() => _factory();
    }

    public static void Initialize()
    {
        Register("container", () =>
        {
            var ecsStorage = AppServiceProvider.Resolve<IECSStorage>();
            var storageRegistry = AppServiceProvider.Resolve<IStorageRegistry>();
            if (ecsStorage == null)
                throw new InvalidOperationException(
                    "IECSStorage 未注册到 AppServiceProvider，无法创建 EcsConfigContainer。请确保 ControlServiceCollection.RegisterAll() 已执行。");
            if (storageRegistry == null)
                throw new InvalidOperationException(
                    "IStorageRegistry 未注册到 AppServiceProvider，无法创建 EcsConfigContainer。请确保 ControlServiceCollection.RegisterAll() 已执行。");
            return new FrontEndDriven.Container.EcsConfigContainer(ecsStorage, storageRegistry);
        });

        // 标准控件工厂（原 StandardControlStrategies 搬运层）并入单次初始化：从 ControlTypeRegistry 一次性注册。
        RegisterStandardFactories();
    }

    /// <summary>将 <see cref="ControlTypeRegistry.ControlFactories"/> 全部注册为本类型工厂（收敛冗余搬运层）。</summary>
    public static void RegisterStandardFactories()
    {
        foreach (var (typeName, factory) in ControlTypeRegistry.ControlFactories)
        {
            Register(typeName, factory);
        }
    }

    public static void Register(IControlStrategy strategy)
    {
        if (strategy == null)
            throw new ArgumentNullException(nameof(strategy));

        _strategyMap[strategy.ControlType] = strategy;
    }

    public static void Register(string controlType, Func<object> factory)
    {
        if (string.IsNullOrEmpty(controlType))
            throw new ArgumentException("控件类型名称不能为空", nameof(controlType));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        _strategyMap[controlType] = new FuncStrategy(controlType, factory);
    }

    public static object? Create(string controlType)
    {
        if (string.IsNullOrEmpty(controlType))
            throw new ArgumentException("控件类型名称不能为空", nameof(controlType));

        return _strategyMap.TryGetValue(controlType, out var strategy)
            ? strategy.Create()
            : null;
    }

    public static TControl? Create<TControl>(string controlType) where TControl : class
    {
        var instance = Create(controlType);
        return instance as TControl;
    }

    public static IControlStrategy? GetStrategy(string controlType)
    {
        if (string.IsNullOrEmpty(controlType))
            return null;

        return _strategyMap.TryGetValue(controlType, out var strategy) ? strategy : null;
    }

    public static bool HasRegistration(string controlType)
    {
        return !string.IsNullOrEmpty(controlType) && _strategyMap.ContainsKey(controlType);
    }

    public static bool Unregister(string controlType)
    {
        if (string.IsNullOrEmpty(controlType))
            return false;

        return _strategyMap.TryRemove(controlType, out _);
    }

    public static string[] GetRegisteredTypes()
    {
        return _strategyMap.Keys.ToArray();
    }
}
