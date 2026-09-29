using AppPhotoImmibili.Core.Analysis;
using SkiaSharp;

namespace AppPhotoImmibili.Core.Imaging;

/// <summary>Dati dei sensori registrati al momento dello scatto.</summary>
public sealed record CaptureContext(double PitchDegrees, double RollDegrees, double? HeightCm, double MaxAngularVelocity);

/// <param name="RefineWithLines">Affina l'assetto dei sensori con le linee verticali rilevate nell'immagine.</param>
public sealed record ProcessingOptions(bool CorrectPerspective = true, bool CorrectTone = true, int JpegQuality = 92,
    bool RefineWithLines = true);

/// <param name="PitchDegrees">Inclinazione effettivamente corretta (dai sensori o dalle linee).</param>
/// <param name="FromLines">Vero se la correzione usa la stima dalle linee verticali.</param>
public sealed record ProcessingResult(bool PerspectiveApplied, string PerspectiveNote, bool ToneApplied,
    double PitchDegrees = 0, double RollDegrees = 0, bool FromLines = false);

/// <summary>Analisi veloce (sul telefono) e post-produzione automatica al salvataggio.</summary>
public sealed class PhotoProcessor
{
    public SharpnessAnalyzer Sharpness { get; } = new();
    public ExposureAnalyzer Exposure { get; } = new();
    public PerspectiveCorrector Perspective { get; } = new();
    public ToneCorrector Tone { get; } = new();
    public VerticalLineDetector Lines { get; } = new();

    /// <summary>Oltre questo scarto tra sensori e linee le linee sono ritenute inaffidabili (oggetti non verticali).</summary>
    public double MaxLineSensorDisagreementDegrees { get; set; } = 3;

    /// <summary>Controlli immediati: nitidezza ed esposizione.</summary>
    public (SharpnessResult Sharpness, ExposureResult Exposure) AnalyzeLocal(SKBitmap photo, CaptureContext ctx)
    {
        var luma = ImageIo.ToLuma(photo, 800);
        return (Sharpness.Analyze(luma, ctx.MaxAngularVelocity), Exposure.Analyze(luma));
    }

    /// <summary>Applica correzione prospettica e bilanciamento della luminosità e salva il JPEG finale.</summary>
    /// <param name="metadata">Se presente viene scritto nell'EXIF del JPEG finale.</param>
    public ProcessingResult ProcessAndSave(SKBitmap photo, CaptureContext ctx, ProcessingOptions options, string outputPath,
        PhotoMetadata? metadata = null)
    {
        var current = photo;
        var perspectiveApplied = false;
        var note = "";
        var (pitch, roll, fromLines) = (ctx.PitchDegrees, ctx.RollDegrees, false);
        try
        {
            if (options.CorrectPerspective)
            {
                if (options.RefineWithLines)
                    (pitch, roll, fromLines) = RefineAttitude(current, ctx);
                var correction = Perspective.Compute(current.Width, current.Height, pitch, roll);
                note = correction.Reason;
                if (correction.Applied)
                {
                    current = Perspective.Apply(current, correction);
                    perspectiveApplied = true;
                    if (fromLines) note += " Assetto affinato con le linee verticali.";
                }
            }

            var toneApplied = false;
            if (options.CorrectTone)
            {
                var adj = Tone.Compute(current);
                var toned = Tone.Apply(current, adj);
                if (!ReferenceEquals(current, photo)) current.Dispose();
                current = toned;
                toneApplied = true;
            }

            var jpeg = ImageIo.EncodeJpeg(current, options.JpegQuality);
            if (metadata is not null)
                jpeg = ExifWriter.Embed(jpeg, metadata with
                {
                    PerspectiveCorrected = perspectiveApplied,
                    CorrectedFromLines = perspectiveApplied && fromLines,
                });
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, jpeg);
            return new ProcessingResult(perspectiveApplied, note, toneApplied, pitch, roll, fromLines);
        }
        finally
        {
            if (!ReferenceEquals(current, photo)) current.Dispose();
        }
    }

    /// <summary>
    /// Usa l'assetto stimato dalle linee verticali se è affidabile e vicino a quello dei sensori:
    /// con lo stesso campo visivo della correzione, le linee rilevate diventano esattamente verticali.
    /// </summary>
    public (double Pitch, double Roll, bool FromLines) RefineAttitude(SKBitmap photo, CaptureContext ctx)
    {
        var luma = ImageIo.ToLuma(photo, 1000);
        var estimate = Lines.Estimate(luma, Perspective.HorizontalFovDegrees);
        if (!estimate.Reliable
            || Math.Abs(estimate.PitchDegrees - ctx.PitchDegrees) > MaxLineSensorDisagreementDegrees
            || Math.Abs(estimate.RollDegrees - ctx.RollDegrees) > MaxLineSensorDisagreementDegrees)
            return (ctx.PitchDegrees, ctx.RollDegrees, false);
        return (estimate.PitchDegrees, estimate.RollDegrees, true);
    }

    /// <summary>Versione per il web (lato lungo ridotto, qualità ottimizzata) con gli stessi metadati EXIF.</summary>
    public static byte[] CreateWebVersion(string jpegPath, PhotoMetadata? metadata, int maxSide = 2048, int quality = 85)
    {
        using var full = ImageIo.LoadUpright(jpegPath);
        using var small = ImageIo.Downscale(full, maxSide);
        var jpeg = ImageIo.EncodeJpeg(small, quality);
        return metadata is null ? jpeg : ExifWriter.Embed(jpeg, metadata);
    }
}
