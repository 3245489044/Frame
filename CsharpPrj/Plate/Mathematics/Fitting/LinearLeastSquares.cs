namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 小规模线性最小二乘：正规方程 + 列缩放 + 部分主元高斯消元。
/// <para>列缩放只用于求解，最终系数还原到原始量纲；秩不足返回 <c>false</c>，不抛异常。</para>
/// </summary>
internal static class LinearLeastSquares
{
    public static bool TrySolve(
        IReadOnlyList<double[]> rows,
        IReadOnlyList<double> observations,
        out double[] coefficients)
    {
        coefficients = Array.Empty<double>();

        int n = observations.Count;
        if (rows.Count != n || n == 0)
            return false;

        int m = rows[0].Length;
        if (m == 0)
            return false;

        for (int i = 0; i < n; i++)
        {
            if (rows[i].Length != m)
                return false;
        }

        var scale = new double[m];
        for (int j = 0; j < m; j++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double value = rows[i][j];
                if (!double.IsFinite(value))
                    return false;
                sum += value * value;
            }

            if (!double.IsFinite(sum) || sum <= 0)
                return false;

            scale[j] = Math.Sqrt(sum);
        }

        var normal = new double[m, m];
        var rhs = new double[m];

        for (int j = 0; j < m; j++)
        {
            for (int k = j; k < m; k++)
            {
                double sum = 0;
                for (int i = 0; i < n; i++)
                    sum += (rows[i][j] / scale[j]) * (rows[i][k] / scale[k]);

                normal[j, k] = sum;
                normal[k, j] = sum;
            }

            double weighted = 0;
            for (int i = 0; i < n; i++)
                weighted += (rows[i][j] / scale[j]) * observations[i];

            rhs[j] = weighted;
        }

        double maxAbs = 0;
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < m; j++)
                maxAbs = Math.Max(maxAbs, Math.Abs(normal[i, j]));
        }

        if (!double.IsFinite(maxAbs) || maxAbs <= 0)
            return false;

        double tolerance = 1e-12 * maxAbs;

        for (int column = 0; column < m; column++)
        {
            int pivot = column;
            double pivotAbs = Math.Abs(normal[column, column]);
            for (int row = column + 1; row < m; row++)
            {
                double candidate = Math.Abs(normal[row, column]);
                if (candidate > pivotAbs)
                {
                    pivot = row;
                    pivotAbs = candidate;
                }
            }

            if (pivotAbs <= tolerance)
                return false;

            if (pivot != column)
            {
                for (int c = column; c < m; c++)
                    (normal[column, c], normal[pivot, c]) = (normal[pivot, c], normal[column, c]);

                (rhs[column], rhs[pivot]) = (rhs[pivot], rhs[column]);
            }

            double diagonal = normal[column, column];
            for (int row = column + 1; row < m; row++)
            {
                double factor = normal[row, column] / diagonal;
                if (factor == 0)
                    continue;

                for (int c = column; c < m; c++)
                    normal[row, c] -= factor * normal[column, c];

                rhs[row] -= factor * rhs[column];
            }
        }

        var scaledSolution = new double[m];
        for (int row = m - 1; row >= 0; row--)
        {
            double sum = rhs[row];
            for (int column = row + 1; column < m; column++)
                sum -= normal[row, column] * scaledSolution[column];

            double diagonal = normal[row, row];
            if (Math.Abs(diagonal) <= tolerance)
                return false;

            scaledSolution[row] = sum / diagonal;
            if (!double.IsFinite(scaledSolution[row]))
                return false;
        }

        coefficients = new double[m];
        for (int j = 0; j < m; j++)
            coefficients[j] = scaledSolution[j] / scale[j];

        return true;
    }
}
