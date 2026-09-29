using System.Numerics;
using AppPhotoImmibili.Core.Analysis;

namespace AppPhotoImmibili.App.Services;

/// <summary>
/// Legge accelerometro, giroscopio e barometro e pubblica assetto e altezza stimata.
/// Gli eventi arrivano sul thread dei sensori: chi aggiorna la UI deve passare al thread principale.
/// </summary>
public sealed class SensorService(AppSettings settings)
{
    private readonly LowPassFilter _pitch = new(0.12);
    private readonly LowPassFilter _roll = new(0.12);
    private readonly Queue<(DateTime Time, double Value)> _gyroWindow = new();
    private readonly object _gyroLock = new();
    private int _users;

    public HeightEstimator Height { get; } = new();

    public DeviceAttitude Attitude { get; private set; }
    public Vector3 LastAcceleration { get; private set; }
    public bool HasBarometer => Barometer.Default.IsSupported;
    public bool HasGyroscope => Gyroscope.Default.IsSupported;

    public event EventHandler<DeviceAttitude>? AttitudeChanged;
    public event EventHandler<double?>? HeightChanged;

    public void Start()
    {
        if (_users++ > 0) return;
        Height.MinCm = settings.HeightMinCm;
        Height.MaxCm = settings.HeightMaxCm;

        if (Accelerometer.Default.IsSupported && !Accelerometer.Default.IsMonitoring)
        {
            Accelerometer.Default.ReadingChanged += OnAccelerometer;
            Accelerometer.Default.Start(SensorSpeed.UI);
        }
        if (Gyroscope.Default.IsSupported && !Gyroscope.Default.IsMonitoring)
        {
            Gyroscope.Default.ReadingChanged += OnGyroscope;
            Gyroscope.Default.Start(SensorSpeed.Game);
        }
        if (Barometer.Default.IsSupported && !Barometer.Default.IsMonitoring)
        {
            Barometer.Default.ReadingChanged += OnBarometer;
            Barometer.Default.Start(SensorSpeed.UI);
        }
    }

    public void Stop()
    {
        if (--_users > 0) return;
        _users = 0;
        if (Accelerometer.Default.IsMonitoring)
        {
            Accelerometer.Default.Stop();
            Accelerometer.Default.ReadingChanged -= OnAccelerometer;
        }
        if (Gyroscope.Default.IsMonitoring)
        {
            Gyroscope.Default.Stop();
            Gyroscope.Default.ReadingChanged -= OnGyroscope;
        }
        if (Barometer.Default.IsMonitoring)
        {
            Barometer.Default.Stop();
            Barometer.Default.ReadingChanged -= OnBarometer;
        }
    }

    /// <summary>Massima velocità angolare (rad/s) nell'ultimo intervallo: indica il rischio di mosso.</summary>
    public double MaxAngularVelocity(TimeSpan window)
    {
        lock (_gyroLock)
        {
            var since = DateTime.UtcNow - window;
            return _gyroWindow.Where(g => g.Time >= since).Select(g => g.Value).DefaultIfEmpty(0).Max();
        }
    }

    private void OnAccelerometer(object? sender, AccelerometerChangedEventArgs e)
    {
        var a = e.Reading.Acceleration;
        LastAcceleration = a;
        var raw = AttitudeCalculator.FromAccelerometer(a.X, a.Y, a.Z, settings.AccelerometerSign);
        Attitude = raw with
        {
            PitchDegrees = _pitch.Push(raw.PitchDegrees - settings.PitchOffset),
            RollDegrees = _roll.Push(raw.RollDegrees - settings.RollOffset),
        };
        AttitudeChanged?.Invoke(this, Attitude);
    }

    private void OnGyroscope(object? sender, GyroscopeChangedEventArgs e)
    {
        var v = e.Reading.AngularVelocity.Length();
        var now = DateTime.UtcNow;
        lock (_gyroLock)
        {
            _gyroWindow.Enqueue((now, v));
            while (_gyroWindow.Count > 0 && now - _gyroWindow.Peek().Time > TimeSpan.FromSeconds(3))
                _gyroWindow.Dequeue();
        }
    }

    private void OnBarometer(object? sender, BarometerChangedEventArgs e)
    {
        var h = Height.Push(e.Reading.PressureInHectopascals);
        HeightChanged?.Invoke(this, h);
    }

    /// <summary>
    /// Calibrazione a telefono appoggiato su un piano, schermo in su: determina il segno dell'accelerometro
    /// della piattaforma. Restituisce false se il telefono non sembra orizzontale.
    /// </summary>
    public bool CalibrateSignFlat()
    {
        var a = LastAcceleration;
        var n = a.Length();
        if (n < 0.1f || Math.Abs(a.Z) / n < 0.9) return false;
        settings.AccelerometerSign = a.Z > 0 ? 1.0 : -1.0;
        return true;
    }

    /// <summary>Calibrazione dello zero: telefono appoggiato a uno stipite o a un muro verticale.</summary>
    public bool CalibrateZeroAgainstWall()
    {
        var a = LastAcceleration;
        var raw = AttitudeCalculator.FromAccelerometer(a.X, a.Y, a.Z, settings.AccelerometerSign);
        if (Math.Abs(raw.PitchDegrees) > 5 || Math.Abs(raw.RollDegrees) > 5) return false;
        settings.PitchOffset = raw.PitchDegrees;
        settings.RollOffset = raw.RollDegrees;
        _pitch.Reset();
        _roll.Reset();
        return true;
    }
}
