
#nullable enable
using System.Text.Json;
using TConfig.Abstractions;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Rendering;

public class TemplatePreprocessStage : RenderStageBase
{
    public override string Name => "TemplatePreprocess";
    public override int Order => 5;

    public override Task ExecuteAsync(IRenderSession session)
    {
        if (!session.Items.TryGetValue("ConfigPath", out var configPathObj) || configPathObj is not string configPath)
        {
            throw new InvalidOperationException("TemplatePreprocessStage: session.Items[\"ConfigPath\"] is required");
        }

        if (!session.Items.TryGetValue("TemplateProcessor", out var processorObj) || processorObj is not Func<string, string> processor)
        {
            session.AddDiagnostic("TemplatePreprocess_Skipped", "No TemplateProcessor in session.Items");
            return Task.CompletedTask;
        }

        try
        {
            var processedJson = processor(configPath);
            if (processedJson != null)
            {
                session.Items["OriginalPath"] = configPath;

                session.Items["ConfigPath"] = processedJson;

                session.AddDiagnostic("TemplatePreprocess_Processed", new
                {
                    originalPath = configPath,
                    inMemory = true,
                    length = processedJson.Length
                });
            }
        }
        catch (Exception ex)
        {
            session.AddDiagnostic("TemplatePreprocess_Failed", new
            {
                configPath,
                error = ex.Message
            });
        }

        return Task.CompletedTask;
    }
}
