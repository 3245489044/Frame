
namespace CommandFramework.Abstractions;

public interface IEventTypeMapper
{
    string ToCommandAction(string eventType);

    string ToDefaultEventType(string commandAction);

    IReadOnlyDictionary<string, string> EventTypeToCommandAction { get; }

    IReadOnlyDictionary<string, string> CommandActionToDefaultEventType { get; }
}
