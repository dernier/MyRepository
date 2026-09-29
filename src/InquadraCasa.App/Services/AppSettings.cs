using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace InquadraCasa.App.Services;

/// <summary>Impostazioni dell'app. La chiave API resta nello storage sicuro del sistema operativo.</summary>
public sealed class AppSettings
{
    private const string ApiKeyName = "anthropic_api_key";

    public double LevelToleranceDegrees
    {
        get => Preferences.Get(nameof(LevelToleranceDegrees), 1.0);
        set => Preferences.Set(nameof(LevelToleranceDegrees), value);
    }

    /// <summary>Campo visivo orizzontale del grandangolo principale (lato lungo).</summary>
    public double HorizontalFovDegrees
    {
        get => Preferences.Get(nameof(HorizontalFovDegrees), 69.0);
        set => Preferences.Set(nameof(HorizontalFovDegrees), value);
    }

    public bool UseWidestLens
    {
        get => Preferences.Get(nameof(UseWidestLens), true);
        set => Preferences.Set(nameof(UseWidestLens), value);
    }

    public double HeightMinCm
    {
        get => Preferences.Get(nameof(HeightMinCm), 120.0);
        set => Preferences.Set(nameof(HeightMinCm), value);
    }

    public double HeightMaxCm
    {
        get => Preferences.Get(nameof(HeightMaxCm), 140.0);
        set => Preferences.Set(nameof(HeightMaxCm), value);
    }

    public bool AutoPerspective
    {
        get => Preferences.Get(nameof(AutoPerspective), true);
        set => Preferences.Set(nameof(AutoPerspective), value);
    }

    public bool AutoTone
    {
        get => Preferences.Get(nameof(AutoTone), true);
        set => Preferences.Set(nameof(AutoTone), value);
    }

    public bool CheckDistractors
    {
        get => Preferences.Get(nameof(CheckDistractors), true);
        set => Preferences.Set(nameof(CheckDistractors), value);
    }

    public bool ShowCornerMask
    {
        get => Preferences.Get(nameof(ShowCornerMask), true);
        set => Preferences.Set(nameof(ShowCornerMask), value);
    }

    public bool IncludeOriginalsInExport
    {
        get => Preferences.Get(nameof(IncludeOriginalsInExport), false);
        set => Preferences.Set(nameof(IncludeOriginalsInExport), value);
    }

    /// <summary>Endpoint alternativo (proxy aziendale). Vuoto = API Anthropic diretta.</summary>
    public string ApiBaseUrl
    {
        get => Preferences.Get(nameof(ApiBaseUrl), "");
        set => Preferences.Set(nameof(ApiBaseUrl), value.Trim());
    }

    // Calibrazione della livella
    public double AccelerometerSign
    {
        get => Preferences.Get(nameof(AccelerometerSign), 1.0);
        set => Preferences.Set(nameof(AccelerometerSign), value);
    }

    public double PitchOffset
    {
        get => Preferences.Get(nameof(PitchOffset), 0.0);
        set => Preferences.Set(nameof(PitchOffset), value);
    }

    public double RollOffset
    {
        get => Preferences.Get(nameof(RollOffset), 0.0);
        set => Preferences.Set(nameof(RollOffset), value);
    }

    public async Task<string> GetApiKeyAsync()
    {
        try { return await SecureStorage.Default.GetAsync(ApiKeyName) ?? ""; }
        catch { return ""; }
    }

    public async Task SetApiKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) SecureStorage.Default.Remove(ApiKeyName);
        else await SecureStorage.Default.SetAsync(ApiKeyName, key.Trim());
    }
}
