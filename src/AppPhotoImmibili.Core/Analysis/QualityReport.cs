using AppPhotoImmibili.Core.Ai;

namespace AppPhotoImmibili.Core.Analysis;

/// <summary>Esito complessivo dei controlli qualità su uno scatto.</summary>
public sealed record QualityReport(
    SharpnessResult Sharpness,
    ExposureResult Exposure,
    DistractorReport? Distractors,
    string? DistractorError)
{
    /// <summary>Vero se conviene riscattare prima di cambiare ambiente.</summary>
    public bool ShouldRetake =>
        !Sharpness.IsSharp || Exposure.BacklightDetected || (Distractors?.HasBlocking ?? false);

    public IEnumerable<string> Messages()
    {
        yield return Sharpness.Message;
        foreach (var s in Exposure.Suggestions) yield return s;
        if (Distractors is { } d)
        {
            foreach (var item in d.Items)
                yield return $"{item.Item} ({item.Position}): {item.Action}";
        }
        if (DistractorError is { } e) yield return e;
    }
}
