using InquadraCasa.Core.Imaging;

namespace InquadraCasa.Core.Analysis;

public sealed record SharpnessResult(
    double Score,
    double GlobalVariance,
    bool IsSharp,
    bool MotionDetected,
    string Message);

/// <summary>
/// Verifica di nitidezza dopo lo scatto.
/// Usa la varianza del Laplaciano per riquadri (3×4): le pareti lisce hanno pochi dettagli, quindi il punteggio
/// è il 75° percentile dei riquadri e non la media globale. Il mosso da movimento è stimato anche dal giroscopio.
/// </summary>
public sealed class SharpnessAnalyzer
{
    /// <summary>Soglia sul punteggio (immagine ridotta a 800 px). Più alta = più severa.</summary>
    public double Threshold { get; set; } = 100;

    /// <summary>Velocità angolare (rad/s) oltre la quale lo scatto è a rischio micro-mosso.</summary>
    public double MaxAngularVelocity { get; set; } = 0.35;

    public const int TileCols = 4;
    public const int TileRows = 3;

    public SharpnessResult Analyze(LumaImage img, double maxAngularVelocityDuringCapture = 0)
    {
        var tileScores = new List<double>(TileCols * TileRows);
        var tw = img.Width / TileCols;
        var th = img.Height / TileRows;
        for (var ty = 0; ty < TileRows; ty++)
            for (var tx = 0; tx < TileCols; tx++)
                tileScores.Add(LaplacianVariance(img, tx * tw, ty * th, tw, th));

        tileScores.Sort();
        var score = Percentile(tileScores, 0.75);
        var global = LaplacianVariance(img, 0, 0, img.Width, img.Height);
        var motion = maxAngularVelocityDuringCapture > MaxAngularVelocity;
        var sharp = score >= Threshold && !motion;

        var message = (sharp, motion) switch
        {
            (true, _) => "Foto nitida.",
            (false, true) => "Il telefono si è mosso durante lo scatto: rischio micro-mosso. Appoggia i gomiti al corpo o usa un treppiede e riscatta.",
            _ => "Foto sfocata o mossa. Tocca lo schermo per mettere a fuoco, resta fermo e riscatta.",
        };
        return new SharpnessResult(score, global, sharp, motion, message);
    }

    /// <summary>Varianza della risposta del Laplaciano 4-connesso su una regione.</summary>
    public static double LaplacianVariance(LumaImage img, int x0, int y0, int w, int h)
    {
        var x1 = Math.Min(img.Width - 1, x0 + w);
        var y1 = Math.Min(img.Height - 1, y0 + h);
        x0 = Math.Max(1, x0);
        y0 = Math.Max(1, y0);

        double sum = 0, sumSq = 0;
        long n = 0;
        var px = img.Pixels;
        var stride = img.Width;
        for (var y = y0; y < y1; y++)
        {
            var row = y * stride;
            for (var x = x0; x < x1; x++)
            {
                var i = row + x;
                var lap = px[i - 1] + px[i + 1] + px[i - stride] + px[i + stride] - 4 * px[i];
                sum += lap;
                sumSq += (double)lap * lap;
                n++;
            }
        }
        if (n == 0) return 0;
        var mean = sum / n;
        return sumSq / n - mean * mean;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var idx = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(idx);
        var hi = (int)Math.Ceiling(idx);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }
}
