
using System.Text.Json.Nodes;
using Modules.Pipeline;

namespace Common.PlaceholderFramework.TemplateExpansion.Abstractions;

public interface IJsonConverter : IPipelineStage
{
    bool Enabled { get; }

    new string Name { get; }
}
