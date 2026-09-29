using AppPhotoImmibili.Core.Analysis;
using AppPhotoImmibili.Core.Imaging;
using SkiaSharp;

namespace AppPhotoImmibili.Core.Tests;

public class LineDetectorTests
{
    private const int W = 1000, H = 750;
    private const double Fov = 69;

    /// <summary>Proietta un punto del riferimento in bolla (x destra, y giù, z avanti) con fotocamera inclinata e ruotata.</summary>
    private static SKPoint Project(double x, double y, double z, double pitchDeg, double rollDeg)
    {
        var f = W / 2.0 / Math.Tan(Fov * Math.PI / 360);
        var t = pitchDeg * Math.PI / 180;
        var cy = Math.Cos(t) * y + Math.Sin(t) * z;
        var cz = -Math.Sin(t) * y + Math.Cos(t) * z;
        var u = f * x / cz;
        var v = f * cy / cz;
        // Telefono ruotato in senso antiorario → scena ruotata in senso orario nell'immagine.
        var r = rollDeg * Math.PI / 180;
        return new SKPoint((float)(u * Math.Cos(r) - v * Math.Sin(r) + W / 2.0), (float)(u * Math.Sin(r) + v * Math.Cos(r) + H / 2.0));
    }

    /// <summary>Stanza sintetica: spigoli, stipiti e mobili verticali a varie distanze, più qualche linea obliqua di disturbo.</summary>
    private static SKBitmap Scene(double pitch, double roll)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(225, 222, 215));
        using var paint = new SKPaint { Color = new SKColor(60, 55, 50), StrokeWidth = 5, IsAntialias = true };
        foreach (var (x, z) in new[] { (-2.2, 4.0), (-1.4, 5.0), (-0.6, 3.5), (0.3, 6.0), (1.1, 4.5), (1.9, 3.8), (2.6, 5.5) })
            canvas.DrawLine(Project(x, -1.4, z, pitch, roll), Project(x, 1.3, z, pitch, roll), paint);
        using var diag = new SKPaint { Color = new SKColor(90, 90, 90), StrokeWidth = 3, IsAntialias = true };
        canvas.DrawLine(50, 700, 600, 420, diag);
        canvas.DrawLine(900, 100, 400, 300, diag);
        return bmp;
    }

    private static LumaImage Luma(SKBitmap bmp) => ImageIo.ToLuma(bmp, W);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(-4, 0)]
    [InlineData(0, 2)]
    [InlineData(3, -1.5)]
    public void Estimate_RecoversPitchAndRoll(double pitch, double roll)
    {
        using var scene = Scene(pitch, roll);
        var est = new VerticalLineDetector().Estimate(Luma(scene), Fov);
        Assert.True(est.Reliable, $"Stima non affidabile ({est.InlierCount} linee)");
        Assert.InRange(est.PitchDegrees, pitch - 0.4, pitch + 0.4);
        Assert.InRange(est.RollDegrees, roll - 0.3, roll + 0.3);
    }

    [Fact]
    public void BlankImage_IsNotReliable()
    {
        using var bmp = new SKBitmap(W, H);
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.White);
        Assert.False(new VerticalLineDetector().Estimate(Luma(bmp), Fov).Reliable);
    }

    [Fact]
    public void Processor_UsesLines_WhenSensorsAreSlightlyOff()
    {
        // Sensori imprecisi di un grado: le linee correggono esattamente.
        using var scene = Scene(4, 0);
        var processor = new PhotoProcessor();
        processor.Perspective.HorizontalFovDegrees = Fov;
        var (pitch, _, fromLines) = processor.RefineAttitude(scene, new CaptureContext(3, 0, null, 0));
        Assert.True(fromLines);
        Assert.InRange(pitch, 3.6, 4.4);

        var c = processor.Perspective.Compute(W, H, pitch, 0);
        using var corrected = processor.Perspective.Apply(scene, c);
        var after = new VerticalLineDetector().Estimate(Luma(corrected), Fov);
        Assert.InRange(Math.Abs(after.PitchDegrees), 0, 0.4);
    }

    [Fact]
    public void Processor_IgnoresLines_FarFromSensors()
    {
        using var scene = Scene(5, 0);
        var processor = new PhotoProcessor();
        var (pitch, _, fromLines) = processor.RefineAttitude(scene, new CaptureContext(-3, 0, null, 0));
        Assert.False(fromLines);
        Assert.Equal(-3, pitch);
    }
}
