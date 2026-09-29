using System;
using SkiaSharp;

namespace InquadraCasa.Core.Imaging;

/// <summary>Matrice 3×3 in doppia precisione per omografie (coordinate immagine, y verso il basso).</summary>
public readonly struct Matrix3
{
    private readonly double[] _m;

    public Matrix3(double m00, double m01, double m02,
                   double m10, double m11, double m12,
                   double m20, double m21, double m22)
    {
        _m = [m00, m01, m02, m10, m11, m12, m20, m21, m22];
    }

    public double this[int r, int c] => _m[r * 3 + c];

    public static Matrix3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static Matrix3 Translation(double tx, double ty) => new(1, 0, tx, 0, 1, ty, 0, 0, 1);

    public static Matrix3 Scale(double s, double cx, double cy) =>
        Translation(cx, cy) * new Matrix3(s, 0, 0, 0, s, 0, 0, 0, 1) * Translation(-cx, -cy);

    /// <summary>Rotazione nel piano immagine attorno a (cx, cy). Angolo positivo = orario sullo schermo.</summary>
    public static Matrix3 Rotation(double degrees, double cx, double cy)
    {
        var a = degrees * Math.PI / 180;
        var c = Math.Cos(a);
        var s = Math.Sin(a);
        return Translation(cx, cy) * new Matrix3(c, -s, 0, s, c, 0, 0, 0, 1) * Translation(-cx, -cy);
    }

    public static Matrix3 operator *(Matrix3 a, Matrix3 b)
    {
        var r = new double[9];
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                r[i * 3 + j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return new Matrix3(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7], r[8]);
    }

    public (double X, double Y) Apply(double x, double y)
    {
        var w = this[2, 0] * x + this[2, 1] * y + this[2, 2];
        return ((this[0, 0] * x + this[0, 1] * y + this[0, 2]) / w,
                (this[1, 0] * x + this[1, 1] * y + this[1, 2]) / w);
    }

    public SKMatrix ToSKMatrix() => new(
        (float)this[0, 0], (float)this[0, 1], (float)this[0, 2],
        (float)this[1, 0], (float)this[1, 1], (float)this[1, 2],
        (float)this[2, 0], (float)this[2, 1], (float)this[2, 2]);
}
