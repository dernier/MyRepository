using SkiaSharp;

namespace InquadraCasa.Core.Imaging;

public sealed record PerspectiveCorrection(Matrix3 Transform, double CropScale, bool Applied, string Reason);

/// <summary>
/// Raddrizza verticali e orizzonte usando l'assetto registrato dai sensori al momento dello scatto.
/// <para>L'inclinazione (pitch) fa convergere i muri: si simula una fotocamera in bolla con l'omografia
/// H = K·R·K⁻¹ (rotazione pura), si ricentra l'immagine e si ritaglia il rettangolo più grande
/// con le stesse proporzioni. Il rollio viene annullato con una rotazione attorno al centro.</para>
/// </summary>
public sealed class PerspectiveCorrector
{
    /// <summary>Campo visivo orizzontale sul lato lungo. 69° ≈ grandangolo principale (26 mm eq.), 108° ≈ ultra-grandangolo (13 mm eq.).</summary>
    public double HorizontalFovDegrees { get; set; } = 69;

    /// <summary>Oltre questa inclinazione il ritaglio sarebbe eccessivo: meglio riscattare.</summary>
    public double MaxCorrectionDegrees { get; set; } = 12;

    /// <summary>Sotto questa soglia la correzione non è percepibile e non viene applicata.</summary>
    public double MinCorrectionDegrees { get; set; } = 0.2;

    /// <param name="pitchDegrees">Elevazione dell'asse ottico (+ verso l'alto).</param>
    /// <param name="rollDegrees">Rollio del telefono: + = bordo superiore ruotato verso sinistra (antiorario visto dallo schermo).</param>
    public PerspectiveCorrection Compute(int width, int height, double pitchDegrees, double rollDegrees)
    {
        if (Math.Abs(pitchDegrees) > MaxCorrectionDegrees || Math.Abs(rollDegrees) > MaxCorrectionDegrees)
            return new(Matrix3.Identity, 1, false, "Inclinazione troppo forte per correggere senza perdere l'inquadratura: riscatta in bolla.");
        if (Math.Abs(pitchDegrees) < MinCorrectionDegrees && Math.Abs(rollDegrees) < MinCorrectionDegrees)
            return new(Matrix3.Identity, 1, false, "Già in bolla: nessuna correzione necessaria.");

        double cx = width / 2.0, cy = height / 2.0;
        var f = Math.Max(width, height) / 2.0 / Math.Tan(HorizontalFovDegrees * Math.PI / 360.0);

        // Il telefono ruotato in senso antiorario fa ruotare la scena in senso orario nell'immagine:
        // la correzione ruota l'immagine in senso antiorario (angolo negativo in coordinate y-giù).
        var roll = Matrix3.Rotation(-rollDegrees, cx, cy);

        var t = pitchDegrees * Math.PI / 180;
        var k = new Matrix3(f, 0, cx, 0, f, cy, 0, 0, 1);
        var kInv = new Matrix3(1 / f, 0, -cx / f, 0, 1 / f, -cy / f, 0, 0, 1);
        // Colonne = assi della fotocamera inclinata espressi nel riferimento in bolla (x destra, y giù, z avanti).
        var r = new Matrix3(1, 0, 0, 0, Math.Cos(t), -Math.Sin(t), 0, Math.Sin(t), Math.Cos(t));
        var h = k * r * kInv * roll;

        // Ricentra: il centro dell'immagine originale torna al centro.
        var (mx, my) = h.Apply(cx, cy);
        h = Matrix3.Translation(cx - mx, cy - my) * h;

        var quad = new[] { h.Apply(0, 0), h.Apply(width, 0), h.Apply(width, height), h.Apply(0, height) };
        var s = LargestCenteredRectScale(quad, width, height, cx, cy);
        var final = Matrix3.Scale(1 / s, cx, cy) * h;
        return new(final, s, true, $"Corretti {pitchDegrees:+0.0;-0.0}° di inclinazione e {rollDegrees:+0.0;-0.0}° di rollio.");
    }

    public SKBitmap Apply(SKBitmap source, PerspectiveCorrection correction)
    {
        var dst = new SKBitmap(new SKImageInfo(source.Width, source.Height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Black);
        canvas.SetMatrix(correction.Transform.ToSKMatrix());
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell));
        return dst;
    }

    /// <summary>Ricerca binaria del fattore di scala del rettangolo centrato contenuto nel quadrilatero.</summary>
    internal static double LargestCenteredRectScale((double X, double Y)[] quad, int width, int height, double cx, double cy)
    {
        double lo = 0.05, hi = 1.0;
        if (RectInside(quad, width, height, cx, cy, hi)) return hi;
        for (var i = 0; i < 40; i++)
        {
            var mid = (lo + hi) / 2;
            if (RectInside(quad, width, height, cx, cy, mid)) lo = mid; else hi = mid;
        }
        return lo;
    }

    private static bool RectInside((double X, double Y)[] quad, int width, int height, double cx, double cy, double s)
    {
        var hw = width * s / 2;
        var hh = height * s / 2;
        return PointInConvex(quad, cx - hw, cy - hh) && PointInConvex(quad, cx + hw, cy - hh)
            && PointInConvex(quad, cx + hw, cy + hh) && PointInConvex(quad, cx - hw, cy + hh);
    }

    private static bool PointInConvex((double X, double Y)[] poly, double x, double y)
    {
        var sign = 0;
        for (var i = 0; i < poly.Length; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Length];
            var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
            if (Math.Abs(cross) < 1e-9) continue;
            var s = Math.Sign(cross);
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return true;
    }
}
