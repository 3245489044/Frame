#region USINGS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class Maths
    {
        #region 激活函数

        public static class Activation
        {
            public static double ReLU(double x) => ActivationFunctions.ReLU(x);

            public static double GELU(double x) => ActivationFunctions.GELU(x);

            public static double Swish(double x) => ActivationFunctions.Swish(x);

            public static double Sigmoid(double x) => ActivationFunctions.Sigmoid(x);

            public static double Tanh(double x) => ActivationFunctions.Tanh(x);

            public static double Softplus(double x) => ActivationFunctions.Softplus(x);

            public static double PReLU(double x, double alpha) => ActivationFunctions.PReLU(x, alpha);

            public static double ELU(double x, double alpha) => ActivationFunctions.ELU(x, alpha);

            public static double LeakyReLU(double x, double alpha) => ActivationFunctions.LeakyReLU(x, alpha);

            public static double Apply(double x, ActivationType type) => ActivationFunctions.Apply(x, type);

            public static string GetName(ActivationType type) => ActivationFunctions.GetName(type);

            public static bool IsValid(double value, ActivationType type) => ActivationFunctions.IsValidActivation(value, type);
        }

        #endregion

        #region 广播操作

        public static class Broadcast
        {
            public static int[] GetShape(int[] shapeA, int[] shapeB) => BroadcastHelper.GetBroadcastShape(shapeA, shapeB);

            public static bool CanBroadcast(int[] shapeA, int[] shapeB) => BroadcastHelper.CanBroadcast(shapeA, shapeB);

            public static int[] GetSourceIndex(int[] targetIndices, int[] sourceShape) => BroadcastHelper.GetSourceIndex(targetIndices, sourceShape);

            public static (int srcI, int srcJ) GetSourceIndex2D(int i, int j, int sourceRows, int sourceCols)
                => BroadcastHelper.GetSourceIndex2D(i, j, sourceRows, sourceCols);

            public static bool HasBroadcastDim(int[] shape) => BroadcastHelper.HasBroadcastDim(shape);

            public static double[,] Add(double[,] a, double[,] b) => BroadcastOperations.Add(a, b);

            public static double[,] Subtract(double[,] a, double[,] b) => BroadcastOperations.Subtract(a, b);

            public static double[,] Multiply(double[,] a, double[,] b) => BroadcastOperations.Multiply(a, b);

            public static double[,] Divide(double[,] a, double[,] b) => BroadcastOperations.Divide(a, b);

            public static double[,] BinaryOp(double[,] a, double[,] b, Func<double, double, double> op)
                => BroadcastOperations.BroadcastBinaryOp(a, b, op);

            public static Task<double[,]> AddAsync(double[,] a, double[,] b, CancellationToken ct = default)
                => BroadcastOperations.AddAsync(a, b, ct);

            public static Task<double[,]> SubtractAsync(double[,] a, double[,] b, CancellationToken ct = default)
                => BroadcastOperations.SubtractAsync(a, b, ct);

            public static Task<double[,]> MultiplyAsync(double[,] a, double[,] b, CancellationToken ct = default)
                => BroadcastOperations.MultiplyAsync(a, b, ct);

            public static Task<double[,]> DivideAsync(double[,] a, double[,] b, CancellationToken ct = default)
                => BroadcastOperations.DivideAsync(a, b, ct);
        }

        #endregion

        #region 归一化

        public static class Normalization
        {
            public static double[,] LayerNorm(double[,] input, double[] gamma, double[] beta, double eps = 1e-5)
                => NormalizationOperations.LayerNorm(input, gamma, beta, eps);

            public static Task<double[,]> LayerNormAsync(double[,] input, double[] gamma, double[] beta, double eps = 1e-5, CancellationToken ct = default)
                => NormalizationOperations.LayerNormAsync(input, gamma, beta, eps, ct);

            public static double[,] RMSNorm(double[,] input, double[] gamma, double eps = 1e-5)
                => NormalizationOperations.RMSNorm(input, gamma, eps);

            public static Task<double[,]> RMSNormAsync(double[,] input, double[] gamma, double eps = 1e-5, CancellationToken ct = default)
                => NormalizationOperations.RMSNormAsync(input, gamma, eps, ct);
        }

        #endregion

        #region 归约操作

        public static class Reduce
        {
            public static double Mean(double[] input) => ReduceOperations.Mean(input);

            public static double[,] Mean(double[,] input, int dim) => ReduceOperations.Mean(input, dim);

            public static double Var(double[] input) => ReduceOperations.Var(input);

            public static double[,] Var(double[,] input, int dim) => ReduceOperations.Var(input, dim);

            public static double Sum(double[] input) => ReduceOperations.Sum(input);

            public static double[,] Sum(double[,] input, int dim) => ReduceOperations.Sum(input, dim);

            public static double Max(double[] input) => ReduceOperations.Max(input);

            public static double[,] Max(double[,] input, int dim) => ReduceOperations.Max(input, dim);

            public static int ArgMax(double[] input) => ReduceOperations.ArgMax(input);

            public static double[,] ArgMax(double[,] input, int dim) => ReduceOperations.ArgMax(input, dim);

            public static double Reduce1D(double[] input, Func<ReadOnlySpan<double>, double> reducer)
                => ReduceOperations.Reduce(input, reducer);

            public static double[,] Reduce2D(double[,] input, int dim, Func<ReadOnlySpan<double>, double> reducer)
                => ReduceOperations.Reduce(input, dim, reducer);
        }

        #endregion

        #region 采样操作

        public static class Sampling
        {
            public static double[] Softmax(double[] logits, double temperature = 1.0)
                => SamplingOperations.Softmax(logits, temperature);

            public static (int index, double prob)[] GetTopK(double[] probs, int k)
                => SamplingOperations.GetTopK(probs, k);

            public static int SampleToken(double[] logits, double temperature, int topK, double topP, double minP, Random rng)
                => SamplingOperations.SampleToken(logits, temperature, topK, topP, minP, rng);

            public static int SampleFromDistribution(double[] probs, Random rng)
                => SamplingOperations.SampleFromDistribution(probs, rng);

            public static double[] CorrectDistribution(double[] probs, int rejectedToken)
                => SamplingOperations.CorrectDistribution(probs, rejectedToken);
        }

        #endregion

        #region Softmax

        public static class Softmax
        {
            public static double[] Compute(double[] input) => SoftmaxCalculator.Compute(input);

            public static double[,] Compute(double[,] input, int dim = 1) => SoftmaxCalculator.Compute(input, dim);

            public static double[,,] Compute(double[,,] input) => SoftmaxCalculator.Compute(input);

            public static double[,] Compute2D(double[,] input) => SoftmaxCalculator.ComputeSoftmax2D(input);

            public static double[] LogSoftmax(double[] input) => SoftmaxCalculator.LogSoftmax(input);
        }

        #endregion

        #region 张量工厂

        public static class Tensor
        {
            public static double[,] Zeros(int rows, int cols) => TensorFactory.Zeros(rows, cols);

            public static double[,] Ones(int rows, int cols) => TensorFactory.Ones(rows, cols);

            public static double[,] Fill(int rows, int cols, double value) => TensorFactory.Fill(rows, cols, value);

            public static double[,] Random(int rows, int cols, Random? rng = null) => TensorFactory.Random(rows, cols, rng);

            public static double[,] Identity(int size) => TensorFactory.Identity(size);

            public static double[,] Diagonal(double[] diagonal) => TensorFactory.Diagonal(diagonal);

            public static double[,] Create<TState>(int rows, int cols, TState state, Func<TState, int, int, double> valueProvider)
                => TensorFactory.Create(rows, cols, state, valueProvider);

            public static double[,] FromArray(double[] data, int rows, int cols) => TensorFactory.FromArray(data, rows, cols);

            public static double[,] FromJaggedArray(double[][] jaggedArray) => TensorFactory.FromJaggedArray(jaggedArray);
        }

        #endregion

        #region 一元运算

        public static class Unary
        {
            public static double[] Apply(double[] input, Func<double, double> func) => UnaryOperations.Apply(input, func);

            public static double[,] Apply(double[,] input, Func<double, double> func) => UnaryOperations.Apply(input, func);

            public static double[] AddScalar(double[] input, double scalar) => UnaryOperations.AddScalar(input, scalar);

            public static double[,] AddScalar(double[,] input, double scalar) => UnaryOperations.AddScalar(input, scalar);

            public static double[] MultiplyScalar(double[] input, double scalar) => UnaryOperations.MultiplyScalar(input, scalar);

            public static double[,] MultiplyScalar(double[,] input, double scalar) => UnaryOperations.MultiplyScalar(input, scalar);

            public static double[] DivideScalar(double[] input, double scalar) => UnaryOperations.DivideScalar(input, scalar);

            public static double[,] DivideScalar(double[,] input, double scalar) => UnaryOperations.DivideScalar(input, scalar);

            public static double[] SubtractScalar(double[] input, double scalar) => UnaryOperations.SubtractScalar(input, scalar);

            public static double[,] SubtractScalar(double[,] input, double scalar) => UnaryOperations.SubtractScalar(input, scalar);

            public static double[] Abs(double[] input) => UnaryOperations.Abs(input);

            public static double[,] Abs(double[,] input) => UnaryOperations.Abs(input);

            public static double[] Square(double[] input) => UnaryOperations.Square(input);

            public static double[,] Square(double[,] input) => UnaryOperations.Square(input);

            public static double[] Sqrt(double[] input) => UnaryOperations.Sqrt(input);

            public static double[,] Sqrt(double[,] input) => UnaryOperations.Sqrt(input);

            public static double[] Exp(double[] input) => UnaryOperations.Exp(input);

            public static double[,] Exp(double[,] input) => UnaryOperations.Exp(input);

            public static double[] Log(double[] input) => UnaryOperations.Log(input);

            public static double[,] Log(double[,] input) => UnaryOperations.Log(input);
        }

        #endregion

        #region 距离计算

        public static class Distance
        {
            public static double Euclidean(double[] a, double[] b) => DistanceCalculator.EuclideanDistance(a, b);

            public static double Manhattan(double[] a, double[] b) => DistanceCalculator.ManhattanDistance(a, b);

            public static double CosineSimilarity(double[] a, double[] b) => DistanceCalculator.CosineSimilarity(a, b);

            public static double CosineDistance(double[] a, double[] b) => DistanceCalculator.CosineDistance(a, b);

            public static double JaccardSimilarity<T>(HashSet<T> set1, HashSet<T> set2) => DistanceCalculator.JaccardSimilarity(set1, set2);

            public static double JaccardDistance<T>(HashSet<T> set1, HashSet<T> set2) => DistanceCalculator.JaccardDistance(set1, set2);

            public static int HammingDistance(string str1, string str2) => DistanceCalculator.HammingDistance(str1, str2);

            public static double Chebyshev(double[] a, double[] b) => DistanceCalculator.ChebyshevDistance(a, b);

            public static double Minkowski(double[] a, double[] b, double p) => DistanceCalculator.MinkowskiDistance(a, b, p);

            public static IDistanceCalculator CreateCalculator(DistanceMetricType metric)
                => DistanceCalculatorFactory.Create(metric);
        }

        #endregion

        #region 相似度

        public static class Similarity
        {
            public static double Jaccard(HashSet<string> setA, HashSet<string> setB)
                => SimilarityCalculators.JaccardSimilarity(setA, setB);

            public static double Cosine(double[] vectorA, double[] vectorB)
                => SimilarityCalculators.CosineSimilarity(vectorA, vectorB);

            public static double PearsonCorrelation(double[] x, double[] y)
                => SimilarityCalculators.PearsonCorrelation(x, y);
        }

        #endregion

        #region 向量运算

        public static class Vector
        {
            public static double EuclideanDistance(double[] a, double[] b) => VectorMath.EuclideanDistance(a, b);

            public static double ManhattanDistance(double[] a, double[] b) => VectorMath.ManhattanDistance(a, b);

            public static double CosineDistance(double[] a, double[] b) => VectorMath.CosineDistance(a, b);

            public static double[] Normalize(double[] vector) => VectorMath.Normalize(vector);

            public static double DotProduct(double[] a, double[] b) => VectorMath.DotProduct(a, b);

            public static double[] Centroid(IEnumerable<double[]> vectors) => VectorOperations.CalculateCentroid(vectors);

            public static List<double[]> Standardize(List<double[]> features) => VectorOperations.StandardizeFeatures(features);

            public static double Norm(double[] vector) => VectorOperations.Norm(vector);
        }

        #endregion

        #region 矩阵运算

        public static class Matrix
        {
            public static MathNet.Numerics.LinearAlgebra.Matrix<double> ToMatrix(List<double[]> vectors)
                => MatrixOperations.ConvertToMatrix(vectors);

            public static List<double[]> FromMatrix(MathNet.Numerics.LinearAlgebra.Matrix<double> matrix)
                => MatrixOperations.ConvertFromMatrix(matrix);
        }

        #endregion

        #region 聚类算法

        public static class Clustering
        {
            public static List<Cluster<double[]>> KMeans(double[][] data, int k)
                => ClusteringAlgorithms.KMeans(data, k);

            public static List<Cluster<double[]>> Hierarchical(double[][] data)
                => ClusteringAlgorithms.HierarchicalClustering(data);

            public static List<Cluster<double[]>> DBSCAN(double[][] data, double epsilon, int minPoints)
                => ClusteringAlgorithms.DBSCAN(data, epsilon, minPoints);

            public static List<Cluster<double[]>> Spectral(double[][] data, int nClusters)
                => ClusteringAlgorithms.SpectralClustering(data, nClusters);

            public static List<Cluster<double[]>> AffinityPropagation(double[][] data)
                => ClusteringAlgorithms.AffinityPropagation(data);

            public static List<Cluster<double[]>> MeanShift(double[][] data)
                => ClusteringAlgorithms.MeanShift(data);

            public static List<Cluster<double[]>> GaussianMixture(double[][] data, int nComponents)
                => ClusteringAlgorithms.GaussianMixture(data, nComponents);

            public static List<Cluster<double[]>> FuzzyCMeans(double[][] data, int nClusters, double m = 2.0)
                => ClusteringAlgorithms.FuzzyCMeans(data, nClusters, m);

            public static List<Cluster<double[]>> BIRCH(double[][] data, int nClusters, double threshold = 0.5)
                => ClusteringAlgorithms.BIRCH(data, nClusters, threshold);

            public static List<Cluster<double[]>> Optics(double[][] data, double epsilon, int minPoints)
                => ClusteringAlgorithms.Optics(data, epsilon, minPoints);

            public static KMeansClusterer CreateKMeansClusterer(int numClusters, int maxIterations = 100,
                DistanceMetricType metric = DistanceMetricType.Euclidean, int numAttempts = 10)
                => new(numClusters, maxIterations, metric, numAttempts);

            public static KMeansClustering<T> CreateKMeansAnalyzer<T>(ClusteringConfig? config = null,
                DistanceMetricType metricType = DistanceMetricType.Euclidean)
                => new(config, metricType);

            public static HierarchicalClustering<T> CreateHierarchicalAnalyzer<T>(ClusteringConfig? config = null,
                LinkageMethod linkageMethod = LinkageMethod.Average,
                DistanceMetricType metricType = DistanceMetricType.Euclidean)
                => new(config, linkageMethod, metricType);
        }

        #endregion

        #region 统计分析

        public static class Statistics
        {
            public static double Entropy(IEnumerable<double> probabilities)
                => StatisticalAnalysis.CalculateEntropy(probabilities);

            public static double Entropy(IEnumerable<int> counts)
                => StatisticalAnalysis.CalculateEntropy(counts);

            public static double Gini(IEnumerable<double> probabilities)
                => StatisticalAnalysis.CalculateGini(probabilities);

            public static double Gini(IEnumerable<int> counts)
                => StatisticalAnalysis.CalculateGini(counts);

            public static double Variance(IEnumerable<double> values)
                => StatisticalAnalysis.CalculateVariance(values);

            public static double Skewness(IEnumerable<double> values)
                => StatisticalAnalysis.CalculateSkewness(values);

            public static double Kurtosis(IEnumerable<double> values)
                => StatisticalAnalysis.CalculateKurtosis(values);

            public static double SilhouetteScore(List<double[]> data, int[] labels, DistanceMetricType metric = DistanceMetricType.Euclidean)
                => StatisticalAnalysis.CalculateSilhouetteScore(data, labels, metric);

            public static double Mean(double[] values)
                => ReduceOperations.Mean(values);

            public static double Median(IEnumerable<double> values)
                => StatisticalAnalysis.Median(values);

            public static double StandardDeviation(IEnumerable<double> values, bool isSample = true)
                => Maths.Utility.StandardDeviation(values, isSample);
        }

        #endregion

        #region 旋转位置编码 (RoPE)

        public static class RotaryPositionEncoding
        {
            public static (double[,] cos, double[,] sin) PrecomputeFrequencies(int seqLen, int dim, double theta = 10000.0)
                => global::MathematicalAlgorithms.RotaryPositionEncoding.PrecomputeFrequencies(seqLen, dim, theta);

            public static double[] ApplyRotation(double[] x, double[] cos, double[] sin, int halfDim)
                => global::MathematicalAlgorithms.RotaryPositionEncoding.ApplyRotation(x, cos, sin, halfDim);
        }

        #endregion

        #region 维度辅助

        public static class Dimension
        {
            public static void Validate(int dim, int maxDim) => DimensionHelper.ValidateDim(dim, maxDim);

            public static int Normalize(int dim, int totalDims) => DimensionHelper.NormalizeDim(dim, totalDims);

            public static string GetName(int dim) => DimensionHelper.GetDimName(dim);

            public static int[] GetReducedShape(int[] shape, int dim, bool keepDim)
                => DimensionHelper.GetReducedShape(shape, dim, keepDim);

            public static int[] GetTransposedShape(int[] shape, int dim1, int dim2)
                => DimensionHelper.GetTransposedShape(shape, dim1, dim2);

            public static int[] BroadcastShapes(int[] shapeA, int[] shapeB)
                => DimensionHelper.BroadcastShapes(shapeA, shapeB);

            public static bool CanBroadcast(int[] shapeA, int[] shapeB)
                => DimensionHelper.CanBroadcast(shapeA, shapeB);

            public static int[] GetBroadcastSourceIndex(int[] targetIndices, int[] sourceShape)
                => DimensionHelper.GetBroadcastSourceIndex(targetIndices, sourceShape);
        }

        #endregion

        #region 数学工具

        public static class Utility
        {
            private static readonly MathUtility _instance = new();

            public static double SafeLog(double value, double baseValue = Math.E)
                => _instance.SafeLog(value, baseValue);

            public static double WeightedAverage(IEnumerable<double> values, IEnumerable<double> weights)
                => _instance.WeightedAverage(values, weights);

            public static double StandardDeviation(IEnumerable<double> values, bool isSample = true)
                => _instance.StandardDeviation(values, isSample);

            public static double Percentile(IEnumerable<double> values, double percentile)
                => _instance.Percentile(values, percentile);

            public static double LinearInterpolation(double x, double x0, double x1, double y0, double y1)
                => _instance.LinearInterpolation(x, x0, x1, y0, y1);
        }

        #endregion

        #region 数学常量

        public static class Constants
        {
            public const double Epsilon = MathConstants.EPSILON;

            public const int ParallelThreshold = MathConstants.PARALLEL_THRESHOLD;

            public const int DefaultKMeansIterations = MathConstants.DEFAULT_KMEANS_ITERATIONS;

            public const int MinimumSampleSize = MathConstants.MINIMUM_SAMPLE_SIZE;

            public const double DefaultMergeThreshold = MathConstants.DEFAULT_MERGE_THRESHOLD;
        }

        #endregion

        #region 内存管理

        public static class Memory
        {
            public static bool HasSufficient(long requiredBytes, object? config = null)
                => MemoryManager.HasSufficientMemory(requiredBytes, config);

            public static long GetCurrentUsage() => MemoryManager.GetCurrentMemoryUsage();

            public static string FormatBytes(long bytes) => MemoryManager.FormatBytes(bytes);
        }

        #endregion

        #region 变换框架 (Transform)

        public static class Transform
        {
            private static readonly TransformRegistry _registry = new();

            public static TransformRegistry Registry => _registry;

            public static Task<TransformResult<TOutput>> RunAsync<TInput, TOutput>(
                string transformName,
                TInput input,
                TransformConfig? config = null,
                IProgress<ComputationProgress>? progress = null,
                CancellationToken cancellationToken = default)
                => _registry.TransformAsync<TInput, TOutput>(transformName, input, config, progress, cancellationToken);

            public static void Register<TInput, TOutput>(string name, ITransform<TInput, TOutput> transform)
                => _registry.Register(name, transform);

            public static bool HasTransform(string name) => _registry.HasTransform(name);

            public static IReadOnlyCollection<string> GetTransformNames() => _registry.GetTransformNames();
        }

        #endregion
    }
}
