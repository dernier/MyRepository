using AppPhotoImmibili.Core.Imaging;

namespace AppPhotoImmibili.Core.Analysis;

/// <summary>Linea quasi verticale trovata nell'immagine: x = X0 + Slope·(y − centro), in pixel dell'immagine analizzata.</summary>
public sealed record DetectedLine(double X0, double Slope, int Votes)
{
    public double AngleDegrees => Math.Atan(Slope) * 180 / Math.PI;
}

/// <summary>
/// Assetto stimato dalle linee verticali (spigoli, stipiti, mobili).
/// <see cref="PitchDegrees"/> e <see cref="RollDegrees"/> hanno le stesse convenzioni di <see cref="DeviceAttitude"/>.
/// </summary>
public sealed record LineAttitudeEstimate(
    IReadOnlyList<DetectedLine> Lines,
    int InlierCount,
    double PitchDegrees,
    double RollDegrees,
    bool Reliable,
    double ResidualDegrees)
{
    public static LineAttitudeEstimate None { get; } = new([], 0, 0, 0, false, 0);
}

/// <summary>
/// Rilevamento delle linee verticali dominanti con una trasformata di Hough ristretta alle direzioni
/// entro ±<see cref="MaxAngleDegrees"/> dalla verticale, e stima di inclinazione e rollio residui.
/// <para>Con una fotocamera inclinata di θ le verticali convergono verso un punto di fuga a distanza f/tan θ dal centro:
/// una verticale che attraversa la riga centrale a distanza u dal centro ha pendenza dx/dy = u·tan θ / f − tan(rollio).
/// Una regressione pesata della pendenza sulla posizione fornisce quindi tan θ (coefficiente) e il rollio (intercetta).</para>
/// </summary>
public sealed class VerticalLineDetector
{
    public double MaxAngleDegrees { get; set; } = 15;
    public double AngleStepDegrees { get; set; } = 0.2;

    /// <summary>Soglia sul modulo del gradiente di Sobel (|gx| + |gy|, massimo 2040).</summary>
    public int EdgeThreshold { get; set; } = 90;

    /// <summary>Lunghezza minima di una linea, come frazione dell'altezza dell'immagine.</summary>
    public double MinLineFraction { get; set; } = 0.18;

    public int MaxLines { get; set; } = 40;

    /// <summary>Ogni pixel di bordo vota le pendenze entro questo scarto dalla direzione del proprio gradiente.</summary>
    public double OrientationWindowDegrees { get; set; } = 3;

    /// <summary>Servono almeno tante linee coerenti per fidarsi della stima.</summary>
    public int MinInliers { get; set; } = 4;

    /// <summary>Scarto massimo (in gradi di pendenza) di una linea dal modello per essere considerata coerente.</summary>
    public double InlierToleranceDegrees { get; set; } = 0.8;

    /// <param name="horizontalFovDegrees">Campo visivo sul lato lungo, lo stesso usato dalla correzione prospettica.</param>
    public LineAttitudeEstimate Estimate(LumaImage img, double horizontalFovDegrees)
    {
        var lines = Detect(img);
        var f = Math.Max(img.Width, img.Height) / 2.0 / Math.Tan(horizontalFovDegrees * Math.PI / 360);
        return Fit(lines, img.Width / 2.0, f);
    }

