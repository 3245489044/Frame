#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class ClusteringEngine
    {
        private static readonly LoopEngine _loopEngine = new();

        internal static LoopConfig CreateLoopConfig(ClusteringComputationConfig config)
        {
            return new LoopConfig
            {
                EnableParallel = config.EnableParallel,
                MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                EnableProgressReporting = config.EnableProgressReporting,
                ParallelThreshold = config.ParallelThreshold,
                ProgressReportInterval = 100
            };
        }

        #region 距离与相似度矩阵

        public static double[,] ComputeDistanceMatrix(
            List<double[]> features,
            IDistanceCalculator distanceCalc,
            ClusteringComputationConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            int n = features.Count;
            var matrix = new double[n, n];
            config ??= ClusteringComputationConfig.Default;

            for (int i = 0; i < n; i++)
                matrix[i, i] = 0;

            var loopConfig = CreateLoopConfig(config);
            _loopEngine.ExecuteInPlaceSync(
                (Features: features, Matrix: matrix, Calc: distanceCalc),
                (ctx, i, j) =>
                {
                    if (j > i)
                    {
                        double dist = ctx.Calc.Calculate(ctx.Features[i], ctx.Features[j]);
                        ctx.Matrix[i, j] = dist;
                        ctx.Matrix[j, i] = dist;
                    }
                },
                new LoopShape(n, n),
                loopConfig);

            return matrix;
        }

        public static double[,] ComputeSimilarityMatrix(
            List<double[]> features,
            double sigma = 1.0,
            ClusteringComputationConfig config = null)
        {
            int n = features.Count;
            var matrix = new double[n, n];
            config ??= ClusteringComputationConfig.Default;

            for (int i = 0; i < n; i++)
                matrix[i, i] = 1.0;

            var loopConfig = CreateLoopConfig(config);
            _loopEngine.ExecuteInPlaceSync(
                (Features: features, Matrix: matrix, Sigma: sigma),
                (ctx, i, j) =>
                {
                    if (j > i)
                    {
                        double dist = EuclideanDistance(ctx.Features[i], ctx.Features[j]);
                        double sim = Math.Exp(-dist * dist / (2.0 * ctx.Sigma * ctx.Sigma));
                        ctx.Matrix[i, j] = sim;
                        ctx.Matrix[j, i] = sim;
                    }
                },
                new LoopShape(n, n),
                loopConfig);

            return matrix;
        }

        public static double[,] ComputeNegativeDistanceMatrix(
            List<double[]> features,
            ClusteringComputationConfig config = null)
        {
            int n = features.Count;
            var matrix = new double[n, n];
            config ??= ClusteringComputationConfig.Default;

            for (int i = 0; i < n; i++)
                matrix[i, i] = 0;

            var loopConfig = CreateLoopConfig(config);
            _loopEngine.ExecuteInPlaceSync(
                (Features: features, Matrix: matrix),
                (ctx, i, j) =>
                {
                    if (j > i)
                    {
                        double negDist = -EuclideanDistance(ctx.Features[i], ctx.Features[j]);
                        ctx.Matrix[i, j] = negDist;
                        ctx.Matrix[j, i] = negDist;
                    }
                },
                new LoopShape(n, n),
                loopConfig);

            return matrix;
        }

        #endregion

        #region K-Means 核心操作

        public static double[][] InitializeKMeansPlusPlus(
            List<double[]> features,
            int k,
            Random random,
            IDistanceCalculator distanceCalc = null)
        {
            int n = features.Count;
            var centroids = new double[k][];

            centroids[0] = features[random.Next(n)];
            for (int c = 1; c < k; c++)
            {
                var distances = new double[n];
                double totalDist = 0;

                for (int i = 0; i < n; i++)
                {
                    double minDist = double.MaxValue;
                    for (int j = 0; j < c; j++)
                    {
                        double dist = distanceCalc != null
                            ? distanceCalc.Calculate(features[i], centroids[j])
                            : EuclideanDistance(features[i], centroids[j]);
                        if (dist < minDist) minDist = dist;
                    }
                    distances[i] = minDist * minDist;
                    totalDist += distances[i];
                }

                double r = random.NextDouble() * totalDist;
                double cumulative = 0;
                for (int i = 0; i < n; i++)
                {
                    cumulative += distances[i];
                    if (cumulative >= r)
                    {
                        centroids[c] = features[i];
                        break;
                    }
                }
            }

            return centroids;
        }

        public static bool AssignClusters(
            List<double[]> data,
            int[] labels,
            double[][] centroids,
            IDistanceCalculator distanceCalc,
            ClusteringComputationConfig config = null)
        {
            int n = data.Count;
            config ??= ClusteringComputationConfig.Default;

            var loopConfig = CreateLoopConfig(config);
            var changedFlags = new bool[n];

            _loopEngine.ExecuteInPlaceSync(
                (Data: data, Labels: labels, Centroids: centroids, Calc: distanceCalc, Changed: changedFlags),
                (ctx, i, _) =>
                {
                    int bestCluster = 0;
                    double bestDist = double.MaxValue;

                    for (int k = 0; k < ctx.Centroids.Length; k++)
                    {
                        double dist = ctx.Calc.Calculate(ctx.Data[i], ctx.Centroids[k]);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestCluster = k;
                        }
                    }

                    if (ctx.Labels[i] != bestCluster)
                    {
                        ctx.Labels[i] = bestCluster;
                        ctx.Changed[i] = true;
                    }
                },
                new LoopShape(n, 1),
                loopConfig);

            for (int i = 0; i < n; i++)
            {
                if (changedFlags[i]) return true;
            }

            return false;
        }

        public static bool AssignClustersEuclidean(
            List<double[]> data,
            int[] labels,
            double[][] centroids)
        {
            bool changed = false;
            for (int i = 0; i < data.Count; i++)
            {
                int bestCluster = 0;
                double bestDist = double.MaxValue;

                for (int k = 0; k < centroids.Length; k++)
                {
                    double dist = EuclideanDistance(data[i], centroids[k]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestCluster = k;
                    }
                }

                if (labels[i] != bestCluster)
                {
                    labels[i] = bestCluster;
                    changed = true;
                }
            }
            return changed;
        }

        public static double[][] UpdateCentroids(
            List<double[]> features,
            int[] assignments,
            int k,
            int dim,
            Random random = null,
            ClusteringComputationConfig config = null)
        {
            var newCentroids = new double[k][];
            var counts = new int[k];

            for (int i = 0; i < k; i++)
                newCentroids[i] = new double[dim];

            for (int i = 0; i < features.Count; i++)
            {
                int cluster = assignments[i];
                for (int j = 0; j < dim; j++)
                    newCentroids[cluster][j] += features[i][j];
                counts[cluster]++;
            }

            for (int i = 0; i < k; i++)
            {
                if (counts[i] > 0)
                {
                    for (int j = 0; j < dim; j++)
                        newCentroids[i][j] /= counts[i];
                }
                else if (random != null)
                {
                    newCentroids[i] = features[random.Next(features.Count)];
                }
            }

            return newCentroids;
        }

        #endregion

        #region 聚类结果构建与统计

        public static List<Cluster<T>> BuildClusters<T>(
            List<double[]> features,
            double[][] centroids,
            int[] assignments,
            int k)
        {
            var clusters = new List<Cluster<T>>();
            for (int i = 0; i < k; i++)
                clusters.Add(new Cluster<T> { Centroid = centroids[i] });

            for (int i = 0; i < features.Count; i++)
            {
                int clusterIdx = assignments[i];
                clusters[clusterIdx].FeatureVectors.Add(features[i]);
            }

            return clusters;
        }

        public static void ComputeClusterStatistics<T>(
            Cluster<T> cluster,
            IDistanceCalculator distanceCalc)
        {
            if (cluster.FeatureVectors.Count == 0)
                return;

            var distances = cluster.FeatureVectors
                .Select(v => distanceCalc.Calculate(v, cluster.Centroid))
                .ToList();

            cluster.AverageDistance = distances.Average();
            cluster.MaxDistance = distances.Max();
            cluster.Radius = cluster.MaxDistance;
            cluster.Diameter = cluster.AverageDistance * 2;
            cluster.Density = cluster.FeatureVectors.Count / (1 + cluster.AverageDistance);
        }

        public static List<Cluster<T>> FinalizeClusters<T>(List<Cluster<T>> clusters)
        {
            foreach (var cluster in clusters)
            {
                if (cluster.FeatureVectors.Count > 0)
                    cluster.Centroid = VectorOperations.CalculateCentroid(cluster.FeatureVectors);
            }

            return clusters.Where(c => c.FeatureVectors.Count > 0).ToList();
        }

        #endregion

        #region 层次聚类操作

        public static Cluster<T> MergeClusters<T>(Cluster<T> clusterA, Cluster<T> clusterB)
        {
            var merged = new Cluster<T>
            {
                FeatureVectors = clusterA.FeatureVectors.Concat(clusterB.FeatureVectors).ToList(),
                Items = clusterA.Items.Concat(clusterB.Items).ToList()
            };

            merged.Centroid = VectorOperations.CalculateCentroid(merged.FeatureVectors);
            return merged;
        }

        public static double AverageLinkageDistance(Cluster<double[]> clusterA, Cluster<double[]> clusterB)
        {
            double totalDist = 0;
            int count = 0;
            foreach (var va in clusterA.FeatureVectors)
            {
                foreach (var vb in clusterB.FeatureVectors)
                {
                    totalDist += EuclideanDistance(va, vb);
                    count++;
                }
            }
            return count > 0 ? totalDist / count : double.MaxValue;
        }

        public static bool TryMergeClosestClusters<T>(
            List<Cluster<T>> clusters,
            Func<Cluster<T>, Cluster<T>, double> distanceFunc)
        {
            if (clusters.Count <= 1) return false;

            int mergeI = 0, mergeJ = 1;
            double minDist = double.MaxValue;

            for (int i = 0; i < clusters.Count; i++)
            {
                for (int j = i + 1; j < clusters.Count; j++)
                {
                    double dist = distanceFunc(clusters[i], clusters[j]);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        mergeI = i;
                        mergeJ = j;
                    }
                }
            }

            var merged = MergeClusters(clusters[mergeI], clusters[mergeJ]);
            clusters.RemoveAt(mergeJ);
            clusters.RemoveAt(mergeI);
            clusters.Add(merged);
            return true;
        }

        #endregion

        #region DBSCAN / OPTICS 操作

        public static List<int> GetNeighbors(double[][] data, int index, double epsilon)
        {
            var neighbors = new List<int>();
            for (int i = 0; i < data.Length; i++)
            {
                if (i == index) continue;
                if (EuclideanDistance(data[index], data[i]) <= epsilon)
                    neighbors.Add(i);
            }
            return neighbors;
        }

        public static double GetNthDistance(double[][] data, int index, List<int> neighbors, int n)
        {
            if (neighbors.Count < n) return double.MaxValue;
            var distances = neighbors.Select(i => EuclideanDistance(data[index], data[i])).OrderBy(d => d).ToList();
            return distances[Math.Min(n - 1, distances.Count - 1)];
        }

        public static void UpdateSeeds(
            double[][] data,
            int centerIndex,
            List<int> neighbors,
            double coreDist,
            ref double[] reachability,
            SortedSet<(double Reachability, int Index)> seeds,
            double epsilon)
        {
            foreach (var neighbor in neighbors)
            {
                if (reachability[neighbor] == double.MaxValue && !seeds.Any(s => s.Index == neighbor))
                {
                    double dist = EuclideanDistance(data[centerIndex], data[neighbor]);
                    double newReachability = Math.Max(coreDist, dist);
                    reachability[neighbor] = newReachability;
                    seeds.Add((newReachability, neighbor));
                }
            }
        }

        #endregion

        #region 核函数与概率密度

        public static double GaussianKernel(double distance, double bandwidth)
        {
            return Math.Exp(-0.5 * (distance / bandwidth) * (distance / bandwidth));
        }

        public static double GaussianPDF(double[] x, double[] mean, double[,] covariance)
        {
            int dim = x.Length;
            double det = 1;
            for (int i = 0; i < dim; i++)
                det *= covariance[i, i];

            if (det < 1e-10) return 1e-10;

            double exponent = 0;
            for (int i = 0; i < dim; i++)
            {
                double diff = x[i] - mean[i];
                exponent += diff * diff / (2 * covariance[i, i]);
            }

            return Math.Exp(-exponent) / (Math.Pow(2 * Math.PI, dim / 2.0) * Math.Sqrt(det));
        }

        public static double EstimateBandwidth(double[][] data)
        {
            int n = data.Length;
            if (n <= 1) return 1.0;

            var distances = new List<double>();
            int sampleSize = Math.Min(100, n);
            var rng = new Random();

            for (int i = 0; i < sampleSize; i++)
            {
                int idx1 = rng.Next(n);
                int idx2 = rng.Next(n);
                if (idx1 != idx2)
                    distances.Add(EuclideanDistance(data[idx1], data[idx2]));
            }

            return distances.Count > 0 ? distances.OrderBy(d => d).ElementAt(distances.Count / 2) : 1.0;
        }

        public static double GetMedian(double[,] matrix)
        {
            int n = matrix.GetLength(0);
            var values = new List<double>();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    values.Add(matrix[i, j]);

            values.Sort();
            return values[values.Count / 2];
        }

        #endregion

        #region 谱聚类辅助操作

        public static double[,] ComputeDegreeMatrix(double[,] similarityMatrix)
        {
            int n = similarityMatrix.GetLength(0);
            var degreeMatrix = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                for (int j = 0; j < n; j++)
                    sum += similarityMatrix[i, j];
                degreeMatrix[i, i] = sum;
            }
            return degreeMatrix;
        }

        public static double[,] ComputeLaplacianMatrix(double[,] similarityMatrix, double[,] degreeMatrix)
        {
            int n = similarityMatrix.GetLength(0);
            var laplacian = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    laplacian[i, j] = degreeMatrix[i, j] - similarityMatrix[i, j];
            return laplacian;
        }

        #endregion

        #region 亲和传播迭代骨架

        public static void UpdateAffinityResponsibility(
            double[,] similarity,
            double[,] availability,
            double[,] responsibility)
        {
            int n = similarity.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    double maxVal = double.MinValue;
                    for (int kk = 0; kk < n; kk++)
                    {
                        if (kk == k) continue;
                        maxVal = Math.Max(maxVal, availability[i, kk] + similarity[i, kk]);
                    }
                    responsibility[i, k] = similarity[i, k] - maxVal;
                }
            }
        }

        public static void UpdateAffinityAvailability(
            double[,] similarity,
            double[,] availability,
            double[,] responsibility)
        {
            int n = similarity.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    if (i == k)
                    {
                        double sum = 0;
                        for (int j = 0; j < n; j++)
                        {
                            if (j == k) continue;
                            sum += Math.Max(0, responsibility[j, k]);
                        }
                        availability[i, k] = sum;
                    }
                    else
                    {
                        double sum = 0;
                        for (int j = 0; j < n; j++)
                        {
                            if (j == i || j == k) continue;
                            sum += Math.Max(0, responsibility[j, k]);
                        }
                        availability[i, k] = Math.Min(0, responsibility[k, k] + sum);
                    }
                }
            }
        }

        public static void ApplyDamping(double[,] matrix, double[,] oldMatrix, double damping)
        {
            int n = matrix.GetLength(0);
            int m = matrix.GetLength(1);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++)
                    matrix[i, j] = damping * matrix[i, j] + (1 - damping) * oldMatrix[i, j];
        }

        #endregion

        #region 模糊 C-Means 辅助操作

        public static double[,] InitializeMembershipMatrix(int n, int nClusters, Random random)
        {
            var membership = new double[n, nClusters];
            for (int i = 0; i < n; i++)
            {
                double total = 0;
                for (int k = 0; k < nClusters; k++)
                {
                    membership[i, k] = random.NextDouble();
                    total += membership[i, k];
                }
                if (total > 0)
                    for (int k = 0; k < nClusters; k++)
                        membership[i, k] /= total;
            }
            return membership;
        }

        public static void UpdateFuzzyCentroids(
            double[][] data,
            double[,] membership,
            double[][] centroids,
            double m)
        {
            int n = data.Length;
            int dim = data[0].Length;
            int nClusters = centroids.Length;

            for (int k = 0; k < nClusters; k++)
            {
                for (int j = 0; j < dim; j++)
                {
                    double numerator = 0, denominator = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double u = Math.Pow(membership[i, k], m);
                        numerator += u * data[i][j];
                        denominator += u;
                    }
                    centroids[k][j] = denominator > 0 ? numerator / denominator : 0;
                }
            }
        }

        public static void UpdateFuzzyMembership(
            double[][] data,
            double[,] membership,
            double[][] centroids,
            double m)
        {
            int n = data.Length;
            int nClusters = centroids.Length;

            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < nClusters; k++)
                {
                    double dist = EuclideanDistance(data[i], centroids[k]);
                    if (dist < 1e-10)
                    {
                        membership[i, k] = 1;
                        for (int j = 0; j < nClusters; j++)
                            if (j != k) membership[i, j] = 0;
                        break;
                    }

                    double sum = 0;
                    for (int j = 0; j < nClusters; j++)
                    {
                        double distJ = EuclideanDistance(data[i], centroids[j]);
                        sum += Math.Pow(dist / distJ, 2.0 / (m - 1));
                    }
                    membership[i, k] = sum > 0 ? 1.0 / sum : 0;
                }
            }
        }

        #endregion

        #region 基础距离函数

        internal static double EuclideanDistance(double[] a, double[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double diff = a[i] - b[i];
                sum += diff * diff;
            }
            return Math.Sqrt(sum);
        }

        #endregion
    }
}
