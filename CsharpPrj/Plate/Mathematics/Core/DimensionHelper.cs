#region USINGS
using System;
#endregion

namespace MathematicalAlgorithms
{
    public static class DimensionHelper
    {
        public static void ValidateDim(int dim, int maxDim)
        {
            if (dim < 0 || dim >= maxDim)
                throw new ArgumentException(
                    $"Dimension must be in range [0, {maxDim})", nameof(dim));
        }

        public static int NormalizeDim(int dim, int totalDims)
        {
            if (dim < 0) dim += totalDims;
            ValidateDim(dim, totalDims);
            return dim;
        }

        public static string GetDimName(int dim)
        {
            return dim switch
            {
                0 => "Batch",
                1 => "SeqLen",
                2 => "Dim",
                _ => $"Dim{dim}"
            };
        }

        public static int[] GetReducedShape(int[] shape, int dim, bool keepDim)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            ValidateDim(dim, shape.Length);

            if (keepDim)
            {
                var result = (int[])shape.Clone();
                result[dim] = 1;
                return result;
            }
            else
            {
                var result = new int[shape.Length - 1];
                int idx = 0;
                for (int i = 0; i < shape.Length; i++)
                {
                    if (i != dim)
                        result[idx++] = shape[i];
                }
                return result;
            }
        }

        public static int[] GetTransposedShape(int[] shape, int dim1, int dim2)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            ValidateDim(dim1, shape.Length);
            ValidateDim(dim2, shape.Length);

            var result = (int[])shape.Clone();
            (result[dim1], result[dim2]) = (result[dim2], result[dim1]);
            return result;
        }

        public static int[] BroadcastShapes(int[] shapeA, int[] shapeB)
        {
            if (shapeA == null) throw new ArgumentNullException(nameof(shapeA));
            if (shapeB == null) throw new ArgumentNullException(nameof(shapeB));

            int maxDims = Math.Max(shapeA.Length, shapeB.Length);
            var result = new int[maxDims];

            for (int i = 0; i < maxDims; i++)
            {
                int dimA = i < shapeA.Length ? shapeA[shapeA.Length - 1 - i] : 1;
                int dimB = i < shapeB.Length ? shapeB[shapeB.Length - 1 - i] : 1;

                if (dimA != dimB && dimA != 1 && dimB != 1)
                    throw new InvalidOperationException(
                        $"Shapes cannot be broadcast together at dimension {maxDims - 1 - i}: {dimA} vs {dimB}");

                result[maxDims - 1 - i] = Math.Max(dimA, dimB);
            }

            return result;
        }

        public static bool CanBroadcast(int[] shapeA, int[] shapeB)
        {
            try
            {
                BroadcastShapes(shapeA, shapeB);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int[] GetBroadcastSourceIndex(int[] targetIndices, int[] sourceShape)
        {
            if (targetIndices == null) throw new ArgumentNullException(nameof(targetIndices));
            if (sourceShape == null) throw new ArgumentNullException(nameof(sourceShape));

            int offset = sourceShape.Length - targetIndices.Length;
            var result = new int[sourceShape.Length];

            for (int i = 0; i < sourceShape.Length; i++)
            {
                int targetIdx = i - offset;
                result[i] = targetIdx >= 0
                    ? Math.Min(targetIndices[targetIdx], sourceShape[i] - 1)
                    : 0;
            }

            return result;
        }
    }
}
