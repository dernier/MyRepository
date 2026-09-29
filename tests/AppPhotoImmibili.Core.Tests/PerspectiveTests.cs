using AppPhotoImmibili.Core.Imaging;
using SkiaSharp;

namespace AppPhotoImmibili.Core.Tests;

public class PerspectiveTests
{
    private const int W = 4000, H = 3000;
    private const double Fov = 69;

    /// <summary>Proietta un punto (riferimento in bolla, y giù) con una fotocamera inclinata verso l'alto di θ.</summary>
    private static (double U, double V) Project(double x, double y, double z, double pitchDeg)
    {
        var f = W / 2.0 / Math.Tan(Fov * Math.PI / 360);
        var t = pitchDeg * Math.PI / 180;
        // coordinate camera = Rᵀ·P, con R colonne = assi camera nel riferimento in bolla
        var cy = Math.Cos(t) * y + Math.Sin(t) * z;
        var cz = -Math.Sin(t) * y + Math.Cos(t) * z;
        return (f * x / cz + W / 2.0, f * cy / cz + H / 2.0);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(-4)]
    public void Pitch_MakesVerticalsParallel(double pitch)
    {
        var corrector = new PerspectiveCorrector { HorizontalFovDegrees = Fov };
        var c = corrector.Compute(W, H, pitch, 0);
        Assert.True(c.Applied);
        Assert.InRange(c.CropScale, 0.6, 1.0);

        // Spigolo verticale di un muro a destra: prima converge, dopo la correzione è verticale.
        var top = Project(1.5, -1.2, 4, pitch);
        var bottom = Project(1.5, 1.3, 4, pitch);
        Assert.True(Math.Abs(top.U - bottom.U) > 20);

        var t2 = c.Transform.Apply(top.U, top.V);
        var b2 = c.Transform.Apply(bottom.U, bottom.V);
        Assert.Equal(t2.X, b2.X, 1);
    }

    [Fact]
    public void Roll_StraightensHorizon()
    {
        var corrector = new PerspectiveCorrector();
        // Telefono ruotato di 2° in senso antiorario: la scena appare ruotata di 2° in senso orario.
        var c = corrector.Compute(W, H, 0, 2);
        var rot = Matrix3.Rotation(2, W / 2.0, H / 2.0);
        var a = rot.Apply(1000, 1500);
        var b = rot.Apply(3000, 1500);
        var a2 = c.Transform.Apply(a.X, a.Y);
        var b2 = c.Transform.Apply(b.X, b.Y);
        Assert.Equal(a2.Y, b2.Y, 1);
    }

    [Fact]
    public void LargeTilt_IsNotCorrected()
    {
        var c = new PerspectiveCorrector().Compute(W, H, 25, 0);
        Assert.False(c.Applied);
    }

    [Fact]
    public void Apply_KeepsSize()
    {
        using var bmp = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.White);
        var corrector = new PerspectiveCorrector();
        using var result = corrector.Apply(bmp, corrector.Compute(400, 300, 5, 1));
        Assert.Equal(400, result.Width);
        Assert.Equal(300, result.Height);
        // Il ritaglio evita bordi neri: gli angoli restano bianchi.
        Assert.True(result.GetPixel(2, 2).Red > 200);
        Assert.True(result.GetPixel(397, 297).Red > 200);
    }

    [Fact]
    public void ExifRightTop_RotatesClockwise()
    {
        using var src = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        src.SetPixel(0, 0, SKColors.Red);
        src.SetPixel(1, 0, SKColors.Blue);
        using var dst = ImageIo.ApplyOrigin(src.Copy(), SKEncodedOrigin.RightTop);
        Assert.Equal(1, dst.Width);
        Assert.Equal(2, dst.Height);
        Assert.Equal(SKColors.Red, dst.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, dst.GetPixel(0, 1));
    }
}
