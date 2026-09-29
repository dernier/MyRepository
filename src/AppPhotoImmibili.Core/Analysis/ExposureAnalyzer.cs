using AppPhotoImmibili.Core.Imaging;

namespace AppPhotoImmibili.Core.Analysis;

public enum BrightZone
{
    None,
    Left,
    Center,
    Right,
}

public sealed record ExposureResult(
    double MedianLuma,
    double ClippedHighlightsPercent,
    double ClippedShadowsPercent,
    double BrightAreaPercent,
    bool BacklightDetected,
    bool Underexposed,
    BrightZone WindowZone,
    IReadOnlyList<string> Suggestions);

/// <summary>
/// Analisi dell'esposizione: rileva finestre in controluce (zone molto luminose e bruciate
/// in un ambiente scuro) e suggerisce HDR e angolazione di ripresa.
/// </summary>
public sealed class ExposureAnalyzer
{
    public byte HighlightClip { get; set; } = 250;
    public byte ShadowClip { get; set; } = 5;
    public byte BrightThreshold { get; set; } = 235;

    /// <summary>Percentuale minima di pixel bruciati per considerare problematico il controluce.</summary>
    public double BacklightClipPercent { get; set; } = 2.0;

    /// <summary>Luminanza mediana dell'ambiente (esclusa la zona luminosa) sotto cui l'interno è "scuro".</summary>
    public double DarkInteriorMedian { get; set; } = 95;

    public ExposureResult Analyze(LumaImage img)
    {
        var total = (double)img.Pixels.Length;
        var hist = img.Histogram();

        var clippedHigh = 0;
        for (int i = HighlightClip; i < 256; i++) clippedHigh += hist[i];
        var clippedLow = 0;
        for (int i = 0; i <= ShadowClip; i++) clippedLow += hist[i];

        var median = MedianFromHistogram(hist, img.Pixels.Length);

        // Mediana dell'ambiente esclusa la zona luminosa e distribuzione orizzontale della zona luminosa
        var interior = new int[256];
        var interiorCount = 0;
        var brightCount = 0;
        var colBright = new long[3];
        var third = Math.Max(1, img.Width / 3);
        for (var y = 0; y < img.Height; y++)
        {
            var row = y * img.Width;
            for (var x = 0; x < img.Width; x++)
            {
                var v = img.Pixels[row + x];
                if (v >= BrightThreshold)
                {
                    brightCount++;
                    colBright[Math.Min(2, x / third)]++;
                }
                else
                {
                    interior[v]++;
                    interiorCount++;
                }
            }
        }
        var interiorMedian = interiorCount > 0 ? MedianFromHistogram(interior, interiorCount) : median;

        var clippedHighPct = clippedHigh / total * 100;
        var clippedLowPct = clippedLow / total * 100;
        var brightPct = brightCount / total * 100;

        var backlight = clippedHighPct >= BacklightClipPercent && brightPct is > 1.5 and < 60
                        && interiorMedian < DarkInteriorMedian;
        var underexposed = !backlight && median < 60;

        var zone = BrightZone.None;
        if (brightCount > 0)
        {
            var max = colBright.Max();
            // Se la zona luminosa è distribuita in modo uniforme la consideriamo centrale.
            zone = max < brightCount * 0.5 ? BrightZone.Center
                 : max == colBright[0] ? BrightZone.Left
                 : max == colBright[2] ? BrightZone.Right
                 : BrightZone.Center;
        }

        var tips = new List<string>();
        if (backlight)
        {
            tips.Add("Controluce rilevato: attiva la modalità HDR della fotocamera prima di riscattare.");
            tips.Add("Accendi tutte le luci interne per ridurre il contrasto con l'esterno.");
            tips.Add(zone switch
            {
                BrightZone.Left => "La finestra è a sinistra: ruotati di 30–45° verso destra, così la luce arriva di lato.",
                BrightZone.Right => "La finestra è a destra: ruotati di 30–45° verso sinistra, così la luce arriva di lato.",
                _ => "La finestra è di fronte: spostati nell'angolo accanto alla finestra e inquadra verso l'interno, con la luce alle spalle.",
            });
        }
        else if (clippedHighPct >= BacklightClipPercent)
        {
            tips.Add("Alcune zone sono bruciate: valuta l'HDR o riduci l'esposizione toccando la zona chiara.");
        }
        if (underexposed)
            tips.Add("Foto scura: accendi le luci, apri le tapparelle o attiva l'HDR.");
        if (clippedLowPct > 15)
            tips.Add("Ombre molto chiuse: aggiungi luce o usa l'HDR per recuperare i dettagli.");

        return new ExposureResult(median, clippedHighPct, clippedLowPct, brightPct, backlight, underexposed, zone, tips);
    }

    public static double MedianFromHistogram(int[] hist, int count)
    {
        var half = count / 2.0;
        var acc = 0;
        for (var i = 0; i < hist.Length; i++)
        {
            acc += hist[i];
            if (acc >= half) return i;
        }
        return 255;
    }
}
