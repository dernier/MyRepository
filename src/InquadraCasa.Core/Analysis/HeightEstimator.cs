using System;
using System.Collections.Generic;
using System.Linq;

namespace InquadraCasa.Core.Analysis;

public enum HeightStatus
{
    NotCalibrated,
    TooLow,
    Ok,
    TooHigh,
}

/// <summary>
/// Stima l'altezza della fotocamera dal pavimento con il barometro.
/// L'agente appoggia il telefono a terra e preme "Azzera": da quel momento la differenza di pressione
/// viene convertita in dislivello con la formula ipsometrica (circa 8,3 m per hPa al livello del mare).
/// La precisione dipende dal sensore (tipicamente ±10–20 cm): è una guida, non una misura.
/// </summary>
public sealed class HeightEstimator
{
    // R·T/(g·M) con T = 288,15 K: altezza di scala dell'atmosfera standard in metri.
    private const double ScaleHeightMeters = 8434.5;

    private readonly LowPassFilter _filter = new(0.08);
    private double? _floorPressure;
    private readonly List<double> _calibrationSamples = [];
    private bool _calibrating;

    public double MinCm { get; set; } = 120;
    public double MaxCm { get; set; } = 140;

    public bool IsCalibrated => _floorPressure is not null;
    public bool IsCalibrating => _calibrating;
    public double? CurrentHeightCm { get; private set; }

    /// <summary>Avvia l'acquisizione della pressione a pavimento.</summary>
    public void BeginFloorCalibration()
    {
        _calibrationSamples.Clear();
        _calibrating = true;
    }

    /// <summary>Chiude la calibrazione (media dei campioni raccolti). Restituisce false se non ci sono campioni.</summary>
    public bool EndFloorCalibration()
    {
        _calibrating = false;
        if (_calibrationSamples.Count == 0) return false;
        _floorPressure = _calibrationSamples.Average();
        _filter.Reset();
        CurrentHeightCm = 0;
        return true;
    }

    public void Reset()
    {
        _floorPressure = null;
        CurrentHeightCm = null;
        _filter.Reset();
    }

    /// <summary>Nuova lettura del barometro in hPa.</summary>
    public double? Push(double pressureHpa)
    {
        if (_calibrating)
        {
            _calibrationSamples.Add(pressureHpa);
            return null;
        }
        if (_floorPressure is not { } p0) return null;

        var p = _filter.Push(pressureHpa);
        CurrentHeightCm = HeightFromPressure(p0, p) * 100.0;
        return CurrentHeightCm;
    }

    public static double HeightFromPressure(double referenceHpa, double currentHpa) =>
        ScaleHeightMeters * Math.Log(referenceHpa / currentHpa);

    public HeightStatus Status => CurrentHeightCm switch
    {
        null => HeightStatus.NotCalibrated,
        var h when h < MinCm => HeightStatus.TooLow,
        var h when h > MaxCm => HeightStatus.TooHigh,
        _ => HeightStatus.Ok,
    };

    /// <summary>
    /// Con il telefono in bolla la linea d'orizzonte dell'immagine coincide con l'altezza della fotocamera.
    /// Una porta standard è alta 210 cm: a 130 cm l'orizzonte taglia lo stipite a circa il 62% dell'altezza.
    /// </summary>
    public static double HorizonFractionOnDoor(double cameraHeightCm, double doorHeightCm = 210) =>
        Math.Clamp(cameraHeightCm / doorHeightCm, 0, 1);
}
