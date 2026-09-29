using InquadraCasa.Core.Analysis;
using InquadraCasa.Core.Imaging;
using SkiaSharp;

namespace InquadraCasa.Core.Tests;

public class QualityTests
{
    private static LumaImage Checkerboard(int w, int h, int cell)
    {
        var img = new LumaImage(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                img[x, y] = (byte)(((x / cell) + (y / cell)) % 2 == 0 ? 40 : 210);
        return img;
    }

    private static LumaImage BoxBlur(LumaImage src, int radius)
    {
        var dst = new LumaImage(src.Width, src.Height);
        for (var y = 0; y < src.Height; y++)
            for (var x = 0; x < src.Width; x++)
            {
                int sum = 0, n = 0;
                for (var dy = -radius; dy <= radius; dy++)
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        int xx = Math.Clamp(x + dx, 0, src.Width - 1), yy = Math.Clamp(y + dy, 0, src.Height - 1);
                        sum += src[xx, yy]; n++;
                    }
                dst[x, y] = (byte)(sum / n);
            }
        return dst;
    }

    [Fact]
    public void Sharpness_DetectsBlur()
    {
        var analyzer = new SharpnessAnalyzer();
        var sharp = Checkerboard(400, 300, 8);
        var blurred = BoxBlur(sharp, 4);

        var r1 = analyzer.Analyze(sharp);
        var r2 = analyzer.Analyze(blurred);
        Assert.True(r1.IsSharp, $"nitida: {r1.Score}");
        Assert.False(r2.IsSharp, $"sfocata: {r2.Score}");
        Assert.True(r1.Score > r2.Score * 5);
    }

    [Fact]
    public void Sharpness_FlagsMotionFromGyroscope()
    {
        var r = new SharpnessAnalyzer().Analyze(Checkerboard(400, 300, 8), maxAngularVelocityDuringCapture: 1.2);
        Assert.False(r.IsSharp);
        Assert.True(r.MotionDetected);
    }

    [Fact]
    public void Exposure_DetectsWindowBacklightOnLeft()
    {
        var img = new LumaImage(300, 200);
        for (var y = 0; y < 200; y++)
            for (var x = 0; x < 300; x++)
                img[x, y] = (byte)(x < 60 && y > 30 && y < 150 ? 255 : 55);

        var r = new ExposureAnalyzer().Analyze(img);
        Assert.True(r.BacklightDetected);
        Assert.Equal(BrightZone.Left, r.WindowZone);
        Assert.Contains(r.Suggestions, s => s.Contains("HDR"));
    }

    [Fact]
    public void Exposure_WellLitRoom_IsFine()
    {
        var img = new LumaImage(300, 200);
        for (var i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = (byte)(90 + i % 100);
        var r = new ExposureAnalyzer().Analyze(img);
        Assert.False(r.BacklightDetected);
        Assert.False(r.Underexposed);
        Assert.Empty(r.Suggestions);
    }

    [Fact]
    public void Tone_BrightensDarkPhoto()
    {
        using var bmp = new SKBitmap(new SKImageInfo(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 200; x++)
                bmp.SetPixel(x, y, new SKColor((byte)(20 + x / 3), (byte)(18 + x / 3), (byte)(15 + x / 3)));

        var tone = new ToneCorrector();
        var adj = tone.Compute(bmp);
        using var result = tone.Apply(bmp, adj);
        var before = ImageIo.ToLuma(bmp).Histogram();
        var after = ImageIo.ToLuma(result).Histogram();
        Assert.True(ExposureAnalyzer.MedianFromHistogram(after, after.Sum()) > ExposureAnalyzer.MedianFromHistogram(before, before.Sum()) + 25);
    }
}
