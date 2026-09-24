#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Modules.Node.Materialization;
using WorkFlowMethod.Infrastructure;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using Share.LogicComposition.Abstractions;
using Share.LogicComposition.DefaultImplementations;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess
{
    public static class NodeConfigFactory
    {
        public static class BaseConfig
        {
            public static readonly Func<NodeConfig, string, NodeConfig> TemplateConfig = (config, select) =>
            {
                CompleteConfig(config, select);
                return config;
            };

            public const string Default = "default", Template = "template";


            public static readonly IReadOnlyDictionary<string, NodeConfig.GetStrConfig> GetStrConfigs = new Dictionary<string, NodeConfig.GetStrConfig>
            {
                [Default] = new() { GetConfigLabel = "\"获取输入\"", GetConfigSymbol = "GetConfig", GetConfigGetstring = "ChainedNodeConfig.BaseNode", GetConfigQuerySteps = "FindNodeNoOutputsSet" },
                [Template] = new() { GetConfigGetstring = "AI.AIInput.RequestCode", GetConfigQuerySteps = "FindNodeNoOutputsSet", GetConfigSymbol = "TEST_1_", GetConfigLabel = "\"获取输入\"" }
            };

            public static readonly IReadOnlyDictionary<string, NodeConfig.FuncPipelineConfig> FuncPipelineConfigs = new Dictionary<string, NodeConfig.FuncPipelineConfig>
            {
                [Default] = new() { ParameterKeys = "\"ParameterKeys\"", ResultKeys = "\"ResultKeys\"", ProcessSymbol = "Process", FuncName = "ProcessData", ParameterType = "string", Params = "\"INITIALPARAMETER_1\", \"INITIALPARAMETER_2\"" }
            };

            public static readonly IReadOnlyDictionary<string, NodeConfig.AddNodeConfig> AddNodeConfigs = new Dictionary<string, NodeConfig.AddNodeConfig>
            {
                [Default] = new() { AddConfigAddKey = "ChainedNodeConfig.BaseNode", AddAICallCellInvoke = true, AddAutoCreateRoot = true, AddParameters = "additional_params" },
                [Template] = new() { AddConfigAddKey = "AI.AIInput.RequestCode", AddAICallCellInvoke = true, AddAutoCreateRoot = true, AddParameters = "additional_params" }
            };

            public static readonly IReadOnlyDictionary<string, NodeConfig.NextNodeConfig> NextNodeConfigs = new Dictionary<string, NodeConfig.NextNodeConfig>
            {
                [Default] = new() { HandleNextNodeRun = true, NextNodeKey = "next_node" },
                [Template] = new() { HandleNextNodeRun = false, NextNodeKey = "next_node" }
            };

            public static readonly IReadOnlyDictionary<string, NodeConfig.BasicConfig> BasicConfigs = new Dictionary<string, NodeConfig.BasicConfig>
            {
                [Default] = new() { Callkey = "call_", DirectString = "DirectINPUT", Show = true, SwitchReturn = "false" },
                [Template] = new() { DirectString = "DirectINPUT", SwitchReturn = "Config", Show = true, Callkey = "call_" }
            };


            public static readonly ITemplateConfigProvider<NodeConfig.GetStrConfig> GetStrTemplates
                = CreateProvider(GetStrConfigs);

            public static readonly ITemplateConfigProvider<NodeConfig.FuncPipelineConfig> FuncPipelineTemplates
                = CreateProvider(FuncPipelineConfigs);

            public static readonly ITemplateConfigProvider<NodeConfig.AddNodeConfig> AddNodeTemplates
                = CreateProvider(AddNodeConfigs);

            public static readonly ITemplateConfigProvider<NodeConfig.NextNodeConfig> NextNodeTemplates
                = CreateProvider(NextNodeConfigs);

            public static readonly ITemplateConfigProvider<NodeConfig.BasicConfig> BasicTemplates
                = CreateProvider(BasicConfigs);

            private static TemplateConfigProvider<T> CreateProvider<T>(IReadOnlyDictionary<string, T> configs)
                where T : class
            {
                var provider = new TemplateConfigProvider<T>(Default);
                provider.RegisterRange(configs);
                return provider;
            }
        }

        public static class ConfigType
        {
            public const string Default = "Default", Template = "Template";
        }

        public static string CreateNode(NodeConfig config)
        {
            var nodeDto = new
            {
                Label = config.Label ?? "",
                Symbol = config.Symbol ?? "",
                GetConfig = new[]
                {
                    new
                    {
                        Label = UniversalMiddleware.Extensions.JsonElementExtensions.TryParseJsonArray(config.getStrConfig?.GetConfigLabel),
                        Symbol = config.getStrConfig?.GetConfigSymbol ?? "",
                        Getstring = config.getStrConfig?.GetConfigGetstring ?? "",
                        QuerySteps = UniversalMiddleware.Extensions.JsonElementExtensions.TryParseJsonArray(config.getStrConfig?.GetConfigQuerySteps)
                    }
                },
                ExeConfig = new
                {
                    ParameterKeys = UniversalMiddleware.Extensions.JsonElementExtensions.ParseArrayContent(config.funcPipelineConfig?.ParameterKeys),
                    ResultKeys = UniversalMiddleware.Extensions.JsonElementExtensions.ParseArrayContent(config.funcPipelineConfig?.ResultKeys),
                    FuncPipeline = new
                    {
                        Calls = new[]
                        {
                            new
                            {
                                Symbol = config.funcPipelineConfig?.ProcessSymbol ?? "",
                                FuncName = config.funcPipelineConfig?.FuncName ?? "",
                                ParameterType = config.funcPipelineConfig?.ParameterType ?? "",
                                Parameters = UniversalMiddleware.Extensions.JsonElementExtensions.TryParseJsonArray(config.funcPipelineConfig?.Params)
                            }
                        }
                    }
                },
                AddConfig = new
                {
                    AddKey = config.addNodeConfig?.AddConfigAddKey ?? "",
                    AutoCreateRoot = config.addNodeConfig?.AddAutoCreateRoot ?? true,
                    AddAICallCellInvoke = config.addNodeConfig?.AddAICallCellInvoke ?? true,
                    AddParameters = config.addNodeConfig?.AddParameters ?? ""
                },
                ConnectConfig = new
                {
                    HandleNextNodeRun = config.nextNodeConfig?.HandleNextNodeRun ?? true,
                    NodeKey = config.nextNodeConfig?.NextNodeKey ?? "",
                    NextNodeKey = config.nextNodeConfig?.NextNodeKey ?? "",
                    ConnectChainInfoKey = config.nextNodeConfig?.ConnectChainInfoKey,
                    ConnectChainInfoConfig = UniversalMiddleware.Extensions.JsonElementExtensions.TryParseJsonArray(config.nextNodeConfig?.ConnectChainInfoConfig),
                    MapKeyConfig = UniversalMiddleware.Extensions.JsonElementExtensions.TryParseJsonArray(config.nextNodeConfig?.MapKeyConfig),
                    UpdateChain = config.nextNodeConfig?.UpdateChain ?? false,
                    BeforeNodeCallkey = config.nextNodeConfig?.BeforeNodeCallkey ?? "",
                    AfterNodeCallkey = config.nextNodeConfig?.AfterNodeCallkey ?? ""
                }
            };

            return ApplyPayloadDefaults(JsonConvert.SerializeObject(nodeDto, ServiceDefaults.SerializerSettings));
        }

        /// <summary>
        /// 载荷缺省声明（「缺失即补、已有即保留」唯一实现 = 框架 <see cref="SpecDefaults"/>）：
        /// 只填载荷字段，<b>不参与</b>"写哪/键是什么"的决策（治 P13：与位置解析物理隔离）。
        /// 缺省空声明 ⇒ 行为不变（零回归）。
        /// </summary>
        public static SpecDefaults PayloadDefaults { get; } = new();

        /// <summary>装载载荷缺省声明（深度合并进已有缺省集）。</summary>
        public static void LoadPayloadDefaults(string defaultsJson) => PayloadDefaults.Load(defaultsJson);

        /// <summary>按载荷缺省补齐 JSON 载荷（框架 SpecDefaults 为唯一实现）。</summary>
        public static string ApplyPayloadDefaults(string payloadJson) => PayloadDefaults.Merge(payloadJson);

        public static void CompleteConfig(NodeConfig config, string configType = ConfigType.Default)
        {
            global::GraphFramework.Helpers.Guard.AgainstNullOrEmpty(config.Symbol, nameof(config.Symbol));

            config.getStrConfig = Merge(config.getStrConfig, BaseConfig.GetStrTemplates, configType);
            config.funcPipelineConfig = Merge(config.funcPipelineConfig, BaseConfig.FuncPipelineTemplates, configType);
            config.addNodeConfig = Merge(config.addNodeConfig, BaseConfig.AddNodeTemplates, configType);
            config.nextNodeConfig = Merge(config.nextNodeConfig, BaseConfig.NextNodeTemplates, configType);
            config.basicConfig = Merge(config.basicConfig, BaseConfig.BasicTemplates, configType);
        }

        private static T Merge<T>(T target, ITemplateConfigProvider<T> provider, string type) where T : class, new()
        {
            if (target == null)
                target = new T();

            if (provider.TryGet(type, out var source) && source != null)
            {
                ObjectMerger.Merge(target, source, Share.LogicComposition.Models.MergeOptions.FillDefaults);
                return target;
            }

            return target;
        }
    }
}
