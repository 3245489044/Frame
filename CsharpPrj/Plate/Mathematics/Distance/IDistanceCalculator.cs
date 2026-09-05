namespace MathematicalAlgorithms
{
    public interface IDistanceCalculator
    {
        double Calculate(double[] a, double[] b);
        DistanceMetricType MetricType { get; }
    }
}


