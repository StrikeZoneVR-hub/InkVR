using System;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Plain .NET vector maths for the ink's inner loops. In this game's build every Unity Vector3, Mathf and
/// Matrix4x4 operation calls into the game's runtime (70–150 ns each, measured with the dev "mathbench"), about
/// fifty times slower than this; painting one drop does thousands of them.
/// </summary>
public struct F3
{
    public float X, Y, Z;

    public F3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static F3 operator +(F3 a, F3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static F3 operator -(F3 a, F3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static F3 operator -(F3 a) => new(-a.X, -a.Y, -a.Z);
    public static F3 operator *(F3 a, float k) => new(a.X * k, a.Y * k, a.Z * k);
    public static F3 operator *(float k, F3 a) => new(a.X * k, a.Y * k, a.Z * k);
    public static F3 operator /(F3 a, float k) => new(a.X / k, a.Y / k, a.Z / k);
    public static float Dot(F3 a, F3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static F3 Cross(F3 a, F3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public float Len2 => X * X + Y * Y + Z * Z;
    public float Len => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public F3 Norm
    {
        get
        {
            var l = Len;
            return l > 1e-12f ? new F3(X / l, Y / l, Z / l) : default;
        }
    }

    public static F3 Bary(F3 a, F3 b, F3 c, float w0, float w1, float w2) =>
        new(a.X * w0 + b.X * w1 + c.X * w2, a.Y * w0 + b.Y * w1 + c.Y * w2, a.Z * w0 + b.Z * w1 + c.Z * w2);

    public static F3 Of(Vector3 v) => new(v.x, v.y, v.z);

    public Vector3 V
    {
        get
        {
            Vector3 v = default;
            v.x = X;
            v.y = Y;
            v.z = Z;
            return v;
        }
    }

    public static readonly F3 Down = new(0, -1, 0), Up = new(0, 1, 0);
}

/// <summary>An affine transform (the top three rows of a Matrix4x4), in plain .NET.</summary>
public struct M34
{
    public float A00, A01, A02, A03, A10, A11, A12, A13, A20, A21, A22, A23;

    public static M34 Of(Matrix4x4 m) => new()
    {
        A00 = m.m00, A01 = m.m01, A02 = m.m02, A03 = m.m03,
        A10 = m.m10, A11 = m.m11, A12 = m.m12, A13 = m.m13,
        A20 = m.m20, A21 = m.m21, A22 = m.m22, A23 = m.m23,
    };

    public F3 Point(F3 p) => new(A00 * p.X + A01 * p.Y + A02 * p.Z + A03, A10 * p.X + A11 * p.Y + A12 * p.Z + A13, A20 * p.X + A21 * p.Y + A22 * p.Z + A23);
    public F3 Vec(F3 v) => new(A00 * v.X + A01 * v.Y + A02 * v.Z, A10 * v.X + A11 * v.Y + A12 * v.Z, A20 * v.X + A21 * v.Y + A22 * v.Z);
    public float ScaleX => MathF.Sqrt(A00 * A00 + A10 * A10 + A20 * A20);

    /// <summary>The transform that applies b first, then a (like a * b with matrices).</summary>
    public static M34 Mul(in M34 a, in M34 b) => new()
    {
        A00 = a.A00 * b.A00 + a.A01 * b.A10 + a.A02 * b.A20,
        A01 = a.A00 * b.A01 + a.A01 * b.A11 + a.A02 * b.A21,
        A02 = a.A00 * b.A02 + a.A01 * b.A12 + a.A02 * b.A22,
        A03 = a.A00 * b.A03 + a.A01 * b.A13 + a.A02 * b.A23 + a.A03,
        A10 = a.A10 * b.A00 + a.A11 * b.A10 + a.A12 * b.A20,
        A11 = a.A10 * b.A01 + a.A11 * b.A11 + a.A12 * b.A21,
        A12 = a.A10 * b.A02 + a.A11 * b.A12 + a.A12 * b.A22,
        A13 = a.A10 * b.A03 + a.A11 * b.A13 + a.A12 * b.A23 + a.A13,
        A20 = a.A20 * b.A00 + a.A21 * b.A10 + a.A22 * b.A20,
        A21 = a.A20 * b.A01 + a.A21 * b.A11 + a.A22 * b.A21,
        A22 = a.A20 * b.A02 + a.A21 * b.A12 + a.A22 * b.A22,
        A23 = a.A20 * b.A03 + a.A21 * b.A13 + a.A22 * b.A23 + a.A23,
    };
}

/// <summary>Plain .NET stand-ins for Mathf in the hot loops.</summary>
public static class FM
{
    public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
    public static float Min(float a, float b) => a < b ? a : b;
    public static float Max(float a, float b) => a > b ? a : b;
}

/// <summary>An axis-aligned box in plain .NET.</summary>
public struct Box3
{
    public F3 Min, Max;

    public static Box3 Of(Bounds b) => new() { Min = F3.Of(b.min), Max = F3.Of(b.max) };

    public bool Contains(F3 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

    public bool Intersects(in Box3 o) =>
        Min.X <= o.Max.X && Max.X >= o.Min.X && Min.Y <= o.Max.Y && Max.Y >= o.Min.Y && Min.Z <= o.Max.Z && Max.Z >= o.Min.Z;

    public void Encapsulate(F3 p, float r)
    {
        if (p.X - r < Min.X) Min.X = p.X - r;
        if (p.Y - r < Min.Y) Min.Y = p.Y - r;
        if (p.Z - r < Min.Z) Min.Z = p.Z - r;
        if (p.X + r > Max.X) Max.X = p.X + r;
        if (p.Y + r > Max.Y) Max.Y = p.Y + r;
        if (p.Z + r > Max.Z) Max.Z = p.Z + r;
    }

    public Box3 Expanded(float r) => new() { Min = new F3(Min.X - r, Min.Y - r, Min.Z - r), Max = new F3(Max.X + r, Max.Y + r, Max.Z + r) };

    public static Box3 Around(F3 p, float r) => new() { Min = new F3(p.X - r, p.Y - r, p.Z - r), Max = new F3(p.X + r, p.Y + r, p.Z + r) };
}
