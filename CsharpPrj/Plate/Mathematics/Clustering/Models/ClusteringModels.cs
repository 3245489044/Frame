#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace MathematicalAlgorithms
{
    #region 聚类算法数据结构

    public enum LinkageMethod { Single, Complete, Average }

    public class ClusteringConfig
    {
        public int? Seed { get; set; }
        public bool EnableParallel { get; set; } = true;
        public bool EnableCaching { get; set; } = true;
        public bool NormalizeFeatures { get; set; } = false;
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    }

    public record ClusteringComputationConfig
    {
        public bool EnableParallel { get; init; } = true;
        public bool EnableCaching { get; init; } = true;
        public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
        public bool EnableProgressReporting { get; init; } = true;
        public double MemoryLimitPercentage { get; init; } = 0.8;
        public int ParallelThreshold { get; init; } = 100;
        public CachePolicy CachePolicy { get; init; } = CachePolicy.Default;
        public long? MemoryLimitBytes { get; init; } = null;

        public static ClusteringComputationConfig Default => new();
    }

    public class Cluster<T>
    {
        public List<T> Items { get; set; } = new();
        public List<double[]> FeatureVectors { get; set; } = new();
        public double[] Centroid { get; set; } = Array.Empty<double>();
        public double Cohesion { get; set; }
        public double Coupling { get; set; }
        public double AverageDistance { get; set; }
        public double MaxDistance { get; set; }
        public double Radius { get; set; }
        public double Diameter { get; set; }
        public double Density { get; set; }
        public int Count => Items.Count;
    }

    public class ClusteringResult
    {
        public int[] Labels { get; set; }
        public double[][] Centroids { get; set; }
        public double Inertia { get; set; }
    }

    #endregion

    #region 聚类分析结果

    public class AnalysisResult<T>
    {
        public List<T> Items { get; set; } = new();
        public List<Cluster<T>> Clusters { get; set; } = new();
        public Dictionary<string, object> Metrics { get; set; } = new();
        public double AnalysisTime { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    #endregion
}
