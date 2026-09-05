#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using WorkFlowMethod.Execution.Engine;
using WorkFlowMethod.Execution.Session;
using GraphFramework.Snapshots;
using Share.FrameworkAbstractions.Utilities;
using OpNodeDef = WorkFlowMethod.Skeletons.NodeProcess.Models.NodeDefinition;
using NodeConfig = WorkFlowMethod.Skeletons.NodeProcess.Models.NodeConfig;
using BasicExecutionResult = TConfig.ExecutionResult;
using ShareExecutionResult = Share.FrameworkAbstractions.Utilities.ExecutionResult;
#endregion

namespace WorkFlowMethod.Execution.Adapters
{
    public static class ModelMapping
    {
    public static TConfig.ExecutionPlan MapToShareWorkflow(GraphSnapshot<string, NodeConfig, object> snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var workflow = new TConfig.ExecutionPlan
        {
            Id = snapshot.GraphId,
            Name = snapshot.GraphId
        };

        foreach (var kvp in snapshot.Nodes)
        {
            var nodeDef = new OpNodeDef
            {
                Id = kvp.Key,
                NodeKey = kvp.Key,
                Symbol = kvp.Value.Symbol,
                Label = kvp.Value.Label,
                Type = "Function",
                OriginalConfig = kvp.Value,
                Properties = new Dictionary<string, object>()
            };
            var shareNode = MapToShareNodeDef(kvp.Key, nodeDef);
            workflow.Steps.Add(shareNode);
        }

        var connections = new List<Dictionary<string, string>>();
        foreach (var kvp in snapshot.Nodes)
        {
            var nodeKey = kvp.Key;
            var nextKeys = snapshot.GetNextNodeKeys(nodeKey);
            foreach (var nextKey in nextKeys)
            {
                connections.Add(new Dictionary<string, string>
                {
                    ["From"] = nodeKey,
                    ["To"] = nextKey
                });
            }
        }
        if (connections.Count > 0)
        {
            workflow.Metadata["Connections"] = connections;
        }

        var entryNode = snapshot.GetEntryNodeKeys();
        if (entryNode != null)
        {
            workflow.Metadata["EntryNodeId"] = entryNode;
        }

        return workflow;
    }

        public static TConfig.StepDefinition MapToShareNodeDef(string nodeKey, OpNodeDef opNode)
        {
            if (opNode == null)
                throw new ArgumentNullException(nameof(opNode));

            var shareNode = new TConfig.StepDefinition
            {
                Id = opNode.NodeKey ?? nodeKey,
                Name = opNode.Symbol ?? opNode.Label ?? nodeKey,
                StepType = MapNodeType(opNode.Type),
                IsEnabled = true,
                IsSkipped = false
            };

            if (opNode.Properties != null)
            {
                foreach (var prop in opNode.Properties)
                {
                    shareNode.Metadata[prop.Key] = prop.Value;
                }
            }

            if (!string.IsNullOrEmpty(opNode.Symbol))
            {
                shareNode.Metadata["FunctionName"] = opNode.Symbol;
            }

            return shareNode;
        }

        private static string MapNodeType(string? nodeType)
        {
            if (string.IsNullOrEmpty(nodeType))
                return "Function";

            return nodeType.ToUpperInvariant() switch
            {
                "FUNCTION" or "FUNC" => "Function",
                "CONDITION" or "COND" or "CONDITIONAL" => "Condition",
                "LOOP" or "ITERATION" => "Loop",
                "PARALLEL" or "PARALLEL_EXECUTION" => "Parallel",
                "START" or "ENTRY" => "Start",
                "END" or "EXIT" => "End",
                _ => "Function"
            };
        }

        public static OpNodeDef MapToOpNodeDef(TConfig.StepDefinition shareNode)
        {
            if (shareNode == null)
                throw new ArgumentNullException(nameof(shareNode));

            return new OpNodeDef
            {
                Id = shareNode.Id,
                NodeKey = shareNode.Id,
                Symbol = shareNode.Name,
                Label = shareNode.Name,
                Type = MapNodeType(shareNode.StepType),
                Properties = new Dictionary<string, object>(shareNode.Metadata)
            };
        }

        private static ExecutionResult CreateOpResult(
            bool isSuccess, string nodeKey, string? errorMessage, TimeSpan? duration)
        {
            var result = isSuccess
                ? ExecutionResult.Success((object?)nodeKey)
                : ExecutionResult.Failure(errorMessage ?? "Unknown error");
            result.Duration = duration ?? TimeSpan.Zero;
            return result;
        }

        public static ExecutionResult MapToOpResult(
            ShareExecutionResult shareResult,
            ExecutionSession? session = null)
        {
            if (shareResult == null)
                throw new ArgumentNullException(nameof(shareResult));

            return CreateOpResult(
                shareResult.IsSuccess,
                session?.SessionId ?? "unknown",
                shareResult.ErrorMessage,
                shareResult.Duration ?? TimeSpan.Zero);
        }

        public static BasicExecutionResult MapToShareResult(ExecutionResult opResult)
        {
            if (opResult == null)
                throw new ArgumentNullException(nameof(opResult));

            if (opResult.IsSuccess)
            {
            return BasicExecutionResult.Success(
                null,
                opResult.Duration);
            }

            return BasicExecutionResult.Failure(
                opResult.ErrorMessage ?? "Unknown error");
        }

        public static ExecutionResult MapToOpNodeResult(
            BasicExecutionResult shareResult,
            string nodeKey)
        {
            if (shareResult == null)
                throw new ArgumentNullException(nameof(shareResult));

            return CreateOpResult(
                shareResult.IsSuccess,
                nodeKey,
                shareResult.ErrorMessage,
                shareResult.ExecutionTime);
        }
    }

}

