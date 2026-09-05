#region USINGS
using System;
using System.Collections.Generic;
#endregion

namespace WorkFlow
{
    public static class WorkflowFactory
    {
        public static List<object> CreateEmptyWorkflow() => new List<object>();

        public static List<object> CreateWorkflowTemplate(string templateName, Dictionary<string, object>? parameters = null)
        {
            var nodes = new List<object>();

            if (!string.IsNullOrEmpty(templateName))
            {
                var defaultLabel = parameters != null && parameters.ContainsKey("DefaultLabel") ? parameters["DefaultLabel"] as string : null;
                var nodeIdPrefix = parameters != null && parameters.ContainsKey("NodeIdPrefix") ? parameters["NodeIdPrefix"] as string : "node";

                WorkflowBuilder.AddNode(
                    nodes,
                    $"{templateName}_Start",
                    $"{nodeIdPrefix}_c0",
                    "StartFunction",
                    new object[] { "Input_" },
                    new object[] { "Output_" },
                    defaultLabel);
            }

            return nodes;
        }
    }
}
