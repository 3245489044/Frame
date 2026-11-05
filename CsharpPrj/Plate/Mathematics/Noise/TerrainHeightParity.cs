using Modules.Compute.Adapters;
using Modules.Compute.Formula;

namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 逐位对拍钩子（等价于 <c>--verify-height</c>）：
/// <b>旧逐点实现</b>（<see cref="TerrainHeightModel.HeightCm"/> / <see cref="TerrainHeightModel.GroundYCm"/>）
/// vs <b>新批量实现</b>（<see cref="TerrainHeightBatchEvaluator"/>）。
///
/// <para>判据（A 档）：逐点 <c>BitConverter.SingleToInt32Bits</c> 相等，且两路 FNV-1a 整体哈希相同。</para>
/// <para><b>本轮只交付入口，未运行</b>；运行归唯一测试任务（见实施进度文档 §7）。</para>
/// </summary>
public static class TerrainHeightParity
{
    public sealed record Report(
        GridRect Rect,
        int Compared,
        int Mismatches,
        long OldHash,
        long NewHash,
        int FirstMismatchX,
        int FirstMismatchZ,
        int OldBits,
        int NewBits)
    {
        /// <summary>逐位一致 ⇔ 无任何点不同。</summary>
        public bool BitExact => Mismatches == 0;

        /// <summary>两路整体哈希一致（逐位一致的必要条件，便于日志一行判定）。</summary>
        public bool HashEqual => OldHash == NewHash;
    }

    /// <summary>对拍 <c>HeightCm</c>（float，逐位）。</summary>
    public static Report CompareHeight(TerrainHeightModel model, GridRect rect)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (rect.IsEmpty)
            return new Report(rect, 0, 0, SignatureSet.Seed, SignatureSet.Seed, 0, 0, 0, 0);

        var oldValues = new float[rect.Count];
        var newValues = new float[rect.Count];

        int index = 0;
        for (int z = rect.MinZ; z <= rect.MaxZ; z++)
        {
            for (int x = rect.MinX; x <= rect.MaxX; x++)
                oldValues[index++] = model.HeightCm(x, z);
        }

        model.Batch.FillHeightCm(rect, newValues);

        return CompareBits(rect, oldValues, newValues);
    }

    /// <summary>对拍 <c>GroundYCm</c>（int）。</summary>
    public static Report CompareGroundY(TerrainHeightModel model, GridRect rect)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (rect.IsEmpty)
            return new Report(rect, 0, 0, SignatureSet.Seed, SignatureSet.Seed, 0, 0, 0, 0);

        var oldValues = new int[rect.Count];
        var newValues = new int[rect.Count];

        int index = 0;
        for (int z = rect.MinZ; z <= rect.MaxZ; z++)
        {
            for (int x = rect.MinX; x <= rect.MaxX; x++)
                oldValues[index++] = model.GroundYCm(x, z);
        }

        model.Batch.FillGroundYCm(rect, newValues);

        return CompareInts(rect, oldValues, newValues);
    }

    private static Report CompareBits(GridRect rect, float[] oldValues, float[] newValues)
    {
        long oldHash = SignatureSet.Mix(SignatureSet.Seed, rect.Width);
        long newHash = SignatureSet.Mix(SignatureSet.Seed, rect.Width);

        int mismatches = 0;
        int firstX = 0;
        int firstZ = 0;
        int firstOldBits = 0;
        int firstNewBits = 0;

        for (int i = 0; i < oldValues.Length; i++)
        {
            int oldBits = BitConverter.SingleToInt32Bits(oldValues[i]);
            int newBits = BitConverter.SingleToInt32Bits(newValues[i]);

            oldHash = SignatureSet.Mix(oldHash, oldBits);
            newHash = SignatureSet.Mix(newHash, newBits);

            if (oldBits != newBits)
            {
                if (mismatches == 0)
                {
                    firstOldBits = oldBits;
                    firstNewBits = newBits;
                    firstX = rect.MinX + (i % rect.Width);
                    firstZ = rect.MinZ + (i / rect.Width);
                }

                mismatches++;
            }
        }

        return new Report(rect, oldValues.Length, mismatches, oldHash, newHash, firstX, firstZ, firstOldBits, firstNewBits);
    }

    private static Report CompareInts(GridRect rect, int[] oldValues, int[] newValues)
    {
        long oldHash = SignatureSet.Mix(SignatureSet.Seed, rect.Width);
        long newHash = SignatureSet.Mix(SignatureSet.Seed, rect.Width);

        int mismatches = 0;
        int firstX = 0;
        int firstZ = 0;
        int firstOldBits = 0;
        int firstNewBits = 0;

        for (int i = 0; i < oldValues.Length; i++)
        {
            oldHash = SignatureSet.Mix(oldHash, oldValues[i]);
            newHash = SignatureSet.Mix(newHash, newValues[i]);

            if (oldValues[i] != newValues[i])
            {
                if (mismatches == 0)
                {
                    firstOldBits = oldValues[i];
                    firstNewBits = newValues[i];
                    firstX = rect.MinX + (i % rect.Width);
                    firstZ = rect.MinZ + (i / rect.Width);
                }

                mismatches++;
            }
        }

        return new Report(rect, oldValues.Length, mismatches, oldHash, newHash, firstX, firstZ, firstOldBits, firstNewBits);
    }
}