    public IReadOnlyList<DetectedLine> Detect(LumaImage img)
    {
        int w = img.Width, h = img.Height;
        var cy = h / 2.0;
        var p = img.Pixels;

        // Pixel di bordo con gradiente prevalentemente orizzontale (cioè bordi quasi verticali).
        var edges = new List<(int X, int Y, double Slope)>(w * h / 16);
        for (var y = 1; y < h - 1; y++)
        {
            var r0 = (y - 1) * w; var r1 = y * w; var r2 = (y + 1) * w;
            for (var x = 1; x < w - 1; x++)
            {
                var gx = p[r0 + x + 1] + 2 * p[r1 + x + 1] + p[r2 + x + 1] - p[r0 + x - 1] - 2 * p[r1 + x - 1] - p[r2 + x - 1];
                var gy = p[r2 + x - 1] + 2 * p[r2 + x] + p[r2 + x + 1] - p[r0 + x - 1] - 2 * p[r0 + x] - p[r0 + x + 1];
                var ax = Math.Abs(gx); var ay = Math.Abs(gy);
                // La tangente al bordo è perpendicolare al gradiente: dx/dy = −gy/gx.
                if (ax + ay >= EdgeThreshold && ax > 2 * ay)
                    edges.Add((x, y, -gy / (double)gx));
            }
        }

        var steps = (int)Math.Round(MaxAngleDegrees / AngleStepDegrees);
        var nSlopes = 2 * steps + 1;
        var slopes = new double[nSlopes];
        for (var i = 0; i < nSlopes; i++)
            slopes[i] = Math.Tan((i - steps) * AngleStepDegrees * Math.PI / 180);

        // Accumulatore (pendenza, x0): x0 = x − pendenza·(y − cy); margine per le linee che escono dai lati.
        var margin = (int)Math.Ceiling(h / 2.0 * slopes[^1]) + 1;
        var cols = w + 2 * margin;
        var acc = new int[nSlopes * cols];
        // Ogni pixel vota solo le pendenze vicine alla direzione locale del bordo: niente picchi spuri
        // a "farfalla" attorno alle linee vere.
        var window = (int)Math.Ceiling(OrientationWindowDegrees / AngleStepDegrees);
        foreach (var (x, y, edgeSlope) in edges)
        {
            var dy = y - cy;
            var center = (int)Math.Round(Math.Atan(edgeSlope) * 180 / Math.PI / AngleStepDegrees) + steps;
            var from = Math.Max(0, center - window);
            var to = Math.Min(nSlopes - 1, center + window);
            for (var s = from; s <= to; s++)
            {
                var x0 = (int)Math.Round(x - slopes[s] * dy) + margin;
                if ((uint)x0 < (uint)cols) acc[s * cols + x0]++;
            }
        }

        // Massimi locali sopra soglia, dal più votato, con soppressione dei vicini.
        var minVotes = (int)(MinLineFraction * h);
        var candidates = new List<(int S, int X, int V)>();
        for (var s = 0; s < nSlopes; s++)
            for (var x = 0; x < cols; x++)
            {
                var v = acc[s * cols + x];
                if (v >= minVotes) candidates.Add((s, x, v));
            }
        candidates.Sort((a, b) => b.V.CompareTo(a.V));

        var result = new List<DetectedLine>();
        // Le linee sono parametrizzate sulla riga centrale: candidati vicini lì (anche con angolo diverso)
        // sono la stessa linea, o i due bordi dello stesso stipite.
        var xRadius = Math.Max(6, w / 100);
        foreach (var c in candidates)
        {
            if (result.Count >= MaxLines) break;
            var x0 = c.X - margin;
            var near = result.Any(l =>
                Math.Abs(l.X0 - x0) <= xRadius &&
                Math.Abs(Math.Atan(l.Slope) - Math.Atan(slopes[c.S])) * 180 / Math.PI <= 4);
            if (!near) result.Add(new DetectedLine(x0, slopes[c.S], c.V));
        }
        return result;
    }

    /// <summary>Regressione pesata e robusta: pendenza = a + b·(x0 − cx)/f, con b = tan(inclinazione) e a = −tan(rollio).</summary>
    public LineAttitudeEstimate Fit(IReadOnlyList<DetectedLine> lines, double cx, double f)
    {
        if (lines.Count < MinInliers) return LineAttitudeEstimate.None with { Lines = lines };

        var inliers = lines.ToList();
        double a = 0, b = 0;
        // Primo passaggio: stima robusta dell'intercetta con la mediana (linee spurie oblique non la spostano).
        a = Median(inliers.Select(l => l.Slope));
        var tol = Math.Tan(InlierToleranceDegrees * Math.PI / 180);

        for (var iter = 0; iter < 5; iter++)
        {
            (a, b) = WeightedLinearFit(inliers, cx, f, a);
            var next = lines.Where(l => Math.Abs(l.Slope - (a + b * (l.X0 - cx) / f)) <= tol * (iter == 0 ? 3 : 1)).ToList();
            if (next.Count < MinInliers) return LineAttitudeEstimate.None with { Lines = lines };
            if (next.Count == inliers.Count && iter > 0) break;
            inliers = next;
        }

        // Le linee devono essere distribuite in larghezza, altrimenti l'inclinazione non è osservabile.
        var us = inliers.Select(l => (l.X0 - cx) / f).ToList();
        var spread = us.Max() - us.Min();
        var residual = Math.Sqrt(inliers.Average(l =>
        {
            var e = Math.Atan(l.Slope) - Math.Atan(a + b * (l.X0 - cx) / f);
            return e * e;
        })) * 180 / Math.PI;

        var pitch = Math.Atan(b) * 180 / Math.PI;
        var roll = -Math.Atan(a) * 180 / Math.PI;
        var reliable = inliers.Count >= MinInliers && spread >= 0.35;
        return new LineAttitudeEstimate(lines, inliers.Count, pitch, roll, reliable, residual);
    }

    private static (double A, double B) WeightedLinearFit(List<DetectedLine> lines, double cx, double f, double fallbackA)
    {
        double sw = 0, su = 0, st = 0, suu = 0, sut = 0;
        foreach (var l in lines)
        {
            var u = (l.X0 - cx) / f;
            double wgt = l.Votes;
            sw += wgt; su += wgt * u; st += wgt * l.Slope; suu += wgt * u * u; sut += wgt * u * l.Slope;
        }
        var den = sw * suu - su * su;
        if (sw <= 0) return (fallbackA, 0);
        if (Math.Abs(den) < 1e-12) return (st / sw, 0);
        var b = (sw * sut - su * st) / den;
        var a = (st - b * su) / sw;
        return (a, b);
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToArray();
        return v.Length == 0 ? 0 : v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
    }
}
