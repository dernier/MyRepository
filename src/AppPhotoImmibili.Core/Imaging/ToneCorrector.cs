using SkiaSharp;

namespace AppPhotoImmibili.Core.Imaging;

public sealed record ToneAdjustment(byte[] RedLut, byte[] GreenLut, byte[] BlueLut, double Gamma, byte Black, byte White);

/// <summary>
/// Bilanciamento automatico della luminosità per gli interni: livelli (punto nero/bianco al 0,5/99,5 percentile),
/// gamma per portare la mediana verso un valore luminoso ma naturale, e bilanciamento del bianco "grigio medio"
/// parziale per attenuare la dominante calda delle lampadine.
/// </summary>
public sealed class ToneCorrector
{
    public double TargetMedian { get; set; } = 0.47;
    public double WhiteBalanceStrength { get; set; } = 0.4;

    private static readonly byte[] IdentityLut = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

    public ToneAdjustment Compute(SKBitmap bitmap)
    {
        using var small = ImageIo.Downscale(bitmap, 512);
        var span = small.GetPixelSpan();
        var hist = new int[256];
        long sr = 0, sg = 0, sb = 0;
        var n = span.Length / 4;
        for (var p = 0; p < span.Length; p += 4)
        {
            hist[LumaImage.Luma(span[p], span[p + 1], span[p + 2])]++;
            sr += span[p]; sg += span[p + 1]; sb += span[p + 2];
        }

        var black = (byte)Math.Min(24, Percentile(hist, n, 0.005));
        var white = (byte)Math.Max(200, Percentile(hist, n, 0.995));
        var median = Percentile(hist, n, 0.5);
        var normMedian = Math.Clamp((median - black) / (double)Math.Max(1, white - black), 0.01, 0.99);
        var gamma = Math.Clamp(Math.Log(TargetMedian) / Math.Log(normMedian), 0.6, 1.4);

        double mr = sr / (double)n, mg = sg / (double)n, mb = sb / (double)n;
        var gray = (mr + mg + mb) / 3;
        double Gain(double m) => Math.Clamp(1 + (gray / Math.Max(1, m) - 1) * WhiteBalanceStrength, 0.85, 1.2);

        return new ToneAdjustment(
            BuildLut(black, white, gamma, Gain(mr)),
            BuildLut(black, white, gamma, Gain(mg)),
            BuildLut(black, white, gamma, Gain(mb)),
            gamma, black, white);
    }

    public SKBitmap Apply(SKBitmap source, ToneAdjustment adj)
    {
        var dst = new SKBitmap(new SKImageInfo(source.Width, source.Height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(dst);
        using var filter = SKColorFilter.CreateTable(IdentityLut, adj.RedLut, adj.GreenLut, adj.BlueLut);
        using var paint = new SKPaint { ColorFilter = filter };
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        return dst;
    }

    internal static byte[] BuildLut(byte black, byte white, double gamma, double gain)
    {
        var lut = new byte[256];
        var range = Math.Max(1, white - black);
        for (var i = 0; i < 256; i++)
        {
            var v = Math.Clamp((i - black) / (double)range, 0, 1);
            v = Math.Pow(v, gamma) * gain;
            lut[i] = (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        }
        return lut;
    }

    private static int Percentile(int[] hist, int count, double p)
    {
        var target = count * p;
        long acc = 0;
        for (var i = 0; i < 256; i++)
        {
            acc += hist[i];
            if (acc >= target) return i;
        }
        return 255;
    }
}
