using System;
using InquadraCasa.Core.Analysis;
using SkiaSharp;

namespace InquadraCasa.Core.Imaging;

/// <summary>Dati dei sensori registrati al momento dello scatto.</summary>
public sealed record CaptureContext(double PitchDegrees, double RollDegrees, double? HeightCm, double MaxAngularVelocity);

public sealed record ProcessingOptions(bool CorrectPerspective = true, bool CorrectTone = true, int JpegQuality = 92);

public sealed record ProcessingResult(bool PerspectiveApplied, string PerspectiveNote, bool ToneApplied);

/// <summary>Analisi veloce (sul telefono) e post-produzione automatica al salvataggio.</summary>
public sealed class PhotoProcessor
{
    public SharpnessAnalyzer Sharpness { get; } = new();
    public ExposureAnalyzer Exposure { get; } = new();
    public PerspectiveCorrector Perspective { get; } = new();
    public ToneCorrector Tone { get; } = new();

    /// <summary>Controlli immediati: nitidezza ed esposizione.</summary>
    public (SharpnessResult Sharpness, ExposureResult Exposure) AnalyzeLocal(SKBitmap photo, CaptureContext ctx)
    {
        var luma = ImageIo.ToLuma(photo, 800);
        return (Sharpness.Analyze(luma, ctx.MaxAngularVelocity), Exposure.Analyze(luma));
    }

    /// <summary>Applica correzione prospettica e bilanciamento della luminosità e salva il JPEG finale.</summary>
    public ProcessingResult ProcessAndSave(SKBitmap photo, CaptureContext ctx, ProcessingOptions options, string outputPath)
    {
        var current = photo;
        var perspectiveApplied = false;
        var note = "";
        try
        {
            if (options.CorrectPerspective)
            {
                var correction = Perspective.Compute(current.Width, current.Height, ctx.PitchDegrees, ctx.RollDegrees);
                note = correction.Reason;
                if (correction.Applied)
                {
                    current = Perspective.Apply(current, correction);
                    perspectiveApplied = true;
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

            ImageIo.SaveJpeg(current, outputPath, options.JpegQuality);
            return new ProcessingResult(perspectiveApplied, note, toneApplied);
        }
        finally
        {
            if (!ReferenceEquals(current, photo)) current.Dispose();
        }
    }
}
