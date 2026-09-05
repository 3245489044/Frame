using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalWorkflowFramework.ActionMapping.Interfaces;
using UniversalWorkflowFramework.ActionMapping.Models;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Diagnostics;


namespace UniversalWorkflowFramework.ActionMapping.Mappers
{
    public class ActionMapper<TInput, TOutput> : IActionMapper<TInput, TOutput>
        where TInput : ActionBase
        where TOutput : ActionBase, new()
    {
        private const string ResourceTypeInput = "input_action";
        private const string ResourceTypeOutput = "output_action";
        private const string MetadataKeyOutputActionType = "outputActionType";
        private const string MetadataKeyParameterMap = "parameterMap";
        private const string MetadataKeyOriginalActionId = "OriginalActionId";
        private const string MetadataKeyMappingType = "MappingType";
        private const string MappingTypeGraphLogic = "GraphLogic";

        private readonly IUniversalReferenceManager _referenceManager;

        public ActionMapper(IUniversalReferenceManager referenceManager)
        {
            _referenceManager = referenceManager ?? throw new ArgumentNullException(nameof(referenceManager));
        }

        public ActionMapper()
            : this(UniversalReferenceManagerFactory.Create())
        {
        }

        public async Task InitializeAsync()
        {
            try
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.Init, new { message = "初始化操作映射器" });
                await _referenceManager.InitializeAsync();
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.InitSuccess, new { message = "操作映射器初始化成功" });
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.InitError, new { message = "初始化操作映射器失败", error = ex.Message });
                throw;
            }
        }

        public async Task<TOutput> MapAsync(TInput input)
        {
            try
            {
                var mapping = await GetMappingAsync(input.ActionType);
                if (mapping == null)
                {
                    throw new InvalidOperationException($"无法将操作映射: {input.ActionType}。图数据库中未找到对应的映射关系。");
                }

                var outputAction = CreateMappedAction(input, mapping);
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.MapSuccess, new { message = $"映射操作成功: {input.ActionType} -> {outputAction.ActionType}", inputType = input.ActionType, outputType = outputAction.ActionType });
                return outputAction;
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.MapError, new { message = "映射操作失败", error = ex.Message });
                throw;
            }
        }

        public async Task AddMappingAsync(string inputType, string outputType, Dictionary<string, string>? parameterMap = null)
        {
            try
            {
                var inputResourceId = new ResourceIdentifier { ResourceType = ResourceTypeInput, Id = inputType };
                var inputLogic = new Dictionary<string, object>
                {
                    { MetadataKeyOutputActionType, outputType }
                };

                if (parameterMap != null && parameterMap.Count > 0)
                {
                    inputLogic[MetadataKeyParameterMap] = parameterMap;
                }

                await _referenceManager.AddResourceAsync(inputResourceId, inputLogic, inputType);
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.AddSuccess, new { message = $"添加操作映射成功: {inputType} -> {outputType}", inputType, outputType });
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.AddError, new { message = $"添加操作映射失败: {inputType} -> {outputType}", inputType, outputType, error = ex.Message });
                throw;
            }
        }

        public async Task<List<UniversalWorkflowFramework.ActionMapping.Interfaces.ActionMapping>> GetAllMappingsAsync()
        {
            try
            {
                var resources = await _referenceManager.GetResourcesByTypeAsync(ResourceTypeInput);
                var mappings = new List<UniversalWorkflowFramework.ActionMapping.Interfaces.ActionMapping>();

                foreach (var resource in resources)
                {
                    var mapping = new UniversalWorkflowFramework.ActionMapping.Interfaces.ActionMapping
                    {
                        InputActionType = resource.Identifier.Id
                    };

                    if (resource.Metadata.TryGetValue(MetadataKeyOutputActionType, out var outputActionTypeObj))
                    {
                        mapping.OutputActionType = outputActionTypeObj.ToString();
                    }

                    if (resource.Metadata.TryGetValue(MetadataKeyParameterMap, out var parameterMapObj) &&
                        TryBuildParameterMap(parameterMapObj, out var parameterMap))
                    {
                        mapping.ParameterMap = parameterMap;
                    }

                    mappings.Add(mapping);
                }

                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.ListSuccess, new { message = $"获取操作映射成功，共 {mappings.Count} 个映射", count = mappings.Count });
                return mappings;
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.ListError, new { message = "获取操作映射失败", error = ex.Message });
                return new List<UniversalWorkflowFramework.ActionMapping.Interfaces.ActionMapping>();
            }
        }

        public async Task RemoveMappingAsync(string inputType)
        {
            try
            {
                var inputResourceId = new ResourceIdentifier { ResourceType = ResourceTypeInput, Id = inputType };
                await _referenceManager.RemoveResourceAsync(inputResourceId);
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.RemoveSuccess, new { message = $"删除操作映射成功: {inputType}", inputType });
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Mapper.RemoveError, new { message = $"删除操作映射失败: {inputType}", inputType, error = ex.Message });
                throw;
            }
        }

        private async Task<UniversalWorkflowFramework.ActionMapping.Interfaces.ActionMapping?> GetMappingAsync(string inputType)
        {
            var inputResourceId = new ResourceIdentifier { ResourceType = ResourceTypeInput, Id = inputType };
            var resource = await _referenceManager.GetResourceAsync(inputResourceId);

            if (resource == null)
            {
                return null;
            }

            var mapping = new UniversalWorkflowFramework.ActionMapping.Interfaces.ActionMapping
            {
                InputActionType = inputType
            };

            if (resource.Metadata.TryGetValue(MetadataKeyOutputActionType, out var outputActionTypeObj))
            {
                mapping.OutputActionType = outputActionTypeObj.ToString();
            }

            if (resource.Metadata.TryGetValue(MetadataKeyParameterMap, out var parameterMapObj) &&
                TryBuildParameterMap(parameterMapObj, out var parameterMap))
            {
                mapping.ParameterMap = parameterMap;
            }

            return mapping;
        }

        private TOutput CreateMappedAction(TInput inputAction, UniversalWorkflowFramework.ActionMapping.Interfaces.ActionMapping mapping)
        {
            var outputAction = new TOutput
            {
                ActionType = mapping.OutputActionType,
                Parameters = MapParameters(inputAction.Parameters, mapping.ParameterMap),
                Metadata = new Dictionary<string, object>(inputAction.Metadata)
            };

            outputAction.Metadata[MetadataKeyOriginalActionId] = inputAction.ActionId;
            outputAction.Metadata[MetadataKeyMappingType] = MappingTypeGraphLogic;

            return outputAction;
        }

        private static bool TryBuildParameterMap(object? parameterMapObj, out Dictionary<string, string> parameterMap)
        {
            if (parameterMapObj is Dictionary<string, string> stringMap)
            {
                parameterMap = new Dictionary<string, string>(stringMap);
                return true;
            }

            if (parameterMapObj is Dictionary<string, object> objectMap)
            {
                parameterMap = new Dictionary<string, string>();
                foreach (var kvp in objectMap)
                {
                    parameterMap[kvp.Key] = kvp.Value.ToString();
                }
                return true;
            }

            parameterMap = new Dictionary<string, string>();
            return false;
        }

        private Dictionary<string, object> MapParameters(Dictionary<string, object> sourceParameters, System.Collections.Generic.Dictionary<string, string> parameterMap)

        {
            var mappedParameters = new Dictionary<string, object>();

            foreach (var kvp in sourceParameters)
            {
                if (parameterMap.TryGetValue(kvp.Key, out var targetKey))
                {
                    mappedParameters[targetKey] = kvp.Value;
                }
                else
                {
                    mappedParameters[kvp.Key] = kvp.Value;
                }
            }

            return mappedParameters;
        }
    }

    public static class ActionMapperFactory
    {
        public static IActionMapper<TInput, TOutput> Create<TInput, TOutput>()
            where TInput : ActionBase
            where TOutput : ActionBase, new()
        {
            return new ActionMapper<TInput, TOutput>();
        }

        public static IActionMapper<TInput, TOutput> Create<TInput, TOutput>(IUniversalReferenceManager referenceManager)
            where TInput : ActionBase
            where TOutput : ActionBase, new()
        {
            return new ActionMapper<TInput, TOutput>(referenceManager);
        }
    }
}
namespace UniversalWorkflowFramework.ActionMapping.Interfaces
{
    public interface IActionMapper<TInput, TOutput>
    {
        Task InitializeAsync();

        Task<TOutput> MapAsync(TInput input);

        Task AddMappingAsync(string inputType, string outputType, Dictionary<string, string>? parameterMap = null);

        Task<List<ActionMapping>> GetAllMappingsAsync();

        Task RemoveMappingAsync(string inputType);
    }

    public class ActionMapping
    {
        public string InputActionType { get; set; }

        public string OutputActionType { get; set; }

        public Dictionary<string, string> ParameterMap { get; set; }

        public ActionMapping()
        {
            InputActionType = string.Empty;
            OutputActionType = string.Empty;
            ParameterMap = new Dictionary<string, string>();
        }
    }
}