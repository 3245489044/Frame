using System;

namespace TConfig.GameMath;

/// <summary>
/// 共享 4x4 矩阵基元。原定义位于 GameEditor.VirtualMatrix/Math/Matrix4x4.cs，
/// 为消除 ECS/Graphics 对虚拟矩阵业务模块的依赖倒置，归位到 TConfig 基元层。
/// </summary>
public readonly struct Matrix4x4 : IEquatable<Matrix4x4>
{
    public readonly float M11, M12, M13, M14;
    public readonly float M21, M22, M23, M24;
    public readonly float M31, M32, M33, M34;
    public readonly float M41, M42, M43, M44;

    public Matrix4x4(
        float m11, float m12, float m13, float m14,
        float m21, float m22, float m23, float m24,
        float m31, float m32, float m33, float m34,
        float m41, float m42, float m43, float m44)
    {
        M11 = m11; M12 = m12; M13 = m13; M14 = m14;
        M21 = m21; M22 = m22; M23 = m23; M24 = m24;
        M31 = m31; M32 = m32; M33 = m33; M34 = m34;
        M41 = m41; M42 = m42; M43 = m43; M44 = m44;
    }

    public static Matrix4x4 Identity => new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1);

    /// <summary>
    /// 平移矩阵（行主序约定，与 CreateFromYawPitchRoll / Perspective / LookAt 一致：
    /// 平移位于最后一行 M41/M42/M43，向量以行向量左乘 v' = v * M）。
    /// 修复前平移错误地位于最后一列（m14/m24/m34，列主序语义），与其它构造器约定混用。
    /// </summary>
    public static Matrix4x4 CreateTranslation(Vec3 position) => new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        position.X, position.Y, position.Z, 1);

    public static Matrix4x4 CreateScale(Vec3 scale) => new(
        scale.X, 0, 0, 0,
        0, scale.Y, 0, 0,
        0, 0, scale.Z, 0,
        0, 0, 0, 1);

    public static Matrix4x4 CreateFromYawPitchRoll(float yaw, float pitch, float roll)
    {
        var sx = (float)System.Math.Sin(roll);
        var cx = (float)System.Math.Cos(roll);
        var sy = (float)System.Math.Sin(pitch);
        var cy = (float)System.Math.Cos(pitch);
        var sz = (float)System.Math.Sin(yaw);
        var cz = (float)System.Math.Cos(yaw);
        return new Matrix4x4(
            cy * cz, sx * sy * cz - cx * sz, cx * sy * cz + sx * sz, 0,
            cy * sz, sx * sy * sz + cx * cz, cx * sy * sz - sx * cz, 0,
            -sy, sx * cy, cx * cy, 0,
            0, 0, 0, 1);
    }

    public static Matrix4x4 Perspective(float fieldOfView, float aspectRatio, float nearClip, float farClip)
    {
        var yScale = 1.0f / (float)System.Math.Tan(fieldOfView / 2.0f);
        var xScale = yScale / aspectRatio;
        var zRange = farClip - nearClip;
        var zScale = -(farClip + nearClip) / zRange;
        var wzScale = -2.0f * farClip * nearClip / zRange;

        return new Matrix4x4(
            xScale, 0, 0, 0,
            0, yScale, 0, 0,
            0, 0, zScale, wzScale,
            0, 0, -1, 0);
    }

    public static Matrix4x4 Orthographic(float width, float height, float nearClip, float farClip)
    {
        return new Matrix4x4(
            2.0f / width, 0, 0, 0,
            0, 2.0f / height, 0, 0,
            0, 0, 2.0f / (nearClip - farClip), 0,
            0, 0, (farClip + nearClip) / (nearClip - farClip), 1);
    }

    public static Matrix4x4 LookAt(Vec3 position, Vec3 target, Vec3 up)
    {
        var z = Vec3.Normalize(position - target);
        var x = Vec3.Normalize(Vec3.Cross(up, z));
        var y = Vec3.Cross(z, x);

        return new Matrix4x4(
            x.X, y.X, z.X, 0,
            x.Y, y.Y, z.Y, 0,
            x.Z, y.Z, z.Z, 0,
            -Vec3.Dot(x, position), -Vec3.Dot(y, position), -Vec3.Dot(z, position), 1);
    }

    public static Matrix4x4 operator *(Matrix4x4 left, Matrix4x4 right)
    {
        return new Matrix4x4(
            left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31 + left.M14 * right.M41,
            left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32 + left.M14 * right.M42,
            left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33 + left.M14 * right.M43,
            left.M11 * right.M14 + left.M12 * right.M24 + left.M13 * right.M34 + left.M14 * right.M44,
            left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31 + left.M24 * right.M41,
            left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32 + left.M24 * right.M42,
            left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33 + left.M24 * right.M43,
            left.M21 * right.M14 + left.M22 * right.M24 + left.M23 * right.M34 + left.M24 * right.M44,
            left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31 + left.M34 * right.M41,
            left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32 + left.M34 * right.M42,
            left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33 + left.M34 * right.M43,
            left.M31 * right.M14 + left.M32 * right.M24 + left.M33 * right.M34 + left.M34 * right.M44,
            left.M41 * right.M11 + left.M42 * right.M21 + left.M43 * right.M31 + left.M44 * right.M41,
            left.M41 * right.M12 + left.M42 * right.M22 + left.M43 * right.M32 + left.M44 * right.M42,
            left.M41 * right.M13 + left.M42 * right.M23 + left.M43 * right.M33 + left.M44 * right.M43,
            left.M41 * right.M14 + left.M42 * right.M24 + left.M43 * right.M34 + left.M44 * right.M44);
    }

    public bool Equals(Matrix4x4 other)
    {
        return M11.Equals(other.M11) && M12.Equals(other.M12) && M13.Equals(other.M13) && M14.Equals(other.M14)
            && M21.Equals(other.M21) && M22.Equals(other.M22) && M23.Equals(other.M23) && M24.Equals(other.M24)
            && M31.Equals(other.M31) && M32.Equals(other.M32) && M33.Equals(other.M33) && M34.Equals(other.M34)
            && M41.Equals(other.M41) && M42.Equals(other.M42) && M43.Equals(other.M43) && M44.Equals(other.M44);
    }

    public override bool Equals(object? obj) => obj is Matrix4x4 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(M11, M12, M13, M14, M21, M22, M23, M24);
    public static bool operator ==(Matrix4x4 left, Matrix4x4 right) => left.Equals(right);
    public static bool operator !=(Matrix4x4 left, Matrix4x4 right) => !left.Equals(right);
}
