
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using TConfig;
using TConfig.ECS.Components;
using Share.EventBindingOrchestrator.Binding;
using Share.EventBindingOrchestrator.Diagnostics;

namespace Share.EventBindingOrchestrator.Gates;

public enum EventBindingInputSource
{
    EcsComponent,
    UiConfiguration,
    DeclarationList
}

public sealed class EventBindingInputResult
{
    public IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> Declarations { get; }

    public EventBindingInputSource Source { get; }

    public DateTime Timestamp { get; }

    public int InvalidSkippedCount { get; }

    public EventBindingInputResult(
        IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> declarations,
        EventBindingInputSource source,
        int invalidSkippedCount)
    {
        Declarations = declarations;
        Source = source;
        Timestamp = DateTime.UtcNow;
        InvalidSkippedCount = invalidSkippedCount;
    }
}

public static class EventBindingInputGate
{
    public static EventBindingInputResult EnterFromEcs(
        EventBindingsComponent component,
        string controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
            throw new ArgumentException("控件名称不能为空", nameof(controlName));

        var declarations = EventBindingDeclarationFactory.Build(component, controlName);
        return FinalizeInput(declarations, EventBindingInputSource.EcsComponent);
    }

    public static EventBindingInputResult EnterFromConfig(UIConfiguration config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var declarations = EventBindingDeclarationFactory.Build(config);
        return FinalizeInput(declarations, EventBindingInputSource.UiConfiguration);
    }

    public static EventBindingInputResult EnterFromDeclarations(
        IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> declarations)
    {
        if (declarations == null)
            throw new ArgumentNullException(nameof(declarations));

        var list = declarations.ToList();
        var invalidCount = ValidateDeclarations(list);
        var validDeclarations = list
            .Where(d => !string.IsNullOrWhiteSpace(d.SourceName)
                       && !string.IsNullOrWhiteSpace(d.EventType)
                       && !string.IsNullOrWhiteSpace(d.FunctionName))
            .ToList();

        return FinalizeInput(validDeclarations, EventBindingInputSource.DeclarationList, invalidCount);
    }


    private static EventBindingInputResult FinalizeInput(
        List<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> declarations,
        EventBindingInputSource source,
        int? preComputedInvalid = null)
    {
        var invalidCount = preComputedInvalid ?? ValidateDeclarations(declarations);

        var valid = declarations
            .Where(d => !string.IsNullOrWhiteSpace(d.SourceName)
                       && !string.IsNullOrWhiteSpace(d.EventType)
                       && !string.IsNullOrWhiteSpace(d.FunctionName))
            .ToList();

        EventBindingDiagnosticContext.Record("inputgate.enter", new
        {
            source = source.ToString(),
            declarationCount = valid.Count,
            invalidSkippedCount = invalidCount,
            timestamp = DateTime.UtcNow
        });

        return new EventBindingInputResult(valid, source, invalidCount);
    }

    private static int ValidateDeclarations(
        IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> declarations)
    {
        if (declarations == null || declarations.Count == 0)
            return 0;

        return declarations.Count(d =>
            string.IsNullOrWhiteSpace(d.SourceName)
            || string.IsNullOrWhiteSpace(d.EventType)
            || string.IsNullOrWhiteSpace(d.FunctionName));
    }
}