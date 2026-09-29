using AppPhotoImmibili.Core.Ai;
using AppPhotoImmibili.Core.Models;
using SkiaSharp;

namespace AppPhotoImmibili.Core.Tests;

public class OnDeviceDetectorTests
{
    private static readonly string ModelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tiny_yolo.onnx");

    [Fact]
    public void TinyModel_DetectsAndMapsToDistractors()
    {
        using var detector = new OnDeviceDistractorDetector(ModelPath);
        Assert.Equal(80, detector.ClassNames.Count);

        // 128×64 in un ingresso 64×64: scala 0,5 con bande di 16 px sopra e sotto.
        using var photo = new SKBitmap(128, 64);
        var detections = detector.Detect(photo);
        Assert.Equal(2, detections.Count);

        var person = detections.Single(d => d.ClassName == "person");
        Assert.Equal(0.5f, (person.Left + person.Right) / 2, 3);
        Assert.Equal(0.5f, (person.Top + person.Bottom) / 2, 3);

        var report = detector.Analyze(photo);
        Assert.Equal(Severity.Alta, report.Items[0].Severity);
        Assert.Equal("Persona nell'inquadratura", report.Items[0].Item);
        Assert.Contains(report.Items, i => i.Item == "Bottiglie o flaconi" && i.Position == "in basso a sinistra");
        Assert.True(report.HasBlocking);
    }

    [Fact]
    public void Postprocess_AppliesNmsAndThreshold_OnTransposedOutput()
    {
        string[] names = ["toilet_seat_up", "chair"];
        // [1, N=3, 4+2]: due riquadri sovrapposti della stessa classe e uno sotto soglia.
        float[] data =
        [
            100, 100, 50, 50, 0.9f, 0,
            102, 101, 50, 50, 0.7f, 0,
            300, 300, 40, 40, 0, 0.2f,
        ];
        var lb = new OnDeviceDistractorDetector.Letterbox(1, 0, 0, 640, 640);
        var result = OnDeviceDistractorDetector.Postprocess(data, [1, 3, 6], names, lb, new OnDeviceDetectorOptions());
        var d = Assert.Single(result);
        Assert.Equal("toilet_seat_up", d.ClassName);

        var report = OnDeviceDistractorDetector.ToReport(result);
        Assert.Equal("Tavoletta del WC alzata", report.Items.Single().Item);
    }

    [Fact]
    public void ToReport_GroupsSameItem_AndIgnoresFurniture()
    {
        var detections = new[]
        {
            new Detection("bottle", 0.8f, 0.1f, 0.1f, 0.2f, 0.3f),
            new Detection("bottle", 0.7f, 0.7f, 0.7f, 0.75f, 0.8f),
            new Detection("chair", 0.9f, 0.4f, 0.4f, 0.6f, 0.8f),
        };
        var report = OnDeviceDistractorDetector.ToReport(detections);
        var item = Assert.Single(report.Items);
        Assert.Equal("Bottiglie o flaconi (2)", item.Item);
        Assert.Equal("in alto a sinistra e altri 1", item.Position);
    }

    [Fact]
    public void ParseNames_ReadsUltralyticsMetadata()
    {
        var names = OnDeviceDistractorDetector.ParseNames("{0: 'cavi', 1: 'disordine', 2: \"tavoletta_alzata\"}");
        Assert.Equal(["cavi", "disordine", "tavoletta_alzata"], names);
    }

    [Fact]
    public void Merge_PrefersCloudSummary_AndAvoidsDuplicates()
    {
        var local = new DistractorReport([new Distractor("Persona nell'inquadratura", "al centro", Severity.Alta, "Esci")], "locale");
        var cloud = new DistractorReport([new Distractor("Persona riflessa nello specchio", "a destra", Severity.Alta, "Spostati")], "cloud");
        var merged = DistractorReport.Merge(local, cloud)!;
        Assert.Equal("cloud", merged.Summary);
        Assert.Single(merged.Items);
    }
}
