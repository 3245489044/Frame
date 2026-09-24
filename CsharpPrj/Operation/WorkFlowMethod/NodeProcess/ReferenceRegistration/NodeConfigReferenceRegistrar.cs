
#region USINGS
extern alias ShareAlias;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using ShareAlias::ReferenceManagement;
using ShareAlias::ReferenceManagement.FieldMapping;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.ReferenceRegistration
{
    public static class NodeConfigReferenceRegistrar
    {
        public static void RegisterAll()
        {
            var registry = TypeReferenceRegistry.Global;

            registry.Register<NodeConfig>(ReferenceTarget.Custom, recurse: true);
            registry.Register<NodeConfig.BasicConfig>(ReferenceTarget.Custom, recurse: true);
            registry.Register<NodeConfig.NextNodeConfig>(ReferenceTarget.Custom, recurse: true);
            registry.Register<NodeConfig.GetStrConfig>(ReferenceTarget.Custom, recurse: true);
            registry.Register<NodeConfig.FuncPipelineConfig>(ReferenceTarget.Custom, recurse: true);

            var jsonHandler = new JsonContentHandler();

            registry.Register<NodeConfig.AddNodeConfig>(
                new ReferenceDefinition(ReferenceTarget.Custom, true, jsonHandler)
                {
                    Condition = NodeReferenceFields.Matches(NodeReferenceFields.Field(NodeReferenceFields.AddConfigAddKey)),
                });

            registry.Register<NodeConfig.NextNodeConfig>(
                new ReferenceDefinition(ReferenceTarget.Custom, true, jsonHandler)
                {
                    Condition = NodeReferenceFields.Matches(NodeReferenceFields.Field(NodeReferenceFields.ConnectChainInfoKey)),
                });

            registry.Register<NodeConfig.FuncPipelineConfig>(
                new ReferenceDefinition(ReferenceTarget.Custom, true, jsonHandler)
                {
                    Condition = NodeReferenceFields.Matches(NodeReferenceFields.Field(NodeReferenceFields.ParameterKeys)),
                });

            registry.Register<NodeConfig.FuncPipelineConfig>(
                new ReferenceDefinition(ReferenceTarget.Custom, true, jsonHandler)
                {
                    Condition = NodeReferenceFields.Matches(NodeReferenceFields.Field(NodeReferenceFields.ResultKeys)),
                });
        }
    }
}
