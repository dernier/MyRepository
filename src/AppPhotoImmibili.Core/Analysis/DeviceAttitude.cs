namespace AppPhotoImmibili.Core.Analysis;

/// <summary>Orientamento dell'interfaccia dedotto dalla gravità.</summary>
public enum HoldOrientation
{
    Portrait,
    /// <summary>Orizzontale con il bordo superiore del telefono rivolto a sinistra.</summary>
    LandscapeTopLeft,
    PortraitUpsideDown,
    /// <summary>Orizzontale con il bordo superiore del telefono rivolto a destra.</summary>
    LandscapeTopRight,
}

/// <summary>
/// Assetto del telefono rispetto alla verticale.
/// <para><see cref="PitchDegrees"/>: inclinazione dell'asse ottico sopra (+) o sotto (−) l'orizzonte.
/// A zero il sensore è perpendicolare al pavimento e i muri restano verticali.</para>
/// <para><see cref="RollDegrees"/>: rotazione attorno all'asse ottico rispetto all'orientamento più vicino
/// (verticale o orizzontale). A zero l'orizzonte è dritto.</para>
/// </summary>
public readonly record struct DeviceAttitude(double PitchDegrees, double RollDegrees, HoldOrientation Orientation)
{
    public bool IsLevel(double toleranceDegrees) =>
        Math.Abs(PitchDegrees) <= toleranceDegrees && Math.Abs(RollDegrees) <= toleranceDegrees;

    public bool IsLandscape => Orientation is HoldOrientation.LandscapeTopLeft or HoldOrientation.LandscapeTopRight;
}

/// <summary>
/// Calcola l'assetto dal vettore dell'accelerometro in coordinate dispositivo
/// (x verso destra, y verso l'alto del telefono in verticale, z uscente dallo schermo).
/// Il vettore atteso è la reazione alla gravità, cioè punta verso l'alto: con il telefono appoggiato
/// a schermo in su vale circa (0, 0, +1). Se una piattaforma riporta il segno opposto usare
/// <paramref name="sign"/> = −1 (vedi calibrazione nell'app).
/// </summary>
public static class AttitudeCalculator
{
    public static DeviceAttitude FromAccelerometer(double x, double y, double z, double sign = 1.0)
    {
        x *= sign; y *= sign; z *= sign;
        var norm = Math.Sqrt(x * x + y * y + z * z);
        if (norm < 1e-6)
            return new DeviceAttitude(0, 0, HoldOrientation.Portrait);

        x /= norm; y /= norm; z /= norm;

        // La fotocamera posteriore guarda lungo −z: sin(elevazione) = (−z)·su.
        var pitch = RadToDeg(Math.Asin(Math.Clamp(-z, -1, 1)));

        // Rotazione nel piano dello schermo: 0° = verticale dritto, 90° = bordo superiore verso sinistra.
        var screenAngle = RadToDeg(Math.Atan2(x, y));
        var quadrant = (int)Math.Round(screenAngle / 90.0);
        var roll = screenAngle - quadrant * 90.0;

        var orientation = (((quadrant % 4) + 4) % 4) switch
        {
            0 => HoldOrientation.Portrait,
            // su = +x: il bordo destro è in alto, quindi il bordo superiore punta a sinistra
            1 => HoldOrientation.LandscapeTopLeft,
            2 => HoldOrientation.PortraitUpsideDown,
            _ => HoldOrientation.LandscapeTopRight,
        };

        return new DeviceAttitude(pitch, roll, orientation);
    }

    public static double RadToDeg(double r) => r * 180.0 / Math.PI;
    public static double DegToRad(double d) => d * Math.PI / 180.0;
}

/// <summary>Filtro passa-basso esponenziale per stabilizzare letture rumorose.</summary>
public sealed class LowPassFilter(double alpha = 0.15)
{
    private bool _init;
    public double Value { get; private set; }

    public double Push(double sample)
    {
        if (!_init) { Value = sample; _init = true; }
        else Value += alpha * (sample - Value);
        return Value;
    }

    public void Reset() => _init = false;
}
