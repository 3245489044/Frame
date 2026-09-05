﻿#region USINGS
using System;
using System.Collections.Generic;
using SkeletonFlow;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Skeletons.NodeProcess;
#endregion

namespace WorkFlowMethod.GraphFramework.SkeletonBridge
{
    public static class SkeletonBridgeIndex
    {
        public static IReadOnlyList<SkeletonInfo> GetSkeletonInfos()
        {
            return new List<SkeletonInfo>
            {
                new SkeletonInfo(
                    name: "NodeChainSkeleton",
                    description: "节点链创建（替代 NodeManager 静态方法）",
                    unifiedMethods: "NodeChainSkeleton.CreateChain (替代 NodeManager.CreateChainInfo)"),
            };
        }
    }

    public class SkeletonInfo
    {
        public string Name { get; }
        public string Description { get; }
        public string UnifiedMethods { get; }

        public SkeletonInfo(string name, string description, string unifiedMethods)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            UnifiedMethods = unifiedMethods ?? throw new ArgumentNullException(nameof(unifiedMethods));
        }
    }
}

