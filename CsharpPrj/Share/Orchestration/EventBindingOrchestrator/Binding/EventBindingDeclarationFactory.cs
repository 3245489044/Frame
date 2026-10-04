
using System;
using System.Collections.Generic;
using System.Linq;
using TConfig;
using TConfig.ECS.Components;
using Share.EventBindingOrchestrator.Parameters;
using Share.EventBindingOrchestrator.Utils;

namespace Share.EventBindingOrchestrator.Binding;

public static class EventBindingDeclarationFactory
{
    // D6 收敛：声明链参数类型统一为配置层 DTO（TConfig.ParameterBinding），废除 ParameterMapping 中间转换
    public static List<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> Build(
        EventBindingsComponent component,
        string controlName)
    {
        if (component.Bindings == null || component.Bindings.Count == 0)
            return new List<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>();

        if (string.IsNullOrWhiteSpace(controlName))
            throw new ArgumentException("控件名称不能为空", nameof(controlName));

        var declarations = new List<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>(component.Bindings.Count);

        foreach (var binding in component.Bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.FunctionName))
                continue;

            IReadOnlyList<ParameterBinding> parameters = binding.Parameters is { Length: > 0 }
                ? binding.Parameters
                : Array.Empty<ParameterBinding>();

            declarations.Add(new EventBindingDeclaration<IReadOnlyList<ParameterBinding>>
            {
                Name = $"{controlName}_{binding.EventName}",
                SourceName = controlName,
                EventType = binding.EventName,
                FunctionName = binding.FunctionName,
                Parameters = parameters,
                OutputTarget = binding.OutputTarget
            });
        }

        return declarations;
    }

    public static List<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> Build(
        UIConfiguration config)
    {
        config = PortGuard.Required(config, nameof(config));

        var declarations = new List<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>();

        if (config.Views == null || config.Views.Count == 0)
            return declarations;

        ControlTreeWalker.WalkAllViews(config, ctrl =>
        {
            if (ctrl?.Events?.Bindings == null) return;
            if (ctrl.Events.EventsDictionary == null) return;

            var bindings = ctrl.Events.Bindings;

            foreach (var kv in ctrl.Events.EventsDictionary)
            {
                var eventName = kv.Key;
                var functionNames = kv.Value;
                if (functionNames == null) continue;

                foreach (var functionName in functionNames)
                {
                    if (string.IsNullOrWhiteSpace(functionName))
                        continue;

                    IReadOnlyList<ParameterBinding> parameters = bindings.Parameters is { Count: > 0 }
                        ? bindings.Parameters
                        : Array.Empty<ParameterBinding>();

                    declarations.Add(new EventBindingDeclaration<IReadOnlyList<ParameterBinding>>
                    {
                        SourceName = ctrl.Name,
                        EventType = eventName,
                        FunctionName = functionName,
                        Parameters = parameters,
                        OutputTarget = bindings.Output
                    });
                }
            }
        });

        return declarations;
    }
}