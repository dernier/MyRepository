using InquadraCasa.Core.Analysis;

namespace InquadraCasa.App.Controls;

public enum MaskMode
{
    Nessuna,
    TrePareti,
    Spigolo,
}

/// <summary>
/// Disegna sopra l'anteprima: griglia dei terzi, maschere di composizione, linea d'orizzonte,
/// guide verticali che mostrano la convergenza dei muri e la bolla. Tutto diventa verde quando il telefono è in bolla.
/// </summary>
public sealed class FramingOverlay : IDrawable
{
    private static readonly Color Ok = Color.FromArgb("#3DBE63");
    private static readonly Color Warn = Color.FromArgb("#F2A541");
    private static readonly Color Bad = Color.FromArgb("#E5484D");
    private static readonly Color Guide = Colors.White.WithAlpha(0.35f);
    private static readonly Color MaskColor = Colors.White.WithAlpha(0.75f);

    public DeviceAttitude Attitude { get; set; }
    public bool HasAttitude { get; set; }
    public double ToleranceDegrees { get; set; } = 1;
    public double HorizontalFovDegrees { get; set; } = 69;
    public MaskMode Mask { get; set; } = MaskMode.TrePareti;
    public bool ShowGrid { get; set; } = true;

    public Color LevelColor => !HasAttitude ? Warn
        : Attitude.IsLevel(ToleranceDegrees) ? Ok
        : Attitude.IsLevel(ToleranceDegrees * 3) ? Warn
        : Bad;

    public void Draw(ICanvas canvas, RectF r)
    {
        float w = r.Width, h = r.Height, cx = r.Center.X, cy = r.Center.Y;
        var color = LevelColor;

        if (ShowGrid) DrawGrid(canvas, r);
        DrawMask(canvas, r);
        if (!HasAttitude) return;

        // Pixel per unità di tangente sul lato lungo dell'anteprima
        var f = (float)(Math.Max(w, h) / 2 / Math.Tan(HorizontalFovDegrees * Math.PI / 360));
        var pitch = (float)(Attitude.PitchDegrees * Math.PI / 180);
        var roll = (float)Attitude.RollDegrees;

        // Linea d'orizzonte: con la fotocamera inclinata verso l'alto scende sotto il centro.
        var horizonY = cy + f * MathF.Tan(pitch);
        canvas.SaveState();
        canvas.Rotate(roll, cx, horizonY);
        canvas.StrokeColor = color;
        canvas.StrokeSize = 3;
        canvas.DrawLine(-w, horizonY, 2 * w, horizonY);
        canvas.RestoreState();

        // Riferimento fisso al centro: la linea d'orizzonte deve sovrapporsi a questi trattini.
        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 2;
        canvas.DrawLine(cx - 90, cy, cx - 50, cy);
        canvas.DrawLine(cx + 50, cy, cx + 90, cy);

        // Guide verticali: mostrano come convergono i muri con l'inclinazione attuale.
        canvas.SaveState();
        canvas.Rotate(roll, cx, cy);
        canvas.StrokeColor = color;
        canvas.StrokeSize = 3;
        foreach (var x in new[] { w * 0.1f, w * 0.9f })
        {
            var d = (cx - x) * (h / 2) * MathF.Tan(pitch) / f;
            canvas.DrawLine(x + d, 0, x - d, h);
        }
        canvas.RestoreState();

        // Bolla
        const float ring = 38, bubble = 13, scale = 7;
        var bx = Math.Clamp((float)Attitude.RollDegrees * scale, -70, 70);
        var by = Math.Clamp((float)Attitude.PitchDegrees * scale, -70, 70);
        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 2;
        canvas.DrawCircle(cx, cy, ring);
        canvas.FillColor = color.WithAlpha(0.85f);
        canvas.FillCircle(cx + bx, cy + by, bubble);

        canvas.FontColor = Colors.White;
        canvas.FontSize = 13;
        canvas.DrawString($"Inclinazione {Attitude.PitchDegrees:+0.0;-0.0;0.0}°   Rollio {Attitude.RollDegrees:+0.0;-0.0;0.0}°",
            cx - 150, cy + ring + 10, 300, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
    }

    private static void DrawGrid(ICanvas canvas, RectF r)
    {
        canvas.StrokeColor = Guide;
        canvas.StrokeSize = 1;
        for (var i = 1; i < 3; i++)
        {
            canvas.DrawLine(r.Width * i / 3, 0, r.Width * i / 3, r.Height);
            canvas.DrawLine(0, r.Height * i / 3, r.Width, r.Height * i / 3);
        }
    }

    private void DrawMask(ICanvas canvas, RectF r)
    {
        if (Mask == MaskMode.Nessuna) return;
        float w = r.Width, h = r.Height;
        canvas.StrokeColor = MaskColor;
        canvas.StrokeSize = 2;
        canvas.StrokeDashPattern = [10, 8];
        canvas.FontColor = MaskColor;
        canvas.FontSize = 12;

        if (Mask == MaskMode.TrePareti)
        {
            // Parete di fondo leggermente decentrata: fotocamera in un angolo, puntata oltre la diagonale.
            float x1 = w * 0.36f, x2 = w * 0.80f, y1 = h * 0.30f, y2 = h * 0.66f;
            canvas.DrawRectangle(x1, y1, x2 - x1, y2 - y1);
            canvas.DrawLine(0, 0, x1, y1);
            canvas.DrawLine(w, 0, x2, y1);
            canvas.DrawLine(0, h, x1, y2);
            canvas.DrawLine(w, h, x2, y2);
            canvas.DrawString("parete di fondo", x1, y1 + 6, x2 - x1, 16, HorizontalAlignment.Center, VerticalAlignment.Top);
        }
        else
        {
            // Spigolo della stanza al centro, due pareti in fuga.
            float cx = w * 0.5f, top = h * 0.28f, bottom = h * 0.70f;
            canvas.DrawLine(cx, top, cx, bottom);
            canvas.DrawLine(cx, top, 0, h * 0.08f);
            canvas.DrawLine(cx, top, w, h * 0.08f);
            canvas.DrawLine(cx, bottom, 0, h * 0.94f);
            canvas.DrawLine(cx, bottom, w, h * 0.94f);
            canvas.DrawString("spigolo", cx + 6, top + 4, 80, 16, HorizontalAlignment.Left, VerticalAlignment.Top);
        }
        canvas.StrokeDashPattern = null;
    }
}
