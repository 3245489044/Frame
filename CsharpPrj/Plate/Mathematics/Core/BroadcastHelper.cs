#region USINGS
using System;
#endregion

namespace MathematicalAlgorithms
{
    public static class BroadcastHelper
    {
        public static int[] GetBroadcastShape(int[] shapeA, int[] shapeB)
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
                GetBroadcastShape(shapeA, shapeB);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int[] GetSourceIndex(int[] targetIndices, int[] sourceShape)
        {
            if (targetIndices == null) throw new ArgumentNullException(nameof(targetIndices));
            if (sourceShape == null) throw new ArgumentNullException(nameof(sourceShape));

            int offset = sourceShape.Length - targetIndices.Length;
            var result = new int[sourceShape.Length];

            for (int i = 0; i < sourceShape.Length; i++)
            {
                int targetIdx = i - offset;
                if (targetIdx >= 0)
                {
                    result[i] = sourceShape[i] == 1 ? 0 : targetIndices[targetIdx];
                }
                else
                {
                    result[i] = 0;
                }
            }

            return result;
        }

        public static (int srcI, int srcJ) GetSourceIndex2D(int i, int j, int sourceRows, int sourceCols)
        {
            int srcI = sourceRows == 1 ? 0 : i;
            int srcJ = sourceCols == 1 ? 0 : j;
            return (srcI, srcJ);
        }

        public static bool HasBroadcastDim(int[] shape)
        {
            if (shape == null) return false;
            for (int i = 0; i < shape.Length; i++)
                if (shape[i] == 1) return true;
            return false;
        }

        public static string ShapeToString(int[] shape)
        {
            if (shape == null) return "null";
            return $"[{string.Join("×", shape)}]";
        }
    }
}
